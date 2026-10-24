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
        // Edits come from other people's computers. Turn away values the editor can't draw or time, which would
        // otherwise throw errors every frame or break the song's timing for everyone.
        uint tick = GetUInt(obj, "tick");
        string text = DuetJson.GetString(obj, "text", string.Empty);
        if (text.Length > MaxTextLength)
            throw new FormatException("Text too long");

        switch (DuetJson.GetString(obj, "kind"))
        {
            case "note":
                {
                    double fret = DuetJson.GetNumber(obj, "fret");
                    if (!(fret >= 0 && fret <= DuetPresence.MaxRawNote))
                        throw new FormatException("Bad note " + fret);
                    return new Note(tick, (int)fret, GetUInt(obj, "length"), (Note.Flags)(int)DuetJson.GetNumber(obj, "flags"));
                }
            case "starpower":
                return new Starpower(tick, GetUInt(obj, "length"), (Starpower.Flags)(int)DuetJson.GetNumber(obj, "flags"));
            case "drumroll":
                {
                    DrumRoll.Type rollType = (DrumRoll.Type)(int)DuetJson.GetNumber(obj, "rollType");
                    if (!Enum.IsDefined(typeof(DrumRoll.Type), rollType))
                        throw new FormatException("Bad drum roll type");
                    return new DrumRoll(tick, GetUInt(obj, "length"), rollType);
                }
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
                    {
                        double time = DuetJson.GetNumber(obj, "anchor");
                        if (!(time >= 0 && time < float.MaxValue))
                            throw new FormatException("Bad BPM anchor");
                        anchor = (float)time;
                    }

                    double value = DuetJson.GetNumber(obj, "value");
                    if (!(value >= 1 && value <= MaxBpmValue))
                        throw new FormatException("Bad BPM " + value);
                    return new BPM(tick, (uint)value, anchor);
                }
            case "timesignature":
                {
                    double numerator = DuetJson.GetNumber(obj, "numerator"), denominator = DuetJson.GetNumber(obj, "denominator");
                    if (!(numerator >= 1 && numerator <= MaxTimeSignaturePart && denominator >= 1 && denominator <= MaxTimeSignaturePart))
                        throw new FormatException("Bad time signature");
                    return new TimeSignature(tick, (uint)numerator, (uint)denominator);
                }
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
                instrument = ParseName<Song.Instrument>(instrument),
                difficulty = ParseName<Song.Difficulty>(DuetJson.GetString(obj, "difficulty")),
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

    const int MaxTextLength = 1024;
    const double MaxBpmValue = 10000000;   // 10,000 BPM, stored times 1000
    const double MaxTimeSignaturePart = 256;

    static uint GetUInt(Dictionary<string, object> obj, string key)
    {
        double value = DuetJson.GetNumber(obj, key);
        if (!(value >= 0))
            return 0;
        return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    }

    // Only a name the enum has: Enum.Parse would also take any number, which isn't a real instrument or difficulty
    static T ParseName<T>(string name) where T : struct
    {
        T value;
        if (name == null || !Enum.TryParse(name, out value) || !Enum.IsDefined(typeof(T), value) || char.IsDigit(name[0]) || name[0] == '-')
            throw new FormatException("Unknown " + typeof(T).Name + " " + name);
        return value;
    }
}
