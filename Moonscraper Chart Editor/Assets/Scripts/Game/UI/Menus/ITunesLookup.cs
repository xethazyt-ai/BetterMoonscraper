// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Looks a song up on the iTunes search by its name and artist, for the album, year and genre. Needs no account or key.
/// </summary>
public static class ITunesLookup
{
    const string UserAgent = "MoonscraperChartEditorCollab/1.0 ( https://github.com/xethazyt-ai/Moonscraper-Chart-Editor-Online )";
    const int TimeoutSeconds = 15;

    public class Result
    {
        public string title, artist, album, year, genre;
    }

    // Calls done with the best match, or with null and an error to show
    public static IEnumerator Find(string name, string artist, Action<Result, string> done)
    {
        name = Clean(name);
        artist = Clean(artist);
        if (name.Length == 0)
        {
            done(null, "Type the song's name (and artist) first.");
            yield break;
        }

        string term = (artist + " " + name).Trim();
        string url = "https://itunes.apple.com/search?media=music&entity=song&limit=25&term=" + UnityWebRequest.EscapeURL(term);
        Dictionary<string, object> found = null;
        string error = null;
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.SetRequestHeader("User-Agent", UserAgent);
            request.timeout = TimeoutSeconds;
            yield return request.SendWebRequest();

            if (request.isNetworkError || request.isHttpError)
            {
                error = "Couldn't reach iTunes (" + request.error + "). Try again in a moment.";
            }
            else
            {
                try
                {
                    found = DuetJson.Read(request.downloadHandler.text) as Dictionary<string, object>;
                }
                catch (Exception e)
                {
                    error = "iTunes sent something unexpected. " + e.Message;
                }
            }
        }

        if (found == null)
        {
            done(null, error ?? "iTunes sent nothing back. Try again in a moment.");
            yield break;
        }

        // The song with the same name by the same artist; failing that, the same name; failing that, the first one
        Dictionary<string, object> best = null;
        int bestScore = -1;
        foreach (object item in DuetJson.GetArray(found, "results"))
        {
            Dictionary<string, object> track = item as Dictionary<string, object>;
            if (track == null)
                continue;

            int score = 0;
            if (Same(DuetJson.GetString(track, "trackName", string.Empty), name))
                score += 2;
            if (artist.Length > 0 && Same(DuetJson.GetString(track, "artistName", string.Empty), artist))
                score += 1;
            if (score > bestScore)
            {
                best = track;
                bestScore = score;
            }
        }

        if (best == null)
        {
            done(null, "Couldn't find that song on iTunes. Check the name and artist.");
            yield break;
        }

        Result result = new Result();
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

    static bool Same(string a, string b)
    {
        return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    // Takes out formatting and anything that would break the search
    static string Clean(string text)
    {
        text = RichTextIni.Strip(text ?? string.Empty);
        return text.Replace("\"", string.Empty).Replace("\\", string.Empty).Trim();
    }
}
