// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Fix Broken Notes: lists notes a few ticks off the step they were charted at (see NoteGridCheck), with a
/// button to go to each, and fixes them all in one step Ctrl+Z undoes, in the whole chart or just the selected notes.
/// The same kind of window as Auto Tempo Map.
/// </summary>
public class NoteGridFixer : MonoBehaviour
{
    const int WindowId = 0x4253;
    const int MaxShown = 40;

    static NoteGridFixer s_instance;

    readonly DuetWindow m_window = new DuetWindow(WindowId, "NoteGridFixer", new Rect(0, 0, 360, 10), new Vector2(300, 160), false, true);
    ChartEditor m_editor;
    bool m_visible;
    bool m_selectedOnly;
    List<NoteGridCheck.Fix> m_fixes;
    Chart m_checkedChart;
    int m_notesOff;
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

    /// <summary>Tools menu entry, added under Auto Tempo Map.</summary>
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
            editor.gameObject.AddComponent<NoteGridFixer>();
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
        m_selectedOnly = SelectedNotes().Count > 1;
        Check();
    }

    void Update()
    {
        // Another chart or difficulty was opened: what was found was for the old one
        if (m_visible && m_fixes != null && m_editor.currentChart != m_checkedChart)
            Check();
    }

    List<Note> SelectedNotes()
    {
        List<Note> notes = new List<Note>();
        foreach (SongObject songObject in m_editor.selectedObjectsManager.currentSelectedObjects)
        {
            Note note = songObject as Note;
            if (note != null)
                notes.Add(note);
        }
        return notes;
    }

    List<Note> NotesToCheck()
    {
        if (m_selectedOnly)
            return SelectedNotes();
        List<Note> notes = new List<Note>();
        if (m_editor.currentChart != null)
        {
            foreach (Note note in m_editor.currentChart.notes)
                notes.Add(note);
        }
        return notes;
    }

    List<NoteGridCheck.Fix> FindFixes(List<Note> notes)
    {
        Song song = m_editor.currentSong;
        List<NoteGridCheck.Line> lines = new List<NoteGridCheck.Line>();
        foreach (TimeSignature ts in song.timeSignatures)
            lines.Add(new NoteGridCheck.Line { tick = ts.tick, beatTicks = song.resolution * 4.0 / Math.Max(1u, ts.denominator) });

        // Frets at each tick, for splitting stray chords in streams (5-fret guitar only)
        bool guitar = m_editor.currentChart != null && m_editor.currentChart.gameMode == Chart.GameMode.Guitar;
        Dictionary<uint, List<int>> frets = guitar ? new Dictionary<uint, List<int>>() : null;
        List<uint> ticks = new List<uint>();
        foreach (Note note in notes)
        {
            if (ticks.Count == 0 || ticks[ticks.Count - 1] != note.tick)
                ticks.Add(note.tick);
            if (frets != null)
            {
                List<int> at;
                if (!frets.TryGetValue(note.tick, out at))
                    frets[note.tick] = at = new List<int>();
                at.Add(note.rawNote);
            }
        }
        ticks.Sort();
        for (int i = ticks.Count - 1; i > 0; --i)
        {
            if (ticks[i] == ticks[i - 1])
                ticks.RemoveAt(i);
        }
        return NoteGridCheck.Find(ticks, lines, song.resolution, frets);
    }

    void Check()
    {
        m_checkedChart = m_editor.currentChart;
        if (m_editor.currentSong == null || m_checkedChart == null)
        {
            m_fixes = null;
            return;
        }

        List<Note> notes = NotesToCheck();
        m_fixes = FindFixes(notes);
        HashSet<uint> off = new HashSet<uint>();
        foreach (NoteGridCheck.Fix fix in m_fixes)
            off.Add(fix.from);
        m_notesOff = 0;
        foreach (Note note in notes)
        {
            if (off.Contains(note.tick))
                ++m_notesOff;
        }
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
            m_window.Draw("Fix Broken Notes", DrawWindow, scale);
    }

    void DrawWindow(int id)
    {
        Song song = m_editor.currentSong;
        if (song == null || m_fixes == null)
        {
            GUILayout.Label("Open a chart first.");
            DrawClose();
            return;
        }

        // Which notes: the whole chart (this instrument and difficulty) or the selected ones
        GUILayout.BeginHorizontal();
        GUILayout.Label("Check:", GUILayout.Width(62));
        if (GUILayout.Toggle(!m_selectedOnly, "Whole chart", "button") && m_selectedOnly)
        {
            m_selectedOnly = false;
            Check();
        }
        bool hasSelection = SelectedNotes().Count > 0;
        GUI.enabled = hasSelection || m_selectedOnly;
        if (GUILayout.Toggle(m_selectedOnly, "Selected notes", "button") && !m_selectedOnly)
        {
            m_selectedOnly = true;
            Check();
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        GUILayout.Label("Each beat (and half beat) is checked for the step its notes are charted at. Notes off it, chords that break a stream and extra notes are listed here.");

        if (m_fixes.Count == 0)
        {
            GUILayout.Label("No broken notes: every note is on its step.");
        }
        else
        {
            GUILayout.Label(m_notesOff + (m_notesOff == 1 ? " note is" : " notes are") + " off the step, at " + m_fixes.Count + (m_fixes.Count == 1 ? " place" : " places") + ":");
            m_scroll = GUILayout.BeginScrollView(m_scroll, GUILayout.MaxHeight(150));
            for (int i = 0; i < m_fixes.Count && i < MaxShown; ++i)
            {
                NoteGridCheck.Fix fix = m_fixes[i];
                int ticksOff = (int)fix.from - (int)fix.to;
                string what;
                if (fix.remove)
                    what = "extra note, removed";
                else if (fix.chordSplit)
                    what = "chord in a stream, split";
                else
                    what = Math.Abs(ticksOff) + (Math.Abs(ticksOff) == 1 ? " tick " : " ticks ") + (ticksOff > 0 ? "late" : "early");
                string label = "Go to " + Utility.timeConvertion(song.TickToTime(fix.from, song.resolution)) + "  (" + what + ", 1/" + fix.step + ")";
                if (GUILayout.Button(label))
                    JumpTo(fix.from);
            }
            if (m_fixes.Count > MaxShown)
                GUILayout.Label("...and " + (m_fixes.Count - MaxShown) + " more.");
            GUILayout.EndScrollView();
        }

        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUI.enabled = m_fixes.Count > 0;
        if (GUILayout.Button("Apply fixes"))
            Apply();
        GUI.enabled = true;
        if (GUILayout.Button("Check again", GUILayout.Width(110)))
        {
            Check();
            m_status = string.Empty;
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

    void JumpTo(uint tick)
    {
        if (m_editor.currentState != ChartEditor.State.Editor)
            return;
        m_editor.movement.SetPosition(tick);
    }

    // Moves every off note onto its step, as one step on the undo list
    void Apply()
    {
        if (m_editor.currentState != ChartEditor.State.Editor)
        {
            m_status = "Stop playing first.";
            return;
        }

        List<Note> notes = NotesToCheck();
        List<NoteGridCheck.Fix> fixes = FindFixes(notes);
        Dictionary<uint, uint> moves = new Dictionary<uint, uint>();
        Dictionary<long, uint> splits = new Dictionary<long, uint>();   // (tick, fret) -> tick
        HashSet<uint> removes = new HashSet<uint>();
        foreach (NoteGridCheck.Fix fix in fixes)
        {
            if (fix.remove)
                removes.Add(fix.from);
            else if (fix.fret >= 0)
                splits[((long)fix.from << 8) | (uint)fix.fret] = fix.to;
            else
                moves[fix.from] = fix.to;
        }

        List<SongObject> oldNotes = new List<SongObject>();
        List<SongObject> newNotes = new List<SongObject>();
        int removed = 0;
        foreach (Note note in notes)
        {
            if (removes.Contains(note.tick))
            {
                oldNotes.Add(note);
                ++removed;
                continue;
            }
            uint to;
            if (!splits.TryGetValue(((long)note.tick << 8) | (uint)note.rawNote, out to) && !moves.TryGetValue(note.tick, out to))
                continue;
            Note moved = note.CloneAs<Note>();
            moved.tick = to;
            oldNotes.Add(note);
            newNotes.Add(moved);
        }
        if (oldNotes.Count == 0)
        {
            m_status = "Nothing to fix.";
            Check();
            return;
        }

        List<SongEditCommand> commands = new List<SongEditCommand> { new SongEditDelete(oldNotes) };
        if (newNotes.Count > 0)
            commands.Add(new SongEditAdd(newNotes));
        m_editor.commandStack.Push(new BatchedSongEditCommand(commands));
        m_status = "Moved " + newNotes.Count + (newNotes.Count == 1 ? " note" : " notes") + " onto their steps"
            + (removed > 0 ? " and removed " + removed + " extra" + (removed == 1 ? " note" : " notes") : string.Empty) + ". Ctrl+Z undoes it.";
        m_editor.services.notificationBar.PushNotification("BROKEN NOTES FIXED", 2, true);
        if (m_selectedOnly)
            m_editor.selectedObjectsManager.TryFindAndSelectSongObjects(newNotes);
        Check();
    }
}
