// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;
using MoonscraperEngine.Audio;

/// <summary>
/// The clap's own volume (Clap Settings menu, can go past 100% to make it louder) and a clap sound of your own
/// (Settings > Customize). Both only change this computer. The sample is rebuilt with the volume baked in, since a
/// sample's own volume can't go above full.
/// </summary>
public static class ClapSound
{
    const string PrefVolume = "Audio.ClapVolumePercent";
    const string PrefCustomName = "Customize.ClapSoundName";
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

    static string customFolder { get { return Path.Combine(CustomizationPrefs.storageFolder, "Clap"); } }

    /// <summary>
    /// The copy of the picked clap sound, or null for the default (or Custom Resources) one
    /// </summary>
    public static string customPath
    {
        get
        {
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

    // The name of the file it was picked from, to show in Settings
    public static string customName
    {
        get { return customPath != null ? PlayerPrefs.GetString(PrefCustomName, "Custom") : null; }
    }

    /// <summary>
    /// Copies the file in (so it keeps working if the original moves) and uses it. Returns an error, or null.
    /// </summary>
    public static string SetCustom(string sourcePath)
    {
        try
        {
            string extension = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (Array.IndexOf(Extensions, extension.TrimStart('.')) < 0)
                return Path.GetFileName(sourcePath) + " isn't a sound file Moonscraper can play.";

            int sampleRate;
            float[] check = AudioManager.DecodeMono(sourcePath, out sampleRate);
            if (check == null || check.Length == 0)
                return "Couldn't read " + Path.GetFileName(sourcePath) + " as a sound.";

            RemoveCustomFiles();
            Directory.CreateDirectory(customFolder);
            File.Copy(sourcePath, Path.Combine(customFolder, "clap" + extension), true);
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
