// See LICENSE in project root for license information.

using System.Collections.Generic;
using UnityEngine;
using MoonscraperChartEditor.Song;

/// <summary>
/// Where someone is in the song and what they're pointing at, sent many times a second so everyone can see each
/// other's cursor, ghost note and place in the song. Positions are in ticks so they line up on every screen,
/// whatever each person's hyperspeed or window size.
/// </summary>
public class DuetPresence
{
    // A note the person would place if they clicked now. Sent as the note itself, so everyone draws it with their own
    // skin and lane layout, and works out strum or HOPO from their copy of the chart.
    public struct Ghost
    {
        public uint tick;
        public int rawNote;
        public int flags;   // Note.Flags, e.g. tap or forced
    }

    public int color;
    public uint viewTick;           // The tick at the strikeline
    public string instrument = string.Empty;
    public string difficulty = string.Empty;

    public bool hasPointer;
    public float pointerX;
    public uint pointerTick;

    public List<Ghost> ghosts = new List<Ghost>();

    // The box they're dragging with the select tool, corner to corner, if any. Removing is a Ctrl drag that deselects.
    public bool hasDrag;
    public float dragX1, dragX2;
    public uint dragTick1, dragTick2;
    public bool dragRemoving;

    public bool isTyping;   // Writing a chat message

    public static readonly string[] ColorNames = { "Red", "Orange", "Yellow", "Green", "Cyan", "Blue", "Purple", "Pink" };

    static readonly Color[] s_colors =
    {
        new Color(0.94f, 0.27f, 0.27f),
        new Color(0.98f, 0.57f, 0.18f),
        new Color(0.98f, 0.85f, 0.20f),
        new Color(0.33f, 0.85f, 0.35f),
        new Color(0.25f, 0.85f, 0.90f),
        new Color(0.30f, 0.50f, 0.98f),
        new Color(0.66f, 0.40f, 0.95f),
        new Color(0.97f, 0.45f, 0.75f),
    };

    public static int ColorCount { get { return s_colors.Length; } }

    // A color is either one of the presets above, by its number, or any color at all: this flag plus 0xRRGGBB
    public const int CustomColorFlag = 0x1000000;

    public static bool IsCustomColor(int color) { return (color & CustomColorFlag) != 0; }

    // Or a rainbow, cycling through every color: this flag plus a speed from 1 (slow) to MaxRainbowSpeed (fast)
    public const int RainbowFlag = 0x2000000;
    public const int MaxRainbowSpeed = 20;
    static readonly System.Diagnostics.Stopwatch s_rainbowClock = System.Diagnostics.Stopwatch.StartNew();

    public static bool IsRainbow(int color) { return (color & RainbowFlag) != 0; }
    public static int RainbowSpeed(int color) { return Mathf.Clamp(color & 0xFF, 1, MaxRainbowSpeed); }
    public static int MakeRainbow(int speed) { return RainbowFlag | Mathf.Clamp(speed, 1, MaxRainbowSpeed); }

    // Speed 1 takes 20 seconds to go round all the colors, the top speed one second
    static Color RainbowNow(int speed)
    {
        double turns = s_rainbowClock.Elapsed.TotalSeconds * speed / MaxRainbowSpeed;
        return FromHue((float)(turns - System.Math.Floor(turns)));
    }

    // A fully bright, fully colorful color with this hue, from 0 to 1
    static Color FromHue(float hue)
    {
        float h = hue * 6;
        float x = 1 - Mathf.Abs(h % 2 - 1);
        switch ((int)h % 6)
        {
            case 0: return new Color(1, x, 0);
            case 1: return new Color(x, 1, 0);
            case 2: return new Color(0, 1, x);
            case 3: return new Color(0, x, 1);
            case 4: return new Color(x, 0, 1);
            default: return new Color(1, 0, x);
        }
    }

    public static int MakeCustomColor(Color color)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(color.r * 255), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(color.g * 255), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(color.b * 255), 0, 255);
        return CustomColorFlag | (r << 16) | (g << 8) | b;
    }

    public static Color GetColor(int color)
    {
        if (IsRainbow(color))
            return RainbowNow(RainbowSpeed(color));

        if (IsCustomColor(color))
            return new Color(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f);

        if (color < 0 || color >= s_colors.Length)
            color = 0;
        return s_colors[color];
    }

    public static string GetColorName(int color)
    {
        if (IsRainbow(color))
            return "Rainbow";
        if (!IsCustomColor(color))
            return ColorNames[color >= 0 && color < ColorNames.Length ? color : 0];
        return "#" + (color & 0xFFFFFF).ToString("X6");
    }

    public static bool IsValidColor(int color)
    {
        if (IsRainbow(color))
            return (color & ~(RainbowFlag | 0xFF)) == 0 && (color & 0xFF) >= 1 && (color & 0xFF) <= MaxRainbowSpeed;
        return IsCustomColor(color) ? (color & ~(CustomColorFlag | 0xFFFFFF)) == 0 : color >= 0 && color < s_colors.Length;
    }

    public bool IsOnSameChart(DuetPresence other)
    {
        return other != null && instrument == other.instrument && difficulty == other.difficulty;
    }

    public Dictionary<string, object> ToJson()
    {
        Dictionary<string, object> json = new Dictionary<string, object>
        {
            { "type", "presence" },
            { "color", (double)color },
            { "view", (double)viewTick },
            { "instrument", instrument },
            { "difficulty", difficulty },
        };

        if (isTyping)
            json["typing"] = true;

        if (hasPointer)
            json["pointer"] = new List<object> { (double)pointerX, (double)pointerTick };

        if (hasDrag)
            json["drag"] = new List<object> { (double)dragX1, (double)dragTick1, (double)dragX2, (double)dragTick2, dragRemoving };

        if (ghosts.Count > 0)
        {
            List<object> list = new List<object>();
            foreach (Ghost ghost in ghosts)
                list.Add(new List<object> { (double)ghost.tick, (double)ghost.rawNote, (double)ghost.flags });
            json["ghosts"] = list;
        }

        return json;
    }

    public static DuetPresence FromJson(Dictionary<string, object> json)
    {
        DuetPresence presence = new DuetPresence();
        presence.color = (int)DuetJson.GetNumber(json, "color");
        presence.viewTick = ToTick(DuetJson.GetNumber(json, "view"));
        presence.instrument = Short(DuetJson.GetString(json, "instrument", string.Empty));
        presence.difficulty = Short(DuetJson.GetString(json, "difficulty", string.Empty));
        if (!IsValidColor(presence.color))
            presence.color = 0;

        object typing;
        presence.isTyping = json.TryGetValue("typing", out typing) && typing is bool && (bool)typing;

        List<object> pointer = DuetJson.GetArray(json, "pointer");
        if (pointer != null && pointer.Count >= 2 && pointer[0] is double && pointer[1] is double)
        {
            presence.hasPointer = true;
            presence.pointerX = (float)(double)pointer[0];
            presence.pointerTick = ToTick((double)pointer[1]);
        }

        List<object> drag = DuetJson.GetArray(json, "drag");
        if (drag != null && drag.Count >= 5 && drag[0] is double && drag[1] is double && drag[2] is double && drag[3] is double && drag[4] is bool
            && IsSensibleX((double)drag[0]) && IsSensibleX((double)drag[2]))
        {
            presence.hasDrag = true;
            presence.dragX1 = (float)(double)drag[0];
            presence.dragTick1 = ToTick((double)drag[1]);
            presence.dragX2 = (float)(double)drag[2];
            presence.dragTick2 = ToTick((double)drag[3]);
            presence.dragRemoving = (bool)drag[4];
        }

        List<object> ghosts = DuetJson.GetArray(json, "ghosts");
        if (ghosts != null)
        {
            foreach (object item in ghosts)
            {
                if (presence.ghosts.Count >= MaxGhosts)
                    break;     // Each one is drawn, so someone sending thousands could bring everyone's editor to a crawl

                List<object> ghost = item as List<object>;
                if (ghost != null && ghost.Count >= 3 && ghost[0] is double && ghost[1] is double && ghost[2] is double
                    && (double)ghost[1] >= 0 && (double)ghost[1] <= MaxRawNote)
                    presence.ghosts.Add(new Ghost { tick = ToTick((double)ghost[0]), rawNote = ToSmallInt((double)ghost[1]), flags = ToSmallInt((double)ghost[2]) });
            }
        }

        return presence;
    }

    public const int MaxGhosts = 64;
    public const int MaxRawNote = 6;    // The highest note number any instrument uses (Guitar Hero Live's open note)

    // The highway is a few units wide, so anything far outside it is nonsense that could upset the drawing
    static bool IsSensibleX(double x)
    {
        return !double.IsNaN(x) && x > -1000 && x < 1000;
    }

    public static string Short(string text)
    {
        return text.Length > 32 ? text.Substring(0, 32) : text;
    }

    public static int ToSmallInt(double value)
    {
        if (double.IsNaN(value) || value < 0 || value > ushort.MaxValue)
            return 0;
        return (int)value;
    }

    public static uint ToTick(double value)
    {
        if (value <= 0 || double.IsNaN(value))
            return 0;
        return value >= uint.MaxValue ? uint.MaxValue : (uint)value;
    }
}

/// <summary>
/// What someone has selected, so everyone sees it highlighted the way Moonscraper highlights their own selection.
/// Sent only when it changes. Each object is named by its type, tick and note (or drum roll kind), which is enough to
/// find it in everyone's copy of the chart.
/// </summary>
public class DuetSelection
{
    public struct Item
    {
        public int classID;     // SongObject.ID
        public uint tick;
        public int extra;       // The note's number, or the drum roll's kind
    }

    public const int MaxItems = 4096;   // Each one is looked up and drawn, so there's a limit
    const int MaxClassID = (int)SongObject.ID.DrumRoll;
    const int MaxExtra = 255;

    public string instrument = string.Empty;
    public string difficulty = string.Empty;
    public List<Item> items = new List<Item>();

    public static bool IsChartObject(int classID)
    {
        return classID == (int)SongObject.ID.Note || classID == (int)SongObject.ID.Starpower
            || classID == (int)SongObject.ID.ChartEvent || classID == (int)SongObject.ID.DrumRoll;
    }

    public static Item ItemFor(SongObject songObject)
    {
        Item item = new Item { classID = songObject.classID, tick = songObject.tick };
        Note note = songObject as Note;
        DrumRoll drumRoll = songObject as DrumRoll;
        if (note != null)
            item.extra = note.rawNote;
        else if (drumRoll != null)
            item.extra = (int)drumRoll.type;
        return item;
    }

    public bool Matches(Item item, SongObject songObject)
    {
        if (songObject.classID != item.classID || songObject.tick != item.tick)
            return false;

        Note note = songObject as Note;
        DrumRoll drumRoll = songObject as DrumRoll;
        if (note != null)
            return note.rawNote == item.extra;
        if (drumRoll != null)
            return (int)drumRoll.type == item.extra;
        return true;
    }

    public Dictionary<string, object> ToJson()
    {
        List<object> flat = new List<object>(items.Count * 3);
        foreach (Item item in items)
        {
            flat.Add((double)item.classID);
            flat.Add((double)item.tick);
            flat.Add((double)item.extra);
        }

        return new Dictionary<string, object>
        {
            { "type", "selection" },
            { "instrument", instrument },
            { "difficulty", difficulty },
            { "items", flat },
        };
    }

    public static DuetSelection FromJson(Dictionary<string, object> json)
    {
        DuetSelection selection = new DuetSelection();
        selection.instrument = DuetPresence.Short(DuetJson.GetString(json, "instrument", string.Empty));
        selection.difficulty = DuetPresence.Short(DuetJson.GetString(json, "difficulty", string.Empty));

        List<object> flat = DuetJson.GetArray(json, "items");
        if (flat == null)
            return selection;

        for (int i = 0; i + 2 < flat.Count && selection.items.Count < MaxItems; i += 3)
        {
            if (!(flat[i] is double) || !(flat[i + 1] is double) || !(flat[i + 2] is double))
                continue;

            double classID = (double)flat[i], extra = (double)flat[i + 2];
            if (!(classID >= 0 && classID <= MaxClassID) || !(extra >= 0 && extra <= MaxExtra))
                continue;

            selection.items.Add(new Item { classID = (int)classID, tick = DuetPresence.ToTick((double)flat[i + 1]), extra = (int)extra });
        }

        return selection;
    }
}
