using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Java text -> game Program. Compiles Java to game Python, runs the game's own tokenizer and parser on it,
    /// then rewrites every node span from Python offsets to Java offsets and registers dependency policies.
    /// Works headless (no Unity runtime needed) so it can be unit tested against the real Core.dll.
    /// </summary>
    public static class JavaParseBridge
    {
        [ThreadStatic] private static bool inOriginalParse;

        /// <summary>True while the bridge itself is running the game's parser (the Harmony prefix must let it through).</summary>
        public static bool InOriginalParse => inOriginalParse;

        /// <summary>Turns a JavaFarmer message into the text shown by the game's error box. The plugin escapes rich text here.</summary>
        public static Func<string, string> FormatMessage = s => s;

        /// <summary>Localises a game parse error key (identity headless).</summary>
        public static Func<string, string> Localize = s => s;

        /// <summary>Compiles and parses Java for the given code window.</summary>
        public static Program Parse(string java, CodeWindow f, IReadOnlyDictionary<string, string> otherFiles = null)
            => Parse(java, f, otherFiles, null);

        /// <summary>
        /// Compiles source of any (non-native) language with its ILanguage.Compile and parses the result; lang null = Java
        /// (the v1 JavaCompiler path).
        /// </summary>
        public static Program Parse(string java, CodeWindow f, IReadOnlyDictionary<string, string> otherFiles, ILanguage lang)
            => Parse(java, f, otherFiles, lang, null);

        /// <summary>otherLanguages: other windows' language ids (their extensions), for cross-language imports.</summary>
        public static Program Parse(string java, CodeWindow f, IReadOnlyDictionary<string, string> otherFiles, ILanguage lang, IReadOnlyDictionary<string, string> otherLanguages)
        {
            java = java ?? "";
            if (java.Trim().Length == 0)
                return RunGameParser("", f);

            CompileResult result;
            try
            {
                var options = new CompileOptions { FileName = f?.fileName ?? "main", OtherFiles = otherFiles, OtherFileLanguages = otherLanguages };
                result = lang == null ? JavaCompiler.Compile(java, options) : lang.Compile(java, options);
            }
            catch (Exception e)
            {
                Log.Error((lang?.DisplayName ?? "Java") + " compiler threw", e);
                throw new ParseException(FormatMessage("internal JavaFarmer error: " + e.Message), 0, ClampEnd(0, 0, java.Length));
            }
            if (result == null)
                throw new ParseException(FormatMessage("internal JavaFarmer error: no compile result"), 0, 0);
            if (!result.Success)
            {
                var d = result.Errors?.FirstOrDefault();
                string msg = d?.Message ?? "compile error";
                int s = ClampStart(d?.Start ?? 0, java.Length);
                int e = ClampEnd(s, d?.End ?? s, java.Length);
                throw new ParseException(FormatMessage(msg), s, e);
            }
            return ParseCompiled(java, result, f);
        }

        /// <summary>Second half of Parse: runs the game parser on a successful CompileResult and remaps it. Public for tests.</summary>
        public static Program ParseCompiled(string java, CompileResult result, CodeWindow f)
        {
            java = java ?? "";
            string py = result.Python ?? "";
            Program program;
            try
            {
                program = RunGameParser(py, f);
            }
            catch (ParseException pe)
            {
                Log.Error("The game rejected the generated Python (" + pe.Message + " at " + pe.startIndex + "-" + pe.endIndex + ") for file '" + f?.fileName + "':\n" + py);
                var (js, je) = MapSpan(new SegmentIndex(result.Map), pe.startIndex, pe.endIndex, java.Length);
                throw new ParseException(FormatMessage("internal JavaFarmer error (please report): the game rejected the generated code: " + SafeLocalize(pe.Message)), js, je);
            }
            Remap(program, result.Map, java.Length);
            try { RuntimeNames.Register(result.Names); } catch (Exception e) { Log.Error("RuntimeNames.Register failed", e); }
            return program;
        }

        private static string SafeLocalize(string s)
        {
            try { return Localize(s); } catch { return s; }
        }

        /// <summary>Tokenize + original Parser.Parse with the re-entrancy guard set.</summary>
        public static Program RunGameParser(string python, CodeWindow f)
        {
            Tokenizer.Tokenize(python ?? "", out TokenStream ts);
            bool prev = inOriginalParse;
            inOriginalParse = true;
            try
            {
                return Parser.Parse(ts, f);
            }
            finally
            {
                inOriginalParse = prev;
            }
        }

        /// <summary>Rewrites node spans Python -> Java and registers dependency policies.</summary>
        public static void Remap(Program program, SourceMap map, int javaLength)
        {
            Remap(program, new SegmentIndex(map), javaLength);
        }

        private static readonly ConditionalWeakTable<Node, object> javaRoots = new ConditionalWeakTable<Node, object>();
        private static readonly ConditionalWeakTable<BoxedNodeParams, object> hiddenNodes = new ConditionalWeakTable<BoxedNodeParams, object>();
        private static readonly ConditionalWeakTable<BoxedNodeParams, object> helperNodes = new ConditionalWeakTable<BoxedNodeParams, object>();

        /// <summary>
        /// Module scaffolding with no Java statement (def main, the __name__ guard and its main() call, global lines):
        /// it has an empty span and must never be highlighted (no blink, no call-stack line, no step-mode line).
        /// </summary>
        public static bool IsHidden(BoxedNodeParams bp) => bp != null && hiddenNodes.TryGetValue(bp, out _);

        /// <summary>A node inside a JavaFarmer helper function (__jdiv, __jget, ...): it has no Java code of its own.</summary>
        public static bool IsHelperCode(BoxedNodeParams bp) => bp != null && helperNodes.TryGetValue(bp, out _);

        /// <summary>True for a syntax tree root produced from Java by this bridge.</summary>
        public static bool IsJavaTree(Node root)
        {
            return root != null && javaRoots.TryGetValue(root, out _);
        }

        /// <summary>
        /// Breakpoints for a Java tree. The game marks the first node in tree order that starts at or after the
        /// breakpoint line; generated Python is not in Java order (methods before main), so pick the node with the
        /// smallest Java start instead (first in tree order on ties, i.e. the outermost statement).
        /// </summary>
        public static void InsertBreakpoints(Node root, HashSet<int> positions)
        {
            var nodes = new List<Node>();
            var seen = new HashSet<Node>(RefEq<Node>.Instance);
            var stack = new Stack<Node>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (n == null || !seen.Add(n)) continue;
                nodes.Add(n);
                if (n.boxedParams != null) n.boxedParams.isBreakpoint = false;
                if (n.slots != null)
                    for (int i = n.slots.Count - 1; i >= 0; i--) stack.Push(n.slots[i]);
            }
            if (positions == null) return;
            foreach (int p in positions.OrderBy(x => x).ToList())
            {
                Node best = null;
                foreach (var n in nodes)
                {
                    var bp = n.boxedParams;
                    if (bp == null || bp.wordStart < p || bp.wordEnd <= bp.wordStart) continue;
                    if (best == null || bp.wordStart < best.boxedParams.wordStart) best = n;
                }
                if (best != null)
                {
                    best.boxedParams.isBreakpoint = true;
                    positions.Remove(p);
                }
            }
        }

        public static void Remap(Program program, SegmentIndex map, int javaLength)
        {
            if (program?.syntaxTree == null) return;
            javaRoots.Remove(program.syntaxTree);
            javaRoots.Add(program.syntaxTree, null);
            var seenParams = new HashSet<BoxedNodeParams>(RefEq<BoxedNodeParams>.Instance);
            var seenNodes = new HashSet<Node>(RefEq<Node>.Instance);
            var stack = new Stack<Node>();
            stack.Push(program.syntaxTree);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                if (node == null || !seenNodes.Add(node)) continue;
                if (node.slots != null)
                    for (int i = node.slots.Count - 1; i >= 0; i--) stack.Push(node.slots[i]);
                if (node is ForNode forNode && forNode.pattern != null) stack.Push(forNode.pattern);

                var bp = node.boxedParams;
                if (bp == null || !seenParams.Add(bp)) continue;
                RemapNode(node, bp, map, javaLength);
            }
        }

        private static void RemapNode(Node node, BoxedNodeParams bp, SegmentIndex map, int javaLength)
        {
            int ps = bp.wordStart, pe = bp.wordEnd;
            if (ps < 0) return; // nodes without a position (e.g. block sequences) keep -1 like vanilla
            Segment seg = map.Find(ps);
            var (js, je) = MapSpan(map, ps, pe, javaLength);
            bp.wordStart = js;
            bp.wordEnd = je;

            hiddenNodes.Remove(bp);
            helperNodes.Remove(bp);
            if (seg != null && seg.NoHighlight)
            {
                bp.wordEnd = bp.wordStart;
                hiddenNodes.Add(bp, null);
            }
            else if (seg == null || seg.JavaStart < 0)
            {
                helperNodes.Add(bp, null);
            }

            var kind = seg?.Kind ?? SegmentKind.Synthetic;
            string ov = seg?.DependencyOverride;
            bool isCall = node is CallNode;
            NodePolicy policy = null;
            if (ov != null) policy = new NodePolicy { Override = ov, IsCall = isCall };
            else if (kind == SegmentKind.Synthetic) policy = new NodePolicy { Skip = true, IsCall = isCall };
            else if (kind == SegmentKind.Structural && !isCall) policy = new NodePolicy { Skip = true, IsCall = false };
            DependencyPolicies.Set(bp, policy);
        }

        /// <summary>
        /// Java span of a Python range: JavaStart of the segment containing pyStart … JavaEnd of the segment containing pyEnd-1.
        /// Helper code (Java range -1) maps to an empty span at 0 (empty spans never blink).
        /// </summary>
        public static (int start, int end) MapSpan(SourceMap map, int pyStart, int pyEnd, int javaLength)
            => MapSpan(new SegmentIndex(map), pyStart, pyEnd, javaLength);

        public static (int start, int end) MapSpan(SegmentIndex map, int pyStart, int pyEnd, int javaLength)
        {
            Segment a = map.Find(Math.Max(0, pyStart));
            if (a == null || a.JavaStart < 0) return (0, 0);
            Segment b = pyEnd - 1 > pyStart ? map.Find(pyEnd - 1) : a;
            int js = a.JavaStart;
            int je = (b != null && b.JavaEnd >= js && b.JavaStart >= 0) ? b.JavaEnd : a.JavaEnd;
            if (je < js) je = js;
            js = ClampStart(js, javaLength);
            je = ClampEnd(js, je, javaLength);
            return (js, je);
        }

        private static int ClampStart(int s, int len)
        {
            if (s < 0) s = 0;
            if (len == 0) return 0;
            if (s > len - 1) s = len - 1;
            return s;
        }

        private static int ClampEnd(int s, int e, int len)
        {
            if (e < s) e = s;
            if (e > len) e = len;
            return e;
        }

        /// <summary>
        /// Python offset -> segment lookup built from SourceMap.Segments (does not depend on SourceMap.Find).
        /// The innermost segment containing the offset wins; in a gap, the nearest preceding segment is used.
        /// </summary>
        public sealed class SegmentIndex
        {
            private readonly Segment[] byOffset;

            public SegmentIndex(SourceMap map)
            {
                var segs = map?.Segments;
                if (segs == null || segs.Count == 0) { byOffset = new Segment[0]; return; }
                int max = 0;
                foreach (var s in segs) if (s != null && s.PyEnd > max) max = s.PyEnd;
                byOffset = new Segment[max + 1];
                // paint longer segments first so nested (shorter) segments win
                var ordered = segs.Where(s => s != null && s.PyEnd > s.PyStart && s.PyStart >= 0)
                                  .OrderByDescending(s => s.PyEnd - s.PyStart).ToList();
                foreach (var s in ordered)
                    for (int i = s.PyStart; i < s.PyEnd && i < byOffset.Length; i++) byOffset[i] = s;
                // gaps: nearest preceding segment (the one that ended last before the gap)
                Segment last = null;
                for (int i = 0; i < byOffset.Length; i++)
                {
                    if (byOffset[i] != null) last = byOffset[i];
                    else byOffset[i] = last;
                }
            }

            public Segment Find(int pyOffset)
            {
                if (byOffset.Length == 0 || pyOffset < 0) return null;
                if (pyOffset >= byOffset.Length) return byOffset[byOffset.Length - 1];
                return byOffset[pyOffset];
            }
        }

        private sealed class RefEq<T> : IEqualityComparer<T> where T : class
        {
            public static readonly RefEq<T> Instance = new RefEq<T>();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T o) => RuntimeHelpers.GetHashCode(o);
        }
    }
}
