// See LICENSE in project root for license information.

using System.IO;
using MoonscraperChartEditor.Song;
using MoonscraperEngine;

/// <summary>
/// Opening a chart picks up what Clone Hero would find next to it: audio named song.ogg, guitar.ogg, drums_1.ogg and
/// so on for any stream the chart doesn't point to itself, and the song name, artist and other details from song.ini
/// where the chart leaves them empty.
/// </summary>
public static class SongFolderDefaults
{
    public static void Apply(Song song, string chartPath)
    {
        string directory;
        try { directory = Path.GetDirectoryName(Path.GetFullPath(chartPath)); }
        catch (System.Exception) { return; }
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return;

        foreach (Song.AudioInstrument audio in EnumX<Song.AudioInstrument>.Values)
        {
            string current = song.GetAudioLocation(audio);
            if (!string.IsNullOrEmpty(current) && File.Exists(current))
                continue;
            string found = FindAudio(directory, audio);
            if (found != null)
                song.SetAudioLocation(audio, found);
        }

        FillFromIni(song);
    }

    static string[] FileNames(Song.AudioInstrument audio)
    {
        switch (audio)
        {
            case Song.AudioInstrument.Drum: return new[] { "drums", "drums_1" };
            case Song.AudioInstrument.Vocals: return new[] { "vocals", "vocals_1" };
            default: return new[] { audio.ToString().ToLowerInvariant() };
        }
    }

    static string FindAudio(string directory, Song.AudioInstrument audio)
    {
        foreach (string name in FileNames(audio))
        {
            foreach (string extension in Globals.validAudioExtensions)
            {
                string path = Path.Combine(directory, name + extension);
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
        }
        return null;
    }

    static void FillFromIni(Song song)
    {
        Metadata metaData = song.metaData;
        if (string.IsNullOrEmpty(metaData.name)) metaData.name = Read(song, "name");
        if (string.IsNullOrEmpty(metaData.artist)) metaData.artist = Read(song, "artist");
        if (string.IsNullOrEmpty(metaData.album)) metaData.album = Read(song, "album");
        if (string.IsNullOrEmpty(metaData.charter))
        {
            string charter = Read(song, "charter");
            metaData.charter = charter.Length > 0 ? charter : Read(song, "frets");
        }
        if (string.IsNullOrEmpty(metaData.year))
            metaData.year = System.Text.RegularExpressions.Regex.Replace(Read(song, "year"), @"\D", "");

        // The chart's genre is "rock" unless someone set it, so song.ini's wins over that default
        string genre = Read(song, "genre");
        if (genre.Length > 0 && (string.IsNullOrEmpty(metaData.genre) || metaData.genre == "rock"))
            metaData.genre = genre;
    }

    static string Read(Song song, string key)
    {
        string value = RichTextIni.ReadIni(song.iniProperties, key);
        return string.IsNullOrEmpty(value) ? string.Empty : RichTextIni.Strip(value).Trim();
    }
}
