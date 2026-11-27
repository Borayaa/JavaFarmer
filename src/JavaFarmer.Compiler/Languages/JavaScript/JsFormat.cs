using System;
using System.Globalization;
using System.Text;

namespace JavaFarmer.Compiler
{
    /// <summary>JavaScript spellings of values: Number.prototype.toString and util.inspect-style string quoting.</summary>
    internal static class JsFormat
    {
        /// <summary>A number as JavaScript's String(n) prints it (3, 0.5, 1e+21, 1e-7; -0 prints as 0).</summary>
        public static string Number(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            if (v == 0) return "0";
            bool neg = v < 0;
            if (neg) v = -v;
            // shortest round-trip digits and decimal exponent
            var r = v.ToString("R", CultureInfo.InvariantCulture);
            string mant = r;
            int exp = 0;
            int e = r.IndexOfAny(new[] { 'E', 'e' });
            if (e >= 0) { mant = r.Substring(0, e); exp = int.Parse(r.Substring(e + 1), CultureInfo.InvariantCulture); }
            int dot = mant.IndexOf('.');
            string digits = dot < 0 ? mant : mant.Remove(dot, 1);
            int pointPos = (dot < 0 ? mant.Length : dot) + exp;   // digits[0..pointPos) are the integer part
            int lead = 0;
            while (lead < digits.Length - 1 && digits[lead] == '0') { lead++; pointPos--; }
            digits = digits.Substring(lead).TrimEnd('0');
            if (digits.Length == 0) return "0";
            int k = digits.Length, n = pointPos;
            var sb = new StringBuilder();
            if (neg) sb.Append('-');
            if (k <= n && n <= 21) sb.Append(digits).Append('0', n - k);
            else if (0 < n && n <= 21) sb.Append(digits, 0, n).Append('.').Append(digits, n, k - n);
            else if (-6 < n && n <= 0) sb.Append("0.").Append('0', -n).Append(digits);
            else
            {
                int ex = n - 1;
                sb.Append(digits[0]);
                if (k > 1) sb.Append('.').Append(digits, 1, k - 1);
                sb.Append('e').Append(ex >= 0 ? "+" : "-").Append(Math.Abs(ex).ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        /// <summary>A string literal in util.inspect style: single quotes, or double / backticks when that avoids escapes.</summary>
        public static string Inspect(string s)
        {
            s = s ?? "";
            char q = '\'';
            if (s.IndexOf('\'') >= 0)
            {
                if (s.IndexOf('"') < 0) q = '"';
                else if (s.IndexOf('`') < 0 && s.IndexOf("${", StringComparison.Ordinal) < 0) q = '`';
            }
            var sb = new StringBuilder();
            sb.Append(q);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\v': sb.Append("\\v"); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c == q) sb.Append('\\').Append(c);
                        else if (c < 0x20 || c == 0x7f) sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append(q);
            return sb.ToString();
        }

        /// <summary>A JavaScript source string literal with the given quote (escapes \ quote and control characters).</summary>
        public static string Literal(string s, char q = '"')
        {
            var sb = new StringBuilder();
            sb.Append(q);
            foreach (var c in s ?? "")
            {
                switch (c)
                {
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\\': sb.Append("\\\\"); break;
                    default:
                        if (c == q) sb.Append('\\').Append(c);
                        else if (c < 0x20) sb.Append("\\x").Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append(q);
            return sb.ToString();
        }

        public static bool IsIdentifierName(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            char c = s[0];
            if (!(char.IsLetter(c) || c == '_' || c == '$')) return false;
            foreach (var ch in s) if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '$')) return false;
            return true;
        }
    }
}
