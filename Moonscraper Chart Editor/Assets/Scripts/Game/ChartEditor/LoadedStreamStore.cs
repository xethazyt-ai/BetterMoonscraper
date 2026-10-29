// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MoonscraperEngine.Audio;

public class LoadedStreamStore
{
    public class StreamConfig
    {
        public OneShotSampleStream stream;
        public string defaultPath;

        public StreamConfig (string defaultPath)
        {
            this.defaultPath = defaultPath;
            stream = null;
        }
    }

    Dictionary<string, StreamConfig> soundMapConfig;

    public LoadedStreamStore(Dictionary<string, StreamConfig> soundMapConfig)
    {
        this.soundMapConfig = soundMapConfig;
    }

    public OneShotSampleStream GetSample(string key)
    {
        StreamConfig config;
        if (soundMapConfig.TryGetValue(key, out config))
        {
            return config.stream;
        }

        return null;
    }

    public void LoadSounds(Skin skin)
    {
        DisposeSounds();    // Allow reloading at any point

        foreach (var soundConfigKV in soundMapConfig)
        {
            soundConfigKV.Value.stream = LoadSoundClip(soundConfigKV.Key, skin, soundConfigKV.Value.defaultPath);
        }
    }

    // Loads one sound again, for example the clap after its volume or sound changes in the settings
    public void ReloadSound(string key, Skin skin)
    {
        StreamConfig config;
        if (!soundMapConfig.TryGetValue(key, out config))
            return;

        if (config.stream != null)
            config.stream.Dispose();
        config.stream = LoadSoundClip(key, skin, config.defaultPath);
    }

    public void DisposeSounds()
    {
        foreach (var soundConfigKV in soundMapConfig)
        {
            if (soundConfigKV.Value.stream != null)
                soundConfigKV.Value.stream.Dispose();
            soundConfigKV.Value.stream = null;
        }
    }

    OneShotSampleStream LoadSoundClip(string customSkinKey, Skin customSkin, string defaultPath)
    {
        string customPath = customSkin.GetSkinItemFilepath(customSkinKey);
        string currentSFX = string.IsNullOrEmpty(customPath) ? defaultPath : customPath;

        if (customSkinKey == SkinKeys.clap)
            return ClapSound.LoadSample(currentSFX, 15);     // At the clap volume, or the clap sound picked in Customize

        return AudioManager.LoadSampleStream(currentSFX, 15);
    }
}
