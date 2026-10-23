// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using MoonscraperChartEditor.Song;
using MoonscraperChartEditor.Song.IO;

/// <summary>
/// Development check for collaboration mode. Keeps a separate copy of the song, taken whenever edit history is
/// cleared (loading a song, switching charts), and applies every finished edit to it. Press F9 to compare that copy
/// with the editor's song. If they match, sending the same edits to another person reproduces your chart exactly.
///
/// Enabled in the Unity editor, or in a build launched with --duet-replay-check.
/// </summary>
public class SongEditReplayCheck : MoonscraperEngine.SystemManagerState.System
{
    public const string CommandLineFlag = "--duet-replay-check";
    const KeyCode CheckKey = KeyCode.F9;

    readonly SongEditGestureTracker m_tracker;
    Song m_replayedSong = null;
    int m_editsSinceSnapshot = 0;
    readonly List<string> m_applyFailures = new List<string>();

    public static bool IsEnabled()
    {
#if UNITY_EDITOR
        return true;
#else
        return Array.IndexOf(Environment.GetCommandLineArgs(), CommandLineFlag) >= 0;
#endif
    }

    public SongEditReplayCheck(SongEditGestureTracker tracker)
    {
        m_tracker = tracker;
        m_tracker.onEditFinished.Register(OnEditFinished);
        m_tracker.onHistoryCleared.Register(TakeSnapshot);
        DuetEditorSong.onRemoteEditApplied += OnRemoteEditApplied;
    }

    public override void SystemUpdate()
    {
        if (Input.GetKeyDown(CheckKey))
        {
            RunCheck();
        }
    }

    static string WorkingDirectory
    {
        get
        {
            string directory = Path.Combine(Application.persistentDataPath, "DuetReplayCheck");
            Directory.CreateDirectory(directory);
            return directory;
        }
    }

    void TakeSnapshot()
    {
        ChartEditor editor = ChartEditor.Instance;
        if (editor == null || editor.currentSong == null)
            return;

        try
        {
            string path = Path.Combine(WorkingDirectory, "snapshot" + MsceIOHelper.FileExtention);
            Write(editor.currentSong, path);
            m_replayedSong = ChartReader.ReadChart(path);
            m_editsSinceSnapshot = 0;
            m_applyFailures.Clear();
        }
        catch (Exception e)
        {
            m_replayedSong = null;
            Debug.LogError("Duet replay check: unable to take snapshot. " + e.Message);
        }
    }

    void OnEditFinished(in SongEditGestureTracker.FinishedEdit edit)
    {
        Replay(edit.kind.ToString(), edit.changes);
    }

    // Edits from the other person in a Duet session are part of the song too
    void OnRemoteEditApplied(IList<SongEditChange> changes)
    {
        Replay("Duet", changes);
    }

    void Replay(string kind, IList<SongEditChange> changes)
    {
        if (m_replayedSong == null)
            return;

        SongEditChangeApplier.Result result = SongEditChangeApplier.Apply(m_replayedSong, changes);
        ++m_editsSinceSnapshot;

        if (!result.success)
        {
            foreach (string failure in result.failures)
            {
                string message = string.Format("{0} edit #{1}: could not apply {2}", kind, m_editsSinceSnapshot, failure);
                m_applyFailures.Add(message);
                Debug.LogError("Duet replay check: " + message);
            }
        }
    }

    void RunCheck()
    {
        ChartEditor editor = ChartEditor.Instance;

        // Count an edit still in progress, such as a sustain being dragged
        m_tracker.FinishPending();

        if (m_replayedSong == null)
        {
            ShowResult("No snapshot yet. Load a chart and try again.");
            return;
        }

        string livePath = Path.Combine(WorkingDirectory, "live" + MsceIOHelper.FileExtention);
        string replayedPath = Path.Combine(WorkingDirectory, "replayed" + MsceIOHelper.FileExtention);

        Write(editor.currentSong, livePath);
        Write(m_replayedSong, replayedPath);

        // Song properties aren't tracked through edits yet, so only compare the chart data
        string[] liveLines = WithoutSongSection(File.ReadAllLines(livePath));
        string[] replayedLines = WithoutSongSection(File.ReadAllLines(replayedPath));

        StringBuilder report = new StringBuilder();
        int mismatchLine = FirstMismatch(liveLines, replayedLines);

        if (mismatchLine < 0 && m_applyFailures.Count <= 0)
        {
            report.AppendFormat("Duet replay check PASSED. {0} edits replayed, {1} chart lines identical.", m_editsSinceSnapshot, liveLines.Length);
            Debug.Log(report.ToString());
        }
        else
        {
            report.AppendFormat("Duet replay check FAILED after {0} edits.", m_editsSinceSnapshot);
            report.AppendLine();

            if (mismatchLine >= 0)
            {
                report.AppendFormat("First difference at chart line {0}:", mismatchLine + 1).AppendLine();
                report.Append("  editor:   ").AppendLine(mismatchLine < liveLines.Length ? liveLines[mismatchLine] : "<end of file>");
                report.Append("  replayed: ").AppendLine(mismatchLine < replayedLines.Length ? replayedLines[mismatchLine] : "<end of file>");
            }

            foreach (string failure in m_applyFailures)
            {
                report.AppendLine(failure);
            }

            report.AppendFormat("Files are in {0}", WorkingDirectory);
            Debug.LogError(report.ToString());
        }

        ShowResult(report.ToString());
    }

    static void ShowResult(string message)
    {
        NativeMessageBox.Show(message, "Duet replay check", NativeMessageBox.Type.OK, ChartEditor.Instance.windowHandleManager.nativeWindow);
    }

    static void Write(Song song, string path)
    {
        ChartWriter.ErrorReport errorReport;
        new ChartWriter(path).Write(song, song.defaultExportOptions, out errorReport);
    }

    static string[] WithoutSongSection(string[] lines)
    {
        List<string> result = new List<string>(lines.Length);
        bool inSongSection = false;

        foreach (string line in lines)
        {
            if (line.Trim() == "[Song]")
                inSongSection = true;
            else if (inSongSection && line.Trim() == "}")
            {
                inSongSection = false;
                continue;
            }

            if (!inSongSection)
                result.Add(line);
        }

        return result.ToArray();
    }

    static int FirstMismatch(string[] a, string[] b)
    {
        int count = Math.Min(a.Length, b.Length);
        for (int i = 0; i < count; ++i)
        {
            if (a[i] != b[i])
                return i;
        }

        return a.Length == b.Length ? -1 : count;
    }
}
