// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;
using MoonscraperEngine.Audio;

/// <summary>
/// The clap's own volume (Clap Settings menu, can go past 100% to make it louder) and a clap sound of your own
/// (Settings > Customize). Both only change this computer. The sample is rebuilt with the volume baked in, since a
/// sample's own volume can't go above full.
/// Clap sounds are picked from the "Clap Sounds" folder next to BetterMoonscraper.exe, which starts with the default
/// clap in it, and stay there: the clap is played from the picked file itself.
/// </summary>
public static class ClapSound
{
    const string PrefVolume = "Audio.ClapVolumePercent";
    const string PrefCustomName = "Customize.ClapSoundName";
    const string PrefCustomPath = "Customize.ClapSoundPath";
    public const string DefaultClapName = "Default clap.wav";
    public const int MinVolumePercent = 0, MaxVolumePercent = 400, DefaultVolumePercent = 100;
    public static readonly string[] Extensions = { "wav", "ogg", "mp3", "opus" };

    static int? s_volumePercent;

    public static int volumePercent
    {
        get
        {
            if (!s_volumePercent.HasValue)
                s_volumePercent = Mathf.Clamp(PlayerPrefs.GetInt(PrefVolume, DefaultVolumePercent), MinVolumePercent, MaxVolumePercent);
            return s_volumePercent.Value;
        }
        set
        {
            int clamped = Mathf.Clamp(value, MinVolumePercent, MaxVolumePercent);
            if (clamped == volumePercent)
                return;
            s_volumePercent = clamped;
            PlayerPrefs.SetInt(PrefVolume, clamped);
            PlayerPrefs.Save();
            Reload();
        }
    }

    static string customFolder { get { return Path.Combine(CustomizationPrefs.storageFolder, "Clap"); } }   // Copies made by older builds

    static string s_soundsFolder;

    /// <summary>
    /// The "Clap Sounds" folder next to BetterMoonscraper.exe, made with the default clap in it the first time it's
    /// asked for. If the program's folder can't be written to, it goes in Moonscraper's own data folder instead.
    /// </summary>
    public static string soundsFolder
    {
        get
        {
            if (s_soundsFolder != null && Directory.Exists(s_soundsFolder))
                return s_soundsFolder;

            string appFolder = Path.GetDirectoryName(Application.dataPath);     // dataPath is <app>/<name>_Data
            foreach (string folder in new[] { Path.Combine(appFolder, "Clap Sounds"), Path.Combine(CustomizationPrefs.storageFolder, "Clap Sounds") })
            {
                try
                {
                    Directory.CreateDirectory(folder);
                    string defaultCopy = Path.Combine(folder, DefaultClapName);
                    if (!File.Exists(defaultCopy) && File.Exists(defaultClapPath))
                        File.Copy(defaultClapPath, defaultCopy);
                    s_soundsFolder = folder;
                    return folder;
                }
                catch (Exception e)
                {
                    Debug.LogWarning("Couldn't make the clap sounds folder " + folder + ". " + e.Message);
                }
            }
            return null;
        }
    }

    static string defaultClapPath { get { return Path.Combine(Application.streamingAssetsPath, "SFX/clap.wav"); } }

    /// <summary>
    /// The picked clap sound, or null for the default (or Custom Resources) one
    /// </summary>
    public static string customPath
    {
        get
        {
            string picked = PlayerPrefs.GetString(PrefCustomPath, string.Empty);
            if (picked.Length > 0 && File.Exists(picked))
                return picked;

            try
            {
                if (!Directory.Exists(customFolder))
                    return null;
                foreach (string file in Directory.GetFiles(customFolder, "clap.*"))
                    return file;
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't look for the custom clap sound. " + e.Message);
            }
            return null;
        }
    }

    // The name of the picked file, to show in Settings
    public static string customName
    {
        get { return customPath != null ? PlayerPrefs.GetString(PrefCustomName, "Custom") : null; }
    }

    /// <summary>
    /// Uses this sound file for the clap, played from where it is. Picking the folder's default clap goes back to
    /// the default (so Custom Resources still apply). Returns an error, or null.
    /// </summary>
    public static string SetCustom(string sourcePath)
    {
        try
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (Array.IndexOf(Extensions, extension.TrimStart('.')) < 0)
                return Path.GetFileName(sourcePath) + " isn't a sound file Moonscraper can play.";

            if (soundsFolder != null && string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(Path.Combine(soundsFolder, DefaultClapName)), StringComparison.OrdinalIgnoreCase))
            {
                ClearCustom();
                return null;
            }

            int sampleRate;
            float[] check = AudioManager.DecodeMono(sourcePath, out sampleRate);
            if (check == null || check.Length == 0)
                return "Couldn't read " + Path.GetFileName(sourcePath) + " as a sound.";

            RemoveCustomFiles();
            PlayerPrefs.SetString(PrefCustomPath, Path.GetFullPath(sourcePath));
            PlayerPrefs.SetString(PrefCustomName, Path.GetFileName(sourcePath));
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            return "Couldn't use " + Path.GetFileName(sourcePath) + ". " + e.Message;
        }

        Reload();
        return null;
    }

    public static void ClearCustom()
    {
        try
        {
            RemoveCustomFiles();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't remove the custom clap sound. " + e.Message);
        }
        PlayerPrefs.DeleteKey(PrefCustomName);
        PlayerPrefs.DeleteKey(PrefCustomPath);
        PlayerPrefs.Save();
        Reload();
    }

    static void RemoveCustomFiles()
    {
        if (!Directory.Exists(customFolder))
            return;
        foreach (string file in Directory.GetFiles(customFolder, "clap.*"))
            File.Delete(file);
    }

    /// <summary>
    /// Loads the clap the editor plays: the custom sound if one is picked, otherwise skinPath (Custom Resources or
    /// the built-in clap), at the clap volume
    /// </summary>
    public static OneShotSampleStream LoadSample(string skinPath, int maxSimultaneousPlaybacks)
    {
        if (s_soundsFolder == null && soundsFolder == null)
            Debug.LogWarning("No Clap Sounds folder");     // Made at launch, so it's there to drop sounds into
        string path = customPath ?? skinPath;
        if (volumePercent == DefaultVolumePercent && path == skinPath)
            return AudioManager.LoadSampleStream(path, maxSimultaneousPlaybacks);

        int sampleRate;
        float[] samples = AudioManager.DecodeMono(path, out sampleRate);
        if (samples == null || samples.Length == 0 || sampleRate <= 0)
        {
            Debug.LogWarning("Couldn't read the clap sound " + path + ", so the default clap plays");
            return AudioManager.LoadSampleStream(skinPath, maxSimultaneousPlaybacks);
        }

        return AudioManager.LoadSampleStream(ToWav(samples, sampleRate, volumePercent / 100f), maxSimultaneousPlaybacks);
    }

    // Picks up a new volume or sound straight away
    public static void Reload()
    {
        if (ChartEditor.InstanceExists && ChartEditor.Instance.sfxAudioStreams != null && SkinManager.Instance != null)
            ChartEditor.Instance.sfxAudioStreams.ReloadSound(SkinKeys.clap, SkinManager.Instance.currentSkin);
    }

    // 16-bit mono PCM; anything pushed past full by the gain is clipped
    static byte[] ToWav(float[] samples, int sampleRate, float gain)
    {
        int dataBytes = samples.Length * 2;
        using (MemoryStream stream = new MemoryStream(44 + dataBytes))
        using (BinaryWriter writer = new BinaryWriter(stream))
        {
            writer.Write(new[] { 'R', 'I', 'F', 'F' });
            writer.Write(36 + dataBytes);
            writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
            writer.Write(16);
            writer.Write((short)1);             // PCM
            writer.Write((short)1);             // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);
            writer.Write(new[] { 'd', 'a', 't', 'a' });
            writer.Write(dataBytes);
            foreach (float sample in samples)
                writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample * gain, -1f, 1f) * short.MaxValue));
            writer.Flush();
            return stream.ToArray();
        }
    }
}
