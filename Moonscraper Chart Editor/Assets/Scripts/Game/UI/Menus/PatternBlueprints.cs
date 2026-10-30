// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using MoonscraperChartEditor.Song;

/// <summary>
/// Pattern blueprints: select some notes, save them as a named pattern, and later put the pattern back anywhere.
/// Fit to step re-spaces the same notes at another step (a 32nd pattern as 16ths takes twice the beat lines). The
/// speed slider changes the step too: above 1x the pattern repeats, whole, to fill the beat lines it took (2x of four
/// 16ths is eight 32nds), below 1x it spreads over more of them (0.5x takes twice as many). Saved on this computer only,
/// in a file that every Moonscraper version reads. Opened with F5; Ctrl+B pastes the picked pattern where the mouse
/// points on the highway (or at the strikeline), even with the window closed. Both are rebindable in Controls.
/// With notes selected, Ctrl+B stamps those notes instead. Pressing it again without moving the mouse puts the next copy
/// straight after the last one, so a pattern can be run out as long as needed.
/// </summary>
public partial class PatternBlueprints : MonoBehaviour
{
    const int WindowId = 0x4250;
    const string FileName = "Pattern Blueprints.json";
    const float MinSpeed = 0.25f;
    const float MaxSpeed = 4f;
    const int MaxStep = 768;

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
    int m_fitStep = 16;             // The step the pattern is re-spaced at
    float m_speed = 1;
    bool m_mirror = false;          // Green and orange swap sides, and so on
    int m_laneShift = 0;            // Lanes to move the pattern by: + towards orange, - towards green
    string m_targetStep = "16";
    string m_status = string.Empty;
    Vector2 m_listScroll;

    // IMGUI wants the same controls in every pass of a frame, so changes to the list wait for the end of the window
    bool m_pendingSave = false;
    int m_pendingDelete = -1;
    int m_pendingSelect = -1;
    bool m_pasteRequested = false;
    bool m_pasteAtMouse = false;
    bool m_showHelp = false;

    public static bool isTyping { get; private set; }

    public static bool IsPointerOver()
    {
        if (s_instance == null || (!s_instance.m_visible && !s_instance.m_libraryVisible) || DuetPanel.isHudHidden)
            return false;

        float scale = DuetPanel.GuiScale;
        Vector2 pointer = new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale);
        bool overMain = s_instance.m_visible && s_instance.m_window.rect.Contains(pointer) && !DuetMenuCover.Covers(s_instance.m_window.rect, scale);
        return overMain || s_instance.IsPointerOverLibrary(pointer, scale);
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

        // Not while a menu is open, or over the Collab Mode window: both use Ctrl+B for bold
        if (!Services.IsTyping && m_editor.currentState == ChartEditor.State.Editor && !DuetPanel.IsPointerOverWindow()
            && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.PatternBlueprintPaste))
        {
            if (SelectedNotes().Count > 0 || m_stampNotes != null || (m_selected >= 0 && m_selected < m_file.blueprints.Count))
            {
                m_pasteRequested = true;
                m_pasteAtMouse = true;
            }
            else
                m_editor.services.notificationBar.PushNotification("SELECT SOME NOTES, OR PICK A PATTERN IN PATTERN BLUEPRINTS (F5), FIRST", 3, true);
        }

        if (m_packRequested != 0)
        {
            bool import = m_packRequested == 1;
            m_packRequested = 0;
            if (import)
                ImportPack();
            else
                ExportPack();
        }

        if (m_pasteRequested)
        {
            m_pasteRequested = false;
            Paste(m_pasteAtMouse);
            m_pasteAtMouse = false;
        }
    }

    // The last Ctrl+B, so pressing it again on the same spot carries on after the last copy
    SongObject[] m_stampNotes;          // What it pasted, as it went on the clipboard
    Clipboard.SelectionArea m_stampArea;
    uint m_stampSpan;                   // Ticks from one copy's start to the next
    uint m_stampTarget;                 // Where the mouse (or strikeline) was
    Chart m_stampChart;
    string m_stampName;
    string m_stampSource;               // "selection", or the pattern and its settings

    const string SelectionSource = "\n selection";

    List<Note> SelectedNotes()
    {
        List<Note> notes = new List<Note>();
        if (m_editor.currentState != ChartEditor.State.Editor)
            return notes;
        foreach (SongObject songObject in m_editor.selectedObjectsManager.currentSelectedObjects)
        {
            Note note = songObject as Note;
            if (note != null && note.chart == m_editor.currentChart)
                notes.Add(note);
        }
        return notes;
    }

    void Paste(bool atMouse)
    {
        if (m_editor.currentState != ChartEditor.State.Editor)
        {
            m_status = "Stop playing first, then paste.";
            return;
        }

        ClipboardObjectController clipboard = ClipboardObjectController.instance ? ClipboardObjectController.instance : FindObjectOfType<ClipboardObjectController>();
        Song song = m_editor.currentSong;
        if (!clipboard || song == null)
        {
            m_status = "Couldn't find Moonscraper's paste tool. Use Copy, then Ctrl+V.";
            return;
        }

        // Ctrl+B stamps the selected notes if there are any; the window's Paste button always pastes the pattern
        List<Note> selected = atMouse ? SelectedNotes() : new List<Note>();
        Blueprint blueprint = m_selected >= 0 && m_selected < m_file.blueprints.Count ? m_file.blueprints[m_selected] : null;
        string blueprintSource = blueprint == null ? null : blueprint.name + "|" + m_fitStep + "|" + m_speed + "|" + m_mirror + "|" + m_laneShift;
        // With nothing selected, Ctrl+B carries on with the notes it stamped last, until a pattern is picked
        bool reuseStamp = selected.Count == 0 && atMouse && m_stampSource == SelectionSource && m_stampNotes != null && m_stampChart == m_editor.currentChart;
        string source = selected.Count > 0 || reuseStamp ? SelectionSource : blueprintSource;

        uint target = clipboard.MouseOrStrikelineTick(atMouse);
        uint start = target;
        bool repeat = m_stampNotes != null && m_stampChart == m_editor.currentChart && target == m_stampTarget && source == m_stampSource;
        if (repeat)
        {
            // Goes after the copies still there, so one that was undone or had notes deleted is filled in again
            for (int copies = 0; copies < 10000 && IsCopyAt(start); ++copies)
                start += m_stampSpan;
        }
        else
        {
            bool built = selected.Count > 0 ? StampFromSelection(selected) : reuseStamp || (blueprint != null && StampFromBlueprint(blueprint));
            if (!built)
            {
                m_stampNotes = null;
                m_stampSource = null;
                m_editor.services.notificationBar.PushNotification("SELECT SOME NOTES, OR PICK A PATTERN IN PATTERN BLUEPRINTS (F5), FIRST", 3, true);
                return;
            }
            m_stampChart = m_editor.currentChart;
            m_stampTarget = target;
            m_stampSource = source;
        }

        ClipboardObjectController.SetData(m_stampNotes, m_stampArea, song);
        clipboard.PasteAndReset(start);
        m_status = "Pasted " + m_stampName + ". Ctrl+B again here adds another copy after it. Ctrl+Z undoes it.";
        m_editor.services.notificationBar.PushNotification("PASTED " + m_stampName.ToUpperInvariant() + ". CTRL+B AGAIN REPEATS IT", 2, true);
    }

    // Whether every note of the stamp is still in the chart, starting here
    bool IsCopyAt(uint start)
    {
        Chart chart = m_editor.currentChart;
        if (chart == null)
            return false;
        foreach (SongObject songObject in m_stampNotes)
        {
            Note stamped = songObject as Note;
            if (stamped == null)
                continue;
            int index, length;
            SongObjectHelper.FindObjectsAtPosition(start + stamped.tick, chart.notes, out index, out length);
            bool found = false;
            for (int i = index; i < index + length && !found; ++i)
                found = chart.notes[i].rawNote == stamped.rawNote;
            if (!found)
                return false;
        }
        return true;
    }

    // The selected notes as they are. The next copy starts one note gap after the last note, so a run of 9 notes
    // between two lines repeats from the next line. Steps that don't divide into ticks have gaps a tick apart, so the
    // gap is the average of the shortest ones.
    bool StampFromSelection(List<Note> selected)
    {
        Song song = m_editor.currentSong;
        selected.Sort((a, b) => a.tick.CompareTo(b.tick));
        uint first = selected[0].tick, last = selected[selected.Count - 1].tick;

        List<uint> gaps = new List<uint>();
        for (int i = 1; i < selected.Count; ++i)
        {
            if (selected[i].tick > selected[i - 1].tick)
                gaps.Add(selected[i].tick - selected[i - 1].tick);
        }
        double gap = song.resolution * 4.0 / Math.Max(1, Globals.gameSettings.step);
        if (gaps.Count > 0)
        {
            uint smallest = uint.MaxValue;
            foreach (uint g in gaps)
                smallest = Math.Min(smallest, g);
            double total = 0;
            int count = 0;
            foreach (uint g in gaps)
            {
                if (g <= smallest + 1)
                {
                    total += g;
                    ++count;
                }
            }
            gap = total / count;
        }

        List<SongObject> notes = new List<SongObject>();
        foreach (Note note in selected)
        {
            Note copy = new Note(note);
            copy.tick = note.tick - first;
            notes.Add(copy);
        }

        m_stampNotes = notes.ToArray();
        m_stampArea = new Clipboard.SelectionArea(new Rect(-2, 0, 4, 0), 0, last - first);
        m_stampSpan = Math.Max(1u, (uint)Math.Round(last - first + gap));
        m_stampName = selected.Count == 1 ? "the selected note" : "the " + selected.Count + " selected notes";
        return true;
    }

    bool StampFromBlueprint(Blueprint blueprint)
    {
        double span;
        SongObject[] notes;
        uint last;
        if (!BuildNotes(blueprint, out notes, out last, out span))
            return false;

        // Covers every lane, so pasting replaces whatever notes were already there, as a normal paste does
        m_stampNotes = notes;
        m_stampArea = new Clipboard.SelectionArea(new Rect(-2, 0, 4, 0), 0, last);
        m_stampSpan = Math.Max(1u, (uint)Math.Round(span * m_editor.currentSong.resolution));
        m_stampName = "\"" + blueprint.name + "\"";
        return true;
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
        if ((!m_visible && !m_libraryVisible) || m_editor == null || DuetPanel.isHudHidden)
        {
            if (isTyping)
                StopTyping();
            return;
        }

        float scale = DuetPanel.GuiScale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

        if (!m_window.isPlaced)
            m_window.PlaceOnce(new Vector2(Screen.width / scale - m_window.rect.width - 400, 60));

        // Clicking outside the windows gives the keyboard back to the editor
        Vector2 mouse = UnityEngine.Event.current.mousePosition;
        if (UnityEngine.Event.current.type == EventType.MouseDown && !(m_visible && m_window.rect.Contains(mouse))
            && !(m_libraryVisible && m_libraryWindow.rect.Contains(mouse)))
            GUIUtility.keyboardControl = 0;

        if (m_visible && !DuetMenuCover.Covers(m_window.rect, scale))
            m_window.Draw("Pattern Blueprints", DrawWindow, scale);
        DrawLibrary(scale);
        isTyping = GUIUtility.keyboardControl != 0;     // The name, step or search box has the keyboard
    }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        m_showHelp = GUILayout.Toggle(m_showHelp, m_showHelp ? "Hide how it works" : "How it works", "button");
        m_libraryVisible = GUILayout.Toggle(m_libraryVisible, "Library", "button", GUILayout.Width(80));
        GUILayout.EndHorizontal();
        if (m_showHelp)
        {
            GUILayout.Label("1. Select the notes of a pattern (drag a box with the cursor tool).");
            GUILayout.Label("2. Type a name and press Save selection. It's kept on this computer for every BetterMoonscraper version.");
            GUILayout.Label("3. Pick the pattern, point the mouse where it should start on the highway and press Ctrl+B (works with this window closed too). Or press Paste pattern to put it at the strikeline. Ctrl+Z undoes it.");
            GUILayout.Label("Fit to step: type a step (8, 12, 16, 24, 32...) and press Fit. The same notes are re-spaced at that step, so 32nds fitted to 16 take twice the beat lines.");
            GUILayout.Label("Library: every saved pattern drawn as a small chart, to scroll through, search, pick and paste.");
            GUILayout.Label("Mirror swaps the pattern left to right (green and orange trade places). The arrows move it a lane towards green or orange, as far as it fits.");
            GUILayout.Label("Packs: Export pack saves every pattern to a file to share; Import pack adds the patterns from one.");
            GUILayout.Label("Speed: above 1x the pattern repeats faster in the same beat lines (2x of four 16ths is eight 32nds), always finishing the last repeat. Below 1x it spreads out over more beat lines (0.5x takes twice as many).");
        }

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
                bool picked = GUILayout.Toggle(m_selected == i, blueprint.name + "  (" + Describe(blueprint, OriginalStep(blueprint), 1) + ")", "button");
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

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Import pack..."))
            m_pendingImport = true;
        GUI.enabled = m_file.blueprints.Count > 0;
        if (GUILayout.Button("Export pack..."))
            m_pendingExport = true;
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        if (m_status.Length > 0)
            GUILayout.Label(m_status);

        ApplyPending();
    }

    void ApplyPending()
    {
        if (m_pendingImport || m_pendingExport)
        {
            bool import = m_pendingImport;
            m_pendingImport = m_pendingExport = false;
            m_packRequested = import ? 1 : 2;     // The file window blocks, so it opens from Update, away from drawing
        }

        if (m_pendingSave)
        {
            m_pendingSave = false;
            SaveSelection();
        }

        if (m_pendingSelect >= 0 && m_pendingSelect < m_file.blueprints.Count)
        {
            Blueprint blueprint = m_file.blueprints[m_pendingSelect];
            m_selected = m_pendingSelect;
            ResetShape(blueprint);
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
        GUILayout.Label("Pastes as: " + Describe(blueprint, m_fitStep, m_speed));

        GUILayout.BeginHorizontal();
        GUILayout.Label("Fit to step", GUILayout.Width(80));
        m_targetStep = GUILayout.TextField(m_targetStep, 3, GUILayout.Width(50));
        int target;
        if (GUILayout.Button("Fit", GUILayout.Width(50)) && int.TryParse(m_targetStep, out target) && target > 0)
            m_fitStep = Mathf.Clamp(target, 1, MaxStep);
        if (GUILayout.Button("Reset", GUILayout.Width(56)))
            ResetShape(blueprint);
        GUILayout.EndHorizontal();

        int minShift, maxShift;
        ShiftRange(blueprint, m_mirror, out minShift, out maxShift);
        m_laneShift = Mathf.Clamp(m_laneShift, minShift, maxShift);
        GUILayout.BeginHorizontal();
        m_mirror = GUILayout.Toggle(m_mirror, "Mirror", "button", GUILayout.Width(70));
        GUILayout.Label("Lanes", GUILayout.Width(44));
        GUI.enabled = m_laneShift > minShift;
        if (GUILayout.Button("<", GUILayout.Width(28)))
            --m_laneShift;
        GUI.enabled = m_laneShift < maxShift;
        if (GUILayout.Button(">", GUILayout.Width(28)))
            ++m_laneShift;
        GUI.enabled = true;
        GUILayout.Label(m_laneShift == 0 ? "as saved" : (m_laneShift > 0 ? "+" : string.Empty) + m_laneShift);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed " + m_speed.ToString("0.##") + "x", GUILayout.Width(80));
        float speed = GUILayout.HorizontalSlider(m_speed, MinSpeed, MaxSpeed);
        m_speed = Mathf.Clamp(Mathf.Round(speed * 4) / 4, MinSpeed, MaxSpeed);   // Quarter steps: 0.5x, 1.5x, 2x...
        if (GUILayout.Button("1x", GUILayout.Width(32)))
            m_speed = 1;
        GUILayout.EndHorizontal();

        string currentMode = m_editor.currentChart != null ? m_editor.currentChart.gameMode.ToString() : string.Empty;
        if (!string.IsNullOrEmpty(blueprint.gameMode) && blueprint.gameMode != currentMode)
            GUILayout.Label("Made on a " + blueprint.gameMode + " chart, so lanes may not line up here.");

        if (GUILayout.Button("Paste pattern (Ctrl+B)", GUILayout.Height(30)))
            m_pasteRequested = true;    // Done in Update, away from drawing the window. The mouse is on this window, so it goes at the strikeline.
    }

    static int OriginalStep(Blueprint blueprint)
    {
        return blueprint.step > 0 ? blueprint.step : 16;
    }

    void ResetShape(Blueprint blueprint)
    {
        m_fitStep = OriginalStep(blueprint);
        m_targetStep = m_fitStep.ToString();
        m_speed = 1;
        m_mirror = false;
        m_laneShift = 0;

        // Picking or saving a pattern means Ctrl+B pastes that, not the notes stamped last
        m_stampNotes = null;
        m_stampSource = null;
    }

    static string Describe(Blueprint blueprint, int fitStep, float speed)
    {
        int count;
        double span;
        Arrange(blueprint, fitStep, speed, false, 0, out count, out span);

        float step = fitStep * speed;
        string stepText = Mathf.Abs(step - Mathf.Round(step)) < 0.01f ? Mathf.RoundToInt(step).ToString() : step.ToString("0.##");
        return count + (count == 1 ? " note" : " notes") + ", " + stepText + " step, " + span.ToString("0.##") + " beats";
    }

    struct Placed
    {
        public double beat;
        public double lengthBeats;
        public BlueprintNote note;
        public int rawNote;         // After Mirror and the lane shift
    }

    // Where each note goes. The pattern is cut into slots one step apart (a chord shares a slot, a rest is an empty
    // one), re-spaced at fitStep and then sped up. Faster than 1x, it plays again until it fills the beat lines it
    // took at 1x, always finishing the last repeat (1.5x plays it twice); slower, it plays once over more of them.
    static List<Placed> Arrange(Blueprint blueprint, int fitStep, float speed, bool mirror, int laneShift, out int noteCount, out double span)
    {
        double originalGap = 4.0 / OriginalStep(blueprint);
        double gap = 4.0 / Math.Max(1, fitStep) / speed;
        double factor = gap / originalGap;

        int slotCount = 0;
        foreach (BlueprintNote note in blueprint.notes)
            slotCount = Math.Max(slotCount, (int)Math.Round(note.beat / originalGap) + 1);
        slotCount = Math.Max(1, slotCount);

        int repeats = speed > 1 ? (int)Math.Ceiling(speed - 1e-4) : 1;
        int playedSlots = repeats * slotCount;
        List<Placed> placed = new List<Placed>();
        for (int repeat = 0; repeat * slotCount < playedSlots; ++repeat)
        {
            foreach (BlueprintNote note in blueprint.notes)
            {
                int slot = (int)Math.Round(note.beat / originalGap);
                int playedSlot = repeat * slotCount + slot;
                placed.Add(new Placed
                {
                    beat = playedSlot * gap + (note.beat - slot * originalGap) * factor,    // Keeps any off-grid nudge
                    lengthBeats = note.lengthBeats * factor,
                    note = note,
                    rawNote = MapLane(blueprint, note.rawNote, mirror, laneShift),
                });
            }
        }

        noteCount = placed.Count;
        span = playedSlots * gap;
        return placed;
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
        ResetShape(blueprint);
        m_newName = string.Empty;
        m_status = "Saved \"" + blueprint.name + "\".";
        Save();
    }

    // The scaled pattern as notes from tick 0, for Moonscraper's own clipboard, so pasting works (and undoes, and
    // reaches Collab) as usual
    bool BuildNotes(Blueprint blueprint, out SongObject[] result, out uint last, out double span)
    {
        result = null;
        last = 0;
        span = 0;
        Song song = m_editor.currentSong;
        if (song == null || blueprint.notes.Count == 0)
        {
            m_status = "Open a chart first.";
            return false;
        }

        float resolution = song.resolution;
        List<SongObject> notes = new List<SongObject>();
        int count;
        foreach (Placed placed in Arrange(blueprint, m_fitStep, m_speed, m_mirror, m_laneShift, out count, out span))
        {
            uint tick = (uint)Math.Round(placed.beat * resolution);
            uint length = (uint)Math.Round(placed.lengthBeats * resolution);
            notes.Add(new Note(tick, placed.rawNote, length, (Note.Flags)placed.note.flags));
            last = Math.Max(last, tick);
        }
        notes.Sort((a, b) => a.tick.CompareTo(b.tick));
        result = notes.ToArray();
        return true;
    }
}
