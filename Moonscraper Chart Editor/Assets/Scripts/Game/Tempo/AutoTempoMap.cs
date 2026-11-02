// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MoonscraperChartEditor.Song;
using MoonscraperEngine;
using MoonscraperEngine.Audio;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools > Auto Tempo Map: listens to the song and places the BPM markers the way charters do. One BPM at the very
/// start sized so the first measure line lands on the first downbeat, then the song's tempo, and a marker at each
/// tempo change it finds. It shows the tempos it could be (half, double) to pick from, and lists the measures that
/// don't line up with the audio so they can be checked by ear. Applying it is one step Ctrl+Z undoes.
/// </summary>
public class AutoTempoMap : MonoBehaviour
{
    const int WindowId = 0x4252;
    const int MaxUnsureShown = 12;

    static AutoTempoMap s_instance;

    readonly DuetWindow m_window = new DuetWindow(WindowId, "AutoTempoMap", new Rect(0, 0, 360, 10), new Vector2(300, 160), false, true);
    ChartEditor m_editor;
    bool m_visible;

    // The analysis runs off the main thread; these are handed back when it's done
    Thread m_thread;
    volatile bool m_working;
    volatile string m_threadError;
    AutoTempoDetector m_detector;
    AutoTempoDetector.Map m_map;
    AutoTempoDetector.Map m_pendingMap;
    Song m_analysedSong;
    double m_chosenBpm;
    int m_beatsPerMeasure = 4;
    AutoTempoDetector.Mode m_mode = AutoTempoDetector.Mode.Auto;
    static readonly AutoTempoDetector.Mode[] s_modes = { AutoTempoDetector.Mode.Auto, AutoTempoDetector.Mode.Follow };
    static readonly string[] s_modeNames = { "Auto", "Follow every beat" };
    string m_status = string.Empty;
    Vector2 m_scroll;

    // Tap-along: beats tapped with T (or the Tap button) while the song plays, in audio seconds
    const KeyCode TapKey = KeyCode.T;
    const double SnapWindow = 0.07;
    readonly List<double> m_taps = new List<double>();
    bool m_tapping;             // Waiting for taps; ends when playback stops
    bool m_tapPlayed;           // Playback started since tapping began
    string m_tapStatus = string.Empty;

    // Testing against a folder of finished charts (runs off the main thread too)
    TempoTest m_test;
    Thread m_testThread;

    public static bool IsPointerOver()
    {
        if (s_instance == null || !s_instance.m_visible || DuetPanel.isHudHidden)
            return false;

        float scale = DuetPanel.GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        SceneManager.sceneLoaded += (scene, mode) => AttachToEditor();
        AttachToEditor();
    }

    static void AttachToEditor()
    {
        if (s_instance)
            return;

        ChartEditor editor = FindObjectOfType<ChartEditor>();
        if (editor)
            editor.gameObject.AddComponent<AutoTempoMap>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
    }

    void Start()
    {
        AddToToolsMenu();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    // Tools menu: "Auto Tempo Map" at the end of the list
    void AddToToolsMenu()
    {
        foreach (DropDownMenu menu in Resources.FindObjectsOfTypeAll<DropDownMenu>())
        {
            if (!menu || !menu.gameObject.scene.IsValid() || menu.title != "Tools")
                continue;

            CustomUnityDropdown dropdown = menu.GetComponent<CustomUnityDropdown>();
            if (!dropdown)
                continue;

            int index = dropdown.options.Count;
            CustomUnityDropdown.OptionData option = new CustomUnityDropdown.OptionData("Auto Tempo Map");
            option.toggleCheckEvent = new CustomUnityDropdown.ToggleIsOnEvent();
            option.hideOnSelectItem = true;
            dropdown.options.Add(option);

            List<UnityEngine.Events.UnityEvent> events = new List<UnityEngine.Events.UnityEvent>(menu.menuEvent ?? new UnityEngine.Events.UnityEvent[0]);
            while (events.Count < index)
                events.Add(new UnityEngine.Events.UnityEvent());
            UnityEngine.Events.UnityEvent open = new UnityEngine.Events.UnityEvent();
            open.AddListener(Open);
            events.Insert(index, open);
            menu.menuEvent = events.ToArray();

            // The menu keeps "nothing picked" as value = the old option count, which is now this option's index, and
            // picking the value it already has does nothing. Park it past the end again, as DropDownMenu does.
            dropdown.options.Add(new CustomUnityDropdown.OptionData() { text = "" });
            dropdown.value = dropdown.options.Count - 1;
            dropdown.options.RemoveAt(dropdown.options.Count - 1);
            return;
        }
        Debug.LogWarning("Couldn't find the Tools menu to add Auto Tempo Map to");
    }

    public void Open()
    {
        m_visible = true;
        if (m_map == null && !m_working)
            Analyse(0);
    }

    void Update()
    {
        UpdateTapping();
        if (m_working)
            return;

        if (m_pendingMap != null)
        {
            m_map = m_pendingMap;
            m_pendingMap = null;
            m_status = m_map.segments.Count > 0 ? string.Empty : "Couldn't find a steady beat in this song's audio.";
        }
        if (m_threadError != null)
        {
            m_status = m_threadError;
            m_threadError = null;
        }

        // A different song was opened: what was worked out was for the old one
        if (m_map != null && m_editor.currentSong != m_analysedSong)
        {
            m_map = null;
            m_detector = null;
        }
    }

    // Reads the song's audio and maps it; 'bpm' picks one of the tempos offered (0 for the best guess)
    void Analyse(double bpm)
    {
        Song song = m_editor.currentSong;
        if (song == null)
            return;

        TimeSignature ts = song.timeSignatures.Count > 0 ? song.timeSignatures[0] : null;
        m_beatsPerMeasure = ts != null ? (int)ts.numerator : 4;
        if (ts != null && ts.denominator != 4)
        {
            m_status = "Auto Tempo Map works with x/4 time signatures for now. The chart starts in " + ts.numerator + "/" + ts.denominator + ".";
            return;
        }

        List<string> files = new List<string>();
        foreach (Song.AudioInstrument instrument in EnumX<Song.AudioInstrument>.Values)
        {
            string path = song.GetAudioLocation(instrument);
            if (!string.IsNullOrEmpty(path) && File.Exists(path) && !files.Contains(path))
                files.Add(path);
        }
        if (files.Count == 0)
        {
            m_status = "This chart has no audio to listen to. Add it in Song Properties first.";
            return;
        }

        m_analysedSong = song;
        m_chosenBpm = bpm;
        m_working = true;
        m_status = m_detector == null ? "Listening to the song..." : "Mapping at that tempo...";
        AutoTempoDetector detector = m_detector;
        int beats = m_beatsPerMeasure;
        AutoTempoDetector.Mode mode = m_mode;

        m_thread = new Thread(() =>
        {
            try
            {
                if (detector == null)
                    detector = new AutoTempoDetector(MixFiles(files), m_mixRate);
                AutoTempoDetector.Map map = detector.MapAuto(beats, bpm, mode);
                m_detector = detector;
                m_pendingMap = map;
            }
            catch (Exception e)
            {
                m_threadError = "Couldn't analyse the audio: " + e.Message;
            }
            finally
            {
                m_working = false;
            }
        });
        m_thread.IsBackground = true;
        m_thread.Start();
    }

    int m_mixRate;

    // Every audio file of the song added together (stems play together), at the first file's sample rate
    float[] MixFiles(List<string> files)
    {
        float[] mix = null;
        foreach (string file in files)
        {
            int rate;
            float[] samples = AudioManager.DecodeMono(file, out rate);
            if (samples == null)
                continue;
            if (mix == null)
            {
                mix = samples;
                m_mixRate = rate;
            }
            else if (rate == m_mixRate)
            {
                if (samples.Length > mix.Length)
                {
                    float[] longer = samples;
                    samples = mix;
                    mix = longer;
                }
                for (int i = 0; i < samples.Length; ++i)
                    mix[i] += samples[i];
            }
        }
        if (mix == null)
            throw new Exception("none of its audio files could be read");
        return mix;
    }

    void OnGUI()
    {
        if (!m_visible || m_editor == null || DuetPanel.isHudHidden)
            return;

        float scale = DuetPanel.GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        if (!m_window.isPlaced)
            m_window.PlaceOnce(new Vector2((Screen.width / scale - m_window.rect.width) / 2, 80));
        if (!DuetMenuCover.Covers(m_window.rect, scale))
            m_window.Draw("Auto Tempo Map", DrawWindow, scale);
    }

    void DrawWindow(int id)
    {
        if (m_test != null)
        {
            DrawTest();
            return;
        }

        if (m_working)
        {
            GUILayout.Label(m_status);
            DrawClose();
            return;
        }

        if (m_map == null || m_map.segments.Count == 0)
        {
            if (m_status.Length > 0)
                GUILayout.Label(m_status);
            if (GUILayout.Button("Listen to the song"))
                Analyse(0);
            if (m_editor.currentSong != null)
                DrawTapAlong();
            DrawClose();
            return;
        }

        Song song = m_editor.currentSong;
        AutoTempoDetector.Segment main = m_map.segments[0];

        // The tempos it could be: picking another maps the song again at that one
        GUILayout.Label("Tempo (pick the one that feels right):");
        GUILayout.BeginHorizontal();
        int shown = 0;
        foreach (AutoTempoDetector.Candidate candidate in m_map.choices)
        {
            if (shown >= 3)
                break;
            ++shown;
            bool chosen = Math.Abs(candidate.bpm - m_map.startBpm) < 0.001;
            if (GUILayout.Toggle(chosen, FormatBpm(candidate.bpm), "button") && !chosen)
                Analyse(candidate.bpm);
        }
        GUILayout.EndHorizontal();

        // Steady tempos with sudden changes, or following a tempo that speeds up and slows down
        GUILayout.BeginHorizontal();
        GUILayout.Label("Changes:", GUILayout.Width(62));
        for (int i = 0; i < s_modeNames.Length; ++i)
        {
            bool on = m_mode == s_modes[i];
            if (GUILayout.Toggle(on, s_modeNames[i], "button") && !on)
            {
                m_mode = s_modes[i];
                Analyse(m_map.startBpm);
            }
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("First measure: " + Utility.timeConvertion((float)(main.downbeat - song.offset)) + ", where the song comes in.");
        if (m_map.followed)
        {
            double slowest = double.MaxValue, fastest = 0;
            foreach (AutoTempoDetector.Segment segment in m_map.segments)
            {
                slowest = Math.Min(slowest, segment.bpm);
                fastest = Math.Max(fastest, segment.bpm);
            }
            GUILayout.Label("Following every beat, from " + FormatBpm(slowest) + " to " + FormatBpm(fastest) + ": " + m_map.segments.Count + " BPM markers follow it.");
        }
        else
        {
            for (int i = 1; i < m_map.segments.Count; ++i)
            {
                AutoTempoDetector.Segment segment = m_map.segments[i];
                string at = Utility.timeConvertion((float)(segment.downbeat - song.offset));
                if (!segment.ramp)
                {
                    GUILayout.Label("Tempo change at " + at + " to " + FormatBpm(segment.bpm));
                    continue;
                }
                // A slowdown or speed-up: one line for all its measures
                int last = i;
                while (last + 1 < m_map.segments.Count && m_map.segments[last + 1].ramp)
                    ++last;
                double to = last + 1 < m_map.segments.Count ? m_map.segments[last + 1].bpm : m_map.segments[last].bpm;
                GUILayout.Label((to < m_map.segments[i - 1].bpm ? "Slows down" : "Speeds up") + " to " + FormatBpm(to) + " from " + at + ", over " + (last - i + 1) + " measures (a BPM marker on each)");
                i = last;
            }
        }

        if (m_map.unsureMeasures.Count == 0)
        {
            GUILayout.Label("All " + m_map.measuresChecked + " measures line up with the audio.");
        }
        else
        {
            GUILayout.Label(m_map.unsureMeasures.Count + " of " + m_map.measuresChecked + " measures don't clearly line up. Check them by ear:");
            m_scroll = GUILayout.BeginScrollView(m_scroll, GUILayout.MaxHeight(110));
            for (int i = 0; i < m_map.unsureMeasures.Count && i < MaxUnsureShown; ++i)
            {
                double chartTime = m_map.unsureMeasures[i] - song.offset;
                if (GUILayout.Button("Go to " + Utility.timeConvertion((float)chartTime)))
                    JumpTo(chartTime);
            }
            if (m_map.unsureMeasures.Count > MaxUnsureShown)
                GUILayout.Label("...and " + (m_map.unsureMeasures.Count - MaxUnsureShown) + " more.");
            GUILayout.EndScrollView();
        }

        if (HasNotes(song))
            GUILayout.Label("This chart already has notes. They stay on their beats, so they'll move in time with the new map.");
        if (main.downbeat < song.offset)
            GUILayout.Label("The song comes in before the chart's start (Offset in Song Properties). Set Offset to 0 first.");

        DrawTapAlong();

        GUILayout.Space(4);
        GUILayout.Label("Tip: turn on the Metronome (M) and play to hear it. Ctrl+Z undoes it.");
        GUILayout.BeginHorizontal();
        GUI.enabled = main.downbeat >= song.offset;
        if (GUILayout.Button("Apply tempo map"))
            Apply();
        GUI.enabled = true;
        if (GUILayout.Button("Listen again", GUILayout.Width(110)))
        {
            m_detector = null;
            Analyse(0);
        }
        GUILayout.EndHorizontal();
        if (m_status.Length > 0)
            GUILayout.Label(m_status);
        DrawClose();
    }

    // Where the song is playing now, in seconds of its audio (what the map is measured in)
    double AudioNow()
    {
        AudioStream stream = m_editor.currentSongAudio != null ? m_editor.currentSongAudio.mainSongAudio : null;
        if (stream != null && stream.isValid)
            return stream.CurrentPositionSeconds;
        return m_editor.currentVisibleTime + m_editor.currentSong.offset;
    }

    void UpdateTapping()
    {
        if (!m_tapping || m_editor == null)
            return;

        if (m_editor.currentState == ChartEditor.State.Playing)
        {
            m_tapPlayed = true;
            if (!Services.IsTyping && Input.GetKeyDown(TapKey))
                Tap();
        }
        else if (m_tapPlayed)
        {
            m_tapping = false;      // Playback stopped: the taps are ready to use
            m_tapStatus = m_taps.Count + " taps" + TappedTempo() + ".";
        }
    }

    void Tap()
    {
        m_taps.Add(AudioNow());
        m_tapStatus = m_taps.Count + " taps" + TappedTempo();
    }

    string TappedTempo()
    {
        if (m_taps.Count < 3)
            return string.Empty;
        List<double> beats = TapTempo.CleanBeats(m_taps);
        double span = beats[beats.Count - 1] - beats[0];
        return span > 0 ? ", about " + FormatBpm(60 * (beats.Count - 1) / span) : string.Empty;
    }

    // Tap along: for a stretch it got wrong (or a song it couldn't hear a beat in), tap the beats and they become the
    // tempo map there
    void DrawTapAlong()
    {
        GUILayout.Space(6);
        GUILayout.Label("Tap along, for a part it got wrong:");
        if (m_tapping)
        {
            GUILayout.Label(m_taps.Count > 0 ? m_tapStatus : "Playing... press " + TapKey + " on every beat, starting on beat 1 of a measure.");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Tap (" + TapKey + ")"))
                Tap();
            if (GUILayout.Button("Stop", GUILayout.Width(80)))
            {
                if (m_editor.currentState == ChartEditor.State.Playing)
                    m_editor.Stop();
                m_tapping = false;
                m_tapStatus = m_taps.Count + " taps" + TappedTempo() + ".";
            }
            GUILayout.EndHorizontal();
            return;
        }

        if (m_taps.Count == 0)
        {
            if (m_tapStatus.Length > 0)
                GUILayout.Label(m_tapStatus);
            GUILayout.Label("Put the playhead just before the part, press Start, then tap " + TapKey + " on every beat from beat 1 of a measure. Stop (or Space) when done.");
            if (GUILayout.Button("Start tapping"))
                StartTapping();
            return;
        }

        if (m_tapStatus.Length > 0)
            GUILayout.Label(m_tapStatus);
        GUILayout.BeginHorizontal();
        GUI.enabled = m_taps.Count >= 4;
        if (GUILayout.Button("Use the taps"))
            UseTaps();
        GUI.enabled = true;
        if (GUILayout.Button("Tap again", GUILayout.Width(100)))
            StartTapping();
        if (GUILayout.Button("Clear", GUILayout.Width(70)))
        {
            m_taps.Clear();
            m_tapStatus = string.Empty;
        }
        GUILayout.EndHorizontal();
    }

    void StartTapping()
    {
        m_taps.Clear();
        m_tapStatus = string.Empty;
        m_tapPlayed = false;
        m_tapping = true;
        if (m_editor.currentState == ChartEditor.State.Editor)
            m_editor.Play();
    }

    // The taps, nudged onto the hits they were meant for, become the map for the stretch they cover
    void UseTaps()
    {
        Song song = m_editor.currentSong;
        if (song == null)
            return;

        // Each tap moves to the clearest hit near it. Taps with nothing clear nearby move by the usual amount, as
        // that's mostly how late (or early) the tapping ran.
        List<double> snapped = new List<double>();
        List<double> shifts = new List<double>();
        foreach (double tap in m_taps)
        {
            double onset = m_detector != null ? m_detector.SnapToOnset(tap, SnapWindow) : double.NaN;
            snapped.Add(onset);
            if (!double.IsNaN(onset))
                shifts.Add(onset - tap);
        }
        shifts.Sort();
        double usualShift = shifts.Count > 0 ? shifts[shifts.Count / 2] : 0;
        for (int i = 0; i < snapped.Count; ++i)
        {
            if (double.IsNaN(snapped[i]))
                snapped[i] = m_taps[i] + usualShift;
        }

        // One BPM marker where the tapping started: the average tempo, rounded to a whole BPM (Robert's call)
        int beatsPerMeasure = m_beatsPerMeasure > 0 ? m_beatsPerMeasure : 4;
        List<double> beats = TapTempo.CleanBeats(snapped);
        AutoTempoDetector.Segment single = TapTempo.ToSingleSegment(beats);
        if (single == null || beats.Count < 4)
        {
            m_tapStatus = "Tap at least 4 beats.";
            return;
        }
        List<AutoTempoDetector.Segment> tapped = new List<AutoTempoDetector.Segment> { single };
        double tapEnd = beats[beats.Count - 1];

        AutoTempoDetector.Map map = new AutoTempoDetector.Map();
        if (m_map != null)
        {
            map.choices = m_map.choices;
            map.musicStart = m_map.musicStart;
            map.startBpm = m_map.startBpm;
            map.measuresChecked = m_map.measuresChecked;
            foreach (double unsure in m_map.unsureMeasures)
            {
                if (unsure < tapped[0].downbeat - 0.05 || unsure > tapEnd)
                    map.unsureMeasures.Add(unsure);
            }
        }
        else
        {
            map.musicStart = tapped[0].downbeat;
            map.startBpm = tapped[0].bpm;
        }
        map.segments = TapTempo.Merge(m_map != null ? m_map.segments : null, tapped, tapEnd, beatsPerMeasure, false);
        m_map = map;
        m_analysedSong = song;

        m_tapStatus = FormatBpm(single.bpm) + " from " + Utility.timeConvertion((float)(single.downbeat - song.offset))
            + ", from your taps. Apply tempo map puts it in the chart.";
        m_taps.Clear();
    }

    void DrawClose()
    {
        if (!m_working && GUILayout.Button("Test on a songs folder..."))
            StartTest();
        if (GUILayout.Button("Close"))
            m_visible = false;
    }

    string TestFolder { get { return Path.Combine(Application.persistentDataPath, "Tempo Test"); } }

    // Maps every song in a folder and compares it with its chart's own tempo map; the charts are only read
    void StartTest()
    {
        string picked;
        if (!FileExplorer.OpenFolderPanel(out picked) || string.IsNullOrEmpty(picked))
            return;

        string output = TestFolder;
        try
        {
            string pack = Path.Combine(output, "Test Pack");
            if (Directory.Exists(pack))
            {
                foreach (string old in Directory.GetFiles(pack, "*.tempo"))
                    File.Delete(old);
            }
            Directory.CreateDirectory(output);
        }
        catch (Exception e)
        {
            m_status = "Couldn't set up the test folder: " + e.Message;
            return;
        }

        m_test = new TempoTest(picked, output, AudioManager.DecodeMono);
        m_testThread = new Thread(m_test.Run);
        m_testThread.IsBackground = true;
        m_testThread.Start();
    }

    void DrawTest()
    {
        GUILayout.Label("Testing Auto Tempo Map against your charts (they're only read, never changed).");
        GUILayout.Label(m_test.status);
        if (m_test.done + m_test.failed > 0)
            GUILayout.Label(m_test.done + " tested" + (m_test.failed > 0 ? ", " + m_test.failed + " skipped" : ""));
        if (!m_test.finished)
        {
            if (GUILayout.Button(m_test.stop ? "Stopping after this song..." : "Stop"))
                m_test.Stop();
            return;
        }

        GUILayout.Label("Results are in the Tempo Test folder: the report, and a Test Pack folder (what the detector heard, no audio) to zip up and send.");
        if (GUILayout.Button("Open results folder"))
            Application.OpenURL("file:///" + TestFolder.Replace('\\', '/'));
        if (GUILayout.Button("Done"))
            m_test = null;
    }

    static string FormatBpm(double bpm)
    {
        return (Math.Round(bpm * 1000) / 1000).ToString("0.###") + " BPM";
    }

    static bool HasNotes(Song song)
    {
        foreach (Song.Instrument instrument in EnumX<Song.Instrument>.Values)
        {
            if (instrument == Song.Instrument.Unrecognised)
                continue;
            foreach (Song.Difficulty difficulty in EnumX<Song.Difficulty>.Values)
            {
                Chart chart = song.GetChart(instrument, difficulty);
                if (chart != null && chart.notes.Count > 0)
                    return true;
            }
        }
        return false;
    }

    void JumpTo(double chartTime)
    {
        if (m_editor.currentState != ChartEditor.State.Editor)
            return;
        Song song = m_editor.currentSong;
        m_editor.movement.SetPosition(song.TimeToTick((float)Math.Max(0, chartTime), song.resolution));
    }

    // Replaces every BPM marker with the new map, as one step on the undo list
    void Apply()
    {
        if (m_editor.currentState != ChartEditor.State.Editor)
        {
            m_status = "Stop playing first.";
            return;
        }

        Song song = m_editor.currentSong;
        TimeSignature ts = song.timeSignatures.Count > 0 ? song.timeSignatures[0] : null;
        uint measureTicks = (uint)Math.Round(song.resolution * m_beatsPerMeasure * (ts != null ? 4.0 / ts.denominator : 1));

        List<AutoTempoDetector.Marker> markers = AutoTempoDetector.ToMarkers(m_map.segments, song.offset, m_beatsPerMeasure);
        if (markers.Count == 0)
            return;

        List<SongEditCommand> commands = new List<SongEditCommand>();
        List<SongObject> oldMarkers = new List<SongObject>();
        BPM first = null;
        foreach (BPM bpm in song.bpms)
        {
            if (bpm.tick == 0 && first == null)
                first = bpm;
            else
                oldMarkers.Add(bpm);
        }
        if (oldMarkers.Count > 0)
            commands.Add(new SongEditDelete(oldMarkers));

        List<SongObject> newMarkers = new List<SongObject>();
        foreach (AutoTempoDetector.Marker marker in markers)
        {
            uint value = (uint)Math.Max(1, Math.Min(uint.MaxValue, Math.Round(marker.bpm * 1000)));
            uint tick = (uint)marker.measure * measureTicks + (uint)Math.Round(marker.beat * measureTicks / m_beatsPerMeasure);
            if (tick == 0 && first != null)
                commands.Add(new SongEditModify<BPM>(first, new BPM(0, value)));
            else
                newMarkers.Add(new BPM(tick, value));
        }
        if (newMarkers.Count > 0)
            commands.Add(new SongEditAdd(newMarkers));

        m_editor.commandStack.Push(new BatchedSongEditCommand(commands));
        m_status = "Applied " + markers.Count + " BPM marker" + (markers.Count == 1 ? string.Empty : "s") + ". Ctrl+Z undoes it.";
        m_editor.services.notificationBar.PushNotification("TEMPO MAP APPLIED", 2, true);
    }
}
