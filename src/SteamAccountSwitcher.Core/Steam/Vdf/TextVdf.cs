using System;
using System.Collections.Generic;
using System.Text;

namespace SteamAccountSwitcher.Core.Steam.Vdf
{
    /// <summary>A text-VDF object: case-insensitive keys mapping to strings or nested objects.</summary>
    public sealed class VdfObject
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> keys = new List<string>();

        /// <summary>Keys in file order (duplicates collapsed; the last value wins).</summary>
        public IReadOnlyList<string> Keys => keys;

        public string GetString(string key) => values.TryGetValue(key, out var v) ? v as string : null;

        public VdfObject GetObject(string key) => values.TryGetValue(key, out var v) ? v as VdfObject : null;

        internal void Set(string key, object value)
        {
            if (!values.ContainsKey(key))
            {
                keys.Add(key);
            }

            values[key] = value;
        }
    }

    /// <summary>
    /// Minimal reader for Valve's text KeyValues format: quoted or bare tokens, braces,
    /// // comments and [$PLATFORM] conditionals (ignored). Read-only by design (SPEC §5.4).
    /// </summary>
    public static class TextVdf
    {
        public static VdfObject Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var pos = 0;
            var root = new VdfObject();
            ParseBody(text, ref pos, root, isRoot: true);
            return root;
        }

        private static void ParseBody(string text, ref int pos, VdfObject target, bool isRoot)
        {
            while (true)
            {
                var key = NextToken(text, ref pos, out var keyKind);
                if (keyKind == TokenKind.End)
                {
                    if (!isRoot)
                    {
                        throw new FormatException("Unexpected end of VDF: missing '}'.");
                    }

                    return;
                }

                if (keyKind == TokenKind.Close)
                {
                    if (isRoot)
                    {
                        throw new FormatException($"Unexpected '}}' at offset {pos - 1}.");
                    }

                    return;
                }

                if (keyKind != TokenKind.String)
                {
                    throw new FormatException($"Expected a key at offset {pos - 1}.");
                }

                var value = NextToken(text, ref pos, out var valueKind);
                if (valueKind == TokenKind.Open)
                {
                    var child = new VdfObject();
                    ParseBody(text, ref pos, child, isRoot: false);
                    target.Set(key, child);
                }
                else if (valueKind == TokenKind.String)
                {
                    target.Set(key, value);
                }
                else
                {
                    throw new FormatException($"Expected a value for key '{key}'.");
                }
            }
        }

        private enum TokenKind
        {
            String,
            Open,
            Close,
            End,
        }

        private static string NextToken(string text, ref int pos, out TokenKind kind)
        {
            while (pos < text.Length)
            {
                var c = text[pos];
                if (char.IsWhiteSpace(c))
                {
                    pos++;
                }
                else if (c == '/' && pos + 1 < text.Length && text[pos + 1] == '/')
                {
                    while (pos < text.Length && text[pos] != '\n')
                    {
                        pos++;
                    }
                }
                else if (c == '[')
                {
                    // Platform conditional such as [$WIN32]; not needed for loginusers.vdf.
                    var end = text.IndexOf(']', pos);
                    pos = end < 0 ? text.Length : end + 1;
                }
                else
                {
                    break;
                }
            }

            if (pos >= text.Length)
            {
                kind = TokenKind.End;
                return null;
            }

            switch (text[pos])
            {
                case '{':
                    pos++;
                    kind = TokenKind.Open;
                    return null;
                case '}':
                    pos++;
                    kind = TokenKind.Close;
                    return null;
                case '"':
                    kind = TokenKind.String;
                    return ReadQuoted(text, ref pos);
                default:
                    kind = TokenKind.String;
                    return ReadBare(text, ref pos);
            }
        }

        private static string ReadQuoted(string text, ref int pos)
        {
            var start = pos;
            pos++;
            var sb = new StringBuilder();
            while (pos < text.Length)
            {
                var c = text[pos++];
                if (c == '"')
                {
                    return sb.ToString();
                }

                if (c == '\\' && pos < text.Length)
                {
                    var e = text[pos++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        default: sb.Append(e); break;
                    }
                }
                else
                {
                    sb.Append(c);
                }
            }

            throw new FormatException($"Unterminated string starting at offset {start}.");
        }

        private static string ReadBare(string text, ref int pos)
        {
            var start = pos;
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] != '{' && text[pos] != '}' && text[pos] != '"')
            {
                pos++;
            }

            return text.Substring(start, pos - start);
        }
    }
}
