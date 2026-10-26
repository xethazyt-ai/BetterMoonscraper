// See LICENSE in project root for license information.

using System;
using System.IO;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Plays the song's own video (video.mp4, video.webm and so on in the chart's folder, as Clone Hero finds it) as the
/// background. It follows the chart: song.ini's video_start_time and the chart's offset place it, it plays at the
/// Speed slider's speed while previewing, and it shows the matching frame while scrolling through the chart.
/// Turned on and off in Settings > Customize; only this computer's view changes.
/// </summary>
public class SongVideoBackground : MonoBehaviour
{
    const string PrefEnabled = "Customize.SongVideo";

    // In the order Clone Hero looks for them. Unity plays H.264 .mp4 and VP8 .webm; VP9/AV1 .webm won't play.
    static readonly string[] s_videoNames = { "video.mp4", "video.webm", "video.avi", "video.mpeg", "video.mpg", "video.mov", "video.m4v", "video.wmv", "video.ogv" };

    const double ResyncSeconds = 0.12;      // Playing: how far the video may drift before it's put back
    const double ScrubSeconds = 1.0 / 30;   // Scrolling: how far the chart has to move before showing another frame
    const float ScrubIntervalSeconds = 0.08f;

    public static SongVideoBackground instance { get; private set; }

    VideoPlayer m_player;
    RenderTexture m_texture;
    string m_path;
    double m_startSeconds;      // song.ini's video_start_time
    bool m_prepared;
    bool m_shown;
    double m_lastScrubTarget = double.NaN;
    float m_lastScrubAt;

    /// <summary>
    /// A line for Settings about the current song's video, such as why it isn't showing
    /// </summary>
    public static string status { get; private set; }

    public static bool isEnabled
    {
        get { return PlayerPrefs.GetInt(PrefEnabled, 1) != 0; }
        set
        {
            PlayerPrefs.SetInt(PrefEnabled, value ? 1 : 0);
            PlayerPrefs.Save();
            if (instance)
                instance.Reload();
        }
    }

    void Awake()
    {
        instance = this;
    }

    void Start()
    {
        ChartEditor.Instance.events.songLoadedEvent.Register(Reload);
        ChartEditor.Instance.events.saveEvent.Register(ReloadIfMoved);
        Reload();
    }

    void OnDestroy()
    {
        if (instance == this)
            instance = null;
        if (ChartEditor.InstanceExists)
        {
            ChartEditor.Instance.events.songLoadedEvent.Deregister(Reload);
            ChartEditor.Instance.events.saveEvent.Deregister(ReloadIfMoved);
        }
        Close();
    }

    // Saving to a new place can give the chart a folder with a video in it
    void ReloadIfMoved()
    {
        if (FindVideo() != m_path)
            Reload();
    }

    public void Reload()
    {
        Close();

        if (!isEnabled)
        {
            status = "Song videos are off.";
            return;
        }

        string path = FindVideo();
        if (path == null)
        {
            status = "This song has no video.";
            return;
        }

        m_path = path;
        m_startSeconds = ReadStartSeconds();
        status = "Loading " + Path.GetFileName(path) + "...";

        GameObject holder = new GameObject("Song Video");
        holder.transform.SetParent(transform, false);
        m_player = holder.AddComponent<VideoPlayer>();
        m_player.playOnAwake = false;
        m_player.source = VideoSource.Url;
        m_player.url = new Uri(path).AbsoluteUri;
        m_player.audioOutputMode = VideoAudioOutputMode.None;     // The song's own audio plays as usual
        m_player.renderMode = VideoRenderMode.RenderTexture;
        m_player.isLooping = false;
        m_player.skipOnDrop = true;
        m_player.waitForFirstFrame = true;
        m_player.prepareCompleted += OnPrepared;
        m_player.errorReceived += OnError;
        m_player.Prepare();
    }

    void Close()
    {
        if (m_player)
        {
            m_player.prepareCompleted -= OnPrepared;
            m_player.errorReceived -= OnError;
            m_player.Stop();
            Destroy(m_player.gameObject);
        }
        m_player = null;
        m_prepared = false;
        m_path = null;
        m_lastScrubTarget = double.NaN;

        if (m_shown)
        {
            m_shown = false;
            if (CustomizationManager.instance)
                CustomizationManager.instance.ApplyBackground();    // Back to the picked or default background
        }

        if (m_texture)
        {
            m_texture.Release();
            Destroy(m_texture);
        }
        m_texture = null;
    }

    // The background was just set again (for example a new one picked in Settings), so put the video back in front
    public void ShowAgain()
    {
        if (m_prepared && m_texture && CustomizationManager.instance)
        {
            CustomizationManager.instance.ShowVideoBackground(m_texture, m_texture.width / (float)m_texture.height);
            m_shown = true;
        }
    }

    void OnPrepared(VideoPlayer player)
    {
        if (player != m_player)
            return;

        int width = (int)Math.Max(16, player.width), height = (int)Math.Max(16, player.height);
        m_texture = new RenderTexture(width, height, 0);
        m_texture.name = "Song Video";
        m_texture.Create();
        player.targetTexture = m_texture;
        m_prepared = true;

        if (CustomizationManager.instance)
        {
            CustomizationManager.instance.ShowVideoBackground(m_texture, width / (float)height);
            m_shown = true;
        }

        status = "Showing " + Path.GetFileName(m_path) + (m_startSeconds != 0 ? " (video_start_time " + Math.Round(m_startSeconds * 1000) + " ms)" : string.Empty) + ".";
        Seek(TargetSeconds());
    }

    void OnError(VideoPlayer player, string message)
    {
        if (player != m_player)
            return;

        Debug.LogWarning("Couldn't play the song's video " + m_path + ": " + message);
        string name = m_path != null ? Path.GetFileName(m_path) : "the video";
        Close();
        status = "Couldn't play " + name + ". Moonscraper plays H.264 .mp4 and VP8 .webm videos; VP9 or AV1 .webm needs converting.";
    }

    void Update()
    {
        if (!m_prepared || !m_player)
            return;

        ChartEditor editor = ChartEditor.Instance;
        double target = TargetSeconds();
        double length = m_player.length;
        bool inside = target >= 0 && (length <= 0 || target < length);

        if (editor.currentState == ChartEditor.State.Playing && inside)
        {
            float speed = Mathf.Max(0.01f, Globals.gameSettings.gameSpeed);
            if (m_player.canSetPlaybackSpeed && Math.Abs(m_player.playbackSpeed - speed) > 0.001f)
                m_player.playbackSpeed = speed;

            if (!m_player.isPlaying)
            {
                Seek(target);
                m_player.Play();
            }
            else if (Math.Abs(m_player.time - target) > ResyncSeconds * Math.Max(1, speed))
            {
                Seek(target);
            }
            m_lastScrubTarget = double.NaN;
            return;
        }

        if (m_player.isPlaying)
            m_player.Pause();

        // Paused: show the frame for wherever the chart is, without seeking on every frame of a scroll
        double clamped = Math.Max(0, length > 0 ? Math.Min(target, length - 0.05) : target);
        if ((double.IsNaN(m_lastScrubTarget) || Math.Abs(clamped - m_lastScrubTarget) > ScrubSeconds)
            && Time.unscaledTime - m_lastScrubAt > ScrubIntervalSeconds)
        {
            Seek(clamped);
            m_lastScrubTarget = clamped;
            m_lastScrubAt = Time.unscaledTime;
        }
    }

    void Seek(double seconds)
    {
        if (m_player && m_player.canSetTime)
            m_player.time = Math.Max(0, seconds);
    }

    // Where the video should be: the song's audio time (the chart's time plus its offset) plus video_start_time
    double TargetSeconds()
    {
        ChartEditor editor = ChartEditor.Instance;
        return editor.currentVisibleTime + editor.currentSong.offset + m_startSeconds;
    }

    static string FindVideo()
    {
        if (!ChartEditor.InstanceExists)
            return null;

        string chartFile = ChartEditor.Instance.currentChartFile;
        if (string.IsNullOrEmpty(chartFile))
            return null;

        string folder = Path.GetDirectoryName(chartFile);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            return null;

        foreach (string name in s_videoNames)
        {
            string path = Path.Combine(folder, name);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    static double ReadStartSeconds()
    {
        string value = RichTextIni.ReadIni(ChartEditor.Instance.currentSong.iniProperties, "video_start_time") ?? string.Empty;

        double ms;
        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out ms))
            return ms / 1000;
        return 0;
    }
}
