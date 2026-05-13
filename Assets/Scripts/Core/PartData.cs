using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace CloseEncounters.Core
{
    public class PartData
    {
        public string id;
        public string partName;
        public string category;
        public string subcategory;
        public int cost;
        public float massKg;
        public int hp;
        public Vector3Int size;
        public float drag;
        public string[] domains;
        public string[] mountPoints;
        public Dictionary<string, object> stats;
        public Dictionary<string, object> meshData;

        public bool IsValidForDomain(string domain)
        {
            if (domains == null || domains.Length == 0) return true;
            for (int i = 0; i < domains.Length; i++)
            {
                if (string.Equals(domains[i], domain, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        public bool IsControlModule()
        {
            return string.Equals(category, "control", StringComparison.OrdinalIgnoreCase)
                || string.Equals(subcategory, "control_module", StringComparison.OrdinalIgnoreCase);
        }

        public T GetStat<T>(string key, T defaultValue = default)
        {
            if (stats == null || !stats.TryGetValue(key, out object raw))
                return defaultValue;

            if (raw is T typed)
                return typed;

            try
            {
                return (T)Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture);
            }
            catch
            {
                return defaultValue;
            }
        }

        public static PartData FromDictionary(Dictionary<string, object> dict)
        {
            var part = new PartData();

            part.id          = GetString(dict, "id", "");
            part.partName    = GetString(dict, "partName", GetString(dict, "name", ""));
            part.category    = GetString(dict, "category", "");
            part.subcategory = GetString(dict, "subcategory", "");
            part.cost        = GetInt(dict, "cost", 0);
            part.massKg      = GetFloat(dict, "mass_kg", GetFloat(dict, "massKg", GetFloat(dict, "mass", 0f)));
            part.hp          = GetInt(dict, "hp", 100);
            part.drag        = GetFloat(dict, "drag", 0f);

            // size
            if (dict.TryGetValue("size", out object sizeObj) && sizeObj is Dictionary<string, object> sizeDict)
            {
                part.size = new Vector3Int(
                    GetInt(sizeDict, "x", 1),
                    GetInt(sizeDict, "y", 1),
                    GetInt(sizeDict, "z", 1)
                );
            }
            else if (dict.TryGetValue("size", out object sizeList) && sizeList is List<object> sl && sl.Count >= 3)
            {
                part.size = new Vector3Int(
                    ToInt(sl[0]),
                    ToInt(sl[1]),
                    ToInt(sl[2])
                );
            }
            else
            {
                part.size = Vector3Int.one;
            }

            part.domains     = GetStringArray(dict, "domains");
            part.mountPoints = GetStringArray(dict, "mountPoints");

            if (dict.TryGetValue("stats", out object statsObj) && statsObj is Dictionary<string, object> sd)
                part.stats = sd;
            else
                part.stats = new Dictionary<string, object>();

            if (dict.TryGetValue("meshData", out object meshObj) && meshObj is Dictionary<string, object> md)
                part.meshData = md;
            else
                part.meshData = new Dictionary<string, object>();

            return part;
        }

        // --- helpers ---

        private static string GetString(Dictionary<string, object> d, string key, string def)
        {
            if (d.TryGetValue(key, out object v) && v != null) return v.ToString();
            return def;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int def)
        {
            if (d.TryGetValue(key, out object v)) return ToInt(v, def);
            return def;
        }

        private static float GetFloat(Dictionary<string, object> d, string key, float def)
        {
            if (d.TryGetValue(key, out object v)) return ToFloat(v, def);
            return def;
        }

        private static int ToInt(object v, int def = 0)
        {
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is float f) return (int)f;
            if (v is double d) return (int)d;
            if (v is string s && int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out int r)) return r;
            return def;
        }

        private static float ToFloat(object v, float def = 0f)
        {
            if (v is float f) return f;
            if (v is double d) return (float)d;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is string s && float.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out float r)) return r;
            return def;
        }

        private static string[] GetStringArray(Dictionary<string, object> d, string key)
        {
            if (!d.TryGetValue(key, out object v)) return Array.Empty<string>();
            if (v is List<object> list)
            {
                var result = new string[list.Count];
                for (int i = 0; i < list.Count; i++)
                    result[i] = list[i]?.ToString() ?? "";
                return result;
            }
            if (v is string s)
                return new[] { s };
            return Array.Empty<string>();
        }
    }

    // -------------------------------------------------------------------------
    // Minimal JSON parser -- handles objects, arrays, strings, numbers, bools, null.
    // No external dependencies. Returns Dictionary<string,object> / List<object>.
    // -------------------------------------------------------------------------
    public static class MiniJson
    {
        public static Dictionary<string, object> Deserialize(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parser = new Parser(json);
            object root = parser.ParseValue();
            return root as Dictionary<string, object>;
        }

        public static List<object> DeserializeArray(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var parser = new Parser(json);
            object root = parser.ParseValue();
            return root as List<object>;
        }

        public static string Serialize(object obj)
        {
            var sb = new StringBuilder(256);
            SerializeValue(obj, sb);
            return sb.ToString();
        }

        // --- Serializer ---

        private static void SerializeValue(object value, StringBuilder sb)
        {
            if (value == null)
            {
                sb.Append("null");
            }
            else if (value is string s)
            {
                SerializeString(s, sb);
            }
            else if (value is bool b)
            {
                sb.Append(b ? "true" : "false");
            }
            else if (value is IDictionary<string, object> dict)
            {
                sb.Append('{');
                bool first = true;
                foreach (var kv in dict)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    SerializeString(kv.Key, sb);
                    sb.Append(':');
                    SerializeValue(kv.Value, sb);
                }
                sb.Append('}');
            }
            else if (value is IList<object> list)
            {
                sb.Append('[');
                for (int i = 0; i < list.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    SerializeValue(list[i], sb);
                }
                sb.Append(']');
            }
            else if (value is int || value is long || value is float || value is double)
            {
                sb.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            else
            {
                SerializeString(value.ToString(), sb);
            }
        }

        private static void SerializeString(string s, StringBuilder sb)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20)
                            sb.AppendFormat("\\u{0:X4}", (int)c);
                        else
                            sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // --- Parser ---

        private class Parser
        {
            private readonly string _json;
            private int _pos;

            public Parser(string json)
            {
                _json = json;
                _pos = 0;
            }

            public object ParseValue()
            {
                SkipWhitespace();
                if (_pos >= _json.Length) return null;

                char c = _json[_pos];
                switch (c)
                {
                    case '{': return ParseObject();
                    case '[': return ParseArray();
                    case '"': return ParseString();
                    case 't':
                    case 'f': return ParseBool();
                    case 'n': return ParseNull();
                    default:  return ParseNumber();
                }
            }

            private Dictionary<string, object> ParseObject()
            {
                var dict = new Dictionary<string, object>();
                _pos++; // skip '{'
                SkipWhitespace();

                if (_pos < _json.Length && _json[_pos] == '}')
                {
                    _pos++;
                    return dict;
                }

                while (_pos < _json.Length)
                {
                    SkipWhitespace();
                    string key = ParseString();
                    SkipWhitespace();

                    if (_pos < _json.Length && _json[_pos] == ':')
                        _pos++;

                    object value = ParseValue();
                    dict[key] = value;

                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                if (_pos < _json.Length && _json[_pos] == '}')
                    _pos++;

                return dict;
            }

            private List<object> ParseArray()
            {
                var list = new List<object>();
                _pos++; // skip '['
                SkipWhitespace();

                if (_pos < _json.Length && _json[_pos] == ']')
                {
                    _pos++;
                    return list;
                }

                while (_pos < _json.Length)
                {
                    list.Add(ParseValue());
                    SkipWhitespace();
                    if (_pos < _json.Length && _json[_pos] == ',')
                    {
                        _pos++;
                        continue;
                    }
                    break;
                }

                if (_pos < _json.Length && _json[_pos] == ']')
                    _pos++;

                return list;
            }

            private string ParseString()
            {
                if (_pos >= _json.Length || _json[_pos] != '"')
                    return "";

                _pos++; // skip opening '"'
                var sb = new StringBuilder();

                while (_pos < _json.Length)
                {
                    char c = _json[_pos++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\' && _pos < _json.Length)
                    {
                        char esc = _json[_pos++];
                        switch (esc)
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case '/':  sb.Append('/');  break;
                            case 'b':  sb.Append('\b'); break;
                            case 'f':  sb.Append('\f'); break;
                            case 'n':  sb.Append('\n'); break;
                            case 'r':  sb.Append('\r'); break;
                            case 't':  sb.Append('\t'); break;
                            case 'u':
                                if (_pos + 4 <= _json.Length)
                                {
                                    string hex = _json.Substring(_pos, 4);
                                    _pos += 4;
                                    if (int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                                        sb.Append((char)code);
                                }
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
                }

                return sb.ToString();
            }

            private object ParseNumber()
            {
                int start = _pos;
                bool isFloat = false;

                if (_pos < _json.Length && _json[_pos] == '-') _pos++;

                while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;

                if (_pos < _json.Length && _json[_pos] == '.')
                {
                    isFloat = true;
                    _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }

                if (_pos < _json.Length && (_json[_pos] == 'e' || _json[_pos] == 'E'))
                {
                    isFloat = true;
                    _pos++;
                    if (_pos < _json.Length && (_json[_pos] == '+' || _json[_pos] == '-')) _pos++;
                    while (_pos < _json.Length && char.IsDigit(_json[_pos])) _pos++;
                }

                string numStr = _json.Substring(start, _pos - start);

                if (isFloat)
                {
                    if (double.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                        return d;
                    return 0.0;
                }

                if (long.TryParse(numStr, NumberStyles.Any, CultureInfo.InvariantCulture, out long l))
                {
                    if (l >= int.MinValue && l <= int.MaxValue)
                        return (int)l;
                    return l;
                }

                return 0;
            }

            private bool ParseBool()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "true")
                {
                    _pos += 4;
                    return true;
                }
                if (_pos + 5 <= _json.Length && _json.Substring(_pos, 5) == "false")
                {
                    _pos += 5;
                    return false;
                }
                _pos++;
                return false;
            }

            private object ParseNull()
            {
                if (_pos + 4 <= _json.Length && _json.Substring(_pos, 4) == "null")
                    _pos += 4;
                return null;
            }

            private void SkipWhitespace()
            {
                while (_pos < _json.Length)
                {
                    char c = _json[_pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r')
                        _pos++;
                    else
                        break;
                }
            }
        }
    }
}
