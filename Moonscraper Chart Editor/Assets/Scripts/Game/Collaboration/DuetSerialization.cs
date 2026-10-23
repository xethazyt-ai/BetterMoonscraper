// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using MoonscraperChartEditor.Song;

/// <summary>
/// Converts song objects and recorded edits to and from the JSON used in Duet Mode messages.
/// Field names follow what a .chart file stores for each object.
/// </summary>
public static class DuetSerialization
{
    public static Dictionary<string, object> ToJson(SongObject songObject)
    {
        Dictionary<string, object> obj = new Dictionary<string, object>();
        obj["tick"] = (double)songObject.tick;

        switch ((SongObject.ID)songObject.classID)
        {
            case SongObject.ID.Note:
                {
                    Note note = (Note)songObject;
                    obj["kind"] = "note";
                    obj["fret"] = (double)note.rawNote;
                    obj["length"] = (double)note.length;
                    obj["flags"] = (double)(int)note.flags;
                    break;
                }
            case SongObject.ID.Starpower:
                {
                    Starpower sp = (Starpower)songObject;
                    obj["kind"] = "starpower";
                    obj["length"] = (double)sp.length;
                    obj["flags"] = (double)(int)sp.flags;
                    break;
                }
            case SongObject.ID.DrumRoll:
                {
                    DrumRoll roll = (DrumRoll)songObject;
                    obj["kind"] = "drumroll";
                    obj["length"] = (double)roll.length;
                    obj["rollType"] = (double)(int)roll.type;
                    break;
                }
            case SongObject.ID.ChartEvent:
                obj["kind"] = "chartevent";
                obj["text"] = ((ChartEvent)songObject).eventName;
                break;
            case SongObject.ID.Section:
                obj["kind"] = "section";
                obj["text"] = ((Section)songObject).title;
                break;
            case SongObject.ID.Event:
                obj["kind"] = "event";
                obj["text"] = ((Event)songObject).title;
                break;
            case SongObject.ID.BPM:
                {
                    BPM bpm = (BPM)songObject;
                    obj["kind"] = "bpm";
                    obj["value"] = (double)bpm.value;
                    if (bpm.anchor.HasValue)
                        obj["anchor"] = (double)bpm.anchor.Value;
                    break;
                }
            case SongObject.ID.TimeSignature:
                {
                    TimeSignature ts = (TimeSignature)songObject;
                    obj["kind"] = "timesignature";
                    obj["numerator"] = (double)ts.numerator;
                    obj["denominator"] = (double)ts.denominator;
                    break;
                }
            default:
                throw new ArgumentException("Unsupported song object " + songObject.GetType());
        }

        return obj;
    }

    public static SongObject SongObjectFromJson(Dictionary<string, object> obj)
    {
        uint tick = GetUInt(obj, "tick");
        string text = DuetJson.GetString(obj, "text", string.Empty);

        switch (DuetJson.GetString(obj, "kind"))
        {
            case "note":
                return new Note(tick, (int)DuetJson.GetNumber(obj, "fret"), GetUInt(obj, "length"), (Note.Flags)(int)DuetJson.GetNumber(obj, "flags"));
            case "starpower":
                return new Starpower(tick, GetUInt(obj, "length"), (Starpower.Flags)(int)DuetJson.GetNumber(obj, "flags"));
            case "drumroll":
                return new DrumRoll(tick, GetUInt(obj, "length"), (DrumRoll.Type)(int)DuetJson.GetNumber(obj, "rollType"));
            case "chartevent":
                return new ChartEvent(tick, text);
            case "section":
                return new Section(text, tick);
            case "event":
                return new Event(text, tick);
            case "bpm":
                {
                    float? anchor = null;
                    if (obj.ContainsKey("anchor"))
                        anchor = (float)DuetJson.GetNumber(obj, "anchor");
                    return new BPM(tick, GetUInt(obj, "value"), anchor);
                }
            case "timesignature":
                return new TimeSignature(tick, GetUInt(obj, "numerator"), GetUInt(obj, "denominator"));
        }

        throw new FormatException("Unknown song object kind " + DuetJson.GetString(obj, "kind"));
    }

    public static Dictionary<string, object> ToJson(SongEditChange change)
    {
        Dictionary<string, object> obj = new Dictionary<string, object>();

        switch (change.type)
        {
            case SongEditChange.Type.Add: obj["do"] = "add"; break;
            case SongEditChange.Type.Delete: obj["do"] = "delete"; break;
            default: obj["do"] = "modify"; break;
        }

        if (!change.location.isSongLevel)
        {
            obj["instrument"] = change.location.instrument.ToString();
            obj["difficulty"] = change.location.difficulty.ToString();
        }

        if (change.before != null)
            obj["before"] = ToJson(change.before);
        if (change.after != null)
            obj["after"] = ToJson(change.after);

        return obj;
    }

    public static SongEditChange ChangeFromJson(Dictionary<string, object> obj)
    {
        SongEditChange change = new SongEditChange();

        switch (DuetJson.GetString(obj, "do"))
        {
            case "add": change.type = SongEditChange.Type.Add; break;
            case "delete": change.type = SongEditChange.Type.Delete; break;
            case "modify": change.type = SongEditChange.Type.Modify; break;
            default: throw new FormatException("Unknown change " + DuetJson.GetString(obj, "do"));
        }

        string instrument = DuetJson.GetString(obj, "instrument");
        if (instrument == null)
        {
            change.location = ChartLocation.SongLevel;
        }
        else
        {
            change.location = new ChartLocation
            {
                isSongLevel = false,
                instrument = (Song.Instrument)Enum.Parse(typeof(Song.Instrument), instrument),
                difficulty = (Song.Difficulty)Enum.Parse(typeof(Song.Difficulty), DuetJson.GetString(obj, "difficulty")),
            };
        }

        Dictionary<string, object> before = DuetJson.GetObject(obj, "before");
        Dictionary<string, object> after = DuetJson.GetObject(obj, "after");
        change.before = before != null ? SongObjectFromJson(before) : null;
        change.after = after != null ? SongObjectFromJson(after) : null;

        return change;
    }

    public static List<object> ToJson(IList<SongEditChange> changes)
    {
        List<object> list = new List<object>(changes.Count);
        foreach (SongEditChange change in changes)
            list.Add(ToJson(change));
        return list;
    }

    public static List<SongEditChange> ChangesFromJson(List<object> list)
    {
        List<SongEditChange> changes = new List<SongEditChange>(list.Count);
        foreach (object item in list)
            changes.Add(ChangeFromJson((Dictionary<string, object>)item));
        return changes;
    }

    /// <summary>
    /// True when two song objects are the same kind at the same position with the same data.
    /// </summary>
    public static bool AreIdentical(SongObject a, SongObject b)
    {
        // SongObject overloads ==, so compare references for null
        if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
            return ReferenceEquals(a, b);

        return DuetJson.Write(ToJson(a)) == DuetJson.Write(ToJson(b));
    }

    static uint GetUInt(Dictionary<string, object> obj, string key)
    {
        return (uint)DuetJson.GetNumber(obj, key);
    }
}
