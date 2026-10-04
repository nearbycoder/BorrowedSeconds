using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace BorrowedSeconds.Sim
{
    /// <summary>
    /// Tiny JSON reader/writer shared by the game, the tests and the console solver
    /// (UnityEngine.JsonUtility is unavailable outside Unity and can't read jagged arrays).
    /// Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;, numbers double.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            int i = 0;
            var v = ParseValue(text, ref i);
            SkipWs(text, ref i);
            if (i != text.Length) throw new FormatException("Trailing JSON at " + i);
            return v;
        }

        static void SkipWs(string s, ref int i)
        {
            while (i < s.Length)
            {
                char c = s[i];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                {
                    while (i < s.Length && s[i] != '\n') i++;
                    continue;
                }
                break;
            }
        }

        static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't' && string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (c == 'f' && string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (c == 'n' && string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            return ParseNumber(s, ref i);
        }

        static Dictionary<string, object> ParseObject(string s, ref int i)
        {
            var d = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == '}') { i++; return d; }
            while (true)
            {
                SkipWs(s, ref i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                i++;
                d[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("Expected ',' or '}' at " + i);
            }
        }

        static List<object> ParseArray(string s, ref int i)
        {
            var l = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("Expected ',' or ']' at " + i);
            }
        }

        static string ParseString(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("Expected string at " + i);
            i++;
            var sb = new StringBuilder();
            while (s[i] != '"')
            {
                char c = s[i++];
                if (c == '\\')
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            i++;
            return sb.ToString();
        }

        static double ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ---- helpers for reading ----
        public static int Int(Dictionary<string, object> d, string key, int def)
        {
            return d.TryGetValue(key, out var v) && v != null ? (int)Math.Round(Convert.ToDouble(v, CultureInfo.InvariantCulture)) : def;
        }

        public static bool Bool(Dictionary<string, object> d, string key, bool def)
        {
            return d.TryGetValue(key, out var v) && v is bool b ? b : def;
        }

        public static string Str(Dictionary<string, object> d, string key, string def)
        {
            return d.TryGetValue(key, out var v) && v is string str ? str : def;
        }

        public static List<object> List(Dictionary<string, object> d, string key)
        {
            return d.TryGetValue(key, out var v) && v is List<object> l ? l : new List<object>();
        }

        // ---- writer ----
        public static string Serialize(object o, bool pretty = true)
        {
            var sb = new StringBuilder();
            Write(sb, o, pretty, 0);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object o, bool pretty, int depth)
        {
            switch (o)
            {
                case null: sb.Append("null"); break;
                case string str: WriteString(sb, str); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> dict:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in dict)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        if (pretty) { sb.Append('\n'); sb.Append(' ', (depth + 1) * 2); }
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        Write(sb, kv.Value, pretty, depth + 1);
                    }
                    if (pretty && dict.Count > 0) { sb.Append('\n'); sb.Append(' ', depth * 2); }
                    sb.Append('}');
                    break;
                }
                case System.Collections.IEnumerable list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(pretty ? ", " : ",");
                        first = false;
                        Write(sb, item, pretty, depth + 1);
                    }
                    sb.Append(']');
                    break;
                }
                default: WriteString(sb, o.ToString()); break;
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
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }
            sb.Append('"');
        }
    }
}
