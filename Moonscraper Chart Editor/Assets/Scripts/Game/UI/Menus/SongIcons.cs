// See LICENSE in project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Clone Hero's source and charter icons, the ones song.ini's "icon" picks: the official list
/// (gitlab.com/clonehero/sources, shown at sources-ca135f.gitlab.io) and each icon's picture. The list and the
/// pictures are downloaded when first needed and kept, so they also work offline afterwards, and new icons show up
/// by themselves.
/// </summary>
public static class SongIcons
{
    static readonly string[] s_sites = { "https://sources-ca135f.gitlab.io/", "https://clonehero.gitlab.io/sources/" };
    // The pictures: the site's copies, else the originals in the repository
    static readonly string[] s_iconSites =
    {
        "https://sources-ca135f.gitlab.io/icons/", "https://clonehero.gitlab.io/sources/icons/", "https://gitlab.com/clonehero/sources/-/raw/master/public/icons/",
    };
    const int TimeoutSeconds = 20;
    const int MaxIconBytes = 1024 * 1024;   // The guidelines ask for 64x64 or 128x128, so anything this big isn't an icon

    public class Icon
    {
        public string id;       // What goes in song.ini, the file name without .png
        public string file;
        public string sha1;
        public string source;   // The full name Clone Hero shows, if the list has one
        public string localPath;    // A picture in the Charter Icons folder, rather than one of Clone Hero's
    }

    public static List<Icon> icons { get; private set; }
    public static string status { get; private set; }
    public static bool isLoading { get; private set; }

    static readonly Dictionary<string, Texture2D> s_textures = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
    static readonly HashSet<string> s_downloading = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    static string folder { get { return Path.Combine(CustomizationPrefs.storageFolder, "Game Icons"); } }

    /// <summary>
    /// "Charter Icons" next to BetterMoonscraper.exe: the user's own icon pictures (say, ones collected from their
    /// songs), searchable alongside Clone Hero's. Named as they'd go in song.ini, so "xethaz.png" is icon = xethaz.
    /// </summary>
    public static string localFolder
    {
        get
        {
            string appFolder = Path.GetDirectoryName(Application.dataPath);     // dataPath is <app>/<name>_Data
            foreach (string candidate in new[] { Path.Combine(appFolder, "Charter Icons"), Path.Combine(CustomizationPrefs.storageFolder, "Charter Icons") })
            {
                try
                {
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Couldn't make the charter icons folder " + candidate + ". " + e.Message);
                }
            }
            return null;
        }
    }

    // Downloads run on an object of their own, so closing the menu mid-download doesn't leave one half done
    class Runner : MonoBehaviour { }
    static Runner s_runner;

    public static void Run(IEnumerator routine)
    {
        if (!s_runner)
        {
            GameObject go = new GameObject("Song Icons");
            UnityEngine.Object.DontDestroyOnLoad(go);
            s_runner = go.AddComponent<Runner>();
        }
        s_runner.StartCoroutine(routine);
    }

    /// <summary>
    /// Gets the list, from the site or failing that the copy from last time. Calls done either way.
    /// </summary>
    public static IEnumerator LoadList(Action done)
    {
        if (icons != null || isLoading)
        {
            while (isLoading)
                yield return null;
            if (done != null)
                done();
            yield break;
        }

        isLoading = true;
        status = "Getting Clone Hero's icon list...";
        string iconsJson = null, sourcesText = null;
        foreach (string site in s_sites)
        {
            string[] got = new string[2];
            yield return Download(site + "icons.json", text => got[0] = text);
            if (got[0] == null)
                continue;
            yield return Download(site + "sources.txt", text => got[1] = text);
            iconsJson = got[0];
            sourcesText = got[1] ?? string.Empty;
            break;
        }

        string listPath = Path.Combine(folder, "icons.json");
        string sourcesPath = Path.Combine(folder, "sources.txt");
        try
        {
            if (iconsJson != null && Parse(iconsJson, sourcesText) != null)
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(listPath, iconsJson);
                File.WriteAllText(sourcesPath, sourcesText);
            }
            else if (File.Exists(listPath))
            {
                iconsJson = File.ReadAllText(listPath);
                sourcesText = File.Exists(sourcesPath) ? File.ReadAllText(sourcesPath) : string.Empty;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't keep or read the icon list. " + e.Message);
        }

        icons = iconsJson != null ? Parse(iconsJson, sourcesText) : null;
        int local = AddLocalIcons();
        status = icons == null ? "Couldn't get Clone Hero's icon list. Check the internet connection and open this again."
            : icons.Count + " icons" + (local > 0 ? " (" + local + " charter icons)" : string.Empty) + ". Type to search by name or source.";
        isLoading = false;     // With no list, the next call tries again
        if (done != null)
            done();
    }

    // Community charter icons that come with BetterMoonscraper (Assets/StreamingAssets/Charter Icons)
    static string bundledFolder { get { return Path.Combine(Application.streamingAssetsPath, "Charter Icons"); } }

    // Pictures in the Charter Icons folders. Clone Hero's own icon wins a name both have, since that's what the game
    // shows for it. Works without the internet too, as the only icons then.
    static int AddLocalIcons()
    {
        HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (icons != null)
        {
            foreach (Icon icon in icons)
                taken.Add(icon.id);
        }

        // The user's own folder first, then the ones that come with BetterMoonscraper
        List<Icon> found = new List<Icon>();
        foreach (string local in new[] { localFolder, bundledFolder })
        {
            if (local == null || !Directory.Exists(local))
                continue;
            try
            {
                foreach (string path in Directory.GetFiles(local))
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                        continue;
                    string id = Path.GetFileNameWithoutExtension(path);
                    if (id.Length == 0 || !taken.Add(id))
                        continue;
                    found.Add(new Icon { id = id, file = Path.GetFileName(path), sha1 = string.Empty, source = "Charter icon", localPath = path });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't read the charter icons in " + local + ". " + e.Message);
            }
        }

        if (found.Count == 0)
            return 0;
        if (icons == null)
            icons = new List<Icon>();
        icons.AddRange(found);
        return found.Count;
    }

    static List<Icon> Parse(string iconsJson, string sourcesText)
    {
        List<object> list;
        try
        {
            list = DuetJson.Read(iconsJson) as List<object>;
        }
        catch (Exception)
        {
            return null;
        }
        if (list == null)
            return null;

        // Lines like: a7x '=' A7X Hero
        Dictionary<string, string> sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in (sourcesText ?? string.Empty).Split('\n'))
        {
            int split = line.IndexOf("'='", StringComparison.Ordinal);
            if (split > 0)
                sources[line.Substring(0, split).Trim()] = line.Substring(split + 3).Trim();
        }

        List<Icon> result = new List<Icon>();
        foreach (object item in list)
        {
            Dictionary<string, object> entry = item as Dictionary<string, object>;
            string file = entry != null ? Path.GetFileName(DuetJson.GetString(entry, "name", string.Empty)) : string.Empty;
            if (!file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;

            Icon icon = new Icon();
            icon.file = file;
            icon.id = file.Substring(0, file.Length - 4);
            icon.sha1 = DuetJson.GetString(entry, "sha1", string.Empty).ToLowerInvariant();
            string source;
            icon.source = sources.TryGetValue(icon.id, out source) ? source : null;
            result.Add(icon);
        }
        return result;
    }

    /// <summary>
    /// Icons whose name or source has all the typed words, best first: the name itself, then names starting with
    /// it, then the rest
    /// </summary>
    public static List<Icon> Search(string query, int max)
    {
        List<Icon> found = new List<Icon>();
        if (icons == null)
            return found;

        query = (query ?? string.Empty).Trim().ToLowerInvariant();
        string[] words = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        List<KeyValuePair<int, Icon>> scored = new List<KeyValuePair<int, Icon>>();
        foreach (Icon icon in icons)
        {
            string id = icon.id.ToLowerInvariant();
            string text = id + " " + (icon.source ?? string.Empty).ToLowerInvariant();
            bool all = true;
            foreach (string word in words)
            {
                if (text.IndexOf(word, StringComparison.Ordinal) < 0)
                {
                    all = false;
                    break;
                }
            }
            if (!all)
                continue;

            int score = id == query ? 0 : id.StartsWith(query, StringComparison.Ordinal) ? 1 : 2;
            scored.Add(new KeyValuePair<int, Icon>(score, icon));
        }

        scored.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : string.Compare(a.Value.id, b.Value.id, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < scored.Count && found.Count < max; ++i)
            found.Add(scored[i].Value);
        return found;
    }

    public static Icon Find(string id)
    {
        if (icons == null || string.IsNullOrEmpty(id))
            return null;
        return icons.Find(icon => string.Equals(icon.id, id.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The icon's picture, from memory, the kept copy or the site. Calls done with null if it can't be had.
    /// </summary>
    public static IEnumerator GetTexture(Icon icon, Action<Texture2D> done)
    {
        Texture2D texture;
        if (s_textures.TryGetValue(icon.file, out texture) && texture)
        {
            done(texture);
            yield break;
        }

        while (s_downloading.Contains(icon.file))
            yield return null;
        if (s_textures.TryGetValue(icon.file, out texture) && texture)
        {
            done(texture);
            yield break;
        }

        // Kept under the list's hash, so an icon that's changed is fetched again. The hash is of the original in the
        // repository; the site's copies are shrunk with pngquant, so a download is only checked for being a PNG.
        string path = icon.localPath ?? Path.Combine(Path.Combine(folder, "icons"), icon.sha1.Length == 40 ? icon.sha1 + ".png" : icon.file);
        byte[] bytes = null;
        try
        {
            if (File.Exists(path))
                bytes = File.ReadAllBytes(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't read the kept icon " + icon.file + ". " + e.Message);
        }

        if (bytes == null && icon.localPath == null)
        {
            s_downloading.Add(icon.file);
            string escaped = UnityWebRequest.EscapeURL(icon.file).Replace("+", "%20");
            foreach (string site in s_iconSites)
            {
                byte[] got = null;
                yield return DownloadBytes(site + escaped, data => got = data);
                if (IsPng(got))
                {
                    bytes = got;
                    break;
                }
            }
            s_downloading.Remove(icon.file);

            if (bytes != null)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    File.WriteAllBytes(path, bytes);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Couldn't keep the icon " + icon.file + ". " + e.Message);
                }
            }
        }

        texture = null;
        if (bytes != null)
        {
            texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (texture.LoadImage(bytes))
            {
                texture.wrapMode = TextureWrapMode.Clamp;
                s_textures[icon.file] = texture;
            }
            else
            {
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }
        done(texture);
    }

    static IEnumerator Download(string url, Action<string> done)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = TimeoutSeconds;
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError)
            {
                Debug.LogWarning("Couldn't get " + url + ": " + request.error);
                done(null);
            }
            else
            {
                done(request.downloadHandler.text);
            }
        }
    }

    static IEnumerator DownloadBytes(string url, Action<byte[]> done)
    {
        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            request.timeout = TimeoutSeconds;
            yield return request.SendWebRequest();
            byte[] data = request.isNetworkError || request.isHttpError ? null : request.downloadHandler.data;
            done(data != null && data.Length > 0 && data.Length <= MaxIconBytes ? data : null);
        }
    }

    static bool IsPng(byte[] data)
    {
        return data != null && data.Length > 8 && data[0] == 0x89 && data[1] == 'P' && data[2] == 'N' && data[3] == 'G';
    }
}
