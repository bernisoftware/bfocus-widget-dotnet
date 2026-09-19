using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Bfocus.Widget
{
    /// <summary>
    /// Escrita de JSON compacto, byte a byte igual ao <c>JSON.stringify</c> do JavaScript.
    /// O payload do usuário vai em base64 e é comparado com o do widget web, então a ordem das
    /// chaves e o escape têm de ser exatos (o System.Text.Json escaparia "&amp;" e "ç").
    /// </summary>
    public static class JsonText
    {
        /// <summary>String JSON com as mesmas regras do JSON.stringify (ES2019).</summary>
        public static string Quote(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            AppendQuoted(sb, value);
            return sb.ToString();
        }

        internal static void AppendQuoted(StringBuilder sb, string value)
        {
            sb.Append('"');
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20)
                        {
                            AppendUnicodeEscape(sb, c);
                        }
                        else if (char.IsHighSurrogate(c))
                        {
                            // Par válido passa inteiro; surrogate solto vira \uXXXX (JSON bem-formado).
                            if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                            {
                                sb.Append(c).Append(value[i + 1]);
                                i++;
                            }
                            else
                            {
                                AppendUnicodeEscape(sb, c);
                            }
                        }
                        else if (char.IsLowSurrogate(c))
                        {
                            AppendUnicodeEscape(sb, c);
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

        private static void AppendUnicodeEscape(StringBuilder sb, char c) =>
            sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
    }

    /// <summary>Monta um objeto JSON compacto preservando a ordem de inserção das chaves.</summary>
    public sealed class JsonObjectBuilder
    {
        private readonly List<KeyValuePair<string, string>> _members = new List<KeyValuePair<string, string>>();

        /// <summary>Acrescenta uma string; <c>null</c> e vazio são omitidos (igual ao widget web).</summary>
        public JsonObjectBuilder AddIfNotEmpty(string key, string? value)
        {
            if (!string.IsNullOrEmpty(value)) _members.Add(new KeyValuePair<string, string>(key, JsonText.Quote(value!)));
            return this;
        }

        public JsonObjectBuilder Add(string key, string value)
        {
            _members.Add(new KeyValuePair<string, string>(key, JsonText.Quote(value)));
            return this;
        }

        /// <summary>Acrescenta um valor já serializado (objeto, número, booleano).</summary>
        public JsonObjectBuilder AddRaw(string key, string rawJson)
        {
            _members.Add(new KeyValuePair<string, string>(key, rawJson));
            return this;
        }

        public bool IsEmpty => _members.Count == 0;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            for (var i = 0; i < _members.Count; i++)
            {
                if (i > 0) sb.Append(',');
                JsonText.AppendQuoted(sb, _members[i].Key);
                sb.Append(':').Append(_members[i].Value);
            }
            sb.Append('}');
            return sb.ToString();
        }
    }
}
