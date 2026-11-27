using System.Text;
using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

public static class Normalise
{
    /// <summary>v1 differential normalisation: CRLF → LF, "3.0" → "3", trimmed.</summary>
    public static string Output(string s) => Regex.Replace((s ?? "").Replace("\r\n", "\n"), @"(-?\d+)\.0(?!\d)", "$1").Trim();

    /// <summary>Makes game string literals raw for CPython: double every backslash inside '...' / "..." tokens.</summary>
    public static string RawStrings(string py) =>
        Regex.Replace(py, "(['\"])(.*?)\\1", m => m.Groups[1].Value + m.Groups[2].Value.Replace("\\", "\\\\") + m.Groups[1].Value);

    static readonly HashSet<string> Fixed = new()
    {
        "def", "while", "for", "if", "else", "elif", "and", "or", "not", "True", "False", "None", "pass", "break", "continue",
        "return", "global", "import", "from", "in", "is", "lambda", "range", "len", "str", "print", "list", "set", "dict",
        "__name__", "Entities", "Items", "Grounds", "Unlocks", "Hats", "Leaderboards", "North", "South", "East", "West",
        "append", "add", "remove", "pop", "insert"
    };

    /// <summary>
    /// Python text with every user identifier replaced by its first-occurrence index (id0, id1, ...) and whitespace
    /// collapsed. Game builtins (GameApi), keywords, constants and helper names (__j*) are kept. Used by round trips:
    /// two programs that differ only in local names / formatting normalise to the same text.
    /// </summary>
    public static string PythonShape(string py)
    {
        var map = new Dictionary<string, string>();
        var sb = new StringBuilder();
        int i = 0;
        py = (py ?? "").Replace("\r\n", "\n");
        while (i < py.Length)
        {
            char c = py[i];
            if (c == '"' || c == '\'')
            {
                int j = py.IndexOf(c, i + 1);
                if (j < 0) j = py.Length - 1;
                sb.Append(py, i, j - i + 1);
                i = j + 1;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int j = i;
                while (j < py.Length && (char.IsLetterOrDigit(py[j]) || py[j] == '_')) j++;
                var w = py.Substring(i, j - i);
                bool attr = i > 0 && py[i - 1] == '.';
                if (Fixed.Contains(w) || w.StartsWith("__j") || GameApi.Find(w) != null)
                    sb.Append(w);
                else if (attr && i >= 2 && IsConstBag(py, i - 1)) sb.Append(w);
                else
                {
                    if (!map.TryGetValue(w, out var n)) map[w] = n = "id" + map.Count;
                    sb.Append(n);
                }
                i = j;
                continue;
            }
            if (c == ' ' || c == '\t')
            {
                // keep indentation depth (count), collapse inner spaces
                bool lineStart = sb.Length == 0 || sb[^1] == '\n';
                int j = i;
                int width = 0;
                while (j < py.Length && (py[j] == ' ' || py[j] == '\t')) { width += py[j] == '\t' ? 4 : 1; j++; }
                if (lineStart) sb.Append(new string('>', width / 4));
                i = j;
                continue;
            }
            sb.Append(c);
            i++;
        }
        return Regex.Replace(sb.ToString(), "\n+", "\n").Trim();
    }

    static bool IsConstBag(string py, int dot)
    {
        int j = dot - 1;
        while (j >= 0 && (char.IsLetterOrDigit(py[j]) || py[j] == '_')) j--;
        var bag = py.Substring(j + 1, dot - j - 1);
        return Array.IndexOf(GameApi.ConstantCategories, bag) >= 0;
    }
}

/// <summary>CPython preludes for differential tests (print/str must format like the language under test).</summary>
public static class Prelude
{
    /// <summary>v1 (Java): null/true/false, floats without ".0", lists [a, b].</summary>
    public const string JavaStyle = @"import builtins as __b
def __fmt(v):
    if v is None:
        return 'null'
    if v is True:
        return 'true'
    if v is False:
        return 'false'
    if isinstance(v, float):
        if v == int(v) and abs(v) < 1e15:
            return __b.str(int(v))
        return repr(v)
    if isinstance(v, list):
        return '[' + ', '.join(__fmt(x) for x in v) + ']'
    return __b.str(v)
def print(*a):
    __b.print(' '.join(__fmt(x) for x in a))
def quick_print(*a):
    print(*a)
def str(*a):
    if len(a) == 0:
        return ''
    return __fmt(a[0])
";

    /// <summary>Prelude for a language whose print uses the given null/true/false spellings (else like JavaStyle).</summary>
    public static string WithLiterals(string nullText, string trueText, string falseText) =>
        JavaStyle.Replace("return 'null'", "return '" + nullText + "'").Replace("return 'true'", "return '" + trueText + "'").Replace("return 'false'", "return '" + falseText + "'");
}
