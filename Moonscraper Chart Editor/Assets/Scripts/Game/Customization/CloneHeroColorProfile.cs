// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

/// <summary>
/// A Clone Hero color profile, the .ini files in Clone Hero's Custom/Colors folder. Sections are [guitar], [drums],
/// [sixfret] and [other], and each line is a key with a #RRGGBB (or #RRGGBBAA) color, e.g. "note_green = #00FF00".
/// </summary>
public class CloneHeroColorProfile
{
    public struct Rgb
    {
        public byte r, g, b;
        public Rgb(byte r, byte g, byte b) { this.r = r; this.g = g; this.b = b; }
    }

    readonly Dictionary<string, Rgb> m_colors = new Dictionary<string, Rgb>(StringComparer.OrdinalIgnoreCase);    // "section/key"

    public int count { get { return m_colors.Count; } }

    public static CloneHeroColorProfile Load(string path)
    {
        return Parse(File.ReadAllText(path));
    }

    public static CloneHeroColorProfile Parse(string text)
    {
        CloneHeroColorProfile profile = new CloneHeroColorProfile();
        string section = string.Empty;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == ';' || (line[0] == '#' && line.IndexOf('=') < 0))
                continue;

            if (line[0] == '[')
            {
                int end = line.IndexOf(']');
                section = (end > 0 ? line.Substring(1, end - 1) : line.Substring(1)).Trim();
                continue;
            }

            int equals = line.IndexOf('=');
            if (equals <= 0)
                continue;

            string key = line.Substring(0, equals).Trim();
            Rgb color;
            if (TryParseHex(line.Substring(equals + 1).Trim(), out color))
                profile.m_colors[section + "/" + key] = color;
        }

        return profile;
    }

    public static bool TryParseHex(string value, out Rgb color)
    {
        color = new Rgb();
        if (value.StartsWith("#"))
            value = value.Substring(1);

        if (value.Length != 6 && value.Length != 8)
            return false;

        uint rgb;
        if (!uint.TryParse(value.Substring(0, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
            return false;

        color = new Rgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    public bool TryGet(string section, string key, out Rgb color)
    {
        return m_colors.TryGetValue(section + "/" + key, out color);
    }
}
