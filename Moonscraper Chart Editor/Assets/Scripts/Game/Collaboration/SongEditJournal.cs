// See LICENSE in project root for license information.

using System.Collections.Generic;
using MoonscraperEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Identifies a chart within a song by instrument and difficulty, independent of any particular Song instance.
/// Song-level objects (BPMs, time signatures, sections, global events) have no chart and use SongLevel.
/// </summary>
public struct ChartLocation
{
    public static readonly ChartLocation SongLevel = new ChartLocation { isSongLevel = true };

    public bool isSongLevel;
    public Song.Instrument instrument;
    public Song.Difficulty difficulty;

    public static bool TryFind(Song song, Chart chart, out ChartLocation location)
    {
        foreach (Song.Instrument instrument in EnumX<Song.Instrument>.Values)
        {
            if (instrument == Song.Instrument.Unrecognised)
                continue;

            foreach (Song.Difficulty difficulty in EnumX<Song.Difficulty>.Values)
            {
                if (song.GetChart(instrument, difficulty) == chart)
                {
                    location = new ChartLocation { isSongLevel = false, instrument = instrument, difficulty = difficulty };
                    return true;
                }
            }
        }

        location = default(ChartLocation);
        return false;
    }

    public Chart GetChart(Song song)
    {
        return isSongLevel ? null : song.GetChart(instrument, difficulty);
    }

    public override string ToString()
    {
        return isSongLevel ? "Song" : instrument + " " + difficulty;
    }
}

public struct SongEditChange
{
    public enum Type
    {
        Add,
        Delete,
        Modify,
    }

    public Type type;
    public ChartLocation location;

    // Detached clones. Add uses after, Delete uses before, Modify uses both.
    public SongObject before;
    public SongObject after;

    public override string ToString()
    {
        switch (type)
        {
            case Type.Add: return string.Format("Add {0} {1}", location, Describe(after));
            case Type.Delete: return string.Format("Delete {0} {1}", location, Describe(before));
            default: return string.Format("Modify {0} {1} -> {2}", location, Describe(before), Describe(after));
        }
    }

    public static string Describe(SongObject songObject)
    {
        Note note = songObject as Note;
        if (note != null)
            return string.Format("Note(tick {0}, fret {1}, length {2}, flags {3})", note.tick, note.rawNote, note.length, note.flags);

        return string.Format("{0}(tick {1})", songObject.GetType().Name, songObject.tick);
    }
}

/// <summary>
/// Records every low-level change made to the song while a command on the song edit command stack is invoked or revoked.
/// The recorded list is the exact effect of the command, including side effects such as overwrites,
/// forced-flag corrections and BPM anchor fixups, so it can be replayed on another copy of the song.
///
/// Changes made by commands outside the main stack are held as "unattached" and handed to the next stack change.
/// The lyric editor works this way: it applies edits as you go on its own stack, then pushes them to the
/// main stack as one already-applied command when it closes.
/// </summary>
public static class SongEditJournal
{
    static List<SongEditChange> s_changes = null;
    static List<SongEditChange> s_unattachedChanges = new List<SongEditChange>();

    static int s_suppressDepth = 0;

    public static bool isRecording { get { return s_changes != null; } }

    /// <summary>
    /// Changes made inside this scope aren't recorded anywhere. Used when applying someone else's edits,
    /// which must not be reported as our own.
    /// </summary>
    public static SuppressScope Suppress()
    {
        ++s_suppressDepth;
        return new SuppressScope();
    }

    public struct SuppressScope : System.IDisposable
    {
        public void Dispose()
        {
            --s_suppressDepth;
        }
    }

    // Starts a recording. Unattached changes happened before it, so they're included first.
    public static void Begin()
    {
        if (isRecording)
        {
            UnityEngine.Debug.LogError("SongEditJournal.Begin called while already recording. Discarding previous recording.");
        }

        s_changes = new List<SongEditChange>(s_unattachedChanges);
        s_unattachedChanges.Clear();
    }

    public static void DiscardUnattachedChanges()
    {
        s_unattachedChanges.Clear();
    }

    public static List<SongEditChange> End()
    {
        List<SongEditChange> changes = s_changes ?? new List<SongEditChange>();
        s_changes = null;
        return changes;
    }

    public static void RecordAdd(SongObject added)
    {
        Record(SongEditChange.Type.Add, added, null, added);
    }

    public static void RecordDelete(SongObject deleted)
    {
        Record(SongEditChange.Type.Delete, deleted, deleted, null);
    }

    public static void RecordModify(SongObject before, SongObject after)
    {
        Record(SongEditChange.Type.Modify, after, before, after);
    }

    // songObjectInSong is the live object, used to find which chart it belongs to.
    static void Record(SongEditChange.Type type, SongObject songObjectInSong, SongObject before, SongObject after)
    {
        if (s_suppressDepth > 0)
            return;

        List<SongEditChange> changes = s_changes ?? s_unattachedChanges;

        ChartLocation location = ChartLocation.SongLevel;
        ChartObject chartObject = songObjectInSong as ChartObject;
        if (chartObject != null)
        {
            Chart chart = chartObject.chart ?? SongEditTarget.chart;
            if (!ChartLocation.TryFind(ChartEditor.Instance.currentSong, chart, out location))
            {
                // Unrecognised charts can't be addressed by instrument and difficulty
                UnityEngine.Debug.LogWarning("SongEditJournal: unable to find chart location for " + songObjectInSong);
                return;
            }
        }

        changes.Add(new SongEditChange
        {
            type = type,
            location = location,
            before = before != null ? before.Clone() : null,
            after = after != null ? after.Clone() : null,
        });
    }
}
