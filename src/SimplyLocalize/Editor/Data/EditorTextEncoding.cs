using System;
using System.IO;
using System.Text;

namespace SimplyLocalize.Editor.Data
{
    /// <summary>Strict decoding with BOM preservation. Replacement characters must not
    /// silently turn damaged source bytes into a valid, writable translation document.</summary>
    internal static class EditorTextEncoding
    {
        internal static string Decode(byte[] bytes, string path, out Encoding encoding, out bool bom)
        {
            int prefix;
            if (StartsWith(bytes, 0xff, 0xfe, 0, 0)) { encoding = new UTF32Encoding(false, true, true); prefix = 4; }
            else if (StartsWith(bytes, 0, 0, 0xfe, 0xff)) { encoding = new UTF32Encoding(true, true, true); prefix = 4; }
            else if (StartsWith(bytes, 0xff, 0xfe)) { encoding = new UnicodeEncoding(false, true, true); prefix = 2; }
            else if (StartsWith(bytes, 0xfe, 0xff)) { encoding = new UnicodeEncoding(true, true, true); prefix = 2; }
            else if (StartsWith(bytes, 0xef, 0xbb, 0xbf)) { encoding = new UTF8Encoding(true, true); prefix = 3; }
            else { encoding = new UTF8Encoding(false, true); prefix = 0; }
            bom = prefix > 0;
            try { return encoding.GetString(bytes, prefix, bytes.Length - prefix); }
            catch (DecoderFallbackException error) { throw new InvalidDataException("Invalid text encoding in " + path + ". Repair the source before editing it.", error); }
        }
        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++) if (bytes[i] != prefix[i]) return false;
            return true;
        }
    }
}
