// See LICENSE in project root for license information.

using System;
using System.Globalization;
using System.Text.RegularExpressions;

/// <summary>
/// Clone Hero shows song.ini text such as the song name or loading phrase with rich text tags (bold, italic, color
/// and so on). The formatting only goes in song.ini: the .chart keeps the plain text, which other games expect.
/// </summary>
public static class RichTextIni
{
    static readonly Regex s_tag = new Regex(@"</?(b|i|u|s|color|size|alpha|mark|sup|sub|lowercase|uppercase|smallcaps|allcaps)(=[^>]*)?>", RegexOptions.IgnoreCase);

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

    // Wraps text[start, end) in open and close. Nothing selected wraps all of it.
    public static string Wrap(string text, ref int start, ref int end, string open, string close)
    {
        text = text ?? string.Empty;
        ClampSelection(text, ref start, ref end);

        string result = text.Substring(0, start) + open + text.Substring(start, end - start) + close + text.Substring(end);
        end += open.Length + close.Length;
        return result;
    }

    // Removes the formatting from text[start, end). Nothing selected clears all of it.
    public static string Clear(string text, ref int start, ref int end)
    {
        text = text ?? string.Empty;
        ClampSelection(text, ref start, ref end);

        string middle = Strip(text.Substring(start, end - start));
        string result = text.Substring(0, start) + middle + text.Substring(end);
        end = start + middle.Length;
        return result;
    }

    static void ClampSelection(string text, ref int start, ref int end)
    {
        start = Math.Max(0, Math.Min(start, text.Length));
        end = Math.Max(0, Math.Min(end, text.Length));
        if (end < start)
        {
            int swap = start;
            start = end;
            end = swap;
        }
        if (start == end)
        {
            start = 0;
            end = text.Length;
        }
    }

    // Close to how Clone Hero draws it, using what uGUI Text can do
    public static string ToPreview(string text, int baseSize)
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

            return "<size=" + Math.Max(4, Math.Min((int)Math.Round(size), 64)) + ">";
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
