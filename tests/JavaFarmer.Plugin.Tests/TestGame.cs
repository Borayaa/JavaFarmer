using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin.Tests;

/// <summary>Headless game helpers (real Core.dll, no Unity runtime).</summary>
public static class TestGame
{
    public static CodeWindow Window(string fileName = "main")
    {
        var cw = (CodeWindow)RuntimeHelpers.GetUninitializedObject(typeof(CodeWindow));
        typeof(CodeWindow).GetField("parsedFunctions")!.SetValue(cw, new ConcurrentDictionary<string, FunctionNode>());
        cw.fileName = fileName;
        return cw;
    }

    public static ProgramState State()
    {
        var st = (ProgramState)RuntimeHelpers.GetUninitializedObject(typeof(ProgramState));
        st.currentDependencies = new List<(string, int, int)>();
        return st;
    }

    public static List<Node> Nodes(Program p)
    {
        var result = new List<Node>();
        var seen = new HashSet<Node>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<Node>();
        stack.Push(p.syntaxTree);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            if (n == null || !seen.Add(n)) continue;
            result.Add(n);
            foreach (var s in n.slots) stack.Push(s);
            if (n is ForNode f && f.pattern != null) stack.Push(f.pattern);
        }
        return result;
    }

    /// <summary>Dependencies a program checks via CheckDependencies, with the JavaFarmer policies applied.</summary>
    public static List<string> CheckedDependencies(Program p)
    {
        var st = State();
        foreach (var n in Nodes(p))
            if (DependencyPolicies.BeforeCheck(n.boxedParams, st.currentDependencies))
                n.CheckDependencies(st, null);
        return st.currentDependencies.Select(d => d.Item1).ToList();
    }

    public static string JavaText(string java, Node n)
    {
        var bp = n.boxedParams;
        return java.Substring(bp.wordStart, bp.wordEnd - bp.wordStart);
    }
}

/// <summary>Builds a hand-written CompileResult: Python text plus segments pointing into the Java text.</summary>
public sealed class PyBuilder
{
    private readonly string java;
    private readonly StringBuilder py = new();
    private readonly List<Segment> segs = new();
    private int javaSearchFrom;

    public PyBuilder(string java) { this.java = java; }

    /// <summary>Text with no segment (gap).</summary>
    public PyBuilder Raw(string text) { py.Append(text); return this; }

    /// <summary>Python text tagged with a segment mapped to the next occurrence of javaAnchor (null = helper, -1/-1).</summary>
    public PyBuilder Add(string text, string javaAnchor, SegmentKind kind = SegmentKind.User, string dependencyOverride = null, bool rewind = false)
    {
        int js = -1, je = -1;
        if (javaAnchor != null)
        {
            js = java.IndexOf(javaAnchor, rewind ? 0 : javaSearchFrom, StringComparison.Ordinal);
            if (js < 0) throw new ArgumentException("anchor not found: " + javaAnchor);
            je = js + javaAnchor.Length;
            if (!rewind) javaSearchFrom = je;
        }
        segs.Add(new Segment { PyStart = py.Length, PyEnd = py.Length + text.Length, JavaStart = js, JavaEnd = je, Kind = kind, DependencyOverride = dependencyOverride });
        py.Append(text);
        return this;
    }

    public CompileResult Build() => new CompileResult { Success = true, Python = py.ToString(), Map = new SourceMap(segs) };
}
