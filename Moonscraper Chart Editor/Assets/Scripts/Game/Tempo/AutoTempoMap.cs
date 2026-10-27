// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using MoonscraperChartEditor.Song;
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
    string m_status = string.Empty;
    Vector2 m_scroll;

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

        m_thread = new Thread(() =>
        {
            try
            {
                if (detector == null)
                    detector = new AutoTempoDetector(MixFiles(files), m_mixRate);
                AutoTempoDetector.Map map = detector.MapSong(beats, bpm);
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
            bool chosen = Math.Abs(candidate.bpm - main.bpm) < 0.001;
            if (GUILayout.Toggle(chosen, FormatBpm(candidate.bpm), "button") && !chosen)
                Analyse(candidate.bpm);
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("First measure: " + Utility.timeConvertion((float)(main.downbeat - song.offset)) + ", where the song comes in.");
        for (int i = 1; i < m_map.segments.Count; ++i)
            GUILayout.Label("Tempo change at " + Utility.timeConvertion((float)(m_map.segments[i].downbeat - song.offset)) + " to " + FormatBpm(m_map.segments[i].bpm));

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

    void DrawClose()
    {
        if (GUILayout.Button("Close"))
            m_visible = false;
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
            uint tick = (uint)marker.measure * measureTicks;
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
