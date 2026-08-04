// Minimal JSON parser for Unity — supports Dictionary and Array access
// Based on the popular SimpleJSON pattern
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace SteelCity.Sim
{
    public abstract class JSONNode
    {
        public abstract JSONNode this[string key] { get; set; }
        public abstract JSONNode this[int index] { get; set; }
        public abstract string Value { get; }
        public virtual bool AsBool => Value == "true";
        public virtual int AsInt => int.TryParse(Value, out var v) ? v : 0;
        public virtual float AsFloat => float.TryParse(Value, out var v) ? v : 0f;
        public virtual JSONArray AsArray => this as JSONArray;
        public virtual JSONObject AsObject => this as JSONObject;
        public static JSONNode Parse(string json) => JSONParser.Parse(json);
    }

    public class JSONObject : JSONNode
    {
        private Dictionary<string, JSONNode> _dict = new();
        public override JSONNode this[string key]
        {
            get => _dict.TryGetValue(key, out var v) ? v : null;
            set => _dict[key] = value;
        }
        public override JSONNode this[int index] { get => null; set { } }
        public override string Value => "";
        public IEnumerator<KeyValuePair<string, JSONNode>> GetEnumerator() => _dict.GetEnumerator();
    }

    public class JSONArray : JSONNode
    {
        private List<JSONNode> _list = new();
        public int Count => _list.Count;
        public override JSONNode this[int index]
        {
            get => index >= 0 && index < _list.Count ? _list[index] : null;
            set
            {
                while (_list.Count <= index) _list.Add(null);
                _list[index] = value;
            }
        }
        public override JSONNode this[string key] { get => null; set { } }
        public override string Value => "";
        public override JSONArray AsArray => this;
    }

    public class JSONString : JSONNode
    {
        private string _value;
        public JSONString(string v) => _value = v;
        public override JSONNode this[string key] { get => null; set { } }
        public override JSONNode this[int index] { get => null; set { } }
        public override string Value => _value;
    }

    public class JSONNumber : JSONNode
    {
        private string _value;
        public JSONNumber(string v) => _value = v;
        public override JSONNode this[string key] { get => null; set { } }
        public override JSONNode this[int index] { get => null; set { } }
        public override string Value => _value;
        public override int AsInt => int.TryParse(_value, out var v) ? v : 0;
        public override float AsFloat => float.TryParse(_value, out var v) ? v : 0f;
    }

    public class JSONBool : JSONNode
    {
        private bool _value;
        public JSONBool(bool v) => _value = v;
        public override JSONNode this[string key] { get => null; set { } }
        public override JSONNode this[int index] { get => null; set { } }
        public override string Value => _value.ToString().ToLower();
        public override bool AsBool => _value;
    }

    public class JSONNull : JSONNode
    {
        public override JSONNode this[string key] { get => null; set { } }
        public override JSONNode this[int index] { get => null; set { } }
        public override string Value => "null";
    }

    public class JSONParser
    {
        private string _json;
        private int _pos;

        public static JSONNode Parse(string json)
        {
            var p = new JSONParser { _json = json, _pos = 0 };
            p.SkipWhitespace();
            return p.ParseValue();
        }

        private JSONNode ParseValue()
        {
            SkipWhitespace();
            if (_pos >= _json.Length) return new JSONNull();

            char c = _json[_pos];
            switch (c)
            {
                case '{': return ParseObject();
                case '[': return ParseArray();
                case '"': return ParseString();
                case 't': case 'f': return ParseBool();
                case 'n': return ParseNull();
                default: return ParseNumber();
            }
        }

        private JSONObject ParseObject()
        {
            var obj = new JSONObject();
            _pos++; // skip {
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == '}') { _pos++; return obj; }

            while (_pos < _json.Length)
            {
                SkipWhitespace();
                if (_json[_pos] == '}') { _pos++; break; }
                if (_json[_pos] == ',') { _pos++; continue; }

                // Parse key
                SkipWhitespace();
                var keyNode = ParseString() as JSONString;
                string key = keyNode?.Value ?? "";
                SkipWhitespace();
                if (_pos < _json.Length && _json[_pos] == ':') _pos++;

                var value = ParseValue();
                obj[key] = value;
            }
            return obj;
        }

        private JSONArray ParseArray()
        {
            var arr = new JSONArray();
            _pos++; // skip [
            SkipWhitespace();
            if (_pos < _json.Length && _json[_pos] == ']') { _pos++; return arr; }

            int idx = 0;
            while (_pos < _json.Length)
            {
                SkipWhitespace();
                if (_json[_pos] == ']') { _pos++; break; }
                if (_json[_pos] == ',') { _pos++; continue; }

                arr[idx] = ParseValue();
                idx++;
            }
            return arr;
        }

        private JSONString ParseString()
        {
            _pos++; // skip opening "
            var sb = new StringBuilder();
            while (_pos < _json.Length && _json[_pos] != '"')
            {
                if (_json[_pos] == '\\' && _pos + 1 < _json.Length)
                {
                    _pos++;
                    char esc = _json[_pos];
                    sb.Append(esc switch
                    {
                        'n' => '\n', 't' => '\t', 'r' => '\r',
                        '"' => '"', '\\' => '\\', '/' => '/',
                        _ => esc
                    });
                }
                else
                {
                    sb.Append(_json[_pos]);
                }
                _pos++;
            }
            _pos++; // skip closing "
            return new JSONString(sb.ToString());
        }

        private JSONNode ParseNumber()
        {
            int start = _pos;
            while (_pos < _json.Length && (char.IsDigit(_json[_pos]) || _json[_pos] == '-' || _json[_pos] == '.' || _json[_pos] == '+' || _json[_pos] == 'e' || _json[_pos] == 'E'))
                _pos++;
            return new JSONNumber(_json.Substring(start, _pos - start));
        }

        private JSONNode ParseBool()
        {
            if (_json[_pos] == 't') { _pos += 4; return new JSONBool(true); }
            _pos += 5; return new JSONBool(false);
        }

        private JSONNode ParseNull() { _pos += 4; return new JSONNull(); }

        private void SkipWhitespace()
        {
            while (_pos < _json.Length && char.IsWhiteSpace(_json[_pos])) _pos++;
        }
    }
}
