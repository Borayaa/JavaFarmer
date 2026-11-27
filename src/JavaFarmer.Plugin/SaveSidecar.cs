// Per-save mod data: Saves/<save>/javafarmer.json. Shared contract (see docs/SPEC-v2.md §5, docs/EXTENSIONS.md §1).
// The plugin-lang work implements the file IO (read on Saver.Load, write in WriteCodeFiles to save + backup paths).
// Owners may ADD members; do not rename or remove these.
//
// File format (version 2):
//   {
//     "version": 2,
//     "language": "java",            // the default language (Mods menu) when the save was written; informational
//     "extensions": { "<extension id>": <the section's JSON value>, ... }
//   }
// Since version 2 every code file carries its own language in its extension (main.java, utils.rs, bot.py).
// Version 1 (or no file) = a save from before: its code files are all *.py; SaveMigration renames them once.
// A section whose text is valid JSON is stored as that JSON value; any other text is stored as a JSON string.
// Extensions should keep their section a JSON object.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace JavaFarmer.Plugin
{
    public static class SaveSidecar
    {
        /// <summary>
        /// Format 2: the default language when the save was written (informational; every file has its own extension).
        /// Format 1: the language all *.py files of the save were written in. Null = unknown.
        /// </summary>
        public static string Language;

        /// <summary>Opaque per-feature JSON blobs, keyed by extension id. Extensions own their blob's format.</summary>
        public static readonly Dictionary<string, string> Sections = new Dictionary<string, string>();

        /// <summary>Raised after a save's sidecar was read (Sections filled, or cleared for a save without one).</summary>
        public static event Action Loaded;

        /// <summary>Raised just before the sidecar is written: extensions put their current state into Sections.</summary>
        public static event Action Saving;

        public static void RaiseLoaded() => Loaded?.Invoke();
        public static void RaiseSaving() => Saving?.Invoke();

        // ------------------------------------------------------------------ added by plugin-lang

        public const string FileName = "javafarmer.json";
        public const int FormatVersion = 2;

        /// <summary>"version" of the file read last (0 = no file / no version). Set to FormatVersion once a save is migrated.</summary>
        public static int FileVersion;
        /// <summary>Language of a save that has code files but no sidecar (a v1 JavaFarmer save).</summary>
        public const string LegacyLanguage = "java";

        /// <summary>Folder of the save whose sidecar was read last (Saves/&lt;save&gt;), or null.</summary>
        public static string SaveDirectory { get; private set; }

        /// <summary>True when the last Read found a javafarmer.json.</summary>
        public static bool FileFound { get; private set; }

        /// <summary>Language, or "java" when unknown (old saves).</summary>
        public static string EffectiveLanguage => string.IsNullOrEmpty(Language) ? LegacyLanguage : Language;

        public static string PathIn(string saveDir) => Path.Combine(saveDir ?? ".", FileName);

        /// <summary>
        /// Reads Saves/&lt;save&gt;/javafarmer.json (missing or broken file: Language = null, Sections cleared) and raises Loaded.
        /// Returns true when the file existed and parsed.
        /// </summary>
        public static bool Read(string saveDir)
        {
            SaveDirectory = saveDir;
            FileFound = false;
            FileVersion = 0;
            Language = null;
            Sections.Clear();
            bool ok = false;
            try
            {
                string path = PathIn(saveDir);
                if (saveDir != null && File.Exists(path))
                {
                    ok = Deserialize(File.ReadAllText(path));
                    FileFound = ok;
                    if (!ok) Log.Warn(path + " is not valid JSON: ignored (the save is treated as written in " + LegacyLanguage + ").");
                }
            }
            catch (Exception e)
            {
                Log.Error("could not read " + FileName + " in " + saveDir, e);
            }
            try { RaiseLoaded(); } catch (Exception e) { Log.Error("SaveSidecar.Loaded handler failed", e); }
            return ok;
        }

        /// <summary>Raises Saving, then writes the sidecar into every given folder (save + backup). Never throws.</summary>
        public static void Write(params string[] dirs) => Write(true, dirs);

        /// <summary>Writes the sidecar into every given folder; raiseSaving = false writes the current state as it is.</summary>
        public static void Write(bool raiseSaving, params string[] dirs)
        {
            if (raiseSaving)
            {
                try { RaiseSaving(); } catch (Exception e) { Log.Error("SaveSidecar.Saving handler failed", e); }
            }
            string json;
            try { json = Serialize(); }
            catch (Exception e) { Log.Error("could not serialize " + FileName, e); return; }
            if (dirs == null) return;
            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir)) continue;
                try
                {
                    Directory.CreateDirectory(dir);
                    string path = PathIn(dir);
                    if (File.Exists(path) && File.ReadAllText(path) == json) continue;
                    File.WriteAllText(path, json);
                }
                catch (Exception e)
                {
                    Log.Error("could not write " + FileName + " in " + dir, e);
                }
            }
        }

        /// <summary>The sidecar text for the current state.</summary>
        public static string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append("{\n  \"version\": ").Append(FormatVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            sb.Append("  \"language\": ");
            if (string.IsNullOrEmpty(Language)) sb.Append("null"); else MiniJson.WriteString(sb, Language);
            sb.Append(",\n  \"extensions\": {");
            var keys = new List<string>(Sections.Keys);
            keys.Sort(StringComparer.Ordinal);
            bool first = true;
            foreach (var k in keys)
            {
                sb.Append(first ? "\n    " : ",\n    ");
                first = false;
                MiniJson.WriteString(sb, k);
                sb.Append(": ");
                string v = Sections[k];
                if (v != null && MiniJson.IsValid(v)) sb.Append(v.Trim());
                else if (v == null) sb.Append("null");
                else MiniJson.WriteString(sb, v);
            }
            sb.Append(first ? "}\n}\n" : "\n  }\n}\n");
            return sb.ToString();
        }

        /// <summary>Fills Language and Sections from sidecar text. Returns false (and leaves both empty) for invalid JSON.</summary>
        public static bool Deserialize(string json)
        {
            Language = null;
            FileVersion = 0;
            Sections.Clear();
            if (!MiniJson.TryParseObject(json, out var top)) return false;
            if (top.TryGetValue("version", out var ver) && int.TryParse(ver.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v)) FileVersion = v;
            if (top.TryGetValue("language", out var lang) && MiniJson.TryReadString(lang, out var l) && !string.IsNullOrWhiteSpace(l))
                Language = l.Trim();
            if (top.TryGetValue("extensions", out var ext) && MiniJson.TryParseObject(ext, out var sections))
            {
                foreach (var kv in sections)
                {
                    if (kv.Value == "null") continue;
                    Sections[kv.Key] = MiniJson.TryReadString(kv.Value, out var s) ? s : kv.Value;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// Just enough JSON for the sidecar: validation, top-level object members as raw JSON text, strings.
    /// (netstandard2.1 on Unity's Mono has no System.Text.Json.)
    /// </summary>
    public static class MiniJson
    {
        public static bool IsValid(string text)
        {
            if (text == null) return false;
            int i = 0;
            if (!SkipValue(text, ref i)) return false;
            SkipWs(text, ref i);
            return i == text.Length;
        }

        /// <summary>Object members -> raw JSON text of each value (later duplicates win).</summary>
        public static bool TryParseObject(string text, out Dictionary<string, string> members)
        {
            members = new Dictionary<string, string>(StringComparer.Ordinal);
            if (text == null) return false;
            int i = 0;
            SkipWs(text, ref i);
            if (i >= text.Length || text[i] != '{') return false;
            i++;
            SkipWs(text, ref i);
            if (i < text.Length && text[i] == '}') { i++; return Rest(text, i); }
            while (true)
            {
                SkipWs(text, ref i);
                if (!ReadString(text, ref i, out var key)) return false;
                SkipWs(text, ref i);
                if (i >= text.Length || text[i] != ':') return false;
                i++;
                SkipWs(text, ref i);
                int start = i;
                if (!SkipValue(text, ref i)) return false;
                members[key] = text.Substring(start, i - start);
                SkipWs(text, ref i);
                if (i >= text.Length) return false;
                if (text[i] == ',') { i++; continue; }
                if (text[i] == '}') { i++; return Rest(text, i); }
                return false;
            }
        }

        private static bool Rest(string text, int i)
        {
            SkipWs(text, ref i);
            return i == text.Length;
        }

        public static bool TryReadString(string json, out string value)
        {
            value = null;
            if (json == null) return false;
            int i = 0;
            SkipWs(json, ref i);
            if (!ReadString(json, ref i, out value)) return false;
            SkipWs(json, ref i);
            return i == json.Length;
        }

        public static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        private static void SkipWs(string t, ref int i)
        {
            while (i < t.Length && (t[i] == ' ' || t[i] == '\t' || t[i] == '\n' || t[i] == '\r')) i++;
        }

        private static bool ReadString(string t, ref int i, out string value)
        {
            value = null;
            if (i >= t.Length || t[i] != '"') return false;
            i++;
            var sb = new StringBuilder();
            while (i < t.Length)
            {
                char c = t[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c < 0x20) return false;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= t.Length) return false;
                char e = t[i++];
                switch (e)
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
                        if (i + 4 > t.Length || !int.TryParse(t.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code)) return false;
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }

        private static bool SkipValue(string t, ref int i)
        {
            SkipWs(t, ref i);
            if (i >= t.Length) return false;
            char c = t[i];
            if (c == '"') return ReadString(t, ref i, out _);
            if (c == '{' || c == '[')
            {
                char close = c == '{' ? '}' : ']';
                i++;
                SkipWs(t, ref i);
                if (i < t.Length && t[i] == close) { i++; return true; }
                while (true)
                {
                    SkipWs(t, ref i);
                    if (c == '{')
                    {
                        if (!ReadString(t, ref i, out _)) return false;
                        SkipWs(t, ref i);
                        if (i >= t.Length || t[i] != ':') return false;
                        i++;
                    }
                    if (!SkipValue(t, ref i)) return false;
                    SkipWs(t, ref i);
                    if (i >= t.Length) return false;
                    if (t[i] == ',') { i++; continue; }
                    if (t[i] == close) { i++; return true; }
                    return false;
                }
            }
            if (Literal(t, ref i, "true") || Literal(t, ref i, "false") || Literal(t, ref i, "null")) return true;
            int start = i;
            if (i < t.Length && t[i] == '-') i++;
            int digits = i;
            while (i < t.Length && char.IsDigit(t[i])) i++;
            if (i == digits) { i = start; return false; }
            if (i < t.Length && t[i] == '.') { i++; int f = i; while (i < t.Length && char.IsDigit(t[i])) i++; if (i == f) return false; }
            if (i < t.Length && (t[i] == 'e' || t[i] == 'E'))
            {
                i++;
                if (i < t.Length && (t[i] == '+' || t[i] == '-')) i++;
                int ex = i;
                while (i < t.Length && char.IsDigit(t[i])) i++;
                if (i == ex) return false;
            }
            return true;
        }

        private static bool Literal(string t, ref int i, string word)
        {
            if (string.CompareOrdinal(t, i, word, 0, word.Length) != 0) return false;
            i += word.Length;
            return true;
        }
    }
}
