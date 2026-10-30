// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System;
using System.IO;
using System.Collections.Generic;

#if BASS_AUDIO
using Un4seen.Bass;
using Un4seen.Bass.Misc;
using Un4seen.Bass.AddOn.Opus;
#endif

namespace MoonscraperEngine.Audio
{
    /// <summary>
    /// A wrapper around a custom audio engine, cause Unity's is quite bad for rhythm games. 
    /// Current custom engine is Un4seen's Bass and Bass.net. See licensing information on whether you'd allowed to use it. Currently under Non-Commerical for Moonscraper, hence why it's allowed to be here.
    /// </summary>
    public static class AudioManager
    {
        public static bool isDisposed { get; private set; }
        static List<AudioStream> liveAudioStreams = new List<AudioStream>();
        private const int c_oggEncodingQualityKbps = 256;
        static List<int> pluginHandles = new List<int>();   // For any calls to BASS_PluginLoad

        #region Memory
        public static bool Init(out string errString)
        {
            errString = string.Empty;
            isDisposed = false;

            bool success = false;

            // Load Bass.Net
            {
				UnityEngine.Debug.Log($"Bass version: {Bass.BASS_GetVersion()}");

                if (!Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_DEV_DEFAULT, 1))
                {
                    UnityEngine.Debug.LogWarning($"BASS_SetConfig dev default {Bass.BASS_ErrorGetCode()}");
                }

                // BetterMoonscraper: keep the sound device running while nothing plays. Otherwise Windows lets it go to
                // sleep after a pause, and on many devices (Bluetooth and HDMI especially) the first second or so of
                // audio after pressing play again is lost while it wakes up.
                if (!Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_DEV_NONSTOP, 1))
                {
                    UnityEngine.Debug.LogWarning($"BASS_SetConfig dev nonstop {Bass.BASS_ErrorGetCode()}");
                }

                if (!Bass.BASS_SetConfig(BASSConfig.BASS_CONFIG_UPDATETHREADS, 1))
                {
                    UnityEngine.Debug.LogError($"BASS_SetConfig update threads {Bass.BASS_ErrorGetCode()}");
                }

                success = Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT | BASSInit.BASS_DEVICE_LATENCY, IntPtr.Zero);
                if (!success)
                {
                    BASSError errorCode = Bass.BASS_ErrorGetCode();

                    if (errorCode != BASSError.BASS_ERROR_ALREADY)
                    {
                        UnityEngine.Debug.Log("Unable to initialise Bass.Net on default device. Will attempt to initialise with Direct Sound option enabled.");
                        success = Bass.BASS_Init(-1, 44100, BASSInit.BASS_DEVICE_DEFAULT | BASSInit.BASS_DEVICE_LATENCY | BASSInit.BASS_DEVICE_DSOUND, IntPtr.Zero);

                        if (!success)
                        {
                            errString = "Failed Bass.Net initialisation. Error code " + errorCode;
                            UnityEngine.Debug.LogError(errString);
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.Log("Bass.Net already initialised on current device.");
                    }
                }
                else
                {
                    UnityEngine.Debug.Log("Bass.Net initialised");
                }
            }

            // Load bass fx plugin
            {
                int bassFxVersion = Un4seen.Bass.AddOn.Fx.BassFx.BASS_FX_GetVersion();  // Call this and load bass_fx plugin immediately
                UnityEngine.Debug.Log("Bass FX version = " + bassFxVersion);
            }

            // Load bass opus plugin for encoding
            {
                int handle = Bass.BASS_PluginLoad("bassopus.dll");
                if (handle != 0)
                {
                    pluginHandles.Add(handle);
                }
                else
                {
                    UnityEngine.Debug.LogWarningFormat("Unable to load bassopus.dll, unable to perform opus encoding, {0}", Bass.BASS_ErrorGetCode());
                }
            }

            return success;
        }

        public static void Dispose()
        {
            if (liveAudioStreams.Count > 0)
            {
                UnityEngine.Debug.LogWarning("Disposing of audio manager but there are still " + liveAudioStreams.Count + " streams remaining. Remaining streams will be cleaned up by the audio manager.");
            }

            // Free any remaining streams 
            for (int i = liveAudioStreams.Count - 1; i >= 0; --i)
            {
                FreeAudioStream(liveAudioStreams[i]);
            }

            UnityEngine.Debug.Assert(liveAudioStreams.Count == 0, "Failed to free " + liveAudioStreams.Count + " remaining audio streams");

            foreach(int pluginHandle in pluginHandles)
            {
                if (!Bass.BASS_PluginFree(pluginHandle))
                {
                    UnityEngine.Debug.LogError($"Failed to free plugin handle {pluginHandle}, {Bass.BASS_ErrorGetCode()}");
                }
            }

            if (!Bass.BASS_Free())
            {
                UnityEngine.Debug.LogError($"Failed to free bass, {Bass.BASS_ErrorGetCode()}");
            }

            UnityEngine.Debug.Log("Freed Bass Audio memory");
            isDisposed = true;
        }

        public static bool FreeAudioStream(AudioStream stream)
        {
            bool success = false;

            if (isDisposed)
            {
                UnityEngine.Debug.LogError("Trying to free a stream when Bass has not been initialised");
                return false;
            }

            if (StreamIsValid(stream))
            {
                if (stream.GetType() == typeof(OneShotSampleStream))
                    success = Bass.BASS_SampleFree(stream.audioHandle);
                else
                    success = Bass.BASS_StreamFree(stream.audioHandle);

                if (!success)
                    UnityEngine.Debug.LogError("Error while freeing audio stream " + stream.audioHandle + ", Error Code " + Bass.BASS_ErrorGetCode());
                else
                {
                    UnityEngine.Debug.Log("Successfully freed audio stream");
                    if (!liveAudioStreams.Remove(stream))
                    {
                        UnityEngine.Debug.LogError("Freed a stream, however it wasn't tracked by the audio manager?");
                    }
                }
            }
            else
            {
                UnityEngine.Debug.LogWarning("Attempted to free an invalid audio stream");
            }

            return success;
        }

        public static bool ConvertToOgg(string sourcePath, string destPath)
        {
            const string EXTENTION = ".ogg";
            UnityEngine.Debug.Assert(destPath.EndsWith(EXTENTION));

            bool success = false;

            if (sourcePath.EndsWith(EXTENTION))
            {
                // Re-encoding is slow as hell, speed this up
                System.IO.File.Copy(sourcePath, destPath, true);
                success = true;
            }
            else
            {
                string inputFile = sourcePath;
                string outputFile = destPath;

#if (UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN)
                string encoderDirectory = UnityEngine.Application.streamingAssetsPath;
                UnityEngine.Debug.Assert(File.Exists(Path.Combine(encoderDirectory, "oggenc2.exe")));

                EncoderOGG encoder = new EncoderOGG(0);
                encoder.EncoderDirectory = encoderDirectory;
                encoder.OGG_Quality = encoder.Kbps2Quality(c_oggEncodingQualityKbps);   // Should equate to about quality level 8 out of a max of 10

                success = BaseEncoder.EncodeFile(inputFile, outputFile, encoder, null, true, false);
                if (!success)
                {
                    UnityEngine.Debug.LogErrorFormat("Unable to encode ogg file from {0} to {1}. Error {2}", sourcePath, destPath, Bass.BASS_ErrorGetCode().ToString());
                }
#elif (UNITY_STANDALONE_LINUX || UNITY_EDITOR_LINUX)
                success = new FFmpegTranscoding().main(sourcePath, destPath);
                if (!success) 
                {
                    UnityEngine.Debug.LogErrorFormat("Unable to encode ogg file from {0} to {1}", sourcePath, destPath);
                }
#else
                UnityEngine.Debug.LogErrorFormat("Unable to encode ogg file from {0} to {1}. Platform not implemented.", sourcePath, destPath);
#endif
            }

            return success;
        }

        /// <summary>
        /// Makes an .ogg of the audio played at another speed, for exporting a slowed down or sped up chart. Pitched
        /// changes the playback rate like a record (faster is higher), otherwise the pitch stays the same. Renders a
        /// temporary .wav next to the output, then encodes that like ConvertToOgg.
        /// </summary>
        public static bool ConvertToOggAtSpeed(string sourcePath, string destPath, float speed, bool pitched)
        {
            string wavPath = Path.ChangeExtension(destPath, null) + ".speed.tmp.wav";
            try
            {
                if (!RenderWavAtSpeed(sourcePath, wavPath, speed, pitched))
                    return false;

                return ConvertToOgg(wavPath, destPath);
            }
            finally
            {
                try
                {
                    if (File.Exists(wavPath))
                        File.Delete(wavPath);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogWarning("Couldn't delete the temporary file " + wavPath + ". " + e.Message);
                }
            }
        }

        /// <summary>
        /// The whole file decoded to mono floats, for analysing it (Auto Tempo Map). Safe off the main thread.
        /// Returns null if it can't be read.
        /// </summary>
        public static float[] DecodeMono(string sourcePath, out int sampleRate)
        {
            sampleRate = 0;
            int handle = StreamCreateFile(sourcePath, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_SAMPLE_FLOAT | BASSFlag.BASS_STREAM_PRESCAN);
            if (handle == 0)
            {
                UnityEngine.Debug.LogWarningFormat("Unable to open {0} to analyse it. Error {1}", sourcePath, Bass.BASS_ErrorGetCode());
                return null;
            }

            try
            {
                BASS_CHANNELINFO info = Bass.BASS_ChannelGetInfo(handle);
                sampleRate = info.freq;
                int channels = Math.Max(1, info.chans);
                long bytes = Bass.BASS_ChannelGetLength(handle, BASSMode.BASS_POS_BYTE);
                List<float> mono = new List<float>(bytes > 0 ? (int)Math.Min(int.MaxValue, bytes / 4 / channels) : 1 << 20);

                float[] buffer = new float[16384 * channels];
                int read;
                while ((read = Bass.BASS_ChannelGetData(handle, buffer, buffer.Length * 4 | (int)BASSData.BASS_DATA_FLOAT)) > 0)
                {
                    int values = read / 4;
                    for (int i = 0; i + channels <= values; i += channels)
                    {
                        float sum = 0;
                        for (int c = 0; c < channels; ++c)
                            sum += buffer[i + c];
                        mono.Add(sum / channels);
                    }
                }
                return mono.ToArray();
            }
            finally
            {
                Bass.BASS_StreamFree(handle);
            }
        }

        static bool RenderWavAtSpeed(string sourcePath, string wavPath, float speed, bool pitched)
        {
            int handle = StreamCreateFile(sourcePath, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_STREAM_PRESCAN);
            if (handle == 0)
            {
                UnityEngine.Debug.LogErrorFormat("Unable to open {0} to change its speed. Error {1}", sourcePath, Bass.BASS_ErrorGetCode());
                return false;
            }

            handle = Un4seen.Bass.AddOn.Fx.BassFx.BASS_FX_TempoCreate(handle, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_FX_FREESOURCE);
            if (handle == 0)
            {
                UnityEngine.Debug.LogErrorFormat("Unable to change the speed of {0}. Error {1}", sourcePath, Bass.BASS_ErrorGetCode());
                return false;
            }

            try
            {
                BASS_CHANNELINFO info = Bass.BASS_ChannelGetInfo(handle);
                if (pitched)
                    Bass.BASS_ChannelSetAttribute(handle, BASSAttribute.BASS_ATTRIB_TEMPO_FREQ, info.freq * speed);
                else
                    Bass.BASS_ChannelSetAttribute(handle, BASSAttribute.BASS_ATTRIB_TEMPO, speed * 100 - 100);

                bool isFloat = (info.flags & BASSFlag.BASS_SAMPLE_FLOAT) != 0;
                short bitsPerSample = (short)(isFloat ? 32 : 16);
                short blockAlign = (short)(info.chans * bitsPerSample / 8);

                using (FileStream file = new FileStream(wavPath, FileMode.Create, FileAccess.Write))
                using (BinaryWriter writer = new BinaryWriter(file))
                {
                    // RIFF header, with the sizes filled in once the data is written
                    writer.Write(new[] { 'R', 'I', 'F', 'F' });
                    writer.Write(0);
                    writer.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' });
                    writer.Write(16);
                    writer.Write((short)(isFloat ? 3 : 1));
                    writer.Write((short)info.chans);
                    writer.Write(info.freq);
                    writer.Write(info.freq * blockAlign);
                    writer.Write(blockAlign);
                    writer.Write(bitsPerSample);
                    writer.Write(new[] { 'd', 'a', 't', 'a' });
                    writer.Write(0);

                    byte[] buffer = new byte[1 << 16];
                    long dataBytes = 0;
                    int read;
                    while ((read = Bass.BASS_ChannelGetData(handle, buffer, buffer.Length)) > 0)
                    {
                        writer.Write(buffer, 0, read);
                        dataBytes += read;
                    }

                    writer.Seek(4, SeekOrigin.Begin);
                    writer.Write((int)(36 + dataBytes));
                    writer.Seek(40, SeekOrigin.Begin);
                    writer.Write((int)dataBytes);
                }

                return true;
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogErrorFormat("Unable to change the speed of {0}. {1}", sourcePath, e);
                return false;
            }
            finally
            {
                Bass.BASS_StreamFree(handle);
            }
        }

        #endregion

        #region Stream Loading

        static int StreamCreateFile(string file, long offset, long length, BASSFlag flags)
        {
            int audioStreamHandle = Bass.BASS_StreamCreateFile(file, offset, length, flags);
            if (audioStreamHandle == 0)
            {
                // Try an opus stream instead as a fallback
                audioStreamHandle = BassOpus.BASS_OPUS_StreamCreateFile(file, offset, length, flags);
            }

            return audioStreamHandle;
        }

        public static AudioStream LoadStream(string filepath)
        {
            int audioStreamHandle = StreamCreateFile(filepath, 0, 0, BASSFlag.BASS_STREAM_DECODE);
            if (audioStreamHandle == 0)
            {
                throw new Exception(String.Format("Failed to load audio file: BASS error {0}", Bass.BASS_ErrorGetCode()));
            }

            var newStream = new AudioStream(audioStreamHandle);
            liveAudioStreams.Add(newStream);
            return newStream;
        }

        public static TempoStream LoadTempoStream(string filepath)
        {
            int audioStreamHandle = StreamCreateFile(filepath, 0, 0, BASSFlag.BASS_STREAM_DECODE | BASSFlag.BASS_ASYNCFILE | BASSFlag.BASS_STREAM_PRESCAN);
            if (audioStreamHandle == 0)
            {
                throw new Exception(String.Format("Failed to load audio file: BASS error {0}", Bass.BASS_ErrorGetCode()));
            }

            audioStreamHandle = Un4seen.Bass.AddOn.Fx.BassFx.BASS_FX_TempoCreate(audioStreamHandle, BASSFlag.BASS_FX_FREESOURCE);

            if (audioStreamHandle == 0)
            {
                throw new Exception(String.Format("Failed to create tempo stream: BASS error {0}", Bass.BASS_ErrorGetCode()));
            }

            var newStream = new TempoStream(audioStreamHandle);
            liveAudioStreams.Add(newStream);
            return newStream;
        }

        public static OneShotSampleStream LoadSampleStream(string filepath, int maxSimultaneousPlaybacks)
        {
            UnityEngine.Debug.Assert(System.IO.File.Exists(filepath), "Filepath " + filepath + " does not exist");

            int audioStreamHandle = Bass.BASS_SampleLoad(filepath, 0, 0, maxSimultaneousPlaybacks, BASSFlag.BASS_DEFAULT);

            if (audioStreamHandle == 0)
            {
                UnityEngine.Debug.LogError($"Failed to load sample for path {filepath}, {Bass.BASS_ErrorGetCode()}");
            }

            var newStream = new OneShotSampleStream(audioStreamHandle, maxSimultaneousPlaybacks);
            liveAudioStreams.Add(newStream);
            return newStream;
        }

        public static OneShotSampleStream LoadSampleStream(UnityEngine.AudioClip clip, int maxSimultaneousPlaybacks)
        {
            var newStream = LoadSampleStream(clip.GetWavBytes(), maxSimultaneousPlaybacks);

            return newStream;
        }

        public static OneShotSampleStream LoadSampleStream(byte[] streamBytes, int maxSimultaneousPlaybacks)
        {
            int audioStreamHandle = Bass.BASS_SampleLoad(streamBytes, 0, streamBytes.Length, maxSimultaneousPlaybacks, BASSFlag.BASS_DEFAULT);

            if (audioStreamHandle == 0)
            {
                UnityEngine.Debug.LogError($"Failed to load sample for sample stream bytes, {Bass.BASS_ErrorGetCode()}");
            }

            var newStream = new OneShotSampleStream(audioStreamHandle, maxSimultaneousPlaybacks);
            liveAudioStreams.Add(newStream);
            return newStream;
        }

        public static void RegisterStream(AudioStream stream)
        {
            liveAudioStreams.Add(stream);
        }

        #endregion

        #region Attributes

        public static float GetAttribute(AudioStream audioStream, AudioAttributes attribute)
        {
            float value = 0;
            if (!Bass.BASS_ChannelGetAttribute(audioStream.audioHandle, (BASSAttribute)attribute, ref value))
            {
                UnityEngine.Debug.LogError($"Failed to get audiostream attribute {attribute} for handle {audioStream.audioHandle}, {Bass.BASS_ErrorGetCode()}");
            }
            return value;
        }

        public static void SetAttribute(AudioStream audioStream, AudioAttributes attribute, float value)
        {
            if (!Bass.BASS_ChannelSetAttribute(audioStream.audioHandle, (BASSAttribute)attribute, value))
            {
                UnityEngine.Debug.LogError($"Failed to set audiostream attribute {attribute} for handle {audioStream.audioHandle}, {Bass.BASS_ErrorGetCode()}");
            }
        }

        public static float GetAttribute(TempoStream audioStream, TempoAudioAttributes attribute)
        {
            float value = 0;
            if (!Bass.BASS_ChannelGetAttribute(audioStream.audioHandle, (BASSAttribute)attribute, ref value))
            {
                UnityEngine.Debug.LogError($"Failed to get tempo stream attribute {attribute} for handle {audioStream.audioHandle}, {Bass.BASS_ErrorGetCode()}");
            }
            return value;
        }

        public static void SetAttribute(TempoStream audioStream, TempoAudioAttributes attribute, float value)
        {
            if (!Bass.BASS_ChannelSetAttribute(audioStream.audioHandle, (BASSAttribute)attribute, value))
            {
                UnityEngine.Debug.LogError($"Failed to set tempo stream attribute {attribute} for handle {audioStream.audioHandle}, {Bass.BASS_ErrorGetCode()}");
            }
        }

        #endregion

        #region Helper Functions

        public static bool StreamIsValid(AudioStream audioStream)
        {
            return audioStream != null && audioStream.isValid;
        }

        #endregion

    }
}
