// See LICENSE in project root for license information.

// Makes edits through the real song edit command stack, sends every finished edit to a second copy of the song
// (the way edits will go to the other person in a session), and checks both copies write identical charts.

using System;
using System.Collections.Generic;
using System.IO;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;

public static class ReplayTests
{
    static Song s_live;
    static Song s_replayed;
    static ChartEditor s_editor;
    static SongEditCommandStack s_stack;
    static SongEditGestureTracker s_tracker;
    static int s_finishedEdits = 0;
    static int s_failures = 0;

    static Chart GuitarExpert { get { return s_live.GetChart(Song.Instrument.Guitar, Song.Difficulty.Expert); } }
    static Chart DrumsHard { get { return s_live.GetChart(Song.Instrument.Drums, Song.Difficulty.Hard); } }

    public static int Run()
    {
        s_live = new Song();
        s_replayed = new Song();

        s_editor = new ChartEditor { currentSong = s_live, currentChart = GuitarExpert };
        ChartEditor.Instance = s_editor;

        s_stack = new SongEditCommandStack();
        s_tracker = new SongEditGestureTracker(s_stack);
        s_tracker.onEditFinished.Register(OnEditFinished);

        // Keys held while pushing, so nothing is reported until Check releases them
        UnityEngine.Input.anyKey = true;

        s_stack.Push(new SongEditAdd(new Note(0, (int)Note.GuitarFret.Green)));
        s_stack.Push(new SongEditAdd(new Note(0, (int)Note.GuitarFret.Red)));
        Check("place a chord");

        s_stack.Push(new SongEditAdd(new Note(192, (int)Note.GuitarFret.Yellow, 96)));
        Check("place a sustain");

        s_stack.Push(new SongEditAdd(new Note(192, (int)Note.GuitarFret.Yellow, 0, Note.Flags.Forced)));
        Check("place over an existing note");

        s_stack.Push(new SongEditAdd(new BPM(768, 150000)));
        s_stack.Push(new SongEditAdd(new Section("Guitar Solo", 768)));
        s_stack.Push(new SongEditAdd(new Starpower(0, 384)));
        Check("BPM, section and star power");

        s_stack.Pop();
        Check("undo star power");
        s_stack.Push();
        Check("redo star power");

        // A sustain drag: the tool replaces its own last command on every mouse move
        s_stack.Push(new SongEditAdd(new Note(384, (int)Note.GuitarFret.Blue)));
        for (uint length = 24; length <= 96; length += 24)
        {
            s_stack.Pop();
            s_stack.Push(new SongEditAdd(new Note(384, (int)Note.GuitarFret.Blue, length)));
        }
        int editsBeforeRelease = s_finishedEdits;
        Check("sustain drag");
        Expect("sustain drag is reported as one edit", s_finishedEdits - editsBeforeRelease == 1);

        s_stack.Push(new SongEditModify<BPM>(new BPM(768, 150000), new BPM(768, 170000)));
        Check("change a BPM");

        s_stack.Push(new SongEditModify<Section>(new Section("Guitar Solo", 768), new Section("Outro", 768)));
        Check("rename a section");

        s_stack.Push(new SongEditDelete(new Note(0, (int)Note.GuitarFret.Red)));
        Check("delete a chord note");

        // An edit made against Hard Drums must land there even though Expert Guitar is on screen when it runs
        s_editor.currentChart = DrumsHard;
        SongEditAdd drumNote = new SongEditAdd(new Note(96, (int)Note.DrumPad.Red));
        s_editor.currentChart = GuitarExpert;
        int guitarNotesBefore = GuitarExpert.notes.Count;
        s_stack.Push(drumNote);
        Check("edit a chart that isn't on screen");
        Expect("drum note went to Hard Drums", DrumsHard.notes.Count == 1 && GuitarExpert.notes.Count == guitarNotesBefore);

        s_stack.Pop();
        Check("undo the off-screen edit");
        Expect("drum note was removed from Hard Drums", DrumsHard.notes.Count == 0);

        s_stack.Push(new BatchedSongEditCommand(new List<SongEditCommand>
        {
            new SongEditDelete(new Note(192, (int)Note.GuitarFret.Yellow)),
            new SongEditAdd(new Note(288, (int)Note.GuitarFret.Yellow)),
        }));
        Check("move a note");

        for (int i = 0; i < 12; ++i)
            s_stack.Pop();
        Check("undo everything");

        for (int i = 0; i < 12; ++i)
            s_stack.Push();
        Check("redo everything");

        // The lyric editor applies edits as you go on its own stack, then pushes them to the main stack
        // as one command that doesn't apply anything the first time it's invoked
        SongEditAdd lyric1 = new SongEditAdd(new Event("lyric Hel-", 960));
        SongEditAdd lyric2 = new SongEditAdd(new Event("lyric lo", 1056));
        lyric1.Invoke();
        lyric2.Invoke();
        s_stack.Push(new AlreadyAppliedCommand(new List<SongEditCommand> { lyric1, lyric2 }));
        Check("lyric editor closes");
        s_stack.Pop();
        Check("undo lyrics");
        s_stack.Push();
        Check("redo lyrics");

        return s_failures;
    }

    static void OnEditFinished(in SongEditGestureTracker.FinishedEdit edit)
    {
        ++s_finishedEdits;

        SongEditChangeApplier.Result result = SongEditChangeApplier.Apply(s_replayed, edit.changes);
        foreach (string failure in result.failures)
        {
            Console.WriteLine("  could not apply: " + failure);
        }
    }

    // Releases all keys so pending edits are reported, then compares the two songs
    static void Check(string label)
    {
        UnityEngine.Input.anyKey = false;
        s_tracker.SystemUpdate();
        UnityEngine.Input.anyKey = true;

        string live = ChartText(s_live, "live");
        string replayed = ChartText(s_replayed, "replayed");
        Expect(label, live == replayed);

        if (live != replayed)
        {
            Console.WriteLine("--- editor ---\n" + live + "\n--- replayed ---\n" + replayed);
        }
    }

    static void Expect(string label, bool condition)
    {
        Console.WriteLine((condition ? "PASS " : "FAIL ") + label);
        if (!condition)
            ++s_failures;
    }

    // The song's chart data as saved, without the [Song] properties section
    static string ChartText(Song song, string name)
    {
        string path = Path.Combine(Path.GetTempPath(), "duet-" + name + MsceIOHelper.FileExtention);
        ChartWriter.ErrorReport errorReport;
        new ChartWriter(path).Write(song, song.defaultExportOptions, out errorReport);

        List<string> lines = new List<string>(File.ReadAllLines(path));
        int start = lines.IndexOf("[Song]");
        int end = lines.IndexOf("}", start);
        lines.RemoveRange(start, end - start + 1);

        return string.Join("\n", lines);
    }

    class AlreadyAppliedCommand : MoonscraperEngine.ICommand
    {
        readonly List<SongEditCommand> m_commands;
        BatchedSongEditCommand m_batch = null;

        public AlreadyAppliedCommand(List<SongEditCommand> commands)
        {
            m_commands = commands;
        }

        public void Invoke()
        {
            if (m_batch == null)
                m_batch = new BatchedSongEditCommand(m_commands);
            else
                m_batch.Invoke();
        }

        public void Revoke()
        {
            m_batch?.Revoke();
        }
    }
}
