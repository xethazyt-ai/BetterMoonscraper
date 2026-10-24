// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Looks a song up online by its name and artist, for the album, year and genre. Tries MusicBrainz (a free, open
/// music database) first, and fills in what it's missing from the iTunes search. Neither needs an account or key.
/// </summary>
public static class MusicBrainzLookup
{
    // MusicBrainz asks every program to say who it is, and to make at most one request a second
    const string UserAgent = "MoonscraperChartEditorCollab/1.0 ( https://github.com/xethazyt-ai/Moonscraper-Chart-Editor-Online )";
    const string Api = "https://musicbrainz.org/ws/2/";
    const int TimeoutSeconds = 15;

    public class Result
    {
        public string title, artist, album, year, genre, source;
    }

    // Calls done with the best match, or with null and an error to show
    public static IEnumerator Find(string name, string artist, Action<Result, string> done)
    {
        name = Clean(name);
        artist = Clean(artist);
        if (name.Length == 0)
        {
            done(null, "Type the song's name first.");
            yield break;
        }

        Result result = null;
        string error = null;
        yield return FindOnMusicBrainz(name, artist, (r, e) => { result = r; error = e; });

        if (result == null || string.IsNullOrEmpty(result.album) || string.IsNullOrEmpty(result.year) || string.IsNullOrEmpty(result.genre))
        {
            Result itunes = null;
            string itunesError = null;
            yield return FindOnITunes(name, artist, (r, e) => { itunes = r; itunesError = e; });

            if (result == null)
            {
                result = itunes;
                error = error ?? itunesError;
            }
            else if (itunes != null)
            {
                if (string.IsNullOrEmpty(result.album)) result.album = itunes.album;
                if (string.IsNullOrEmpty(result.year)) result.year = itunes.year;
                if (string.IsNullOrEmpty(result.genre)) result.genre = itunes.genre;
                result.source += " and iTunes";
            }
        }

        if (result == null)
            done(null, error ?? "Couldn't find that song online. Check the name and artist.");
        else
            done(result, null);
    }

    static IEnumerator FindOnMusicBrainz(string name, string artist, Action<Result, string> done)
    {
        string query = "recording:\"" + name + "\"" + (artist.Length > 0 ? " AND artist:\"" + artist + "\"" : string.Empty);
        Dictionary<string, object> found = null;
        string error = null;
        yield return Get(Api + "recording/?fmt=json&limit=10&query=" + UnityWebRequest.EscapeURL(query), (json, e) => { found = json; error = e; });
        if (found == null)
        {
            done(null, error);
            yield break;
        }

        Dictionary<string, object> recording = null;
        foreach (object item in DuetJson.GetArray(found, "recordings"))
        {
            recording = item as Dictionary<string, object>;
            if (recording != null)
                break;      // Sorted best match first
        }
        if (recording == null)
        {
            done(null, "MusicBrainz doesn't know that song. Check the name and artist.");
            yield break;
        }

        Result result = null;
        yield return DetailsFromRecording(recording, r => result = r);
        done(result, null);
    }

    // The album, year and genre of a MusicBrainz recording (from a search, or looked up by its ID)
    static IEnumerator DetailsFromRecording(Dictionary<string, object> recording, Action<Result> done)
    {
        Result result = new Result();
        result.source = "MusicBrainz";
        result.title = DuetJson.GetString(recording, "title", string.Empty);
        foreach (object credit in DuetJson.GetArray(recording, "artist-credit"))
        {
            Dictionary<string, object> creditObject = credit as Dictionary<string, object>;
            if (creditObject != null)
                result.artist += DuetJson.GetString(creditObject, "name", string.Empty) + DuetJson.GetString(creditObject, "joinphrase", string.Empty);
        }

        string firstDate = DuetJson.GetString(recording, "first-release-date", string.Empty);
        if (firstDate.Length >= 4)
            result.year = firstDate.Substring(0, 4);

        // The album: the earliest official album it's on, otherwise the earliest release of any kind
        Dictionary<string, object> best = null;
        string bestDate = null;
        bool bestIsAlbum = false;
        foreach (object item in DuetJson.GetArray(recording, "releases"))
        {
            Dictionary<string, object> release = item as Dictionary<string, object>;
            if (release == null)
                continue;

            Dictionary<string, object> group = DuetJson.GetObject(release, "release-group");
            bool isAlbum = group != null && DuetJson.GetString(group, "primary-type", string.Empty) == "Album"
                && DuetJson.GetString(release, "status", "Official") == "Official";
            string date = DuetJson.GetString(release, "date", string.Empty);
            if (date.Length == 0)
                date = "9999";

            if (best == null || (isAlbum && !bestIsAlbum) || (isAlbum == bestIsAlbum && string.CompareOrdinal(date, bestDate) < 0))
            {
                best = release;
                bestDate = date;
                bestIsAlbum = isAlbum;
            }
        }

        string releaseGroupId = null;
        if (best != null)
        {
            result.album = DuetJson.GetString(best, "title", null);
            Dictionary<string, object> group = DuetJson.GetObject(best, "release-group");
            if (group != null)
                releaseGroupId = DuetJson.GetString(group, "id", null);
        }

        result.genre = TopName(DuetJson.GetArray(recording, "genres")) ?? TopName(DuetJson.GetArray(recording, "tags"));

        // Genres are mostly filed on the album, so ask for those (a second after the first request, as asked)
        if (string.IsNullOrEmpty(result.genre) && !string.IsNullOrEmpty(releaseGroupId))
        {
            yield return new WaitForSecondsRealtime(1.1f);
            Dictionary<string, object> group = null;
            yield return Get(Api + "release-group/" + UnityWebRequest.EscapeURL(releaseGroupId) + "?fmt=json&inc=genres", (json, e) => group = json);
            if (group != null)
                result.genre = TopName(DuetJson.GetArray(group, "genres"));
        }

        if (!string.IsNullOrEmpty(result.genre))
            result.genre = char.ToUpperInvariant(result.genre[0]) + result.genre.Substring(1);

        done(result);
    }

    static IEnumerator FindOnITunes(string name, string artist, Action<Result, string> done)
    {
        string term = (artist + " " + name).Trim();
        Dictionary<string, object> found = null;
        string error = null;
        yield return Get("https://itunes.apple.com/search?media=music&entity=song&limit=15&term=" + UnityWebRequest.EscapeURL(term), (json, e) => { found = json; error = e; });
        if (found == null)
        {
            done(null, error);
            yield break;
        }

        // The first song with the same name, otherwise the first song
        Dictionary<string, object> best = null;
        foreach (object item in DuetJson.GetArray(found, "results"))
        {
            Dictionary<string, object> track = item as Dictionary<string, object>;
            if (track == null)
                continue;

            if (best == null)
                best = track;
            if (string.Equals(DuetJson.GetString(track, "trackName", string.Empty).Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                best = track;
                break;
            }
        }

        if (best == null)
        {
            done(null, "Couldn't find that song online. Check the name and artist.");
            yield break;
        }

        Result result = new Result();
        result.source = "iTunes";
        result.title = DuetJson.GetString(best, "trackName", string.Empty);
        result.artist = DuetJson.GetString(best, "artistName", string.Empty);
        result.album = DuetJson.GetString(best, "collectionName", null);
        if (result.album != null && result.album.EndsWith(" - Single"))
            result.album = result.album.Substring(0, result.album.Length - " - Single".Length);
        string date = DuetJson.GetString(best, "releaseDate", string.Empty);
        if (date.Length >= 4)
            result.year = date.Substring(0, 4);
        result.genre = DuetJson.GetString(best, "primaryGenreName", null);
        done(result, null);
    }

    // Identifies the song by its sound: a Chromaprint fingerprint of the audio file, looked up on AcoustID (which links
    // fingerprints to MusicBrainz recordings). Only a match AcoustID is at least 90% sure of counts.
    public static bool canMatchAudio { get { return AcoustIdClientKey.Length > 0 && File.Exists(fpcalcPath); } }

    // AcoustID's free key for this app (https://acoustid.org/new-application). Apps ship with theirs, like this.
    const string AcoustIdClientKey = "PEVuwLfy74";
    const double MinAudioScore = 0.9;

    static string fpcalcPath
    {
        get { return Path.Combine(Path.GetDirectoryName(Application.dataPath), Path.Combine("fpcalc", "fpcalc.exe")); }
    }

    public static IEnumerator FindByAudio(string audioPath, Action<Result, string> done)
    {
        if (!canMatchAudio)
        {
            done(null, "Audio matching isn't in this build.");
            yield break;
        }
        if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
        {
            done(null, "Load the song's audio first (Song Properties > Audio).");
            yield break;
        }

        // Fingerprinting decodes the first two minutes of audio, which takes a moment, so it runs off the main thread
        string fingerprint = null, fingerprintError = null;
        int duration = 0;
        string exe = fpcalcPath;    // Unity's paths can only be read on the main thread
        Thread thread = new Thread(() =>
        {
            try
            {
                Fingerprint(exe, audioPath, out fingerprint, out duration);
            }
            catch (Exception e)
            {
                fingerprintError = e.Message;
            }
        });
        thread.IsBackground = true;
        thread.Start();
        while (thread.IsAlive)
            yield return null;

        if (fingerprint == null)
        {
            done(null, "Couldn't fingerprint the audio. " + fingerprintError);
            yield break;
        }

        Dictionary<string, object> found = null;
        string error = null;
        string url = "https://api.acoustid.org/v2/lookup";
        yield return Send(url, () =>
        {
            WWWForm form = new WWWForm();
            form.AddField("client", AcoustIdClientKey);
            form.AddField("meta", "recordingids");
            form.AddField("duration", duration.ToString(CultureInfo.InvariantCulture));
            form.AddField("fingerprint", fingerprint);
            return UnityWebRequest.Post(url, form);
        }, (json, e) => { found = json; error = e; });
        if (found == null)
        {
            done(null, error);
            yield break;
        }

        string recordingId = null;
        double bestScore = 0;
        foreach (object item in DuetJson.GetArray(found, "results"))
        {
            Dictionary<string, object> match = item as Dictionary<string, object>;
            if (match == null)
                continue;

            double score = DuetJson.GetNumber(match, "score", 0);
            foreach (object recording in DuetJson.GetArray(match, "recordings"))
            {
                Dictionary<string, object> recordingObject = recording as Dictionary<string, object>;
                string id = recordingObject != null ? DuetJson.GetString(recordingObject, "id", null) : null;
                if (id != null && score > bestScore)
                {
                    bestScore = score;
                    recordingId = id;
                }
            }
        }

        if (recordingId == null || bestScore < MinAudioScore)
        {
            done(null, recordingId == null
                ? "No exact audio match. This recording may not be in AcoustID yet."
                : "No exact audio match (best was " + Math.Round(bestScore * 100) + "% sure), so nothing was filled in.");
            yield break;
        }

        yield return new WaitForSecondsRealtime(1.1f);     // MusicBrainz asks for a second between requests
        Dictionary<string, object> details = null;
        yield return Get(Api + "recording/" + UnityWebRequest.EscapeURL(recordingId) + "?fmt=json&inc=artist-credits+releases+release-groups+genres+tags",
            (json, e) => { details = json; error = e; });
        if (details == null)
        {
            done(null, error);
            yield break;
        }

        Result result = null;
        yield return DetailsFromRecording(details, r => result = r);
        result.source = "an audio match (" + Math.Round(bestScore * 100) + "% sure)";

        if (string.IsNullOrEmpty(result.genre) && !string.IsNullOrEmpty(result.title))
        {
            Result itunes = null;
            yield return FindOnITunes(Clean(result.title), Clean(result.artist), (r, e) => itunes = r);
            if (itunes != null)
                result.genre = itunes.genre;
        }

        done(result, null);
    }

    static void Fingerprint(string exe, string audioPath, out string fingerprint, out int duration)
    {
        fingerprint = null;
        duration = 0;

        ProcessStartInfo start = new ProcessStartInfo(exe, "-json -length 120 \"" + audioPath + "\"");
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        using (Process process = Process.Start(start))
        {
            string output = process.StandardOutput.ReadToEnd();
            string errors = process.StandardError.ReadToEnd();
            if (!process.WaitForExit(60000))
            {
                process.Kill();
                throw new TimeoutException("fpcalc took too long");
            }

            Dictionary<string, object> json = output.Trim().Length > 0 ? DuetJson.Read(output.Trim()) as Dictionary<string, object> : null;
            if (json == null)
                throw new FormatException(errors.Trim().Length > 0 ? errors.Trim() : "fpcalc gave no fingerprint");

            fingerprint = DuetJson.GetString(json, "fingerprint", null);
            duration = (int)Math.Round(DuetJson.GetNumber(json, "duration", 0));
            if (string.IsNullOrEmpty(fingerprint))
                throw new FormatException("fpcalc gave no fingerprint");
        }
    }

    static string TopName(List<object> tags)
    {
        string name = null;
        double bestCount = double.MinValue;
        foreach (object item in tags)
        {
            Dictionary<string, object> tag = item as Dictionary<string, object>;
            if (tag == null)
                continue;

            double count = DuetJson.GetNumber(tag, "count", 0);
            if (count > bestCount)
            {
                bestCount = count;
                name = DuetJson.GetString(tag, "name", null);
            }
        }
        return name;
    }

    // Takes out formatting and anything that would break the search
    static string Clean(string text)
    {
        text = RichTextIni.Strip(text ?? string.Empty);
        return text.Replace("\"", string.Empty).Replace("\\", string.Empty).Trim();
    }

    static IEnumerator Get(string url, Action<Dictionary<string, object>, string> done)
    {
        return Send(url, () => UnityWebRequest.Get(url), done);
    }

    static IEnumerator Send(string url, Func<UnityWebRequest> makeRequest, Action<Dictionary<string, object>, string> done)
    {
        for (int attempt = 0; ; ++attempt)
        {
            using (UnityWebRequest request = makeRequest())
            {
                request.SetRequestHeader("User-Agent", UserAgent);
                request.SetRequestHeader("Accept", "application/json");
                request.timeout = TimeoutSeconds;
                yield return request.SendWebRequest();

                // MusicBrainz answers 503 when it's busy or asked too often. It's fine again after a short wait.
                if (request.responseCode == 503 && attempt < 2)
                {
                    yield return new WaitForSecondsRealtime(1.5f * (attempt + 1));
                    continue;
                }

                if (request.isNetworkError || request.isHttpError)
                {
                    done(null, "Couldn't reach " + new Uri(url).Host + " (" + request.error + "). Try again in a moment.");
                    yield break;
                }

                try
                {
                    done(DuetJson.Read(request.downloadHandler.text) as Dictionary<string, object>, null);
                }
                catch (Exception e)
                {
                    done(null, new Uri(url).Host + " sent something unexpected. " + e.Message);
                }
                yield break;
            }
        }
    }
}
