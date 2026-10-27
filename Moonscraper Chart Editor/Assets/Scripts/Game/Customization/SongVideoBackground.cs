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

    const double ResyncSeconds = 0.1;       // Playing: how far the video may drift before it's caught up with a seek
    const float ResyncIntervalSeconds = 1.0f;   // ...and not more often than this, so a slow seek can't keep restarting
    const float SeekTimeoutSeconds = 1.5f;  // A seek that never reports back stops blocking the next one
    const float StuckSeconds = 0.75f;       // Playing: a picture that hasn't moved for this long gets restarted
    const double ScrubSeconds = 1.0 / 30;   // Scrolling: how far the chart has to move before showing another frame
    const float ScrubIntervalSeconds = 0.08f;

    public static SongVideoBackground instance { get; private set; }

    VideoPlayer m_player;
    RenderTexture m_texture;
    string m_path;
    double m_startSeconds;      // song.ini's video_start_time
    bool m_prepared;
    bool m_triedFileUrl;
    bool m_shown;
    double m_lastScrubTarget = double.NaN;
    float m_lastScrubAt;

    // One seek at a time: asking for another while the video is still finding a frame starts it over, so a far jump
    // (which has to decode from the last keyframe) could keep restarting and leave the picture stuck for seconds
    bool m_seeking;
    float m_seekStartedAt;
    float m_lastSeekDuration = 0.25f;
    double m_queuedSeek = double.NaN;

    // Playing: when the video's time last moved, to notice it stuck (such as restarting after it reached its end)
    bool m_wasPlaying;
    long m_lastObservedFrame = -1;
    float m_lastAdvanceAt;
    float m_lastResyncAt;

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
                instance.SetVisible(value);
        }
    }

    void Awake()
    {
        instance = this;
    }

    // Collab Mode joiners: the host's video, kept with the other received files rather than next to the chart
    static string s_collabVideo;

    void OnCollabVideo(string path)
    {
        s_collabVideo = path;
        if (FindVideo() != m_path)
            Reload();
    }

    void Start()
    {
        Application.quitting += StopForQuit;
        DuetSession.songVideoReceived += OnCollabVideo;
        ChartEditor.Instance.events.songLoadedEvent.Register(Reload);
        ChartEditor.Instance.events.saveEvent.Register(ReloadIfMoved);
        Reload();
    }

    // Stops the video before Unity starts tearing things down, rather than while it's mid-frame
    void StopForQuit()
    {
        Application.quitting -= StopForQuit;
        if (m_player)
        {
            m_player.prepareCompleted -= OnPrepared;
            m_player.errorReceived -= OnError;
            m_player.seekCompleted -= OnSeekCompleted;
            m_player.Stop();
            m_player.targetTexture = null;
        }
        m_prepared = false;
    }

    void OnDestroy()
    {
        Application.quitting -= StopForQuit;
        DuetSession.songVideoReceived -= OnCollabVideo;
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

        // The video is loaded even while the checkbox is off, so turning it on shows it straight away
        string path = FindVideo();
        if (path == null)
        {
            DuetSession session = DuetPanel.activeSession;
            bool downloading = session != null && session.role == DuetSession.Role.Client && session.songFiles != null && !session.songFiles.isComplete;
            status = downloading ? "The host's files are still downloading; the video shows once it's here." : "This song has no video.";
            return;
        }

        m_path = path;
        m_triedFileUrl = false;
        m_startSeconds = ReadStartSeconds();
        status = "Loading " + Path.GetFileName(path) + "...";

        GameObject holder = new GameObject("Song Video");
        holder.transform.SetParent(transform, false);
        m_player = holder.AddComponent<VideoPlayer>();
        m_player.playOnAwake = false;
        m_player.source = VideoSource.Url;
        m_player.url = path.Replace('\\', '/');      // A plain path: Unity can't open file:// links with %20 for spaces
        m_player.audioOutputMode = VideoAudioOutputMode.None;     // The song's own audio plays as usual
        m_player.renderMode = VideoRenderMode.RenderTexture;
        m_player.isLooping = false;
        m_player.skipOnDrop = true;
        m_player.waitForFirstFrame = true;
        m_player.prepareCompleted += OnPrepared;
        m_player.errorReceived += OnError;
        m_player.seekCompleted += OnSeekCompleted;
        m_player.Prepare();
    }

    void Close()
    {
        if (m_player)
        {
            m_player.prepareCompleted -= OnPrepared;
            m_player.errorReceived -= OnError;
            m_player.seekCompleted -= OnSeekCompleted;
            m_player.Stop();
            Destroy(m_player.gameObject);
        }
        m_player = null;
        m_prepared = false;
        m_path = null;
        m_lastScrubTarget = double.NaN;
        m_seeking = false;
        m_queuedSeek = double.NaN;

        if (m_shown)
        {
            m_shown = false;
            if (CustomizationManager.instance && !QuitState.isQuitting)
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
        if (m_prepared && m_texture && CustomizationManager.instance && isEnabled)
        {
            CustomizationManager.instance.ShowVideoBackground(m_texture, m_texture.width / (float)m_texture.height);
            m_shown = true;
        }
    }

    // The checkbox: shows or hides the loaded video at once, at the right frame
    void SetVisible(bool visible)
    {
        if (!m_player)
        {
            if (visible)
                Reload();   // Nothing loaded yet, such as after a video that couldn't play
            return;
        }

        if (visible)
        {
            if (!m_player.isPlaying)
                Seek(TargetSeconds());  // While a preview plays it kept playing hidden, so it's already at the right frame
            m_lastScrubTarget = double.NaN;
            ShowAgain();
            return;
        }

        if (m_player.isPlaying && !IsPreviewPlaying())
            m_player.Pause();
        if (m_shown)
        {
            m_shown = false;
            if (CustomizationManager.instance)
                CustomizationManager.instance.ApplyBackground();
        }
    }

    void OnPrepared(VideoPlayer player)
    {
        if (player != m_player)
            return;

        int width = (int)Math.Max(16, player.width), height = (int)Math.Max(16, player.height);
        if (m_prepared && m_texture)
        {
            // Ready again after stopping at its end: same video, same picture
            player.targetTexture = m_texture;
            Seek(TargetSeconds());
            return;
        }
        m_texture = new RenderTexture(width, height, 0);
        m_texture.name = "Song Video";
        m_texture.Create();
        player.targetTexture = m_texture;
        m_prepared = true;

        if (CustomizationManager.instance && isEnabled)
        {
            CustomizationManager.instance.ShowVideoBackground(m_texture, width / (float)height);
            m_shown = true;
        }

        status = (isEnabled ? "Showing " : "Ready to show ") + Path.GetFileName(m_path) + (m_startSeconds != 0 ? " (video_start_time " + Math.Round(m_startSeconds * 1000) + " ms)" : string.Empty) + ".";
        Seek(TargetSeconds());
    }

    void OnError(VideoPlayer player, string message)
    {
        if (player != m_player)
            return;

        Debug.LogWarning("Couldn't play the song's video " + m_path + ": " + message);
        string name = m_path != null ? Path.GetFileName(m_path) : "the video";
        if (!m_triedFileUrl && m_path != null)
        {
            // Some Windows setups want a file:// link instead, so try that once before giving up
            m_triedFileUrl = true;
            string path = m_path;
            m_player.url = "file:///" + path.Replace('\\', '/');
            m_player.Prepare();
            return;
        }
        Close();
        status = "Couldn't play " + name + ". BetterMoonscraper plays H.264 .mp4 and VP8 .webm videos; VP9 or AV1 .webm needs converting.";
        if (ChartEditor.InstanceExists)
            ChartEditor.Instance.services.notificationBar.PushNotification("COULDN'T PLAY " + name.ToUpperInvariant() + " (H.264 MP4 OR VP8 WEBM ONLY)", 5, true);
    }

    void Update()
    {
        if (!m_prepared || !m_player || QuitState.isQuitting)
            return;

        if (!isEnabled && !IsPreviewPlaying())
        {
            if (m_player.isPlaying)
                m_player.Pause();   // Hidden: don't spend time decoding it
            return;
        }
        // Hidden while a preview plays, it keeps playing out of sight, so ticking the box again shows it at once
        // instead of waiting for the video to seek and start again

        ChartEditor editor = ChartEditor.Instance;
        double target = TargetSeconds();
        double length = m_player.length;
        bool inside = target >= 0 && (length <= 0 || target < length);

        if (editor.currentState == ChartEditor.State.Playing && inside)
        {
            float speed = Mathf.Max(0.01f, Globals.gameSettings.gameSpeed);

            // Aim where the song will be once the seek is done, rather than where it was when it started
            double ahead = target + m_lastSeekDuration * speed;
            if (!m_wasPlaying)
            {
                m_wasPlaying = true;
                m_lastObservedFrame = -1;
                m_lastAdvanceAt = Time.unscaledTime;
            }

            // Only set when the Speed slider changes: on Windows every change of playback speed makes the decoder
            // start over, so nudging it each frame to catch up left the picture frozen after a jump
            if (m_player.canSetPlaybackSpeed && Math.Abs(m_player.playbackSpeed - speed) > 0.001f)
                m_player.playbackSpeed = speed;

            if (!m_player.isPrepared)
            {
                // It stopped at its end and let go of the file: get it ready again, then carry on from here
                m_player.Prepare();
            }
            else if (!m_player.isPlaying)
            {
                Seek(ahead);
                m_player.Play();
            }
            else if (!seekBusy)
            {
                double drift = target - m_player.time;
                if (Math.Abs(drift) > ResyncSeconds * Math.Max(1, speed) && Time.unscaledTime - m_lastResyncAt > ResyncIntervalSeconds)
                {
                    m_lastResyncAt = Time.unscaledTime;
                    Seek(ahead);
                }
            }

            // Stuck: the picture hasn't moved for a while although the song is playing, so start it again from here.
            // This watches the frame shown, not the clock, which can keep running while no new frames come out.
            if (m_player.frame != m_lastObservedFrame || seekBusy)
            {
                m_lastObservedFrame = m_player.frame;
                m_lastAdvanceAt = Time.unscaledTime;
            }
            else if (Time.unscaledTime - m_lastAdvanceAt > StuckSeconds && !seekBusy && m_player.isPrepared)
            {
                m_seeking = false;
                m_player.Pause();
                Seek(ahead);
                m_player.Play();
                m_lastAdvanceAt = Time.unscaledTime;
            }

            m_lastScrubTarget = double.NaN;
            return;
        }
        m_wasPlaying = false;

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

    static bool IsPreviewPlaying()
    {
        return ChartEditor.InstanceExists && ChartEditor.Instance.currentState == ChartEditor.State.Playing;
    }

    void Seek(double seconds)
    {
        if (!m_player || !m_player.canSetTime)
            return;

        if (seekBusy)
        {
            m_queuedSeek = seconds;     // Done once the current seek finishes; only the latest one counts
            return;
        }

        m_seeking = true;
        m_seekStartedAt = Time.unscaledTime;
        m_queuedSeek = double.NaN;
        m_player.time = Math.Max(0, seconds);
    }

    bool seekBusy { get { return m_seeking && Time.unscaledTime - m_seekStartedAt < SeekTimeoutSeconds; } }

    void OnSeekCompleted(VideoPlayer player)
    {
        if (player != m_player)
            return;

        m_seeking = false;
        m_lastSeekDuration = Mathf.Clamp(Time.unscaledTime - m_seekStartedAt, 0.05f, 1.5f);
        if (!double.IsNaN(m_queuedSeek))
        {
            double queued = m_queuedSeek;
            m_queuedSeek = double.NaN;
            // Playing works out its own target each frame, so only a paused scroll needs the queued one
            if (!IsPreviewPlaying())
                Seek(queued);
        }
    }

    // Where the video should be: the song's audio time (the chart's time plus its offset) plus video_start_time
    double TargetSeconds()
    {
        ChartEditor editor = ChartEditor.Instance;
        return editor.currentVisibleTime + editor.currentSong.offset + m_startSeconds;
    }

    static readonly string[] s_videoExtensions = { ".mp4", ".webm", ".avi", ".mpeg", ".mpg", ".mov", ".m4v", ".wmv", ".ogv", ".mkv" };

    // The chart's folder, or for a .mid chart, a joiner or a song not saved yet, the folder its audio is in. video.*
    // first, as Clone Hero names it, else any one video file there.
    static string FindVideo()
    {
        if (!ChartEditor.InstanceExists || ChartEditor.Instance.currentSong == null)
            return null;

        DuetSession session = DuetPanel.activeSession;
        if (session != null && session.role == DuetSession.Role.Client)
            return s_collabVideo != null && File.Exists(s_collabVideo) ? s_collabVideo : null;

        foreach (string folder in SongFolders())
        {
            foreach (string name in s_videoNames)
            {
                string path = Path.Combine(folder, name);
                if (File.Exists(path))
                    return path;
            }
        }

        foreach (string folder in SongFolders())
        {
            try
            {
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string file in files)
                {
                    if (Array.IndexOf(s_videoExtensions, Path.GetExtension(file).ToLowerInvariant()) >= 0)
                        return file;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("Couldn't look for a video in " + folder + ": " + e.Message);
            }
        }
        return null;
    }

    static System.Collections.Generic.List<string> SongFolders()
    {
        System.Collections.Generic.List<string> folders = new System.Collections.Generic.List<string>();
        ChartEditor editor = ChartEditor.Instance;

        AddFolder(folders, editor.currentChartFile);
        foreach (MoonscraperChartEditor.Song.Song.AudioInstrument audio in Enum.GetValues(typeof(MoonscraperChartEditor.Song.Song.AudioInstrument)))
        {
            string location = null;
            try { location = editor.currentSong.GetAudioLocation(audio); }
            catch (Exception) { }
            AddFolder(folders, location);
        }
        return folders;
    }

    static void AddFolder(System.Collections.Generic.List<string> folders, string file)
    {
        if (string.IsNullOrEmpty(file))
            return;
        try
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(file));
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder) && !folders.Contains(folder))
                folders.Add(folder);
        }
        catch (Exception) { }   // Not a path
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
