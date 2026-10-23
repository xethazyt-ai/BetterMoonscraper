// See LICENSE in project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// A small JSON reader and writer for Duet Mode network messages.
/// Objects are Dictionary&lt;string, object&gt;, arrays are List&lt;object&gt;, numbers are double,
/// plus string, bool and null.
/// </summary>
public static class DuetJson
{
    public static string Write(object value)
    {
        StringBuilder sb = new StringBuilder();
        WriteValue(sb, value);
        return sb.ToString();
    }

    public static object Read(string json)
    {
        int pos = 0;
        object value = ReadValue(json, ref pos);
        SkipWhitespace(json, ref pos);
        if (pos != json.Length)
            throw new FormatException("Unexpected data after JSON value at " + pos);
        return value;
    }

    #region Writing

    static void WriteValue(StringBuilder sb, object value)
    {
        if (value == null)
        {
            sb.Append("null");
        }
        else if (value is string)
        {
            WriteString(sb, (string)value);
        }
        else if (value is bool)
        {
            sb.Append((bool)value ? "true" : "false");
        }
        else if (value is IDictionary<string, object>)
        {
            sb.Append('{');
            bool first = true;
            foreach (KeyValuePair<string, object> pair in (IDictionary<string, object>)value)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                WriteString(sb, pair.Key);
                sb.Append(':');
                WriteValue(sb, pair.Value);
            }
            sb.Append('}');
        }
        else if (value is System.Collections.IList)
        {
            sb.Append('[');
            bool first = true;
            foreach (object item in (System.Collections.IList)value)
            {
                if (!first)
                    sb.Append(',');
                first = false;
                WriteValue(sb, item);
            }
            sb.Append(']');
        }
        else if (value is IConvertible)
        {
            // Every other supported type is a number
            sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture));
        }
        else
        {
            throw new ArgumentException("Can't write " + value.GetType() + " as JSON");
        }
    }

    static void WriteString(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.AppendFormat("\\u{0:x4}", (int)c);
                    else
                        sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    #endregion

    #region Reading

    static void SkipWhitespace(string s, ref int pos)
    {
        while (pos < s.Length && char.IsWhiteSpace(s[pos]))
            ++pos;
    }

    static object ReadValue(string s, ref int pos)
    {
        SkipWhitespace(s, ref pos);
        if (pos >= s.Length)
            throw new FormatException("Unexpected end of JSON");

        char c = s[pos];
        switch (c)
        {
            case '{': return ReadObject(s, ref pos);
            case '[': return ReadArray(s, ref pos);
            case '"': return ReadString(s, ref pos);
            case 't': Expect(s, ref pos, "true"); return true;
            case 'f': Expect(s, ref pos, "false"); return false;
            case 'n': Expect(s, ref pos, "null"); return null;
            default: return ReadNumber(s, ref pos);
        }
    }

    static void Expect(string s, ref int pos, string literal)
    {
        if (string.CompareOrdinal(s, pos, literal, 0, literal.Length) != 0)
            throw new FormatException("Expected " + literal + " at " + pos);
        pos += literal.Length;
    }

    static Dictionary<string, object> ReadObject(string s, ref int pos)
    {
        Dictionary<string, object> result = new Dictionary<string, object>();
        ++pos;  // {
        SkipWhitespace(s, ref pos);
        if (pos < s.Length && s[pos] == '}')
        {
            ++pos;
            return result;
        }

        while (true)
        {
            SkipWhitespace(s, ref pos);
            string key = ReadString(s, ref pos);
            SkipWhitespace(s, ref pos);
            Expect(s, ref pos, ":");
            result[key] = ReadValue(s, ref pos);
            SkipWhitespace(s, ref pos);

            if (pos < s.Length && s[pos] == ',')
            {
                ++pos;
                continue;
            }

            Expect(s, ref pos, "}");
            return result;
        }
    }

    static List<object> ReadArray(string s, ref int pos)
    {
        List<object> result = new List<object>();
        ++pos;  // [
        SkipWhitespace(s, ref pos);
        if (pos < s.Length && s[pos] == ']')
        {
            ++pos;
            return result;
        }

        while (true)
        {
            result.Add(ReadValue(s, ref pos));
            SkipWhitespace(s, ref pos);

            if (pos < s.Length && s[pos] == ',')
            {
                ++pos;
                continue;
            }

            Expect(s, ref pos, "]");
            return result;
        }
    }

    static string ReadString(string s, ref int pos)
    {
        Expect(s, ref pos, "\"");
        StringBuilder sb = new StringBuilder();

        while (pos < s.Length)
        {
            char c = s[pos++];
            if (c == '"')
                return sb.ToString();

            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }

            if (pos >= s.Length)
                break;

            char escaped = s[pos++];
            switch (escaped)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (pos + 4 > s.Length)
                        throw new FormatException("Bad unicode escape at " + pos);
                    sb.Append((char)int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    pos += 4;
                    break;
                default:
                    throw new FormatException("Bad escape at " + pos);
            }
        }

        throw new FormatException("Unterminated string");
    }

    static double ReadNumber(string s, ref int pos)
    {
        int start = pos;
        while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0)
            ++pos;

        if (pos == start)
            throw new FormatException("Unexpected character '" + s[pos] + "' at " + pos);

        return double.Parse(s.Substring(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    #endregion

    #region Helpers for reading message fields

    public static string GetString(Dictionary<string, object> obj, string key, string defaultValue = null)
    {
        object value;
        return obj.TryGetValue(key, out value) && value is string ? (string)value : defaultValue;
    }

    public static double GetNumber(Dictionary<string, object> obj, string key, double defaultValue = 0)
    {
        object value;
        return obj.TryGetValue(key, out value) && value is double ? (double)value : defaultValue;
    }

    public static bool GetBool(Dictionary<string, object> obj, string key, bool defaultValue = false)
    {
        object value;
        return obj.TryGetValue(key, out value) && value is bool ? (bool)value : defaultValue;
    }

    public static Dictionary<string, object> GetObject(Dictionary<string, object> obj, string key)
    {
        object value;
        return obj.TryGetValue(key, out value) ? value as Dictionary<string, object> : null;
    }

    public static List<object> GetArray(Dictionary<string, object> obj, string key)
    {
        object value;
        return obj.TryGetValue(key, out value) && value is List<object> ? (List<object>)value : new List<object>();
    }

    #endregion
}
