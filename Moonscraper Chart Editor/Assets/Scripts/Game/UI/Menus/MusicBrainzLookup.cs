// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
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

        result.genre = TopName(DuetJson.GetArray(recording, "tags"));

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

        done(result, null);
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
        for (int attempt = 0; ; ++attempt)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
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
