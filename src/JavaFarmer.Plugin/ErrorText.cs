using System;
using System.Text.RegularExpressions;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>Rewrites the arguments of the game's runtime error messages so they name Java functions.</summary>
    public static class ErrorText
    {
        private static readonly Regex callName = new Regex("^([A-Za-z_][A-Za-z0-9_]*)\\(\\)$");

        /// <summary>Maps string args of an "error_*" message in place. Returns true when something changed.</summary>
        public static bool MapArgs(string key, object[] args, Func<string, string> toJava = null)
        {
            if (args == null || key == null || !key.StartsWith("error_")) return false;
            bool changed = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (!(args[i] is string s)) continue;
                string m = MapText(s, toJava);
                if (m != null && m != s) { args[i] = m; changed = true; }
            }
            return changed;
        }

        /// <summary>Error args in the given language (Java: the v1 table; Python: unchanged).</summary>
        public static bool MapArgs(string key, object[] args, ILanguage lang)
        {
            if (lang == null || lang.IsNative) return false;
            if (lang.Id == "java") return MapArgs(key, args);
            if (args == null || key == null || !key.StartsWith("error_")) return false;
            bool changed = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (!(args[i] is string s)) continue;
                string m = MapText(s, lang);
                if (m != null && m != s) { args[i] = m; changed = true; }
            }
            return changed;
        }

        /// <summary>
        /// MapText for any language: builtins by ILanguage.ApiName, renamed user names back to the source names,
        /// not/and/or as !/&amp;&amp;/|| unless they are keywords of the language.
        /// </summary>
        public static string MapText(string s, ILanguage lang)
        {
            if (string.IsNullOrEmpty(s) || lang == null || lang.IsNative) return null;
            if (lang.Id == "java") return MapText(s);
            var kw = ActiveLanguage.KeywordSet(lang);
            switch (s)
            {
                case "not": return kw.Contains("not") ? null : "!";
                case "and": return kw.Contains("and") ? null : "&&";
                case "or": return kw.Contains("or") ? null : "||";
            }
            Func<string, string> api = py => { try { return lang.ApiName(py); } catch { return null; } };
            var m = callName.Match(s);
            if (m.Success)
            {
                string j = api(m.Groups[1].Value) ?? UserName(m.Groups[1].Value);
                return string.IsNullOrEmpty(j) ? null : j + "()";
            }
            if (s.IndexOf(' ') < 0)
            {
                string j = api(s) ?? UserName(s);
                return string.IsNullOrEmpty(j) ? null : j;
            }
            return null;
        }

        /// <summary>"can_harvest()" -> "canHarvest()", "can_harvest" -> "canHarvest", "not" -> "!"; null when unchanged.</summary>
        public static string MapText(string s, Func<string, string> toJava = null)
        {
            if (string.IsNullOrEmpty(s)) return null;
            toJava = toJava ?? EditorSupport.SafeToJava;
            switch (s)
            {
                case "not": return "!";
                case "and": return "&&";
                case "or": return "||";
                case "//": return "/";
                case "in": return ".contains()";
                case "not in": return "!.contains()";
            }
            var m = callName.Match(s);
            if (m.Success)
            {
                string j = toJava(m.Groups[1].Value) ?? UserName(m.Groups[1].Value);
                return string.IsNullOrEmpty(j) ? null : j + "()";
            }
            if (s.IndexOf(' ') < 0)
            {
                string j = toJava(s) ?? UserName(s);
                return string.IsNullOrEmpty(j) ? null : j;
            }
            return null;
        }

        /// <summary>A renamed user identifier ("list_j", "__jlambda2") -> its Java name; null when unchanged.</summary>
        private static string UserName(string py)
        {
            if (!IsIdentifier(py)) return null;
            string j = RuntimeNames.ToJava(py);
            return j == py ? null : j;
        }

        private static bool IsIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s) || !(char.IsLetter(s[0]) || s[0] == '_')) return false;
            foreach (char c in s) if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            return true;
        }
    }
}
