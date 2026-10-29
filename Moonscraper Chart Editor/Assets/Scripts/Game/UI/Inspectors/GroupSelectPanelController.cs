// Copyright (c) 2016-2020 Alexander Ong
// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using MoonscraperChartEditor.Song;

public class GroupSelectPanelController : MonoBehaviour
{
    ChartEditor editor;
    [SerializeField]
    Dropdown fretSelectDropdown;
    [SerializeField]
    Dropdown drumsFretSelectDropdown;
    [SerializeField]
    Dropdown ghlFretSelectDropdown;
    [SerializeField]
    Dropdown drums4LaneSelectDropdown;
    [SerializeField]
    Button setNoteNatural;
    [SerializeField]
    Button setNoteStrum;
    [SerializeField]
    Button setNoteHopo;
    [SerializeField]
    Button setNoteTap;
    [SerializeField]
    Button setNoteTom;
    [SerializeField]
    Button setNoteCymbal;
    [SerializeField]
    Button setNoteDyncamicsNone;
    [SerializeField]
    Button setNoteAccent;
    [SerializeField]
    Button setNoteGhost;
    [SerializeField]
    Button setDoubleKick;
    [SerializeField]
    Button altDoubleKick;
    [SerializeField]
    Text hitStrengthSubTitle;
    [SerializeField]
    Text kickTypeSubTitle;

    Dictionary<Chart.GameMode, Dropdown> laneSelectForGamemodeLookup = new Dictionary<Chart.GameMode, Dropdown>();
    Dictionary<Chart.GameMode, Dictionary<int, Dropdown>> laneSelectLaneCountOverrideLookup = new Dictionary<Chart.GameMode, Dictionary<int, Dropdown>>();
    Dropdown currentFretSelector = null;

    Dictionary<MSChartEditorInputActions, Button> shortcutBindings;

    // Use this for initialization
    void Start () {
        shortcutBindings = new Dictionary<MSChartEditorInputActions, Button>()
        {
            { MSChartEditorInputActions.NoteSetNatural, setNoteNatural },
            { MSChartEditorInputActions.NoteSetStrum, setNoteStrum },
            { MSChartEditorInputActions.NoteSetHopo, setNoteHopo },
            { MSChartEditorInputActions.NoteSetTap, setNoteTap},
            { MSChartEditorInputActions.NoteSetTom, setNoteTom },
            { MSChartEditorInputActions.NoteSetCymbal, setNoteCymbal },
            { MSChartEditorInputActions.NoteSetDynamicsNone, setNoteDyncamicsNone },
            { MSChartEditorInputActions.NoteSetAccent, setNoteAccent },
            { MSChartEditorInputActions.NoteSetGhost, setNoteGhost },
            { MSChartEditorInputActions.NoteSetDoubleKick, setDoubleKick },
            { MSChartEditorInputActions.NoteSetAltDoubleKick, altDoubleKick },
        };

        // Setup lane selector dictionaries and hide all selector varients
        {
            laneSelectForGamemodeLookup[Chart.GameMode.Guitar] = fretSelectDropdown;
            laneSelectForGamemodeLookup[Chart.GameMode.Drums] = drumsFretSelectDropdown;
            laneSelectForGamemodeLookup[Chart.GameMode.GHLGuitar] = ghlFretSelectDropdown;

            var drumsOverrideLaneSelectDict = new Dictionary<int, Dropdown>();
            drumsOverrideLaneSelectDict[4] = drums4LaneSelectDropdown;
            laneSelectLaneCountOverrideLookup[Chart.GameMode.Drums] = drumsOverrideLaneSelectDict;

            currentFretSelector = laneSelectForGamemodeLookup[Chart.GameMode.Guitar];

            foreach (var dropKeyVal in laneSelectForGamemodeLookup)
            {
                dropKeyVal.Value.gameObject.SetActive(false);
            }

            foreach (var overrideKeyVal in laneSelectLaneCountOverrideLookup)
            {
                foreach (var dropKeyVal in overrideKeyVal.Value)
                {
                    dropKeyVal.Value.gameObject.SetActive(false);
                }
            }
        }

        editor = ChartEditor.Instance;
        BuildStepInfo();
        editor.events.chartReloadedEvent.Register(UpdateUIActiveness);
        editor.events.lanesChangedEvent.Register(OnLanesChanged);
        editor.events.drumsModeOptionChangedEvent.Register(UpdateUIActiveness);

        UpdateUIActiveness();

    }

    Dropdown GetCurrentFretSelector(Chart.GameMode gameMode, int laneCount)
    {
        Dropdown dropdown = fretSelectDropdown;

        Dictionary<int, Dropdown> overrideLookup;
        if (!(laneSelectLaneCountOverrideLookup.TryGetValue(gameMode, out overrideLookup) && overrideLookup.TryGetValue(laneCount, out dropdown)))
        {
            // No overrides present, go with the defaults
            laneSelectForGamemodeLookup.TryGetValue(gameMode, out dropdown);
        }

        return dropdown;
    }

    void Update()
    {
        if (!Services.IsTyping && !Globals.modifierInputActive)
            UpdateShortcuts();

        UpdateStepInfo();

        // Its own key (Ctrl+E by default), which holds a modifier, so it's checked outside the shortcuts above
        if (!Services.IsTyping && MSChartEditorInput.GetInputDown(MSChartEditorInputActions.EqualSpacing))
            EqualiseNoteSpacing();
    }

    // "Step- 16" and "NPS- 13.33" above Flags-, for the selected notes
    Text m_stepText;
    Text m_npsText;
    int m_stepInfoCount = -1;
    uint m_stepInfoFirst, m_stepInfoLast;
    float m_stepInfoCheckedAt;

    void BuildStepInfo()
    {
        Text flags = null;
        foreach (Text text in GetComponentsInChildren<Text>(true))
        {
            if (text.text.Trim() == "Flags-")
                flags = text;
        }
        if (!flags)
        {
            Debug.LogWarning("Selection panel layout has changed, so the step and notes per second weren't added");
            return;
        }

        int index = flags.transform.GetSiblingIndex();
        m_stepText = MakeInfoText(flags, "Step Info", index);
        m_npsText = MakeInfoText(flags, "NPS Info", index + 1);
    }

    static Text MakeInfoText(Text template, string name, int siblingIndex)
    {
        Text text = Instantiate(template, template.transform.parent);
        text.name = name;
        text.text = string.Empty;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.transform.SetSiblingIndex(siblingIndex);
        return text;
    }

    void UpdateStepInfo()
    {
        if (!m_stepText || !editor)
            return;

        // Only worked out again when the selection looks different, or every half second (the BPM may change)
        IList<SongObject> selected = editor.selectedObjectsManager.currentSelectedObjects;
        uint first = selected.Count > 0 ? selected[0].tick : 0;
        uint last = selected.Count > 0 ? selected[selected.Count - 1].tick : 0;
        if (selected.Count == m_stepInfoCount && first == m_stepInfoFirst && last == m_stepInfoLast && Time.unscaledTime - m_stepInfoCheckedAt < 0.5f)
            return;
        m_stepInfoCount = selected.Count;
        m_stepInfoFirst = first;
        m_stepInfoLast = last;
        m_stepInfoCheckedAt = Time.unscaledTime;

        // Chords count once, as in Clone Hero
        List<Note> positions = new List<Note>();
        foreach (SongObject songObject in selected)
        {
            Note note = songObject as Note;
            if (note != null && (positions.Count == 0 || positions[positions.Count - 1].tick != note.tick))
                positions.Add(note);
        }
        positions.Sort((a, b) => a.tick.CompareTo(b.tick));
        for (int i = positions.Count - 1; i > 0; --i)
        {
            if (positions[i].tick == positions[i - 1].tick)
                positions.RemoveAt(i);
        }

        m_stepText.text = "Step- " + StepText(positions);
        m_npsText.text = "NPS- " + NpsText(positions);
    }

    // The step the notes are spaced at, like the Step box: notes per four beats. Gaps may be a tick out from rounding
    // (sevenths and the like), but anything more and the notes aren't all on one step.
    string StepText(List<Note> positions)
    {
        if (positions.Count < 2)
            return "N/A";

        uint smallest = uint.MaxValue, largest = 0;
        for (int i = 1; i < positions.Count; ++i)
        {
            uint gap = positions[i].tick - positions[i - 1].tick;
            smallest = System.Math.Min(smallest, gap);
            largest = System.Math.Max(largest, gap);
        }
        if (largest - smallest > 1 || smallest == 0)
            return "N/A";

        double averageGap = (double)(positions[positions.Count - 1].tick - positions[0].tick) / (positions.Count - 1);
        double step = editor.currentSong.resolution * 4 / averageGap;
        double rounded = System.Math.Round(step);
        if (System.Math.Abs(step - rounded) < 0.02)
            return ((int)rounded).ToString();
        return step.ToString("0.##");
    }

    // Notes per second through the selection: how many notes follow the first, over the time from the first to the
    // last, so even 16ths at 200 BPM read 13.33. Chords count as one note.
    static string NpsText(List<Note> positions)
    {
        if (positions.Count < 2)
            return "N/A";

        double seconds = positions[positions.Count - 1].time - positions[0].time;
        if (seconds <= 0)
            return "N/A";
        return ((positions.Count - 1) / seconds).ToString("0.##");
    }

    void OnLanesChanged(in int laneCount)
    {
        UpdateUIActiveness();
    }

    void UpdateUIActiveness()
    {
        currentFretSelector.gameObject.SetActive(false);
        currentFretSelector = GetCurrentFretSelector(editor.currentGameMode, editor.laneInfo.laneCount);
        currentFretSelector.gameObject.SetActive(true);

        bool drumsMode = Globals.drumMode;
        bool proDrumsMode = drumsMode && Globals.gameSettings.drumsModeOptions == GameSettings.DrumModeOptions.ProDrums;
        bool doubleKickActive = proDrumsMode && ChartEditor.Instance.currentDifficulty == Song.Difficulty.Expert;
        setNoteNatural.gameObject.SetActive(!drumsMode);
        setNoteStrum.gameObject.SetActive(!drumsMode);
        setNoteHopo.gameObject.SetActive(!drumsMode);
        setNoteTap.gameObject.SetActive(!drumsMode);
        setNoteTom.gameObject.SetActive(drumsMode);
        setNoteCymbal.gameObject.SetActive(proDrumsMode);
        setNoteAccent.gameObject.SetActive(proDrumsMode);
        setNoteDyncamicsNone.gameObject.SetActive(proDrumsMode);
        setNoteGhost.gameObject.SetActive(proDrumsMode);
        setDoubleKick.gameObject.SetActive(doubleKickActive);
        altDoubleKick.gameObject.SetActive(doubleKickActive);
        hitStrengthSubTitle.gameObject.SetActive(proDrumsMode);
        kickTypeSubTitle.gameObject.SetActive(proDrumsMode);
    }

    void UpdateShortcuts()
    {
        foreach (var kv in shortcutBindings)
        {
            if (MSChartEditorInput.GetInputDown(kv.Key) && kv.Value.isActiveAndEnabled)
            {
                kv.Value.onClick.Invoke();
            }
        }
    }

    int GetOpenNoteForGameMode(Chart.GameMode gameMode)
    {
        int rawNoteValue = 0;
        switch (gameMode)
        {
            case Chart.GameMode.Guitar:
                {
                    rawNoteValue = (int)Note.GuitarFret.Open;
                    break;
                }
            case Chart.GameMode.Drums:
                {
                    rawNoteValue = (int)Note.DrumPad.Kick;
                    break;
                }
            case Chart.GameMode.GHLGuitar:
                {
                    rawNoteValue = (int)Note.GHLiveGuitarFret.Open;
                    break;
                }
            default:
                {
                    Debug.Assert(false, "Unhandled open note selection for gamemode " + editor.currentChart.gameMode);
                    break;
                }
        }

        return rawNoteValue;
    }

    public void ApplyFretDropdownSelection()
    {
        Dropdown activeDropDown = currentFretSelector;

        int totalLanesPlusOpen = editor.laneInfo.laneCount + 1;
        if (activeDropDown && activeDropDown.value >= 0 && activeDropDown.value < totalLanesPlusOpen)
        {
            int rawNoteValue = activeDropDown.value;
            if (activeDropDown.value == editor.laneInfo.laneCount)
            {
                // Set to be the open note
                rawNoteValue = GetOpenNoteForGameMode(editor.currentGameMode);
            }

            SetFretType(rawNoteValue);
        }
    }

    public void SetFretType(int noteNumber)
    {
        List<SongObject> selected = new List<SongObject>();

        List<SongEditCommand> deleteCommands = new List<SongEditCommand>();
        List<SongEditCommand> addCommands = new List<SongEditCommand>();
        
        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note && chartObject.song != null) // check null in case note was already deleted when overwritten by changing a note before it
            {
                Note note = chartObject as Note;
                if (note.rawNote != noteNumber)
                {
                    Note newNote = new Note(note);
                    newNote.rawNote = noteNumber;

                    deleteCommands.Add(new SongEditDelete(note));
                    addCommands.Add(new SongEditAdd(newNote));
                    selected.Add(newNote);
                }
            }
            else
                selected.Add(chartObject);
        }

        // Delete commands must come first, as add commands can overwrite notes we might try to delete later
        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();
        songEditCommands.AddRange(deleteCommands);
        songEditCommands.AddRange(addCommands);

        editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        editor.selectedObjectsManager.TryFindAndSelectSongObjects(selected);
    }

    public void SetZeroSustain()
    {
        SetSustain(0);
    }

    public void SetMaxSustain()
    {
        SetSustain(uint.MaxValue);
    }

    void SetSustain(uint length)
    {
        uint songEndTick = editor.currentSong.TimeToTick(editor.currentSongLength, editor.currentSong.resolution);

        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();

        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                Note note = chartObject as Note;
                uint assignedLength = length;
                if (length == uint.MaxValue)
                    assignedLength = songEndTick - note.tick;

                songEditCommands.Add(new SongEditModifyValidated(note, new Note(note.tick, note.rawNote, assignedLength, note.flags)));
            }
        }

        if (songEditCommands.Count > 0)
            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
    }

    public void SetNatural()
    {
        SetNoteType(Note.NoteType.Natural);
    }

    public void SetStrum()
    {
        SetNoteType(Note.NoteType.Strum);
    }

    public void SetHopo()
    {
        SetNoteType(Note.NoteType.Hopo);
    }

    public void SetTap()
    {
        SetNoteType(Note.NoteType.Tap);
    }

    public void SetTom()
    {
        SetNoteType(Note.NoteType.Natural, Note.Flags.ProDrums_Accent | Note.Flags.ProDrums_Ghost | Note.Flags.DoubleKick);
    }

    public void SetCymbal()
    {
        SetNoteType(Note.NoteType.Cymbal, Note.Flags.ProDrums_Accent | Note.Flags.ProDrums_Ghost | Note.Flags.DoubleKick);
    }

    public void SetNoDynamics()
    {
        SetDynamics(Note.Flags.None, Note.Flags.ProDrums_Ghost | Note.Flags.ProDrums_Accent);
    }

    public void SetAccent()
    {
        SetDynamics(Note.Flags.ProDrums_Accent, Note.Flags.ProDrums_Ghost);
    }

    public void SetGhost()
    {
        SetDynamics(Note.Flags.ProDrums_Ghost, Note.Flags.ProDrums_Accent);
    }

    void SetNoteType(Note.NoteType type, Note.Flags flagsToKeep = Note.Flags.None)
    {
        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();
        List<ChartObject> objectsToSelect = new List<ChartObject>();

        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                Note note = chartObject as Note;
                Note newNote = new Note(note);
                newNote.flags = note.GetFlagsToSetType(type);
                if (flagsToKeep != Note.Flags.None)
                {
                    newNote.flags |= note.flags & flagsToKeep;
                }
                songEditCommands.Add(new SongEditModifyValidated(note, newNote));
                objectsToSelect.Add(newNote);
            }
        }

        if (songEditCommands.Count > 0)
        {
            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        }
    }

    void SetDynamics(Note.Flags flag, Note.Flags flagToExclude)
    {
        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();
        List<ChartObject> objectsToSelect = new List<ChartObject>();

        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                Note note = chartObject as Note;
                if (note.IsOpenNote())
                    continue;
                Note newNote = new Note(note);
                newNote.flags |= flag;
                if ((newNote.flags & flagToExclude) != Note.Flags.None)
                    newNote.flags &= ~flagToExclude;
                songEditCommands.Add(new SongEditModifyValidated(note, newNote));
                objectsToSelect.Add(newNote);
            }
        }

        if (songEditCommands.Count > 0)
        {
            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        }
    }

    public void SetDoubleKick()
    {
        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();

        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                Note note = chartObject as Note;
                if (note.IsOpenNote() && (note.flags & Note.Flags.DoubleKick) == 0)
                {
                    Note newNote = new Note(note);
                    newNote.flags |= Note.Flags.DoubleKick;

                    songEditCommands.Add(new SongEditModifyValidated(note, newNote));
                }
            }
        }

        if (songEditCommands.Count > 0)
        {
            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        }
    }

    public void AlternateDoubleKick()
    {
        List<SongEditCommand> songEditCommands = new List<SongEditCommand>();

        bool applyDoubleKick = false;
        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                Note note = chartObject as Note;
                if (note.IsOpenNote())
                {
                    Note newNote = new Note(note);

                    if (applyDoubleKick)
                    {
                        newNote.flags |= Note.Flags.DoubleKick;
                    }
                    else
                    {
                        newNote.flags &= ~Note.Flags.DoubleKick;
                    }

                    songEditCommands.Add(new SongEditModifyValidated(note, newNote));
                    applyDoubleKick = !applyDoubleKick;
                }
            }
        }

        if (songEditCommands.Count > 0)
        {
            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        }
    }

    public void EqualiseNoteSpacing()
    {
        List<SongObject> notesToDelete = new List<SongObject>();
        List<SongObject> notesToAdd = new List<SongObject>();

        uint? startTickMaybe = null;
        uint? endTickMaybe = null;
        int totalNotes = 0;
        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID == (int)SongObject.ID.Note)
            {
                if (!startTickMaybe.HasValue)
                {
                    startTickMaybe = chartObject.tick;
                }

                if (!endTickMaybe.HasValue || chartObject.tick > endTickMaybe.Value)
                {
                    ++totalNotes;   // Only 1 note for chords
                }

                endTickMaybe = chartObject.tick;
            }
        }

        if (startTickMaybe.HasValue && endTickMaybe.HasValue && totalNotes > 1)
        {
            uint startTick = startTickMaybe.Value;
            uint endTick = endTickMaybe.Value;
            uint tickDiff = endTick - startTick;

            // A real step instead of whatever the span divides into: the step in 8s (1/8, 1/16, 1/24, 1/32...) whose
            // spacing makes the notes fill the span best. The first note stays put; the last one lands on the step.
            double wholeNote = editor.currentSong.resolution * 4;
            double offsetPerNote = wholeNote / 8;
            double bestMiss = double.MaxValue;
            for (int step = 8; wholeNote / step >= 1; step += 8)
            {
                double spacing = wholeNote / step;
                double miss = System.Math.Abs(spacing * (totalNotes - 1) - tickDiff);
                if (miss < bestMiss)
                {
                    bestMiss = miss;
                    offsetPerNote = spacing;
                }
            }

            int index = -1;
            uint lastTickAdded = uint.MaxValue;
            foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
            {
                if (chartObject.classID == (int)SongObject.ID.Note)
                {
                    // Keep chords at the same tick
                    if (lastTickAdded != chartObject.tick)
                    {
                        ++index;
                    }

                    uint newTick = startTick + (uint)System.Math.Round(offsetPerNote * index);
                    Note newNote = chartObject.CloneAs<Note>();
                    newNote.tick = newTick;

                    notesToDelete.Add(chartObject);
                    notesToAdd.Add(newNote);
                    
                    lastTickAdded = chartObject.tick;
                }
            }
        }

        if (notesToDelete.Count > 0 && notesToAdd.Count > 0)
        {
            List<SongEditCommand> songEditCommands = new List<SongEditCommand>();
            songEditCommands.Add(new SongEditDelete(notesToDelete));
            songEditCommands.Add(new SongEditAdd(notesToAdd));

            editor.commandStack.Push(new BatchedSongEditCommand(songEditCommands));
        }

        // Reset the highlights back to the objects that were shifted
        List<SongObject> highlightObjects = new List<SongObject>();
        foreach (ChartObject chartObject in editor.selectedObjectsManager.currentSelectedObjects)
        {
            if (chartObject.classID != (int)SongObject.ID.Note)
            {
                highlightObjects.Add(chartObject);
            }
        }

        foreach (ChartObject chartObject in notesToAdd)
        {
            int pos = SongObjectHelper.FindObjectPosition(chartObject, editor.currentChart.chartObjects);

            if (pos != SongObjectHelper.NOTFOUND)
            {
                highlightObjects.Add(editor.currentChart.chartObjects[pos]);
            }
            else
            {
                Debug.LogError("Unable to re-find note after EqualiseNoteSpacing has been invoked.");
            }
        }

        editor.selectedObjectsManager.SetCurrentSelectedObjects(highlightObjects);
    }
}
