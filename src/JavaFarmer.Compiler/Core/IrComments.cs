using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Attaches source comments to IR nodes by position (for frontends whose parser drops comments):
    /// a comment on the line of a statement's end becomes its Trailing comment, other comments become Leading comments
    /// of the next statement in the innermost enclosing block, or that block's / the module's TrailingComments.
    /// Requires Span on statements, blocks (covering their body) and functions.
    /// </summary>
    public static class IrComments
    {
        public static void Attach(IrModule module, IEnumerable<IrComment> comments, string source = null)
        {
            if (module == null || comments == null) return;
            foreach (var c in comments.Where(c => c != null && !c.Span.IsNone).OrderBy(c => c.Span.Start))
            {
                if (module.TrailingComments == null) module.TrailingComments = new List<IrComment>();
                Place(c, module.Items, module.TrailingComments, source);
            }
            if (module.TrailingComments != null && module.TrailingComments.Count == 0) module.TrailingComments = null;
        }

        static bool Contains(Span outer, Span inner) => !outer.IsNone && outer.Start <= inner.Start && inner.End <= outer.End;

        static bool SameLine(string src, int from, int to)
        {
            if (src == null) return false;
            for (int i = from; i < to && i < src.Length; i++) if (src[i] == '\n') return false;
            return true;
        }

        static void Place<T>(IrComment c, List<T> items, List<IrComment> trailing, string src) where T : IrNode
        {
            foreach (var item in items)
            {
                if (!Contains(item.Span, c.Span)) continue;
                foreach (var (stmts, trail, span) in SubBlocks(item))
                {
                    if (!Contains(span, c.Span)) continue;
                    Place(c, stmts, trail, src);
                    return;
                }
                if (item.Trailing == null && SameLine(src, c.Span.End, item.Span.End)) item.Trailing = c;
                else item.AddLeading(c);
                return;
            }
            T prev = null, next = null;
            foreach (var item in items)
            {
                if (item.Span.IsNone) continue;
                if (item.Span.End <= c.Span.Start) prev = item;
                else if (item.Span.Start >= c.Span.End) { next = item; break; }
            }
            if (prev != null && prev.Trailing == null && SameLine(src ?? "", prev.Span.End, c.Span.Start) && src != null) prev.Trailing = c;
            else if (next != null) next.AddLeading(c);
            else trailing.Add(c);
        }

        /// <summary>Nested statement lists of a node: (statements, their trailing-comment list, span of the block).</summary>
        static IEnumerable<(List<IrStmt>, List<IrComment>, Span)> SubBlocks(IrNode n)
        {
            IEnumerable<(List<IrStmt>, List<IrComment>, Span)> Of(IrBlock b)
            {
                if (b == null) yield break;
                if (b.TrailingComments == null) b.TrailingComments = new List<IrComment>();
                yield return (b.Stmts, b.TrailingComments, b.Span);
            }
            switch (n)
            {
                case IrFunction f: foreach (var x in Of(f.Body)) yield return x; break;
                case IrBlock b: foreach (var x in Of(b)) yield return x; break;
                case IrIf i:
                    foreach (var x in Of(i.Then)) yield return x;
                    if (i.Else is IrBlock eb) foreach (var x in Of(eb)) yield return x;
                    else if (i.Else is IrIf ei) yield return (new List<IrStmt> { ei }, i.Then.TrailingComments ?? new List<IrComment>(), ei.Span);
                    break;
                case IrWhile w: foreach (var x in Of(w.Body)) yield return x; break;
                case IrDoWhile d: foreach (var x in Of(d.Body)) yield return x; break;
                case IrLoop l: foreach (var x in Of(l.Body)) yield return x; break;
                case IrClassicFor cf: foreach (var x in Of(cf.Body)) yield return x; break;
                case IrRangeFor rf: foreach (var x in Of(rf.Body)) yield return x; break;
                case IrForEach fe: foreach (var x in Of(fe.Body)) yield return x; break;
                case IrSwitch sw: foreach (var cs in sw.Cases) foreach (var x in Of(cs.Body)) yield return x; break;
            }
            // lambdas with block bodies anywhere inside the node's expressions
            if (!(n is IrBlock))
                foreach (var lam in IrWalk.Descendants(n).OfType<IrLambda>())
                    if (lam.Body != null) foreach (var x in Of(lam.Body)) yield return x;
        }

        /// <summary>Removes the empty TrailingComments lists Attach created (cosmetic; printers treat null and empty alike).</summary>
        public static void Tidy(IrModule m)
        {
            foreach (var b in IrWalk.Descendants(m).OfType<IrBlock>())
                if (b.TrailingComments != null && b.TrailingComments.Count == 0) b.TrailingComments = null;
        }
    }
}
