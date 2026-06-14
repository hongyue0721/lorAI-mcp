using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LorAIHost
{
    /// <summary>
    /// Lightweight JSON serializer and parser for Unity .NET Framework 4.7.2.
    /// No external dependencies (no Newtonsoft.Json).
    /// </summary>
    public static class JsonHelper
    {
        // ───────────────────────── Serializer ─────────────────────────

        /// <summary>
        /// Serialize an object to a JSON string.
        /// </summary>
        public static string Serialize(object obj, bool indent = false)
        {
            var sb = new StringBuilder(256);
            WriteValue(sb, obj, indent, 0);
            return sb.ToString();
        }

        private static void WriteValue(StringBuilder sb, object value, bool indent, int depth)
        {
            if (value == null)
            {
                sb.Append("null");
                return;
            }

            if (value is string s)
            {
                WriteString(sb, s);
                return;
            }

            if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
                return;
            }

            if (value is int i)
            {
                sb.Append(i.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is long l)
            {
                sb.Append(l.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is short sh)
            {
                sb.Append(sh.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is byte by)
            {
                sb.Append(by.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value is float f)
            {
                if (float.IsNaN(f) || float.IsInfinity(f))
                    sb.Append("null");
                else
                    sb.Append(f.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is double d)
            {
                if (double.IsNaN(d) || double.IsInfinity(d))
                    sb.Append("null");
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            if (value is decimal dec)
            {
                sb.Append(dec.ToString(CultureInfo.InvariantCulture));
                return;
            }

            if (value.GetType().IsEnum)
            {
                WriteString(sb, value.ToString());
                return;
            }

            if (value is IDictionary dict)
            {
                WriteObject(sb, dict, indent, depth);
                return;
            }

            if (value is IList list)
            {
                WriteArray(sb, list, indent, depth);
                return;
            }

            // Fallback: convert to string
            WriteString(sb, value.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("X4"));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
        }

        private static void WriteObject(StringBuilder sb, IDictionary dict, bool indent, int depth)
        {
            sb.Append('{');
            bool first = true;
            foreach (DictionaryEntry entry in dict)
            {
                if (!first)
                    sb.Append(',');
                first = false;

                if (indent)
                {
                    sb.Append('\n');
                    AppendIndent(sb, depth + 1);
                }

                WriteString(sb, entry.Key.ToString());
                sb.Append(':');
                if (indent) sb.Append(' ');
                WriteValue(sb, entry.Value, indent, depth + 1);
            }

            if (!first && indent)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append('}');
        }

        private static void WriteArray(StringBuilder sb, IList list, bool indent, int depth)
        {
            sb.Append('[');
            for (int i = 0; i < list.Count; i++)
            {
                if (i > 0)
                    sb.Append(',');

                if (indent)
                {
                    sb.Append('\n');
                    AppendIndent(sb, depth + 1);
                }

                WriteValue(sb, list[i], indent, depth + 1);
            }

            if (list.Count > 0 && indent)
            {
                sb.Append('\n');
                AppendIndent(sb, depth);
            }

            sb.Append(']');
        }

        private static void AppendIndent(StringBuilder sb, int depth)
        {
            for (int i = 0; i < depth; i++)
                sb.Append("  ");
        }

        // ───────────────────────── Parser ─────────────────────────

        /// <summary>
        /// Parse a JSON string into a Dictionary. Values are stored as raw strings
        /// unless they are nested objects or arrays.
        /// </summary>
        public static Dictionary<string, object> Parse(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, object>();

            int index = 0;
            SkipWhitespace(json, ref index);
            var result = ParseObject(json, ref index, false);
            return result;
        }

        /// <summary>
        /// Parse a JSON string with typed value conversion:
        /// integers → int, decimals → float, "true"/"false" → bool, "null" → null, rest → string.
        /// </summary>
        public static Dictionary<string, object> ParseTyped(string json)
        {
            if (string.IsNullOrEmpty(json))
                return new Dictionary<string, object>();

            int index = 0;
            SkipWhitespace(json, ref index);
            var result = ParseObject(json, ref index, true);
            return result;
        }

        // ─── Internal recursive-descent parser ───

        private static Dictionary<string, object> ParseObject(string json, ref int index, bool typed)
        {
            var dict = new Dictionary<string, object>();
            Expect(json, ref index, '{');
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == '}')
            {
                index++;
                return dict;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                string key = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                Expect(json, ref index, ':');
                SkipWhitespace(json, ref index);
                object value = ParseValue(json, ref index, typed);
                dict[key] = value;

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }

                break;
            }

            SkipWhitespace(json, ref index);
            Expect(json, ref index, '}');
            return dict;
        }

        private static List<object> ParseArray(string json, ref int index, bool typed)
        {
            var list = new List<object>();
            Expect(json, ref index, '[');
            SkipWhitespace(json, ref index);

            if (index < json.Length && json[index] == ']')
            {
                index++;
                return list;
            }

            while (index < json.Length)
            {
                SkipWhitespace(json, ref index);
                object value = ParseValue(json, ref index, typed);
                list.Add(value);

                SkipWhitespace(json, ref index);
                if (index < json.Length && json[index] == ',')
                {
                    index++;
                    continue;
                }

                break;
            }

            SkipWhitespace(json, ref index);
            Expect(json, ref index, ']');
            return list;
        }

        private static object ParseValue(string json, ref int index, bool typed)
        {
            if (index >= json.Length)
                return null;

            char c = json[index];

            if (c == '"')
            {
                string raw = ParseString(json, ref index);
                if (typed)
                    return ConvertTypedString(raw);
                return raw;
            }

            if (c == '{')
            {
                return ParseObject(json, ref index, typed);
            }

            if (c == '[')
            {
                return ParseArray(json, ref index, typed);
            }

            if (c == 't' || c == 'f')
            {
                return ParseBool(json, ref index, typed);
            }

            if (c == 'n')
            {
                ParseNull(json, ref index);
                return null;
            }

            // Number
            return ParseNumber(json, ref index, typed);
        }

        private static string ParseString(string json, ref int index)
        {
            Expect(json, ref index, '"');
            var sb = new StringBuilder();

            while (index < json.Length)
            {
                char c = json[index];
                if (c == '"')
                {
                    index++;
                    return sb.ToString();
                }

                if (c == '\\')
                {
                    index++;
                    if (index >= json.Length)
                        throw new FormatException("Unexpected end of string escape.");

                    char esc = json[index];
                    switch (esc)
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        case 'b':  sb.Append('\b'); break;
                        case 'f':  sb.Append('\f'); break;
                        case 'u':
                            if (index + 4 >= json.Length)
                                throw new FormatException("Incomplete unicode escape.");
                            string hex = json.Substring(index + 1, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            index += 4;
                            break;
                        default:
                            sb.Append(esc);
                            break;
                    }
                }
                else
                {
                    sb.Append(c);
                }

                index++;
            }

            throw new FormatException("Unterminated string.");
        }

        private static object ParseNumber(string json, ref int index, bool typed)
        {
            int start = index;
            bool hasDot = false;
            bool hasExp = false;

            if (index < json.Length && json[index] == '-')
                index++;

            while (index < json.Length)
            {
                char c = json[index];
                if (c >= '0' && c <= '9')
                {
                    index++;
                }
                else if (c == '.' && !hasDot && !hasExp)
                {
                    hasDot = true;
                    index++;
                }
                else if ((c == 'e' || c == 'E') && !hasExp)
                {
                    hasExp = true;
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                        index++;
                }
                else
                {
                    break;
                }
            }

            string numStr = json.Substring(start, index - start);

            if (!typed)
                return numStr;

            if (hasDot || hasExp)
            {
                if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dv))
                    return (float)dv;
                return numStr;
            }

            if (int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                return iv;

            if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lv))
                return lv;

            return numStr;
        }

        private static object ParseBool(string json, ref int index, bool typed)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "true")
            {
                index += 4;
                return typed ? (object)true : "true";
            }

            if (json.Length - index >= 5 && json.Substring(index, 5) == "false")
            {
                index += 5;
                return typed ? (object)false : "false";
            }

            throw new FormatException("Expected boolean at index " + index);
        }

        private static void ParseNull(string json, ref int index)
        {
            if (json.Length - index >= 4 && json.Substring(index, 4) == "null")
            {
                index += 4;
                return;
            }

            throw new FormatException("Expected null at index " + index);
        }

        private static object ConvertTypedString(string raw)
        {
            // Try int
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
                return iv;

            // Try float (only if it contains a dot to avoid converting id-like strings)
            if (raw.Contains(".") && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv))
                return fv;

            // Try bool
            if (raw == "true") return true;
            if (raw == "false") return false;
            if (raw == "null") return null;

            return raw;
        }

        private static void Expect(string json, ref int index, char expected)
        {
            if (index >= json.Length || json[index] != expected)
            {
                throw new FormatException(
                    string.Format("Expected '{0}' at index {1}, got '{2}'.",
                        expected,
                        index,
                        index < json.Length ? json[index].ToString() : "EOF"));
            }
            index++;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length && char.IsWhiteSpace(json[index]))
                index++;
        }
    }
}
