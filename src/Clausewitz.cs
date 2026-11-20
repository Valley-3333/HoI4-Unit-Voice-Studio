using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace UnitVoiceStudio {
    public sealed class Node {
        public string Key, Value;
        public int Start, End, ValueStart, ValueEnd;
        public List<Node> Children = new List<Node>();
        public Node Child(string key) { return Children.FirstOrDefault(n => n.Key == key); }
        public string Get(string key) { var n = Child(key); return n == null ? null : n.Value; }
    }
    // A small structural reader: comments and quoted braces never affect nesting.
    public sealed class Clausewitz {
        sealed class Token { public string Text; public int Start, End; }
        readonly List<Token> tokens = new List<Token>();
        int pos;
        public static List<Node> Parse(string text) { return new Clausewitz(text).Read(false); }
        Clausewitz(string text) {
            for (int i = 0; i < text.Length;) {
                if (char.IsWhiteSpace(text[i]) || text[i] == '\uFEFF') { i++; continue; }
                if (text[i] == '#') { while (i < text.Length && text[i] != '\n') i++; continue; }
                int start = i;
                string value;
                if (text[i] == '"') {
                    i++; var b = new StringBuilder(); bool closed = false;
                    while (i < text.Length) {
                        char c = text[i++];
                        if (c == '"') { closed = true; break; }
                        if (c == '\\' && i < text.Length && (text[i] == '"' || text[i] == '\\')) c = text[i++];
                        b.Append(c);
                    }
                    if (!closed) throw new InvalidDataException("Unclosed quoted string");
                    value = b.ToString();
                } else if ("{}=".IndexOf(text[i]) >= 0) value = text[i++].ToString();
                else { while (i < text.Length && !char.IsWhiteSpace(text[i]) && "{}=#".IndexOf(text[i]) < 0) i++; value = text.Substring(start, i - start); }
                tokens.Add(new Token { Text = value, Start = start, End = i });
            }
        }
        List<Node> Read(bool nested) {
            var result = new List<Node>();
            while (pos < tokens.Count && tokens[pos].Text != "}") {
                Token key = tokens[pos++];
                var n = new Node { Key = key.Text, Start = key.Start, End = key.End, ValueStart = key.Start, ValueEnd = key.End, Value = key.Text };
                if (pos < tokens.Count && tokens[pos].Text == "=") {
                    pos++;
                    if (pos == tokens.Count) throw new InvalidDataException("Missing value: " + key.Text);
                    Token v = tokens[pos++]; n.ValueStart = v.Start;
                    if (v.Text == "{") {
                        n.Value = null; n.Children = Read(true);
                        if (pos == tokens.Count || tokens[pos].Text != "}") throw new InvalidDataException("Unclosed block: " + key.Text);
                        n.End = n.ValueEnd = tokens[pos++].End;
                    } else { n.Value = v.Text; n.End = n.ValueEnd = v.End; }
                }
                result.Add(n);
            }
            if (!nested && pos != tokens.Count) throw new InvalidDataException("Unexpected closing brace");
            return result;
        }
    }
}
