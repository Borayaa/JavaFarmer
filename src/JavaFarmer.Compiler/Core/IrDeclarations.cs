using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Places local declarations for sources that have none (Python, sloppy JS, R), so printers of block-scoped
    /// languages can declare each Local once, in the right block:
    /// - For every Local symbol not yet declared by an IrVarDecl, find the smallest statement list (block, or the
    ///   module's items for script locals) that contains all its uses. If the statement there that holds the first use is
    ///   a plain "sym = value" (value not reading sym), it becomes IrVarDecl(sym, value) (comments, spans and layout move
    ///   along); otherwise IrVarDecl(sym, Init = null, Synthetic) is inserted before that statement and the
    ///   assignments stay.
    /// - Loop variables (IrRangeFor / IrForEach Var, ValueVar) are declared by their loop: nothing is placed when every
    ///   use is a loop that binds the symbol or lies inside the body of such a loop. Otherwise they get a declaration
    ///   before (the loop counts as a use).
    /// - Params, globals and symbols that already have an IrVarDecl are never touched, so Place is idempotent.
    /// Run it after IrTypeInference so the declarations see the inferred symbol types. Never throws.
    /// </summary>
    public static class IrDeclarations
    {
        public static void Place(IrModule m)
        {
            if (m == null) return;
            try { new Placer(m).Run(); }
            catch (Exception) { /* best effort */ }
        }

        sealed class Frame
        {
            public object Owner;   // IrModule (Items) or IrBlock (Stmts)
            public int Index;
        }

        sealed class Use
        {
            public Frame[] Path;
            public bool IsLoop;          // the loop statement that binds the symbol
            public bool InBindingLoop;   // inside the body of a loop that binds the symbol
        }

        sealed class Placer
        {
            readonly IrModule mod;
            readonly Dictionary<IrSymbol, List<Use>> uses = new Dictionary<IrSymbol, List<Use>>();
            readonly List<IrSymbol> order = new List<IrSymbol>();
            readonly HashSet<IrSymbol> declared = new HashSet<IrSymbol>();
            readonly List<Frame> path = new List<Frame>();
            readonly List<IrStmt> loops = new List<IrStmt>();

            public Placer(IrModule m) { mod = m; }

            void Record(IrSymbol s, bool isLoop)
            {
                if (s == null || s.Kind != IrSymbolKind.Local) return;
                if (!uses.TryGetValue(s, out var list)) { uses[s] = list = new List<Use>(); order.Add(s); }
                bool inLoop = false;
                foreach (var l in loops)
                    if ((l is IrRangeFor rf && rf.Var == s) || (l is IrForEach fe && (fe.Var == s || fe.ValueVar == s))) { inLoop = true; break; }
                list.Add(new Use { Path = path.Select(f => new Frame { Owner = f.Owner, Index = f.Index }).ToArray(), IsLoop = isLoop, InBindingLoop = inLoop });
            }

            void WalkSeq(object owner, System.Collections.IList items)
            {
                var f = new Frame { Owner = owner };
                path.Add(f);
                for (int i = 0; i < items.Count; i++) { f.Index = i; Walk((IrNode)items[i]); }
                path.RemoveAt(path.Count - 1);
            }

            void Walk(IrNode n)
            {
                switch (n)
                {
                    case null: return;
                    case IrBlock b: WalkSeq(b, b.Stmts); return;
                    case IrVarDecl v: if (v.Symbol != null) declared.Add(v.Symbol); Walk(v.Init); return;
                    case IrName nm: Record(nm.Symbol, false); return;
                    case IrRangeFor rf:
                        Record(rf.Var, true);
                        Walk(rf.Start); Walk(rf.End); Walk(rf.Step);
                        loops.Add(rf); Walk(rf.Body); loops.RemoveAt(loops.Count - 1);
                        return;
                    case IrForEach fe:
                        Record(fe.Var, true);
                        if (fe.ValueVar != null) Record(fe.ValueVar, true);
                        Walk(fe.Iterable);
                        loops.Add(fe); Walk(fe.Body); loops.RemoveAt(loops.Count - 1);
                        return;
                }
                foreach (var c in IrWalk.Children(n)) Walk(c);
            }

            static IrNode At(object owner, int i) => owner is IrModule m ? m.Items[i] : ((IrBlock)owner).Stmts[i];

            static bool Reads(IrNode tree, IrSymbol s) => IrWalk.Descendants(tree).Any(x => x is IrName nm && nm.Symbol == s);

            sealed class Op { public object Owner; public int Index; public IrSymbol Sym; public bool Replace; public int Seq; }

            public void Run()
            {
                // walk: module items are one statement list; function bodies are their own blocks
                WalkSeq(mod, mod.Items);
                var ops = new List<Op>();
                int seq = 0;
                foreach (var s in order)
                {
                    if (declared.Contains(s)) continue;
                    var us = uses[s];
                    if (us.Count == 0) continue;
                    if (us.Any(u => u.IsLoop) && us.All(u => u.IsLoop || u.InBindingLoop)) continue;   // declared by its loop(s)
                    // deepest common statement list
                    int depth = 0;
                    while (true)
                    {
                        if (us.Any(u => u.Path.Length <= depth)) break;
                        var owner = us[0].Path[depth].Owner;
                        if (us.Any(u => u.Path[depth].Owner != owner)) break;
                        if (depth + 1 < us.Min(u => u.Path.Length) && us.All(u => u.Path[depth].Index == us[0].Path[depth].Index)
                            && us.All(u => u.Path[depth + 1].Owner == us[0].Path[depth + 1].Owner))
                        {
                            depth++;
                            continue;
                        }
                        break;
                    }
                    if (us.Any(u => u.Path.Length <= depth)) continue;   // should not happen
                    var blockOwner = us[0].Path[depth].Owner;
                    int k = us.Min(u => u.Path[depth].Index);
                    var stmt = At(blockOwner, k);
                    bool replace = stmt is IrAssign a && a.Op == null && a.Target is IrName tn && tn.Symbol == s && !Reads(a.Value, s);
                    ops.Add(new Op { Owner = blockOwner, Index = k, Sym = s, Replace = replace, Seq = seq++ });
                }
                // apply per owner from the end, so indices stay valid; at one index: replacement, then inserts in first-use order
                foreach (var g in ops.GroupBy(o => o.Owner))
                {
                    foreach (var o in g.OrderByDescending(o => o.Index).ThenBy(o => o.Replace ? 0 : 1).ThenByDescending(o => o.Seq))
                    {
                        if (o.Replace)
                        {
                            var a = (IrAssign)At(o.Owner, o.Index);
                            var tn = (IrName)a.Target;
                            var decl = new IrVarDecl
                            {
                                Symbol = o.Sym, Init = a.Value, NameSpan = tn.NameSpan.IsNone ? tn.Span : tn.NameSpan, TypeExplicit = false,
                                Span = a.Span, Leading = a.Leading, Trailing = a.Trailing, BlankLinesBefore = a.BlankLinesBefore, Synthetic = a.Synthetic,
                            };
                            Set(o.Owner, o.Index, decl);
                        }
                        else
                        {
                            var decl = new IrVarDecl { Symbol = o.Sym, Init = null, Synthetic = true, TypeExplicit = false };
                            // the declaration takes over the statement's leading comments / blank lines so they stay on top
                            var next = At(o.Owner, o.Index);
                            Insert(o.Owner, o.Index, decl);
                            decl.Leading = next.Leading; next.Leading = null;
                            decl.BlankLinesBefore = next.BlankLinesBefore; next.BlankLinesBefore = 0;
                        }
                    }
                }
            }

            static void Set(object owner, int i, IrStmt s)
            {
                if (owner is IrModule m) m.Items[i] = s; else ((IrBlock)owner).Stmts[i] = s;
            }

            static void Insert(object owner, int i, IrStmt s)
            {
                if (owner is IrModule m) m.Items.Insert(i, s); else ((IrBlock)owner).Stmts.Insert(i, s);
            }
        }
    }
}
