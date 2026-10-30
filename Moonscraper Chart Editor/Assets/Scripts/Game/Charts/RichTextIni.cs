// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Clone Hero shows song.ini text such as the song name or loading phrase with rich text tags (bold, italic, color
/// and so on). The formatting only goes in song.ini: the .chart keeps the plain text, which other games expect.
/// </summary>
public static class RichTextIni
{
    const string TagPattern = @"</?(b|i|u|s|color|size|alpha|mark|sup|sub|lowercase|uppercase|smallcaps|allcaps)(=[^>]*)?>";
    static readonly Regex s_tag = new Regex(TagPattern, RegexOptions.IgnoreCase);
    static readonly Regex s_tagHere = new Regex(@"\G" + TagPattern, RegexOptions.IgnoreCase);
    static readonly Regex s_emptyPair = new Regex(@"<(b|i|u|s|color|size)(=[^>]*)?></\1>", RegexOptions.IgnoreCase);

    // uGUI Text, which draws the preview, can't underline or strike through, and only knows sizes in pixels
    static readonly Regex s_previewUnsupported = new Regex(@"</?(u|s|alpha|mark|sup|sub|lowercase|uppercase|smallcaps|allcaps)(=[^>]*)?>", RegexOptions.IgnoreCase);
    static readonly Regex s_size = new Regex(@"<size=([+-]?)([0-9.]+)\s*(%|px|em)?\s*>", RegexOptions.IgnoreCase);

    public static bool HasTags(string text)
    {
        return !string.IsNullOrEmpty(text) && s_tag.IsMatch(text);
    }

    public static string Strip(string text)
    {
        return string.IsNullOrEmpty(text) ? (text ?? string.Empty) : s_tag.Replace(text, string.Empty);
    }

    // The General fields show the text without its tags, so their selections and edits are in visible characters.
    // These map them onto the tagged text that goes in song.ini.

    // Where each visible character of tagged text is
    static List<int> VisiblePositions(string tagged)
    {
        List<int> positions = new List<int>();
        int i = 0;
        while (i < tagged.Length)
        {
            Match tag = s_tagHere.Match(tagged, i);
            if (tag.Success)
            {
                i += tag.Length;
                continue;
            }
            positions.Add(i++);
        }
        return positions;
    }

    // Wraps the visible characters [start, end) in open and close. Nothing selected wraps all of it.
    public static string WrapVisible(string tagged, int start, int end, string open, string close)
    {
        tagged = tagged ?? string.Empty;
        List<int> visible = VisiblePositions(tagged);
        ClampSelection(visible.Count, ref start, ref end);
        if (visible.Count == 0)
            return tagged;

        int closeAt = visible[end - 1] + 1;
        int openAt = visible[start];
        return Normalise(tagged.Substring(0, openAt) + open + tagged.Substring(openAt, closeAt - openAt) + close + tagged.Substring(closeAt));
    }

    // Bold, italic and so on as a switch: if every visible character in [start, end) already has the tag (name like
    // "b"), it comes off them; otherwise they're wrapped in open and close. Nothing selected means all of it.
    public static string ToggleVisible(string tagged, int start, int end, string name, string open, string close)
    {
        tagged = tagged ?? string.Empty;
        List<int> visible = VisiblePositions(tagged);
        ClampSelection(visible.Count, ref start, ref end);
        if (visible.Count == 0)
            return tagged;

        bool all = true;
        for (int i = start; i < end && all; ++i)
            all = FindOpen(OpenTagsAt(tagged, visible[i]), name) != null;

        // Off for the selection first, so turning it on over partly-bold text doesn't nest one inside another. Close
        // it where the selection starts, drop it inside, and open it again where the selection ends if it
        // carried on past there
        int from = start > 0 ? visible[start - 1] + 1 : 0;
        int to = end < visible.Count ? visible[end] : tagged.Length;
        string openAtFrom = FindOpen(OpenTagsAt(tagged, from), name);
        string openAtTo = to < tagged.Length ? FindOpen(OpenTagsAt(tagged, to), name) : null;
        StringBuilder result = new StringBuilder(tagged.Substring(0, from));
        if (openAtFrom != null)
            result.Append("</").Append(name).Append('>');
        string middle = tagged.Substring(from, to - from);
        int copied = 0;
        foreach (Match tag in s_tag.Matches(middle))
        {
            result.Append(middle, copied, tag.Index - copied);
            if (TagName(tag.Value) != name)
                result.Append(tag.Value);
            copied = tag.Index + tag.Length;
        }
        result.Append(middle, copied, middle.Length - copied);
        if (openAtTo != null)
            result.Append(openAtTo);
        result.Append(tagged.Substring(to));
        string removed = Normalise(result.ToString());
        return all ? removed : WrapVisible(removed, start, end, open, close);
    }

    static string FindOpen(List<string> open, string name)
    {
        for (int i = open.Count - 1; i >= 0; --i)
        {
            if (TagName(open[i]) == name)
                return open[i];
        }
        return null;
    }

    // Fades the visible characters [start, end) from one color to another, a color tag per letter (spaces are
    // skipped). Colors are hex like #FF0000. Nothing selected fades all of it.
    public static string GradientVisible(string tagged, int start, int end, string fromHex, string toHex)
    {
        tagged = tagged ?? string.Empty;
        int count = VisiblePositions(tagged).Count;
        ClampSelection(count, ref start, ref end);
        int from, to;
        if (count == 0 || !TryParseRgb(fromHex, out from) || !TryParseRgb(toHex, out to))
            return tagged;

        int letters = 0;
        for (int i = start; i < end; ++i)
        {
            if (!char.IsWhiteSpace(tagged[VisiblePositions(tagged)[i]]))
                ++letters;
        }

        int letter = 0;
        for (int i = start; i < end; ++i)
        {
            if (char.IsWhiteSpace(tagged[VisiblePositions(tagged)[i]]))
                continue;
            double t = letters > 1 ? letter / (double)(letters - 1) : 0;
            ++letter;
            tagged = WrapVisible(tagged, i, i + 1, "<color=" + LerpHex(from, to, t) + ">", "</color>");
        }
        return tagged;
    }

    public static bool TryParseRgb(string hex, out int rgb)
    {
        rgb = 0;
        string normalised;
        return TryParseColor(hex, out normalised) && int.TryParse(normalised.Substring(1, 6), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb);
    }

    // Between two 0xRRGGBB colors, t from 0 (from) to 1 (to), as #RRGGBB
    public static string LerpHex(int from, int to, double t)
    {
        int rgb = 0;
        for (int shift = 16; shift >= 0; shift -= 8)
        {
            int a = (from >> shift) & 0xFF, b = (to >> shift) & 0xFF;
            rgb |= Math.Max(0, Math.Min(255, (int)Math.Round(a + (b - a) * t))) << shift;
        }
        return "#" + rgb.ToString("X6");
    }

    // Removes the formatting from the visible characters [start, end). Nothing selected clears all of it.
    public static string ClearVisible(string tagged, int start, int end)
    {
        tagged = tagged ?? string.Empty;
        List<int> visible = VisiblePositions(tagged);
        ClampSelection(visible.Count, ref start, ref end);
        if (start == 0 && end == visible.Count)
            return Strip(tagged);

        // Close whatever is open where the selection starts, and open it again where the selection ends
        int from = start > 0 ? visible[start - 1] + 1 : 0;
        int to = end < visible.Count ? visible[end] : tagged.Length;
        List<string> open = OpenTagsAt(tagged, from);
        StringBuilder result = new StringBuilder(tagged.Substring(0, from));
        for (int i = open.Count - 1; i >= 0; --i)
            result.Append("</").Append(TagName(open[i])).Append('>');
        result.Append(Strip(tagged.Substring(from, to - from)));
        foreach (string tag in open)
            result.Append(tag);
        result.Append(tagged.Substring(to));
        return Normalise(result.ToString());
    }

    static string TagName(string tag)
    {
        Match match = s_tag.Match(tag);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : string.Empty;
    }

    static List<string> OpenTagsAt(string tagged, int position)
    {
        List<string> open = new List<string>();
        foreach (Match tag in s_tag.Matches(tagged.Substring(0, position)))
        {
            string name = tag.Groups[1].Value.ToLowerInvariant();
            if (!tag.Value.StartsWith("</"))
            {
                open.Add(tag.Value);
                continue;
            }

            for (int i = open.Count - 1; i >= 0; --i)
            {
                if (TagName(open[i]) == name)
                {
                    open.RemoveAt(i);
                    break;
                }
            }
        }
        return open;
    }

    // Puts the tags in proper nesting order (closing and reopening where spans cross, dropping a close with no open),
    // so the General fields can draw it. Clone Hero reads it the same either way.
    public static string Normalise(string tagged)
    {
        if (string.IsNullOrEmpty(tagged))
            return tagged ?? string.Empty;

        StringBuilder result = new StringBuilder(tagged.Length + 16);
        List<string> open = new List<string>();
        int i = 0;
        while (i < tagged.Length)
        {
            Match tag = s_tagHere.Match(tagged, i);
            if (!tag.Success)
            {
                result.Append(tagged[i++]);
                continue;
            }
            i += tag.Length;

            string name = tag.Groups[1].Value.ToLowerInvariant();
            if (!tag.Value.StartsWith("</"))
            {
                open.Add(tag.Value);
                result.Append(tag.Value);
                continue;
            }

            int match = open.Count - 1;
            while (match >= 0 && TagName(open[match]) != name)
                --match;
            if (match < 0)
                continue;   // Nothing to close

            for (int k = open.Count - 1; k > match; --k)
                result.Append("</").Append(TagName(open[k])).Append('>');
            result.Append(tag.Value);
            for (int k = match + 1; k < open.Count; ++k)
                result.Append(open[k]);
            open.RemoveAt(match);
        }

        for (int k = open.Count - 1; k >= 0; --k)
            result.Append("</").Append(TagName(open[k])).Append('>');

        return s_emptyPair.Replace(result.ToString(), string.Empty);
    }

    static void ClampSelection(int length, ref int start, ref int end)
    {
        start = Math.Max(0, Math.Min(start, length));
        end = Math.Max(0, Math.Min(end, length));
        if (end < start)
        {
            int swap = start;
            start = end;
            end = swap;
        }
        if (start == end)
        {
            start = 0;
            end = length;
        }
    }

    // The visible text was edited (typed in, deleted from, pasted over) to become plain. Makes the same edit to the
    // tagged text, keeping the formatting around it: typing at the end of red text carries on in red.
    public static string ApplyPlainEdit(string tagged, string plain)
    {
        tagged = tagged ?? string.Empty;
        plain = plain ?? string.Empty;
        string oldPlain = Strip(tagged);
        if (oldPlain == plain)
            return tagged;
        if (plain.Length == 0)
            return string.Empty;

        int prefix = 0;
        while (prefix < oldPlain.Length && prefix < plain.Length && oldPlain[prefix] == plain[prefix])
            ++prefix;
        int suffix = 0;
        while (suffix < oldPlain.Length - prefix && suffix < plain.Length - prefix
            && oldPlain[oldPlain.Length - 1 - suffix] == plain[plain.Length - 1 - suffix])
            ++suffix;

        List<int> visible = VisiblePositions(tagged);
        StringBuilder result = new StringBuilder(tagged);
        for (int k = oldPlain.Length - suffix - 1; k >= prefix; --k)
            result.Remove(visible[k], 1);

        int insertAt;
        if (prefix > 0)
            insertAt = visible[prefix - 1] + 1;
        else if (visible.Count > 0)
            insertAt = visible[0];
        else
        {
            // Only tags are left (all the text was deleted): type inside them
            int firstClose = tagged.IndexOf("</", StringComparison.Ordinal);
            insertAt = firstClose >= 0 ? firstClose : tagged.Length;
        }

        result.Insert(insertAt, plain.Substring(prefix, plain.Length - prefix - suffix));
        return Normalise(result.ToString());
    }

    // Close to how Clone Hero draws it, using what uGUI Text can do
    public static string ToPreview(string text, int baseSize, int maxSize = 64)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        text = s_previewUnsupported.Replace(text, string.Empty);
        return s_size.Replace(text, match =>
        {
            float amount;
            if (!float.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                return string.Empty;

            string sign = match.Groups[1].Value;
            string unit = match.Groups[3].Value.ToLowerInvariant();
            float size;
            if (unit == "%")
                size = baseSize * amount / 100;
            else if (unit == "em")
                size = baseSize * amount;
            else if (sign == "+")
                size = baseSize + amount;
            else if (sign == "-")
                size = baseSize - amount;
            else
                size = amount;

            return "<size=" + Math.Max(4, Math.Min((int)Math.Round(size), maxSize)) + ">";
        });
    }

    public static bool TryParseColor(string hex, out string normalised)
    {
        normalised = null;
        hex = (hex ?? string.Empty).Trim().TrimStart('#');
        if (hex.Length != 6 && hex.Length != 8)
            return false;

        foreach (char c in hex)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        normalised = "#" + hex.ToUpperInvariant();
        return true;
    }

    // song.ini keys are matched without caring about case or spacing, as Clone Hero does
    public static string ReadIni(INIParser ini, string key)
    {
        string section, realKey;
        return FindKey(ini, key, out section, out realKey) ? ini.ReadValue(section, realKey, string.Empty).Trim() : null;
    }

    public static void WriteIni(INIParser ini, string key, string value)
    {
        string section, realKey;
        if (!FindKey(ini, key, out section, out realKey))
        {
            section = SongIniFunctions.INI_SECTION_HEADER;
            realKey = key + " ";
        }

        ini.WriteValue(section, realKey, " " + (value ?? string.Empty).Trim());
    }

    public static void DeleteIni(INIParser ini, string key)
    {
        string section, realKey;
        for (int tries = 0; tries < 8 && FindKey(ini, key, out section, out realKey); ++tries)
            ini.KeyDelete(section, realKey);
    }

    static bool FindKey(INIParser ini, string key, out string section, out string realKey)
    {
        section = null;
        realKey = null;

        foreach (string candidateSection in new[] { SongIniFunctions.INI_SECTION_HEADER, SongIniFunctions.Deprecated_INI_SECTION_HEADER })
        {
            string text = ini.GetSectionValues(new[] { candidateSection });
            foreach (string line in text.Split('\n'))
            {
                int equals = line.IndexOf('=');
                if (equals <= 0)
                    continue;

                string lineKey = line.Substring(0, equals);
                if (string.Equals(lineKey.Trim(), key, StringComparison.OrdinalIgnoreCase) && ini.IsKeyExists(candidateSection, lineKey))
                {
                    section = candidateSection;
                    realKey = lineKey;
                    return true;
                }
            }
        }

        return false;
    }

    // What a song details field should show: the formatted song.ini value when it's the same text as the chart's
    public static string DisplayValue(INIParser ini, string key, string chartValue)
    {
        string iniValue = ReadIni(ini, key);
        if (HasTags(iniValue) && (string.IsNullOrEmpty(chartValue) || Strip(iniValue) == chartValue))
            return iniValue;

        return chartValue ?? string.Empty;
    }

    // Puts formatted text in song.ini. Plain text is only written over a formatted value (clearing its formatting),
    // so song.ini keeps working as it always has for people who don't use formatting. Returns whether it changed.
    public static bool StoreFormatted(INIParser ini, string key, string text)
    {
        string current = ReadIni(ini, key);
        if (current == text || (!HasTags(text) && !HasTags(current)))
            return false;

        WriteIni(ini, key, text);
        return true;
    }
}
