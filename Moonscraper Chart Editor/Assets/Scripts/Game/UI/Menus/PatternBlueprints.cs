// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using MoonscraperChartEditor.Song;

/// <summary>
/// Pattern blueprints: select some notes, save them as a named pattern, and later put the pattern back anywhere,
/// stretched or squeezed to another step (a 16th note pattern played as 8ths, say). Saved on this computer only,
/// in a file that every Moonscraper version reads. Opened with F9 (rebindable in Controls).
/// </summary>
public class PatternBlueprints : MonoBehaviour
{
    const int WindowId = 0x4250;
    const string FileName = "Pattern Blueprints.json";
    const float MinScale = 1 / 16f;
    const float MaxScale = 16f;

    [Serializable]
    public class BlueprintNote
    {
        public double beat;            // From the first note, in beats (quarter notes)
        public int rawNote;
        public int flags;
        public double lengthBeats;
    }

    [Serializable]
    public class Blueprint
    {
        public string name;
        public string gameMode;
        public int step;               // The spacing it was made with, as Moonscraper's step (16 = 16th notes). 0 if unknown.
        public List<BlueprintNote> notes = new List<BlueprintNote>();
    }

    [Serializable]
    class BlueprintFile
    {
        public List<Blueprint> blueprints = new List<Blueprint>();
    }

    static PatternBlueprints s_instance;

    readonly DuetWindow m_window = new DuetWindow(WindowId, "PatternBlueprints", new Rect(0, 0, 340, 10), new Vector2(280, 200), false, true);
    ChartEditor m_editor;
    BlueprintFile m_file = new BlueprintFile();
    bool m_visible = false;
    string m_newName = string.Empty;
    int m_selected = -1;
    float m_scale = 1;
    string m_targetStep = "16";
    string m_status = string.Empty;
    Vector2 m_listScroll;

    // IMGUI wants the same controls in every pass of a frame, so changes to the list wait for the end of the window
    bool m_pendingSave = false;
    int m_pendingDelete = -1;
    int m_pendingSelect = -1;

    public static bool isTyping { get; private set; }

    public static bool IsPointerOver()
    {
        if (s_instance == null || !s_instance.m_visible || DuetPanel.isHudHidden)
            return false;

        float scale = DuetPanel.GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        return s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
    }

    static string filePath { get { return Path.Combine(Application.persistentDataPath, FileName); } }

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
            editor.gameObject.AddComponent<PatternBlueprints>();
    }

    void Awake()
    {
        s_instance = this;
        m_editor = GetComponent<ChartEditor>();
        Load();
    }

    void OnDestroy()
    {
        if (s_instance == this)
            s_instance = null;
        isTyping = false;
    }

    void Update()
    {
        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.PatternBlueprints))
            Toggle();
    }

    public void Toggle()
    {
        m_visible = !m_visible;
        if (!m_visible)
            StopTyping();
    }

    void StopTyping()
    {
        isTyping = false;
        GUIUtility.keyboardControl = 0;
    }

    void Load()
    {
        try
        {
            if (File.Exists(filePath))
                m_file = JsonUtility.FromJson<BlueprintFile>(File.ReadAllText(filePath)) ?? new BlueprintFile();
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't read the pattern blueprints. " + e.Message);
            m_file = new BlueprintFile();
        }

        if (m_file.blueprints == null)
            m_file.blueprints = new List<Blueprint>();
    }

    void Save()
    {
        try
        {
            string temp = filePath + ".tmp";
            File.WriteAllText(temp, JsonUtility.ToJson(m_file, true));
            if (File.Exists(filePath))
                File.Delete(filePath);
            File.Move(temp, filePath);
        }
        catch (Exception e)
        {
            Debug.LogWarning("Couldn't save the pattern blueprints. " + e.Message);
            m_status = "Couldn't save: " + e.Message;
        }
    }

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
            m_window.PlaceOnce(new Vector2(Screen.width / scale - m_window.rect.width - 400, 60));

        // Clicking outside the window gives the keyboard back to the editor
        if (Event.current.type == EventType.MouseDown && !m_window.rect.Contains(Event.current.mousePosition))
            GUIUtility.keyboardControl = 0;

        if (DuetMenuCover.Covers(m_window.rect, scale))
            return;

        m_window.Draw("Pattern Blueprints", DrawWindow, scale);
        isTyping = GUIUtility.keyboardControl != 0;     // The name or step box has the keyboard
    }

    void DrawWindow(int id)
    {
        GUILayout.Label("Select notes, name the pattern, then save it. Pick a saved pattern to put it back, at any step.");

        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("BlueprintName");
        m_newName = GUILayout.TextField(m_newName, 40);
        if (GUILayout.Button("Save selection", GUILayout.Width(110)))
            m_pendingSave = true;
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        if (m_file.blueprints.Count == 0)
        {
            GUILayout.Label("No patterns saved yet.");
        }
        else
        {
            m_listScroll = GUILayout.BeginScrollView(m_listScroll, GUILayout.MaxHeight(160));
            for (int i = 0; i < m_file.blueprints.Count; ++i)
            {
                Blueprint blueprint = m_file.blueprints[i];
                GUILayout.BeginHorizontal();
                bool picked = GUILayout.Toggle(m_selected == i, blueprint.name + "  (" + Describe(blueprint, 1) + ")", "button");
                if (picked && m_selected != i)
                    m_pendingSelect = i;
                if (GUILayout.Button("X", GUILayout.Width(26)))
                    m_pendingDelete = i;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        if (m_selected >= 0 && m_selected < m_file.blueprints.Count)
            DrawSelected(m_file.blueprints[m_selected]);

        if (m_status.Length > 0)
            GUILayout.Label(m_status);

        ApplyPending();
    }

    void ApplyPending()
    {
        if (m_pendingSave)
        {
            m_pendingSave = false;
            SaveSelection();
        }

        if (m_pendingSelect >= 0 && m_pendingSelect < m_file.blueprints.Count)
        {
            Blueprint blueprint = m_file.blueprints[m_pendingSelect];
            m_selected = m_pendingSelect;
            m_scale = 1;
            m_targetStep = blueprint.step > 0 ? blueprint.step.ToString() : "16";
            m_status = string.Empty;
        }
        m_pendingSelect = -1;

        if (m_pendingDelete >= 0 && m_pendingDelete < m_file.blueprints.Count)
        {
            m_status = "Deleted \"" + m_file.blueprints[m_pendingDelete].name + "\".";
            m_file.blueprints.RemoveAt(m_pendingDelete);
            if (m_selected == m_pendingDelete)
                m_selected = -1;
            else if (m_selected > m_pendingDelete)
                --m_selected;
            Save();
        }
        m_pendingDelete = -1;
    }

    void DrawSelected(Blueprint blueprint)
    {
        GUILayout.Space(6);
        GUILayout.Label("Scale: x" + m_scale.ToString("0.###") + "  ->  " + Describe(blueprint, m_scale));

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Half as long"))
            m_scale = Mathf.Max(MinScale, m_scale / 2);
        if (GUILayout.Button("Twice as long"))
            m_scale = Mathf.Min(MaxScale, m_scale * 2);
        if (GUILayout.Button("Reset", GUILayout.Width(56)))
            m_scale = 1;
        GUILayout.EndHorizontal();

        if (blueprint.step > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Fit to step", GUILayout.Width(80));
            m_targetStep = GUILayout.TextField(m_targetStep, 4, GUILayout.Width(50));
            int target;
            if (GUILayout.Button("Fit") && int.TryParse(m_targetStep, out target) && target > 0)
                m_scale = Mathf.Clamp((float)blueprint.step / target, MinScale, MaxScale);
            GUILayout.EndHorizontal();
        }

        string currentMode = m_editor.currentChart != null ? m_editor.currentChart.gameMode.ToString() : string.Empty;
        if (!string.IsNullOrEmpty(blueprint.gameMode) && blueprint.gameMode != currentMode)
            GUILayout.Label("Made on a " + blueprint.gameMode + " chart, so lanes may not line up here.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Copy (then Ctrl+V)"))
        {
            if (CopyToClipboard(blueprint))
                m_status = "Copied. Paste it with Ctrl+V like any copied notes.";
        }
        if (GUILayout.Button("Paste at strikeline"))
        {
            if (CopyToClipboard(blueprint))
            {
                if (ClipboardObjectController.instance && m_editor.currentState == ChartEditor.State.Editor)
                {
                    ClipboardObjectController.instance.PasteAtCursor();
                    m_status = "Pasted. Ctrl+Z undoes it.";
                }
                else
                {
                    m_status = "Copied. Paste it with Ctrl+V.";
                }
            }
        }
        GUILayout.EndHorizontal();
    }

    static string Describe(Blueprint blueprint, float scale)
    {
        double lastBeat = 0;
        foreach (BlueprintNote note in blueprint.notes)
            lastBeat = Math.Max(lastBeat, note.beat);

        string text = blueprint.notes.Count + (blueprint.notes.Count == 1 ? " note" : " notes");
        if (blueprint.step > 0)
        {
            float step = blueprint.step / scale;
            text += ", " + (Mathf.Abs(step - Mathf.Round(step)) < 0.01f ? Mathf.RoundToInt(step).ToString() : step.ToString("0.##")) + " step";
        }
        return text + ", " + (lastBeat * scale).ToString("0.##") + " beats";
    }

    void SaveSelection()
    {
        Song song = m_editor.currentSong;
        List<Note> notes = new List<Note>();
        foreach (SongObject songObject in m_editor.selectedObjectsManager.currentSelectedObjects)
        {
            Note note = songObject as Note;
            if (note != null)
                notes.Add(note);
        }

        if (song == null || notes.Count == 0)
        {
            m_status = "Select some notes first.";
            return;
        }

        notes.Sort((a, b) => a.tick.CompareTo(b.tick));
        float resolution = song.resolution;
        uint first = notes[0].tick;

        Blueprint blueprint = new Blueprint();
        blueprint.name = m_newName.Trim().Length > 0 ? m_newName.Trim() : "Pattern " + (m_file.blueprints.Count + 1);
        blueprint.gameMode = m_editor.currentChart != null ? m_editor.currentChart.gameMode.ToString() : string.Empty;

        // The step is the smallest gap between notes, measured in beat lines
        double smallestGap = double.MaxValue;
        foreach (Note note in notes)
        {
            double beat = (note.tick - first) / (double)resolution;
            blueprint.notes.Add(new BlueprintNote
            {
                beat = beat,
                rawNote = note.rawNote,
                flags = (int)note.flags,
                lengthBeats = note.length / (double)resolution,
            });
        }
        for (int i = 1; i < blueprint.notes.Count; ++i)
        {
            double gap = blueprint.notes[i].beat - blueprint.notes[i - 1].beat;
            if (gap > 1e-6 && gap < smallestGap)
                smallestGap = gap;
        }
        blueprint.step = smallestGap < double.MaxValue ? Mathf.Clamp((int)Math.Round(4 / smallestGap), 1, 768) : 0;

        m_file.blueprints.Add(blueprint);
        m_selected = m_file.blueprints.Count - 1;
        m_scale = 1;
        m_targetStep = blueprint.step > 0 ? blueprint.step.ToString() : "16";
        m_newName = string.Empty;
        m_status = "Saved \"" + blueprint.name + "\".";
        Save();
    }

    // Puts the scaled pattern on Moonscraper's own clipboard, so pasting works (and undoes, and reaches Collab) as usual
    bool CopyToClipboard(Blueprint blueprint)
    {
        Song song = m_editor.currentSong;
        if (song == null || blueprint.notes.Count == 0)
        {
            m_status = "Open a chart first.";
            return false;
        }

        float resolution = song.resolution;
        List<SongObject> notes = new List<SongObject>();
        uint last = 0;
        foreach (BlueprintNote saved in blueprint.notes)
        {
            uint tick = (uint)Math.Round(saved.beat * m_scale * resolution);
            uint length = (uint)Math.Round(saved.lengthBeats * m_scale * resolution);
            notes.Add(new Note(tick, saved.rawNote, length, (Note.Flags)saved.flags));
            last = Math.Max(last, tick);
        }
        notes.Sort((a, b) => a.tick.CompareTo(b.tick));

        // Covers every lane, so pasting replaces whatever notes were already there, as a normal paste does
        Clipboard.SelectionArea area = new Clipboard.SelectionArea(new Rect(-2, 0, 4, 0), 0, last);
        ClipboardObjectController.SetData(notes.ToArray(), area, song);
        return true;
    }
}
