using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed partial class JsLowering
    {
        /// <summary>Source → IR: parse, lower, attach comments, infer types, type-directed fix-ups and checks.</summary>
        public static IrModule Build(string source, CompileOptions options, List<Diagnostic> errors)
        {
            source = source ?? "";
            var comments = new List<IrComment>();
            List<JsStmt> program;
            try { program = JsParser.Parse(source, comments); }
            catch (JsSyntaxError e)
            {
                int s = Math.Max(0, Math.Min(e.Span.Start, source.Length));
                int en = Math.Max(s, Math.Min(e.Span.End, source.Length));
                errors.Add(new Diagnostic(e.Message, s, en));
                return null;
            }
            var l = new JsLowering(source, options?.FileName, options?.OtherFiles, errors) { options = options };
            l.LowerModule(program);
            if (errors.Count > 0) return null;
            var m = l.mod;
            if (comments.Count > 0)
            {
                IrComments.Attach(m, comments, source);
                IrComments.Tidy(m);
            }
            // windows in other languages: give calls into them their real types (rule 6 of docs/LANGUAGE-GUIDE.md §9)
            if (l.tupleCandidates.Count > 0) l.ConstTuples();
            if (HasForeign(options)) ForeignModules.AnnotateExternals(m, options);
            IrTypeInference.Infer(m);
            l.FixUps();
            l.Checks();
            return errors.Count > 0 ? null : m;
        }

        static bool HasForeign(CompileOptions options)
        {
            if (options?.OtherFileLanguages == null || options.OtherFiles == null) return false;
            foreach (var kv in options.OtherFileLanguages)
                if (options.OtherFiles.ContainsKey(kv.Key) && ForeignModules.IsForeign(options, kv.Key, "js")) return true;
            return false;
        }

        /// <summary>
        /// "const p = [x, y]" whose variable is only read (indexed, measured, searched, destructured, iterated, used as a
        /// Map key / Set item / stored value) becomes a game tuple, like an inline array literal used as a key: game lists
        /// cannot be dict keys or set items, tuples can (and compare by value). Any other use keeps the list.
        /// </summary>
        void ConstTuples()
        {
            var bySym = new Dictionary<IrSymbol, IrListLit>();
            foreach (var kv in tupleCandidates)
                if (!kv.Value.Exported && kv.Value.Sym.IsConst) bySym[kv.Value.Sym] = kv.Key;
            if (bySym.Count == 0) return;
            var bad = new HashSet<IrSymbol>();
            var holders = new List<IrNode>();
            void Walk(IrNode n, IrNode parent, IrNode grand)
            {
                if (n == null) return;
                if (n is IrName nm && nm.Symbol != null && bySym.ContainsKey(nm.Symbol) && !ReadOnlyUse(nm, parent, grand)) bad.Add(nm.Symbol);
                if ((n is IrVarDecl vd && vd.Init is IrListLit vl && bySym.ContainsKey(vd.Symbol) && bySym[vd.Symbol] == vl)
                    || (n is IrGlobal g && g.Init is IrListLit gl && bySym.ContainsKey(g.Symbol) && bySym[g.Symbol] == gl)) holders.Add(n);
                foreach (var c in IrWalk.Children(n)) Walk(c, n, parent);
            }
            Walk(mod, null, null);
            foreach (var h in holders)
            {
                var sym = h is IrVarDecl d ? d.Symbol : ((IrGlobal)h).Symbol;
                if (bad.Contains(sym)) continue;
                var list = bySym[sym];
                var t = new IrTupleLit { Span = list.Span, ParenSpan = list.ParenSpan, Leading = list.Leading, Trailing = list.Trailing };
                t.Items.AddRange(list.Items);
                if (h is IrVarDecl vd2) vd2.Init = t; else ((IrGlobal)h).Init = t;
            }
        }

        static bool ReadOnlyUse(IrName n, IrNode parent, IrNode grand)
        {
            switch (parent)
            {
                case IrIndex ix:
                    if (ix.Index == n) return true;   // a key
                    // p[0] read (not p[0] = v / p[0] += v)
                    return !(grand is IrAssign ga && ga.Target == ix);
                case IrOpCall o:
                    if (o.Args.Count > 0 && o.Args[0] == n)
                        return o.Op == IrOp.Len || o.Op == IrOp.IsEmpty || o.Op == IrOp.Contains || o.Op == IrOp.ListIndexOf
                            || o.Op == IrOp.ListCopy || o.Op == IrOp.SetCopy;
                    return true;   // a key, an item or a stored value
                case IrAssign a:
                    return a.Value == n && a.Target is IrTupleLit;   // const [x, y] = p
                case IrForEach fe:
                    return fe.Iterable == n;
                case IrTupleLit _:
                case IrSetLit _:
                    return true;   // an item of a tuple / set
                case IrMapLit ml:
                    return ml.Keys.Contains(n);
                case IrCall c:
                    // console.log(p): FormatValue shows a game tuple like an array
                    return c.Kind == IrCallKind.Builtin && (c.Builtin == "print" || c.Builtin == "quick_print");
            }
            return false;
        }

        void FixUps()
        {
            foreach (var n in fixupCandidates)
            {
                switch (n)
                {
                    case IrBinary b when b.Op == IrBinOp.Add && ((b.Left.Type?.IsString ?? false) || (b.Right.Type?.IsString ?? false)):
                        b.Op = IrBinOp.Concat;
                        b.Type = IrType.String;
                        break;
                    case IrAssign a when a.Op == IrBinOp.Add && ((a.Target.Type?.IsString ?? false) || (a.Value.Type?.IsString ?? false)):
                        a.Op = IrBinOp.Concat;
                        break;
                    case IrOpCall o when o.Op == IrOp.MapRemoveIfPresent && o.Args.Count == 2 && o.Args[0].Type?.Kind == IrTypeKind.Set:
                        o.Op = IrOp.RemoveValueIfPresent;
                        break;
                }
            }
            // for (const [k, v] of map): the key / value form of a map loop
            foreach (var fe in tupleLoops)
            {
                if (fe.Iterable?.Type?.Kind != IrTypeKind.Map) continue;
                int at = 0;
                var body = fe.Body.Stmts;
                while (at < body.Count && body[at] is IrVarDecl d && d.Synthetic && d.Init == null) at++;
                if (at >= body.Count || !(body[at] is IrAssign un) || !un.Synthetic || !(un.Target is IrTupleLit tl) || tl.Items.Count != 2) continue;
                if (!(tl.Items[0] is IrName kn) || !(tl.Items[1] is IrName vn)) continue;
                fe.Var = kn.Symbol; fe.VarSpan = kn.Span;
                fe.ValueVar = vn.Symbol; fe.ValueVarSpan = vn.Span;
                body.RemoveRange(0, at + 1);
                if (fe.Var.Type == null || !fe.Var.Type.IsKnown) fe.Var.Type = fe.Iterable.Type.Key ?? IrType.Unknown;
                if (fe.ValueVar.Type == null || !fe.ValueVar.Type.IsKnown) fe.ValueVar.Type = fe.Iterable.Type.Elem ?? IrType.Unknown;
            }
        }

        static string Describe(IrType t)
        {
            switch (t?.Kind ?? IrTypeKind.Unknown)
            {
                case IrTypeKind.Int: case IrTypeKind.Float: return "a number";
                case IrTypeKind.Bool: return "a boolean";
                case IrTypeKind.String: return "a string";
                case IrTypeKind.List: case IrTypeKind.Array: case IrTypeKind.Tuple: return "an array";
                case IrTypeKind.Map: return "a Map";
                case IrTypeKind.Set: return "a Set";
                case IrTypeKind.Func: return "a function";
                case IrTypeKind.Game: return (("AEIOU".IndexOf(t.GameName[0]) >= 0) ? "an " : "a ") + t.GameName;
            }
            return null;
        }

        void Checks()
        {
            foreach (var (fe, isIn, at) in forChecks)
            {
                var k = fe.Iterable?.Type?.Kind ?? IrTypeKind.Unknown;
                if (isIn && (k == IrTypeKind.List || k == IrTypeKind.Array || k == IrTypeKind.Tuple || k == IrTypeKind.String || k == IrTypeKind.Set))
                    Error("for...in loops over the keys of an object: use for (const x of " + (k == IrTypeKind.String ? "text" : "list") + ") to loop over the " + (k == IrTypeKind.String ? "characters" : "items"), at);
                else if (!isIn && k == IrTypeKind.Map && fe.ValueVar == null && !(fe.Var?.Tag is string tag && tag == PyLowering.TupleForTag)
                         && !(fe.Iterable is IrOpCall io && (io.Op == IrOp.MapKeys || io.Op == IrOp.MapValues)))
                    Error("for...of over a Map gives [key, value] pairs: use for (const [key, value] of map) or for (const key of map.keys()) in JavaFarmer", at);
            }
            foreach (var kv in lenSource)
            {
                if (kv.Key.Args[0] is IrOpCall) continue;   // Object.keys(o).length
                var k = kv.Key.Args[0].Type?.Kind ?? IrTypeKind.Unknown;
                if (kv.Value == "length" && (k == IrTypeKind.Map || k == IrTypeKind.Set))
                    Error("a " + (k == IrTypeKind.Map ? "Map" : "Set") + " has no .length in JavaScript: use .size", kv.Key.NameSpan);
                else if (kv.Value == "size" && (k == IrTypeKind.List || k == IrTypeKind.Array || k == IrTypeKind.Tuple || k == IrTypeKind.String))
                    Error((k == IrTypeKind.String ? "strings" : "arrays") + " have no .size in JavaScript: use .length", kv.Key.NameSpan);
            }
            foreach (var (call, api, js) in builtinCalls)
            {
                for (int i = 0; i < call.Args.Count && i < api.Params.Count; i++)
                {
                    var p = api.Params[i];
                    if (p.Variadic) break;
                    var at = call.Args[i].Type;
                    if (at == null || !at.IsKnown || at.Kind == IrTypeKind.Any || at.Kind == IrTypeKind.Null || at.Kind == IrTypeKind.Unknown) continue;
                    var pt = p.Type;
                    bool bad = false;
                    if (pt != null && pt.Kind == IrTypeKind.Game)
                        bad = at.Kind != IrTypeKind.Game || (at.GameName != pt.GameName && at.GameName != null);
                    else if (pt != null && pt.Kind == IrTypeKind.Func)
                        bad = at.Kind != IrTypeKind.Func;
                    else if (pt != null && pt.IsNumeric)
                        bad = !(at.IsNumeric || at.Kind == IrTypeKind.Bool);
                    else if (pt != null && pt.Kind == IrTypeKind.String)
                        bad = at.Kind != IrTypeKind.String;
                    if (!bad) continue;
                    var want = pt.Kind == IrTypeKind.Func ? "a function" : pt.IsNumeric ? "a number" : Describe(pt);
                    var got = Describe(at);
                    if (want == null || got == null) continue;
                    Error(js + "() expects " + want + " as argument " + (i + 1) + ", but got " + got, call.Args[i].Span);
                }
            }
        }
    }
}
