// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Moonscraper Chart Editor became BetterMoonscraper, which moves where Unity keeps its settings: the files in
/// AppData\LocalLow (config.ini, controls.json, Customization, pattern blueprints, Collab Logs...) and the
/// settings in the registry (Collab name and color, sliders, theme...). The first time BetterMoonscraper starts,
/// this copies them over from the old place, so nothing has to be set up again. The old copies are left alone.
/// </summary>
public static class RenameMigration
{
    const string OldCompany = "FireFox Dev";
    const string OldProduct = "Moonscraper Chart Editor";
    const string DoneFile = "Settings copied from Moonscraper.txt";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void CopyOldSettings()
    {
#if !UNITY_EDITOR
        try
        {
            string newFolder = Application.persistentDataPath;
            string marker = Path.Combine(newFolder, DoneFile);
            if (File.Exists(marker))
                return;

            DirectoryInfo company = Directory.GetParent(newFolder);
            DirectoryInfo localLow = company != null ? company.Parent : null;
            string oldFolder = localLow != null ? Path.Combine(Path.Combine(localLow.FullName, OldCompany), OldProduct) : null;

            if (oldFolder != null && Directory.Exists(oldFolder) && !SamePath(oldFolder, newFolder))
            {
                CopyFolder(oldFolder, newFolder);
                Debug.Log("Copied settings files from " + oldFolder);
            }

            CopyRegistrySettings();

            Directory.CreateDirectory(newFolder);
            File.WriteAllText(marker, "BetterMoonscraper copied its settings from Moonscraper Chart Editor's on " + DateTime.Now + ". Delete this file to copy them again.");
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't copy settings over from Moonscraper Chart Editor. " + e);
        }
#endif
    }

    // Copies files that aren't there yet; anything BetterMoonscraper already has wins. Old crash logs are skipped.
    static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string file in Directory.GetFiles(from))
        {
            string name = Path.GetFileName(file);
            if (name.StartsWith("output_log", StringComparison.OrdinalIgnoreCase) || name.StartsWith("Player", StringComparison.OrdinalIgnoreCase))
                continue;

            string target = Path.Combine(to, name);
            if (!File.Exists(target))
                File.Copy(file, target);
        }

        foreach (string folder in Directory.GetDirectories(from))
            CopyFolder(folder, Path.Combine(to, Path.GetFileName(folder)));
    }

    static bool SamePath(string a, string b)
    {
        return string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
    }

    // Unity keeps PlayerPrefs on Windows under HKCU\Software\<company>\<product>, one value per setting
    static void CopyRegistrySettings()
    {
#if UNITY_STANDALONE_WIN
        string oldPath = @"Software\" + OldCompany + @"\" + OldProduct;
        string newPath = @"Software\" + Application.companyName + @"\" + Application.productName;
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
            return;

        using (Microsoft.Win32.RegistryKey oldKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(oldPath))
        {
            if (oldKey == null)
                return;

            using (Microsoft.Win32.RegistryKey newKey = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(newPath))
            {
                int copied = 0;
                foreach (string name in oldKey.GetValueNames())
                {
                    if (newKey.GetValue(name) != null)
                        continue;
                    newKey.SetValue(name, oldKey.GetValue(name), oldKey.GetValueKind(name));
                    ++copied;
                }
                Debug.Log("Copied " + copied + " settings from Moonscraper Chart Editor's registry key");
            }
        }
#endif
    }
}
