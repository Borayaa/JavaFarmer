using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>Generic traversal and small analyses over the IR (used by the emitter, printers and inference).</summary>
    public static class IrWalk
    {
        /// <summary>Appends the direct non-null children, in evaluation / source order (no allocation besides the list).</summary>
        public static void AddChildren(IrNode n, List<IrNode> into)
        {
            // leaves are the most common nodes: answer them before the long type switch
            if (n is IrName || n is IrLiteral || n is IrConst || n is IrBreak || n is IrContinue || n is IrFuncRef) return;
            void Y(IrNode x) { if (x != null) into.Add(x); }
            switch (n)
            {
                case IrModule m: foreach (var i in m.Items) Y(i); break;
                case IrGlobal g: if (g.Init != null) Y(g.Init); break;
                case IrFunction f: foreach (var p in f.Params) Y(p); Y(f.Body); break;
                case IrParam p: if (p.Default != null) Y(p.Default); break;
                case IrBlock b: foreach (var s in b.Stmts) Y(s); break;
                case IrVarDecl v: if (v.Init != null) Y(v.Init); break;
                case IrAssign a: Y(a.Target); Y(a.Value); break;
                case IrExprStmt e: Y(e.Expr); break;
                case IrIf i: Y(i.Cond); Y(i.Then); if (i.Else != null) Y(i.Else); break;
                case IrWhile w: Y(w.Cond); Y(w.Body); break;
                case IrDoWhile d: Y(d.Body); Y(d.Cond); break;
                case IrLoop l: Y(l.Body); break;
                case IrClassicFor f:
                    foreach (var s in f.Init) Y(s);
                    if (f.Cond != null) Y(f.Cond);
                    foreach (var u in f.Update) Y(u);
                    Y(f.Body);
                    break;
                case IrRangeFor r:
                    if (r.Start != null) Y(r.Start);
                    Y(r.End);
                    if (r.Step != null) Y(r.Step);
                    Y(r.Body);
                    break;
                case IrForEach fe: Y(fe.Iterable); Y(fe.Body); break;
                case IrSwitch sw: Y(sw.Selector); foreach (var c in sw.Cases) Y(c); break;
                case IrCase c: foreach (var l in c.Labels) Y(l); Y(c.Body); break;
                case IrReturn r: if (r.Value != null) Y(r.Value); break;
                case IrBinary b: Y(b.Left); Y(b.Right); break;
                case IrUnary u: Y(u.Operand); break;
                case IrConditional c: Y(c.Cond); Y(c.Then); Y(c.Else); break;
                case IrCall c:
                    if (c.Callee != null) Y(c.Callee);
                    foreach (var a in c.Args) Y(a);
                    break;
                case IrOpCall o: foreach (var a in o.Args) Y(a); break;
                case IrIndex ix: Y(ix.Target); Y(ix.Index); break;
                case IrListLit l: foreach (var i in l.Items) Y(i); break;
                case IrSetLit s: foreach (var i in s.Items) Y(i); break;
                case IrTupleLit t: foreach (var i in t.Items) Y(i); break;
                case IrMapLit m:
                    for (int i = 0; i < m.Keys.Count; i++) { Y(m.Keys[i]); Y(m.Values[i]); }
                    break;
                case IrLambda l:
                    foreach (var p in l.Params) Y(p);
                    if (l.BodyExpr != null) Y(l.BodyExpr); else if (l.Body != null) Y(l.Body);
                    break;
                case IrCast c: Y(c.Operand); break;
            }
        }

        /// <summary>Direct children in evaluation / source order. Lambdas are children (their bodies too).</summary>
        public static List<IrNode> Children(IrNode n)
        {
            var l = new List<IrNode>();
            AddChildren(n, l);
            return l;
        }

        /// <summary>The node and all descendants (pre-order).</summary>
        public static IEnumerable<IrNode> Descendants(IrNode n, bool intoLambdas = true)
        {
            var res = new List<IrNode>();
            CollectDescendants(n, res, intoLambdas);
            return res;
        }

        /// <summary>Pre-order list of the node and its descendants.</summary>
        public static void CollectDescendants(IrNode n, List<IrNode> into, bool intoLambdas = true)
        {
            if (n == null) return;
            var stack = new List<IrNode> { n };
            var tmp = new List<IrNode>();
            while (stack.Count > 0)
            {
                var cur = stack[stack.Count - 1];
                stack.RemoveAt(stack.Count - 1);
                into.Add(cur);
                if (!intoLambdas && cur is IrLambda && cur != n) continue;
                tmp.Clear();
                AddChildren(cur, tmp);
                for (int i = tmp.Count - 1; i >= 0; i--) stack.Add(tmp[i]);
            }
        }

        static IrSymbol NameSym(IrExpr e) => e is IrName n ? n.Symbol : null;

        /// <summary>Symbols written by assignments (=, op=, ++, unpacking), for-loop variables and declarations in the subtree.</summary>
        public static IEnumerable<IrSymbol> AssignedSymbols(IrNode tree, bool intoLambdas = true)
        {
            foreach (var n in Descendants(tree, intoLambdas))
            {
                switch (n)
                {
                    case IrAssign a:
                        if (a.Target is IrTupleLit t) { foreach (var i in t.Items) if (NameSym(i) != null) yield return NameSym(i); }
                        else if (NameSym(a.Target) != null) yield return NameSym(a.Target);
                        break;
                    case IrRangeFor r: if (r.Var != null) yield return r.Var; break;
                    case IrForEach fe: if (fe.Var != null) yield return fe.Var; if (fe.ValueVar != null) yield return fe.ValueVar; break;
                }
            }
        }

        /// <summary>True if the subtree assigns the symbol (=, op=, ++/--, loop variable).</summary>
        public static bool Assigns(IrNode tree, IrSymbol sym)
        {
            foreach (var s in AssignedSymbols(tree)) if (s == sym) return true;
            return false;
        }

        /// <summary>The set of symbols AssignedSymbols yields (handy when asking about many symbols).</summary>
        public static HashSet<IrSymbol> AssignedSet(IrNode tree, bool intoLambdas = true) => new HashSet<IrSymbol>(AssignedSymbols(tree, intoLambdas));

        /// <summary>Locals/params of enclosing functions that the lambda reads or writes (closure captures), in first-use order.</summary>
        public static List<IrSymbol> Captures(IrLambda lambda)
        {
            var declared = new HashSet<IrSymbol>();
            foreach (var n in Descendants(lambda))
            {
                if (n is IrVarDecl v) declared.Add(v.Symbol);
                if (n is IrParam p) declared.Add(p.Symbol);
                if (n is IrRangeFor r && r.Var != null) declared.Add(r.Var);
                if (n is IrForEach fe) { if (fe.Var != null) declared.Add(fe.Var); if (fe.ValueVar != null) declared.Add(fe.ValueVar); }
            }
            var res = new List<IrSymbol>();
            foreach (var n in Descendants(lambda))
                if (n is IrName nm && nm.Symbol != null && (nm.Symbol.Kind == IrSymbolKind.Local || nm.Symbol.Kind == IrSymbolKind.Param)
                    && !declared.Contains(nm.Symbol) && !res.Contains(nm.Symbol))
                    res.Add(nm.Symbol);
            return res;
        }

        /// <summary>Other modules referenced by the subtree (external calls / globals / function refs), first-use order.</summary>
        public static List<string> ReferencedModules(IrNode tree)
        {
            var res = new List<string>();
            foreach (var n in Descendants(tree))
            {
                IrSymbol s = null;
                if (n is IrName nm) s = nm.Symbol;
                else if (n is IrCall c && c.Kind == IrCallKind.Function) s = c.Function;
                else if (n is IrFuncRef fr) s = fr.Function;
                if (s != null && s.IsExternal && s.Module != null && !res.Contains(s.Module)) res.Add(s.Module);
            }
            return res;
        }

        /// <summary>Strips nothing (the IR has no parentheses); kept for symmetry with printers.</summary>
        public static IrExpr Unwrap(IrExpr e) => e;
    }
}
