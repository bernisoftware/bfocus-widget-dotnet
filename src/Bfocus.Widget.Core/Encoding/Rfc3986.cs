using System.Text;

namespace Bfocus.Widget
{
    /// <summary>
    /// Codificação de componente de URL pela RFC 3986: só <c>[A-Za-z0-9-._~]</c> ficam como estão,
    /// o resto vira <c>%XX</c> maiúsculo sobre os bytes UTF-8. Feita à mão porque
    /// <c>Uri.EscapeDataString</c> muda de comportamento entre .NET Framework e .NET.
    /// </summary>
    public static class Rfc3986
    {
        private const string Hex = "0123456789ABCDEF";

        public static string Encode(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var bytes = System.Text.Encoding.UTF8.GetBytes(value);
            var sb = new StringBuilder(bytes.Length * 3);
            foreach (var b in bytes)
            {
                if (IsUnreserved(b))
                {
                    sb.Append((char)b);
                }
                else
                {
                    sb.Append('%').Append(Hex[b >> 4]).Append(Hex[b & 0xF]);
                }
            }
            return sb.ToString();
        }

        private static bool IsUnreserved(byte b) =>
            (b >= (byte)'A' && b <= (byte)'Z') ||
            (b >= (byte)'a' && b <= (byte)'z') ||
            (b >= (byte)'0' && b <= (byte)'9') ||
            b == (byte)'-' || b == (byte)'.' || b == (byte)'_' || b == (byte)'~';
    }
}
