using System;
using System.Collections.Generic;
using System.Text;

namespace JavaFarmer.Compiler
{
    public sealed partial class SourceMap
    {
        private Segment FindImpl(int pyOffset)
        {
            var segs = Segments;
            if (segs == null || segs.Count == 0) return null;
            int lo = 0, hi = segs.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (segs[mid].PyStart <= pyOffset) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return best < 0 ? segs[0] : segs[best];
        }
    }

    /// <summary>Attributes of an emitted Python range: segment kind, Java span, optional dependency override.</summary>
    internal struct Attr
    {
        public SegmentKind Kind;
        public int JS, JE;
        public string Ovr;
        public bool NoHl;
        public static Attr User(int s, int e) => new Attr { Kind = SegmentKind.User, JS = s, JE = e };
        public static Attr User(Node n) => new Attr { Kind = SegmentKind.User, JS = n.Start, JE = n.End };
        public static Attr Syn(int s, int e) => new Attr { Kind = SegmentKind.Synthetic, JS = s, JE = e };
        public static Attr Syn(Node n) => new Attr { Kind = SegmentKind.Synthetic, JS = n.Start, JE = n.End };
        /// <summary>Synthetic module scaffolding that must never be highlighted (Segment.NoHighlight).</summary>
        public static Attr Scaffold(int s, int e) => new Attr { Kind = SegmentKind.Synthetic, JS = s, JE = e, NoHl = true };
        public static Attr Struct(int s, int e, string ovr = null) => new Attr { Kind = SegmentKind.Structural, JS = s, JE = e, Ovr = ovr };
        public static readonly Attr Helper = new Attr { Kind = SegmentKind.Synthetic, JS = -1, JE = -1 };
        public bool Same(Attr o) => Kind == o.Kind && JS == o.JS && JE == o.JE && Ovr == o.Ovr && NoHl == o.NoHl;
    }

    /// <summary>A piece of Python expression text with per-part attributes and a precedence level.</summary>
    internal sealed class Frag
    {
        /// <summary>
        /// A text part, or (Sub != null) the first SubCount items of another fragment's list. Lists only grow, so a
        /// recorded prefix never changes: adding a fragment is O(1) instead of copying its parts, and a fragment that
        /// changes after it was added does not change the fragments it was added to (the old copy semantics).
        /// </summary>
        internal struct Item
        {
            public string Text;
            public Attr A;
            public List<Item> Sub;
            public int SubCount;
        }

        readonly List<Item> items = new List<Item>();
        public int Prec = Prec_Atom;
        public bool IsCall;          // a Python call (valid as an expression statement)

        public const int Prec_Or = 1, Prec_And = 2, Prec_Not = 3, Prec_Cmp = 4, Prec_Add = 5, Prec_Mul = 6, Prec_Unary = 7, Prec_Pow = 8, Prec_Atom = 9;

        public Frag() { }
        public Frag(string text, Attr a, int prec = Prec_Atom) { Add(text, a); Prec = prec; }
        public Frag Add(string text, Attr a) { if (text.Length > 0) items.Add(new Item { Text = text, A = a }); return this; }
        public Frag Add(Frag f)
        {
            var src = f.items;
            int n = src.Count;
            if (n == 0) return this;
            if (n <= 2) { for (int i = 0; i < n; i++) items.Add(src[i]); }   // small: copying is cheaper than a reference
            else items.Add(new Item { Sub = src, SubCount = n });
            return this;
        }
        public string Text { get { var sb = new StringBuilder(); ForEach(items, items.Count, (t, a) => sb.Append(t)); return sb.ToString(); } }

        /// <summary>Every text part in order (sub-fragments expanded).</summary>
        public void ForEach(Action<string, Attr> visit) => ForEach(items, items.Count, visit);

        static void ForEach(List<Item> list, int count, Action<string, Attr> visit)
        {
            for (int i = 0; i < count; i++)
            {
                var it = list[i];
                if (it.Sub != null) ForEach(it.Sub, it.SubCount, visit);
                else visit(it.Text, it.A);
            }
        }
    }

    /// <summary>Writes Python text and records a Segment for every character.</summary>
    internal sealed class PyWriter
    {
        readonly StringBuilder sb = new StringBuilder();
        readonly List<Segment> segs = new List<Segment>();
        public int Indent;
        public int Length => sb.Length;
        public string Text => sb.ToString();
        public List<Segment> Segments => segs;

        public void Write(string text, Attr a)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = sb.Length;
            sb.Append(text);
            if (segs.Count > 0)
            {
                var last = segs[segs.Count - 1];
                if (last.PyEnd == start && last.Kind == a.Kind && last.JavaStart == a.JS && last.JavaEnd == a.JE && last.DependencyOverride == a.Ovr && last.NoHighlight == a.NoHl)
                {
                    last.PyEnd = sb.Length;
                    return;
                }
            }
            segs.Add(new Segment { PyStart = start, PyEnd = sb.Length, JavaStart = a.JS, JavaEnd = a.JE, Kind = a.Kind, DependencyOverride = a.Ovr, NoHighlight = a.NoHl });
        }

        public void Write(Frag f) => f.ForEach(writePart ?? (writePart = Write));

        Action<string, Attr> writePart;

        public void BeginLine(Attr a) => Write(new string(' ', Indent * 4), a);
        public void EndLine(Attr a) => Write("\n", a);

        public void Line(Frag f, Attr lineAttr)
        {
            BeginLine(lineAttr);
            Write(f);
            EndLine(lineAttr);
        }

        public void Line(string text, Attr a)
        {
            BeginLine(a);
            Write(text, a);
            EndLine(a);
        }

        /// <summary>Appends another writer's output (segments shifted).</summary>
        public void Append(PyWriter other)
        {
            int offset = sb.Length;
            sb.Append(other.sb);
            foreach (var s in other.segs)
                segs.Add(new Segment { PyStart = s.PyStart + offset, PyEnd = s.PyEnd + offset, JavaStart = s.JavaStart, JavaEnd = s.JavaEnd, Kind = s.Kind, DependencyOverride = s.DependencyOverride, NoHighlight = s.NoHighlight });
        }
    }
}
