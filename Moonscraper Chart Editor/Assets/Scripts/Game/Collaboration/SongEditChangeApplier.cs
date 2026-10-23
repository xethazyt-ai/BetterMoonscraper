// See LICENSE in project root for license information.

using System.Collections.Generic;
using MoonscraperChartEditor.Song;

/// <summary>
/// Applies recorded song edit changes directly to a Song's data. Doesn't touch the editor, its views or
/// its command history, so it can run against any Song, such as a copy kept for comparison.
/// </summary>
public static class SongEditChangeApplier
{
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

    static SongObject Find(Song song, Chart chart, SongObject songObject)
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
