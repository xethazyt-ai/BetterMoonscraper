// See LICENSE in project root for license information.

using System.Collections.Generic;
using MoonscraperChartEditor.Song;

/// <summary>
/// Something recorded song edit changes can be applied to: a Song's raw data, or the song open in the editor.
/// </summary>
public interface ISongEditChangeTarget
{
    // The object in the song with the same identity (kind, position and lane or text), or null
    SongObject Find(ChartLocation location, SongObject songObject);
    void Apply(SongEditChange change);
}

/// <summary>
/// Applies recorded song edit changes directly to a Song's data. Doesn't touch the editor, its views or
/// its command history, so it can run against any Song, such as a copy kept for comparison.
/// </summary>
public static class SongEditChangeApplier
{
    public class SongDataTarget : ISongEditChangeTarget
    {
        readonly Song m_song;

        public SongDataTarget(Song song)
        {
            m_song = song;
        }

        public SongObject Find(ChartLocation location, SongObject songObject)
        {
            return SongEditChangeApplier.Find(m_song, location.GetChart(m_song), songObject);
        }

        public void Apply(SongEditChange change)
        {
            if (!ApplyChange(m_song, change))
                UnityEngine.Debug.LogError("Unable to apply " + change);

            m_song.UpdateCache();
            m_song.UpdateAllChartCaches();
        }
    }

    /// <summary>
    /// Applies the changes only if each one finds the song exactly as it expects: nothing already in the way of an add,
    /// and the exact object it recorded for a delete or modify. Otherwise undoes what it applied and returns false,
    /// leaving the song as it was.
    /// </summary>
    public static bool TryApplyStrict(ISongEditChangeTarget target, IList<SongEditChange> changes, out string failure)
    {
        List<SongEditChange> applied = new List<SongEditChange>(changes.Count);

        foreach (SongEditChange change in changes)
        {
            if (!CanApply(target, change))
            {
                failure = "Song doesn't match " + change;

                for (int i = applied.Count - 1; i >= 0; --i)
                    target.Apply(Inverse(applied[i]));

                return false;
            }

            target.Apply(change);
            applied.Add(change);
        }

        failure = null;
        return true;
    }

    static bool CanApply(ISongEditChangeTarget target, SongEditChange change)
    {
        switch (change.type)
        {
            case SongEditChange.Type.Add:
                return target.Find(change.location, change.after) == null;

            default:
                SongObject found = target.Find(change.location, change.before);
                return found != null && DuetSerialization.AreIdentical(found, change.before);
        }
    }

    public static SongEditChange Inverse(SongEditChange change)
    {
        SongEditChange inverse = change;
        inverse.before = change.after;
        inverse.after = change.before;

        switch (change.type)
        {
            case SongEditChange.Type.Add: inverse.type = SongEditChange.Type.Delete; break;
            case SongEditChange.Type.Delete: inverse.type = SongEditChange.Type.Add; break;
        }

        return inverse;
    }

    public struct Result
    {
        public int applied;
        public List<string> failures;

        public bool success { get { return failures.Count <= 0; } }
    }

    public static Result Apply(Song song, IList<SongEditChange> changes)
    {
        Result result = new Result { applied = 0, failures = new List<string>() };

        foreach (SongEditChange change in changes)
        {
            if (ApplyChange(song, change))
                ++result.applied;
            else
                result.failures.Add(change.ToString());
        }

        song.UpdateCache();
        song.UpdateAllChartCaches();

        return result;
    }

    static bool ApplyChange(Song song, SongEditChange change)
    {
        Chart chart = change.location.GetChart(song);

        switch (change.type)
        {
            case SongEditChange.Type.Add:
                return Add(song, chart, change.after.Clone());

            case SongEditChange.Type.Delete:
                return Delete(song, chart, change.before);

            case SongEditChange.Type.Modify:
                return Modify(song, chart, change.before, change.after);
        }

        return false;
    }

    static bool Add(Song song, Chart chart, SongObject songObject)
    {
        ChartObject chartObject = songObject as ChartObject;
        if (chartObject != null)
        {
            if (chart == null)
                return false;

            chart.Add(chartObject, false);
            return true;
        }

        Event eventObject = songObject as Event;
        if (eventObject != null)
        {
            song.Add(eventObject, false);
            return true;
        }

        SyncTrack syncTrack = songObject as SyncTrack;
        if (syncTrack != null)
        {
            song.Add(syncTrack, false);
            return true;
        }

        return false;
    }

    static bool Delete(Song song, Chart chart, SongObject songObject)
    {
        SongObject found = Find(song, chart, songObject);
        if (found == null)
            return false;

        ChartObject chartObject = found as ChartObject;
        if (chartObject != null)
            return chart.Remove(chartObject, false);

        Event eventObject = found as Event;
        if (eventObject != null)
            return song.Remove(eventObject, false);

        SyncTrack syncTrack = found as SyncTrack;
        if (syncTrack != null)
            return song.Remove(syncTrack, false);

        return false;
    }

    static bool Modify(Song song, Chart chart, SongObject before, SongObject after)
    {
        SongObject found = Find(song, chart, before);
        if (found == null)
            return false;

        switch ((SongObject.ID)found.classID)
        {
            case SongObject.ID.Note:
                (found as Note).CopyFrom(after as Note);
                return true;

            case SongObject.ID.Starpower:
                (found as Starpower).CopyFrom(after as Starpower);
                return true;

            case SongObject.ID.DrumRoll:
                (found as DrumRoll).CopyFrom(after as DrumRoll);
                return true;

            case SongObject.ID.ChartEvent:
                (found as ChartEvent).CopyFrom(after as ChartEvent);
                return true;

            case SongObject.ID.BPM:
                (found as BPM).CopyFrom(after as BPM);
                return true;

            case SongObject.ID.TimeSignature:
                (found as TimeSignature).CopyFrom(after as TimeSignature);
                return true;

            case SongObject.ID.Section:
            case SongObject.ID.Event:
                (found as Event).CopyFrom(after as Event);
                return true;
        }

        return false;
    }

    public static SongObject Find(Song song, Chart chart, SongObject songObject)
    {
        int index;

        ChartObject chartObject = songObject as ChartObject;
        if (chartObject != null)
        {
            if (chart == null)
                return null;

            index = SongObjectHelper.FindObjectPosition(chartObject, chart.chartObjects);
            return index != SongObjectHelper.NOTFOUND ? chart.chartObjects[index] : null;
        }

        Event eventObject = songObject as Event;
        if (eventObject != null)
        {
            index = SongObjectHelper.FindObjectPosition(eventObject, song.eventsAndSections);
            return index != SongObjectHelper.NOTFOUND ? song.eventsAndSections[index] : null;
        }

        SyncTrack syncTrack = songObject as SyncTrack;
        if (syncTrack != null)
        {
            index = SongObjectHelper.FindObjectPosition(syncTrack, song.syncTrack);
            return index != SongObjectHelper.NOTFOUND ? song.syncTrack[index] : null;
        }

        return null;
    }
}
