// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using MoonscraperChartEditor.Song;
using UnityEngine;
using UnityEngine.SceneManagement;
using Event = UnityEngine.Event;

/// <summary>
/// Tools > Metric Modulation (Ctrl+M): sets a new tempo by saying a note value of the old tempo lasts as long as a
/// note value of the new one, e.g. "the old 8th triplet is the new quarter note". The notes keep the same speed
/// across the change, only where the beat falls moves.
///
/// New BPM = old BPM x (old notes per beat) / (new notes per beat). Note values are Moonscraper steps (notes per
/// 4/4 measure): quarter 4, 8th 8, 8th triplet 12, 16th 16, dotted 8th 16/3 and so on.
///
/// Works on the selected BPM marker, or adds one at the strikeline (snapped to the current step). The old tempo is
/// the one playing just before that spot. One Ctrl+Z undoes it, and in Collab Mode it's sent like any BPM edit.
/// Keyboard: Ctrl+M opens it with the old step box ready to type in, Tab goes to the new step, Enter applies.
/// </summary>
public class MetricModulation : MonoBehaviour
{
    const int WindowId = 0x4254;
    const string OldField = "MetricModulationOld";
    const string NewField = "MetricModulationNew";

    struct NoteValue
    {
        public string name;
        public string step;     // As typed in the step box
        public NoteValue(string name, string step) { this.name = name; this.step = step; }
    }

    static readonly NoteValue[] s_values =
    {
        new NoteValue("Half", "2"),
        new NoteValue("Dotted quarter", "8/3"),
        new NoteValue("Quarter", "4"),
        new NoteValue("Quarter triplet", "6"),
        new NoteValue("Dotted 8th", "16/3"),
        new NoteValue("8th", "8"),
        new NoteValue("8th triplet", "12"),
        new NoteValue("16th", "16"),
        new NoteValue("Quintuplet", "20"),
        new NoteValue("16th triplet", "24"),
        new NoteValue("32nd", "32"),
        new NoteValue("Whole", "1"),
    };

    static MetricModulation s_instance;

    readonly DuetWindow m_window = new DuetWindow(WindowId, "MetricModulation", new Rect(0, 0, 440, 10), new Vector2(360, 200), false, true);
    ChartEditor m_editor;
    bool m_visible;
    bool m_focusOld;
    bool m_showHelp;
    string m_oldStep = "12";
    string m_newStep = "4";
    string m_status = string.Empty;

    public static bool isTyping { get; private set; }

    public static bool IsPointerOver()
    {
        if (s_instance == null || !s_instance.m_visible || DuetPanel.isHudHidden)
            return false;

        float scale = DuetPanel.GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
    }

    /// <summary>Tools menu entry, added under Fix Broken Notes.</summary>
    public static void OpenWindow()
    {
        if (s_instance)
            s_instance.Open();
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
            editor.gameObject.AddComponent<MetricModulation>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
    }

    void Open()
    {
        m_visible = true;
        m_focusOld = true;
        m_status = string.Empty;
    }

    void Close()
    {
        m_visible = false;
        StopTyping();
    }

    void StopTyping()
    {
        isTyping = false;
        GUIUtility.keyboardControl = 0;
    }

    void Update()
    {
        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.MetricModulation))
        {
            if (m_visible)
                Close();
            else
                Open();
        }
    }

    // ---- The maths ----

    /// <summary>Reads a step as typed: "12", "5.5" or a fraction like "16/3" (dotted 8th). 0 if it isn't one.</summary>
    public static double ParseStep(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        text = text.Trim().Replace(',', '.');
        int slash = text.IndexOf('/');
        double value;
        if (slash > 0)
        {
            double top, bottom;
            if (!double.TryParse(text.Substring(0, slash), NumberStyles.Float, CultureInfo.InvariantCulture, out top)
                || !double.TryParse(text.Substring(slash + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out bottom) || bottom <= 0)
                return 0;
            value = top / bottom;
        }
        else if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            return 0;

        return value > 0 && value <= 1024 ? value : 0;
    }

    /// <summary>The new tempo (BPM x 1000, as charts store it) when the old step's notes last as long as the new step's.</summary>
    public static uint NewBpmValue(uint oldValue, double oldStep, double newStep)
    {
        if (oldStep <= 0 || newStep <= 0)
            return 0;
        double value = Math.Round(oldValue * oldStep / newStep);
        return value < 1 || value > uint.MaxValue ? 0 : (uint)value;
    }

    static string NameOf(string stepText)
    {
        double step = ParseStep(stepText);
        foreach (NoteValue value in s_values)
        {
            if (Math.Abs(ParseStep(value.step) - step) < 1e-6)
                return value.name.ToLowerInvariant();
        }
        return step > 0 ? "1/" + stepText.Trim() + " note" : "?";
    }

    static string FormatBpm(uint value)
    {
        return ((decimal)value / 1000m).ToString("0.###", CultureInfo.InvariantCulture);
    }

    // Where the change goes: the selected BPM marker, else the strikeline snapped to the current step
    bool FindTarget(out uint tick, out BPM marker)
    {
        tick = 0;
        marker = null;
        Song song = m_editor.currentSong;
        if (song == null)
            return false;

        foreach (SongObject selected in m_editor.selectedObjectsManager.currentSelectedObjects)
        {
            BPM bpm = selected as BPM;
            if (bpm != null && bpm.song == song)
            {
                marker = bpm;
                tick = bpm.tick;
                return true;
            }
        }

        tick = Snapable.TickToSnappedTick(m_editor.currentTickPos, Globals.gameSettings.step, song);
        int pos = SongObjectHelper.FindObjectPosition(tick, song.bpms);
        if (pos != SongObjectHelper.NOTFOUND)
            marker = song.bpms[pos];
        return true;
    }

    // The tempo playing just before the change
    static BPM TempoBefore(Song song, uint tick)
    {
        return tick > 0 ? song.GetPrevBPM(tick - 1) : song.GetPrevBPM(0);
    }

    void Apply()
    {
        Song song = m_editor.currentSong;
        if (song == null)
            return;
        if (m_editor.currentState != ChartEditor.State.Editor)
        {
            m_status = "Stop playing first.";
            return;
        }

        uint tick;
        BPM marker;
        if (!FindTarget(out tick, out marker))
            return;

        BPM before = TempoBefore(song, tick);
        uint value = NewBpmValue(before.value, ParseStep(m_oldStep), ParseStep(m_newStep));
        if (value == 0)
        {
            m_status = "Type a step in both boxes, like 12 or 16/3.";
            return;
        }

        if (marker != null)
        {
            if (marker.value == value)
            {
                m_status = "That marker is already " + FormatBpm(value) + " BPM.";
                return;
            }
            MoonscraperEngine.ICommand command = BPMPropertiesPanelController.GenerateCommandsAdjustedForAnchors(marker, value);
            if (command == null)
            {
                m_status = "Couldn't change that marker.";
                return;
            }
            m_editor.commandStack.Push(command);
        }
        else
        {
            m_editor.commandStack.Push(new SongEditAdd(new BPM(tick, value)));
        }

        int pos = SongObjectHelper.FindObjectPosition(tick, song.bpms);
        if (pos != SongObjectHelper.NOTFOUND)
        {
            m_editor.selectedObjectsManager.SelectSongObject(song.bpms[pos], song.bpms);
            value = song.bpms[pos].value;   // A later anchor can hold it back
        }
        m_editor.songObjectPoolManager.SetAllPoolsDirty();

        m_status = "Set " + FormatBpm(value) + " BPM at " + Utility.timeConvertion(song.TickToTime(tick, song.resolution)) + ". Ctrl+Z undoes it.";
        m_editor.services.notificationBar.PushNotification("METRIC MODULATION " + FormatBpm(before.value) + " TO " + FormatBpm(value) + " BPM", 2, true);
        Debug.Log(string.Format("Metric modulation at tick {0}: old 1/{1} = new 1/{2}, {3} -> {4} BPM", tick, m_oldStep, m_newStep, FormatBpm(before.value), FormatBpm(value)));
    }

    // ---- Window ----

    void OnGUI()
    {
        if (!m_visible || m_editor == null || DuetPanel.isHudHidden)
        {
            if (isTyping)
                StopTyping();
            return;
        }

        float scale = DuetPanel.GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
        if (!m_window.isPlaced)
            m_window.PlaceOnce(new Vector2((Screen.width / scale - m_window.rect.width) / 2, 80));

        // Clicking outside the window gives the keyboard back to the editor
        if (Event.current.type == EventType.MouseDown && !m_window.rect.Contains(Event.current.mousePosition))
            GUIUtility.keyboardControl = 0;

        if (!DuetMenuCover.Covers(m_window.rect, scale))
            m_window.Draw("Metric Modulation", DrawWindow, scale);
        isTyping = GUIUtility.keyboardControl != 0;
    }

    void HandleKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown)
            return;

        string focused = GUI.GetNameOfFocusedControl();
        bool inOld = focused == OldField, inNew = focused == NewField;
        if (!inOld && !inNew)
            return;

        if (e.keyCode == KeyCode.Tab)
        {
            GUI.FocusControl(inOld ? NewField : OldField);
            e.Use();
        }
        else if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
        {
            e.Use();
            Apply();
            GUIUtility.keyboardControl = 0;     // Back to charting
        }
        else if (e.keyCode == KeyCode.Escape)
        {
            e.Use();
            GUIUtility.keyboardControl = 0;
        }
    }

    void DrawWindow(int id)
    {
        Song song = m_editor.currentSong;
        if (song == null)
        {
            GUILayout.Label("Open a chart first.");
            DrawClose();
            return;
        }

        HandleKeys();

        uint tick;
        BPM marker;
        FindTarget(out tick, out marker);
        BPM before = TempoBefore(song, tick);
        string where = Utility.timeConvertion(song.TickToTime(tick, song.resolution));

        m_showHelp = GUILayout.Toggle(m_showHelp, m_showHelp ? "Hide how it works" : "How it works", "button");
        if (m_showHelp)
        {
            GUILayout.Label("A new tempo where a note of the old tempo lasts as long as a note of the new one, so the notes keep the same speed and only the beat moves. Like \"the old 8th triplet is the new quarter note\": 120 BPM becomes 360.");
            GUILayout.Label("Pick a note or type its step (notes per 4/4 measure, 16/3 for a dotted 8th). It changes the selected BPM marker, or adds one at the strikeline. Ctrl+M opens this, Tab moves between the boxes and Enter applies.");
        }

        GUILayout.Label(marker != null
            ? "Changes the BPM marker at " + where + "."
            : "Adds a BPM marker at the strikeline, " + where + ". Select a BPM marker to change that one.");
        GUILayout.Label("Old tempo: " + FormatBpm(before.value) + " BPM");

        GUILayout.Space(4);
        DrawSide("This note of the old tempo", OldField, ref m_oldStep);
        GUILayout.Space(4);
        DrawSide("...lasts as long as this note of the new tempo", NewField, ref m_newStep);

        if (m_focusOld && Event.current.type == EventType.Repaint)
        {
            m_focusOld = false;
            GUI.FocusControl(OldField);
        }

        GUILayout.Space(6);
        uint value = NewBpmValue(before.value, ParseStep(m_oldStep), ParseStep(m_newStep));
        if (value == 0)
        {
            GUILayout.Label("Type a step in both boxes, like 12 or 16/3.");
        }
        else
        {
            double notesPerSecond = before.value / 1000.0 / 60.0 * ParseStep(m_oldStep) / 4.0;
            GUILayout.Label("Old " + NameOf(m_oldStep) + " = new " + NameOf(m_newStep) + ":  " + FormatBpm(before.value) + " BPM  ->  " + FormatBpm(value) + " BPM");
            GUILayout.Label("Those notes stay at " + notesPerSecond.ToString("0.##", CultureInfo.InvariantCulture) + " per second.");
        }

        GUILayout.BeginHorizontal();
        GUI.enabled = value != 0;
        if (GUILayout.Button(value != 0 ? "Apply " + FormatBpm(value) + " BPM" : "Apply"))
            Apply();
        GUI.enabled = true;
        if (GUILayout.Button("Swap", GUILayout.Width(70)))
        {
            string old = m_oldStep;
            m_oldStep = m_newStep;
            m_newStep = old;
            GUIUtility.keyboardControl = 0;
        }
        GUILayout.EndHorizontal();

        if (m_status.Length > 0)
            GUILayout.Label(m_status);
        DrawClose();
    }

    void DrawSide(string title, string fieldName, ref string step)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(title);
        GUILayout.FlexibleSpace();
        GUILayout.Label("Step", GUILayout.Width(36));
        GUI.SetNextControlName(fieldName);
        step = GUILayout.TextField(step, 8, GUILayout.Width(60));
        GUILayout.EndHorizontal();

        const int PerRow = 4;
        double current = ParseStep(step);
        for (int row = 0; row * PerRow < s_values.Length; ++row)
        {
            GUILayout.BeginHorizontal();
            for (int i = row * PerRow; i < (row + 1) * PerRow && i < s_values.Length; ++i)
            {
                NoteValue value = s_values[i];
                bool on = Math.Abs(ParseStep(value.step) - current) < 1e-6;
                if (GUILayout.Toggle(on, value.name, "button") && !on)
                {
                    step = value.step;
                    GUIUtility.keyboardControl = 0;
                }
            }
            GUILayout.EndHorizontal();
        }
    }

    void DrawClose()
    {
        if (GUILayout.Button("Close"))
            Close();
    }
}
