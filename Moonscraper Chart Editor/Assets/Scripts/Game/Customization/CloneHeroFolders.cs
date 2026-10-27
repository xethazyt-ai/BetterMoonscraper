// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;

/// <summary>
/// Finds Clone Hero's Custom folder and the color profiles, highways and backgrounds inside it.
/// </summary>
public static class CloneHeroFolders
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif" };
    public static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".m4v", ".ogv", ".avi" };
    static readonly string[] s_profileExtensions = { ".ini" };

    // Clone Hero has used both names for its image background folder
    static readonly string[] s_colorFolders = { "Colors" };
    static readonly string[] s_highwayFolders = { "Highways" };
    static readonly string[] s_backgroundFolders = { "Image Backgrounds", "Backgrounds" };
    static readonly string[] s_videoBackgroundFolders = { "Video Backgrounds" };

    public class Item
    {
        public string name;     // Path inside its folder without the extension, e.g. "Group/My Highway"
        public string path;
    }

    /// <summary>
    /// The usual places Clone Hero keeps its Custom folder: Documents\Clone Hero\Custom for a normal install, or
    /// PlayerData\Custom next to the game for a portable one (which we can't know about, so Browse covers that).
    /// </summary>
    public static string FindCustomFolder()
    {
        List<string> candidates = new List<string>();

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (!string.IsNullOrEmpty(documents))
            candidates.Add(Path.Combine(documents, "Clone Hero"));
        if (!string.IsNullOrEmpty(home))
        {
            candidates.Add(Path.Combine(Path.Combine(home, "Documents"), "Clone Hero"));
            candidates.Add(Path.Combine(Path.Combine(Path.Combine(home, "OneDrive"), "Documents"), "Clone Hero"));
            candidates.Add(Path.Combine(home, ".clonehero"));
        }

        foreach (string folder in candidates)
        {
            string custom = NormaliseCustomFolder(folder);
            if (custom != null)
                return custom;
        }

        return null;
    }

    /// <summary>
    /// Accepts the Custom folder itself, the folder holding it (Documents\Clone Hero) or a portable install folder
    /// (with PlayerData\Custom). Returns the Custom folder, or null if none of those exist.
    /// </summary>
    public static string NormaliseCustomFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder))
            return null;

        try
        {
            folder = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] options = {
                Path.Combine(folder, "Custom"),
                Path.Combine(Path.Combine(folder, "PlayerData"), "Custom"),
                folder,
            };

            foreach (string option in options)
            {
                if (Directory.Exists(option) && LooksLikeCustomFolder(option))
                    return option;
            }

            if (Directory.Exists(folder))
                return folder;      // Picked by hand, so trust it even if it's empty for now
        }
        catch (Exception)
        {
        }

        return null;
    }

    static bool LooksLikeCustomFolder(string folder)
    {
        foreach (string[] names in new[] { s_colorFolders, s_highwayFolders, s_backgroundFolders, s_videoBackgroundFolders })
        {
            foreach (string name in names)
            {
                if (Directory.Exists(Path.Combine(folder, name)))
                    return true;
            }
        }
        return false;
    }

    public static List<Item> ListColorProfiles(string customFolder) { return List(customFolder, s_colorFolders, s_profileExtensions); }
    public static List<Item> ListHighways(string customFolder) { return List(customFolder, s_highwayFolders, ImageExtensions); }

    // Image Backgrounds and Video Backgrounds together, as Clone Hero offers both for its background
    public static List<Item> ListBackgrounds(string customFolder)
    {
        List<Item> items = List(customFolder, s_backgroundFolders, ImageExtensions);
        items.AddRange(List(customFolder, s_videoBackgroundFolders, VideoExtensions));
        items.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return items;
    }

    public static bool IsVideo(string path)
    {
        return !string.IsNullOrEmpty(path) && Array.IndexOf(VideoExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;
    }

    static List<Item> List(string customFolder, string[] subfolders, string[] extensions)
    {
        List<Item> items = new List<Item>();
        if (string.IsNullOrEmpty(customFolder))
            return items;

        foreach (string subfolder in subfolders)
        {
            string root = Path.Combine(customFolder, subfolder);
            if (!Directory.Exists(root))
                continue;

            foreach (string path in AllFiles(root))
            {
                if (Array.IndexOf(extensions, Path.GetExtension(path).ToLowerInvariant()) < 0)
                    continue;

                string relative = path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string name = Path.Combine(Path.GetDirectoryName(relative), Path.GetFileNameWithoutExtension(relative)).Replace('\\', '/');
                items.Add(new Item { name = name, path = path });
            }
        }

        items.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase));
        return items;
    }

    // Group folders are allowed, so look through subfolders too
    static List<string> AllFiles(string folder)
    {
        List<string> files = new List<string>();
        try
        {
            files.AddRange(Directory.GetFiles(folder));
            foreach (string subfolder in Directory.GetDirectories(folder))
                files.AddRange(AllFiles(subfolder));
        }
        catch (Exception)
        {
        }
        return files;
    }
}
