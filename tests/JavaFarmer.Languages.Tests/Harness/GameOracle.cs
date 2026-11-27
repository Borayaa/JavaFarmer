using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace JavaFarmer.Languages.Tests;

/// <summary>Runs the real game Tokenizer + Parser headless on generated Python (SPEC §2, §11).</summary>
public static class GameOracle
{
    static readonly object Gate = new();

    /// <summary>Headless, ResourceManager has no items, so "Entities.X" cannot be folded into a constant.
    /// Renaming the bag keeps the same syntax shape (a dotted name) and the same text length.</summary>
    public static string NeutraliseConstBags(string python) =>
        Regex.Replace(python, @"\b(Entities|Items|Grounds|Unlocks|Hats|Leaderboards)\.", m => m.Groups[1].Value[..^1] + "z.");

    /// <summary>Returns null when the game parser accepts the code, otherwise "key @start-end".</summary>
    public static string TryParse(string python)
    {
        lock (Gate)
        {
            var cw = (CodeWindow)RuntimeHelpers.GetUninitializedObject(typeof(CodeWindow));
            var field = typeof(CodeWindow).GetField("parsedFunctions", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!;
            field.SetValue(cw, Activator.CreateInstance(field.FieldType));
            try
            {
                Tokenizer.Tokenize(NeutraliseConstBags(python), out var ts);
                global::Parser.Parse(ts, cw);
                return null;
            }
            catch (ParseException e)
            {
                var s = typeof(ParseException).GetField("startIndex")?.GetValue(e);
                var en = typeof(ParseException).GetField("endIndex")?.GetValue(e);
                return $"{e.Message} @{s}-{en}";
            }
            catch (Exception e)
            {
                return e.GetType().Name + ": " + e.Message;
            }
        }
    }
}
