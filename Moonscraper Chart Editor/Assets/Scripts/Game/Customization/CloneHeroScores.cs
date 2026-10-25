// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Reads Clone Hero's scores file (scoredata.bin, or scores.bin before v1) and finds the scores for one chart.
/// Clone Hero names each song by the MD5 of its chart file, so scores only match the chart exactly as Clone Hero
/// last played it: saving changes in Moonscraper gives it a new hash until Clone Hero plays it again.
/// </summary>
public static class CloneHeroScores
{
    public enum Instrument { Guitar, Bass, Rhythm, GuitarCoop, GHLGuitar, GHLBass, Drums, Keys, Band }

    public class Score
    {
        public Instrument instrument;
        public int difficulty;          // 0 Easy to 3 Expert
        public float percent;
        public bool fc;
        public int stars;
        public int score;
        public int speed = 100;         // Percent
        public int modifiers = 1;       // Old file only, 1 = none
    }

    public class Song
    {
        public int playCount;
        public List<Score> scores = new List<Score>();
    }

    public enum Status { NoChart, NoScoresFile, Unreadable, NotPlayed, Found }

    public class Result
    {
        public Status status;
        public string scoresFile;
        public Song song;
    }

    static readonly string[] s_scoreFileNames = { "scoredata.bin", "scores.bin" };
    static readonly string[] s_modifierNames = { null, "All Strums", "All HOPOs", "All Taps", "All Opens", "Mirror", "Shuffle", "HOPOs to Taps" };
    static readonly string[] s_difficultyNames = { "Easy", "Medium", "Hard", "Expert" };
    const int OldFileVersion = 20180220;

    // Parsed songs, by uppercase hex hash, kept until the scores file changes
    static string s_loadedFile;
    static DateTime s_loadedTime;
    static Dictionary<string, Song> s_songs;

    // The chart's hash, kept until the chart file changes
    static string s_hashedChart;
    static DateTime s_hashedTime;
    static string s_chartHash;

    /// <summary>The scores for the chart file at chartPath, reading the files again only when they changed.</summary>
    public static Result Lookup(string chartPath)
    {
        Result result = new Result();
        result.scoresFile = FindScoresFile();

        if (string.IsNullOrEmpty(chartPath) || !File.Exists(chartPath))
        {
            result.status = Status.NoChart;
            return result;
        }
        if (result.scoresFile == null)
        {
            result.status = Status.NoScoresFile;
            return result;
        }

        try
        {
            DateTime scoresTime = File.GetLastWriteTimeUtc(result.scoresFile);
            if (s_songs == null || s_loadedFile != result.scoresFile || s_loadedTime != scoresTime)
            {
                s_songs = Parse(File.ReadAllBytes(result.scoresFile));
                s_loadedFile = result.scoresFile;
                s_loadedTime = scoresTime;
            }

            DateTime chartTime = File.GetLastWriteTimeUtc(chartPath);
            if (s_chartHash == null || s_hashedChart != chartPath || s_hashedTime != chartTime)
            {
                s_chartHash = HashFile(chartPath);
                s_hashedChart = chartPath;
                s_hashedTime = chartTime;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Clone Hero scores: couldn't read " + result.scoresFile + ": " + e.Message);
            s_songs = null;
            result.status = Status.Unreadable;
            return result;
        }

        if (s_songs == null)
        {
            result.status = Status.Unreadable;
            return result;
        }

        Song song;
        if (s_songs.TryGetValue(s_chartHash, out song))
        {
            result.status = Status.Found;
            result.song = song;
        }
        else
            result.status = Status.NotPlayed;

        return result;
    }

    /// <summary>
    /// Clone Hero keeps its scores in its own data folder (AppData\LocalLow\srylain Inc_\Clone Hero on Windows), next
    /// to Moonscraper's (LocalLow\FireFox Dev\Moonscraper Chart Editor). A portable install keeps them in PlayerData,
    /// the folder holding the Custom folder picked in Settings > Customize.
    /// </summary>
    public static string FindScoresFile()
    {
        List<string> folders = new List<string>();

        try
        {
            DirectoryInfo company = Directory.GetParent(Application.persistentDataPath);
            DirectoryInfo dataRoot = company != null ? company.Parent : null;
            if (dataRoot != null && dataRoot.Exists)
            {
                foreach (DirectoryInfo folder in dataRoot.GetDirectories())
                {
                    if (folder.Name.StartsWith("srylain", StringComparison.OrdinalIgnoreCase))
                        folders.Add(Path.Combine(folder.FullName, "Clone Hero"));
                }
            }
        }
        catch (Exception) { }

        string custom = CustomizationPrefs.cloneHeroFolder;
        if (!string.IsNullOrEmpty(custom))
        {
            try
            {
                DirectoryInfo parent = Directory.GetParent(custom.TrimEnd('/', '\\'));
                if (parent != null)
                    folders.Add(parent.FullName);
            }
            catch (Exception) { }
        }

        foreach (string name in s_scoreFileNames)
        {
            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path))
                    return path;
            }
        }

        return null;
    }

    public static string HashFile(string path)
    {
        using (MD5 md5 = MD5.Create())
        using (FileStream stream = File.OpenRead(path))
            return ToHex(md5.ComputeHash(stream));
    }

    static string ToHex(byte[] bytes)
    {
        StringBuilder builder = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes)
            builder.Append(b.ToString("X2"));
        return builder.ToString();
    }

    /// <summary>
    /// Both layouts start with a version and a song count. Before v1 (version 20180220) each song's hash is a
    /// length-prefixed hex string and a score is 13 bytes; v1 (version 20211009 in Robert's file) stores the 16 hash
    /// bytes, puts Easy first and widens the modifiers to 4 bytes, so a score is 16 bytes. The layout that reads the whole file to its exact end wins, or null for neither.
    /// </summary>
    public static Dictionary<string, Song> Parse(byte[] data)
    {
        if (data == null || data.Length < 8)
            return null;

        int version = BitConverter.ToInt32(data, 0);
        Dictionary<string, Song> songs;
        if (version == OldFileVersion)
            return TryParse(data, false, out songs) ? songs : (TryParse(data, true, out songs) ? songs : null);

        return TryParse(data, true, out songs) ? songs : (TryParse(data, false, out songs) ? songs : null);
    }

    static bool TryParse(byte[] data, bool v1, out Dictionary<string, Song> songs)
    {
        songs = new Dictionary<string, Song>();
        try
        {
            using (BinaryReader reader = new BinaryReader(new MemoryStream(data)))
            {
                reader.ReadInt32();
                int songCount = reader.ReadInt32();
                if (songCount < 0)
                    return false;

                for (int i = 0; i < songCount; ++i)
                {
                    string hash;
                    if (v1)
                        hash = ToHex(reader.ReadBytes(16));
                    else
                    {
                        hash = reader.ReadString().ToUpperInvariant();
                        if (hash.Length != 32)
                            return false;
                    }

                    int scoreCount = reader.ReadByte();
                    Song song = new Song();
                    song.playCount = reader.ReadUInt16();
                    reader.ReadByte();     // Rating

                    for (int s = 0; s < scoreCount; ++s)
                        song.scores.Add(v1 ? ReadScoreV1(reader) : ReadScoreOld(reader));

                    songs[hash] = song;
                }

                return reader.BaseStream.Position == reader.BaseStream.Length;
            }
        }
        catch (EndOfStreamException)
        {
            return false;
        }
    }

    static Score ReadScoreOld(BinaryReader reader)
    {
        Score score = new Score();
        score.instrument = (Instrument)reader.ReadInt16();
        score.difficulty = 3 - Mathf.Clamp(reader.ReadByte(), 0, 3);     // Stored Expert first
        score.percent = reader.ReadByte();
        score.fc = reader.ReadBoolean();
        score.speed = reader.ReadInt16();
        score.stars = reader.ReadByte();
        score.modifiers = reader.ReadByte();
        score.score = reader.ReadInt32();
        return score;
    }

    static Score ReadScoreV1(BinaryReader reader)
    {
        Score score = new Score();
        score.instrument = (Instrument)reader.ReadInt16();
        score.difficulty = Mathf.Clamp(reader.ReadByte(), 0, 3);
        score.percent = reader.ReadByte();
        score.fc = reader.ReadByte() != 0;
        score.speed = reader.ReadUInt16();
        score.stars = reader.ReadByte();
        reader.ReadInt32();     // Modifiers, but v1 has new ones and their bits aren't known, so they aren't shown
        score.score = reader.ReadInt32();
        return score;
    }

    public static string DifficultyName(int difficulty)
    {
        return s_difficultyNames[Mathf.Clamp(difficulty, 0, 3)];
    }

    public static string InstrumentName(Instrument instrument)
    {
        switch (instrument)
        {
            case Instrument.GuitarCoop: return "Co-op Guitar";
            case Instrument.GHLGuitar: return "GHL Guitar";
            case Instrument.GHLBass: return "GHL Bass";
            default: return Enum.IsDefined(typeof(Instrument), instrument) ? instrument.ToString() : "Instrument " + (int)instrument;
        }
    }

    /// <summary>"All HOPOs, Mirror", or null for no modifiers</summary>
    public static string ModifierNames(int modifiers)
    {
        List<string> names = new List<string>();
        for (int bit = 1; bit < s_modifierNames.Length; ++bit)
        {
            if ((modifiers & (1 << bit)) != 0)
                names.Add(s_modifierNames[bit]);
        }
        return names.Count > 0 ? string.Join(", ", names.ToArray()) : null;
    }
}
