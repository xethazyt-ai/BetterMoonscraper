// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// The color profile, highway, background and note skin picked in Settings > Customize. Everything here is local to
/// this computer and never sent to anyone in Collab Mode.
///
/// Choices live in PlayerPrefs and the chosen files are copied into Application.persistentDataPath, which every
/// Moonscraper build on this computer shares. So a new build unzipped into a new folder still has them, even if the
/// original files are moved or deleted.
/// </summary>
public static class CustomizationPrefs
{
    public enum Kind
    {
        ColorProfile,
        Highway,
        Background,
        NoteSkin,
    }

    const string PrefPrefix = "Customize.";
    const string CloneHeroFolderKey = PrefPrefix + "CloneHeroFolder";

    static readonly string[] s_noteSkinExtensions = { ".png", ".jpg", ".jpeg", ".dds" };

    public static string storageFolder
    {
        get { return Path.Combine(Application.persistentDataPath, "Customization"); }
    }

    public static string cloneHeroFolder
    {
        get { return PlayerPrefs.GetString(CloneHeroFolderKey, string.Empty); }
        set { PlayerPrefs.SetString(CloneHeroFolderKey, value ?? string.Empty); PlayerPrefs.Save(); }
    }

    /// <summary>
    /// Where the file (or note skin folder) was picked from, to show it as selected in Settings
    /// </summary>
    public static string GetSource(Kind kind)
    {
        return PlayerPrefs.GetString(PrefPrefix + kind + ".Source", string.Empty);
    }

    /// <summary>
    /// Our own saved copy of the chosen file or folder, or null when using Moonscraper's default
    /// </summary>
    public static string GetStored(Kind kind)
    {
        string stored = PlayerPrefs.GetString(PrefPrefix + kind + ".Stored", string.Empty);
        if (string.IsNullOrEmpty(stored))
            return null;

        stored = Path.Combine(storageFolder, stored);
        return File.Exists(stored) || Directory.Exists(stored) ? stored : null;
    }

    /// <summary>
    /// Copies the chosen file (or note skin folder) into our storage and remembers it. Returns the copy's path.
    /// </summary>
    public static string Store(Kind kind, string source)
    {
        string kindFolder = Path.Combine(storageFolder, kind.ToString());
        string tempFolder = kindFolder + ".new";

        DeleteFolder(tempFolder);
        Directory.CreateDirectory(tempFolder);

        string name;
        if (kind == Kind.NoteSkin)
        {
            name = "Skin";
            CopyImages(source, Path.Combine(tempFolder, name));
        }
        else
        {
            name = Path.GetFileName(source);
            File.Copy(source, Path.Combine(tempFolder, name), true);
        }

        // Only replace the old copy once the new one is complete
        DeleteFolder(kindFolder);
        Directory.Move(tempFolder, kindFolder);

        PlayerPrefs.SetString(PrefPrefix + kind + ".Source", source);
        PlayerPrefs.SetString(PrefPrefix + kind + ".Stored", Path.Combine(kind.ToString(), name));
        PlayerPrefs.Save();

        return Path.Combine(kindFolder, name);
    }

    public static void Clear(Kind kind)
    {
        PlayerPrefs.DeleteKey(PrefPrefix + kind + ".Source");
        PlayerPrefs.DeleteKey(PrefPrefix + kind + ".Stored");
        PlayerPrefs.Save();

        try
        {
            DeleteFolder(Path.Combine(storageFolder, kind.ToString()));
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't delete the old " + kind + " copy. " + e.Message);
        }
    }

    // Note skins are only images, so leave anything else in the folder behind
    static void CopyImages(string sourceFolder, string destFolder)
    {
        Directory.CreateDirectory(destFolder);

        foreach (string file in Directory.GetFiles(sourceFolder))
        {
            if (Array.IndexOf(s_noteSkinExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                File.Copy(file, Path.Combine(destFolder, Path.GetFileName(file)), true);
        }

        foreach (string folder in Directory.GetDirectories(sourceFolder))
            CopyImages(folder, Path.Combine(destFolder, Path.GetFileName(folder)));
    }

    static void DeleteFolder(string folder)
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, true);
    }
}
