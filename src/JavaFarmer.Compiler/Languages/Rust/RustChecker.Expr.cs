using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed partial class RustChecker
    {
        RTy CheckExpr(RExpr e)
        {
            var t = CheckCore(e);
            e.Type = t;
            return t;
        }

        RTy CheckCore(RExpr e)
        {
            switch (e)
            {
                case RLit l: return Lit(l);
                case RPathExpr pe: return PathValue(pe);
                case RUnary u: return Unary(u);
                case RBinary b: return Binary(b);
                case RAssign a: CheckAssign(a); return RTy.Unit;
                case RCast c: return Cast(c);
                case RField f: return Field(f);
                case RIndex ix: return Index(ix);
                case RRange r:
                    {
                        if (r.From == null || r.To == null) throw NotSupported("an open range (`" + (r.From == null ? ".." : "a..") + "`) outside slicing", r);
                        var a = CheckExpr(r.From);
                        var b = CheckExpr(r.To);
                        if (!Unify(a, b)) throw Mismatch(a, b, r.To);
                        return RTy.Range(a);
                    }
                case RTuple t:
                    if (t.Items.Count == 0) return RTy.Unit;
                    return RTy.Tuple(t.Items.Select(CheckExpr).ToArray());
                case RArray arr: return ArrayExpr(arr);
                case RBlockExpr be:
                    if (inConstInit) throw NotSupported("a block in a constant", be);
                    return CheckBlock(be.Block);
                case RIf i: return If(i);
                case RMatch m: return Match(m);
                case RWhile w: return While(w);
                case RLoop lp: return Loop(lp);
                case RFor f: return For(f);
                case RBreak br:
                    if (Fn.LoopDepth == 0) throw Err("`break` outside of a loop (E0268)", br);
                    if (loopStack.Count > 0) loopStack[loopStack.Count - 1].HasBreak = true;
                    diverged = true;
                    return RTy.Never;
                case RContinue co:
                    if (Fn.LoopDepth == 0) throw Err("`continue` outside of a loop (E0268)", co);
                    diverged = true;
                    return RTy.Never;
                case RReturn r:
                    {
                        if (inConstInit) throw Err("return statement outside of function body (E0572)", r);
                        if (r.Value != null)
                        {
                            var vt = CheckExpr(r.Value);
                            Coerce(Fn.Ret, vt, r.Value);
                        }
                        else if (!Unify(Fn.Ret, RTy.Unit) && RTy.Prune(Fn.Ret).Kind != RK.Never)
                            throw Err("mismatched types: expected " + Q(Fn.Ret) + ", found `()` (E0308)", r);
                        diverged = true;
                        return RTy.Never;
                    }
                case RClosure c: return Closure(c);
                case RCall call: return Call(call);
                case RMethodCall mc: return MethodCall(mc);
                case RMacro m: return Macro(m);
            }
            throw NotSupported("this expression", e);
        }

        RTy Lit(RLit l)
        {
            RTy t;
            switch (l.Kind)
            {
                case RLitKind.Int:
                    if (l.Suffix == "f32" || l.Suffix == "f64") t = RTy.Float(l.Suffix);
                    else if (l.Suffix != null) t = RTy.Int(l.Suffix);
                    else t = NewVar(1);
                    intLits.Add(l);
                    break;
                case RLitKind.Float: t = l.Suffix != null ? RTy.Float(l.Suffix) : NewVar(2); break;
                case RLitKind.Bool: t = RTy.Bool; break;
                case RLitKind.Str: t = RTy.StrRef; break;
                default: throw NotSupported("a `char` literal", l);
            }
            l.Type = t;
            return t;
        }

        // ------------------------------------------------------------------ paths
        RTy PathValue(RPathExpr pe, bool forWrite = false)
        {
            var segs = pe.Segs;
            foreach (var s in segs) if (s.Generics != null) throw NotSupported("generic arguments here", s.Start, s.End);
            if (segs.Count == 1)
            {
                var n = segs[0].Name;
                var v = Lookup(n, out int level);
                if (v != null)
                {
                    pe.Var = v;
                    NoteCapture(v, level);
                    if (!forWrite) RequireInit(v, pe);
                    return v.Type;
                }
                if (Globals.TryGetValue(n, out var g))
                {
                    pe.Var = g;
                    if (g.Mut && unsafeDepth == 0) throw Err("use of mutable static is unsafe and requires unsafe block (E0133)", pe);
                    if (g.Mut && inConstInit) throw Err("constants cannot refer to statics (E0013)", pe);
                    return g.Type;
                }
                if (Fns.TryGetValue(n, out var f))
                {
                    if (inConstInit) throw Err("cannot call non-const fn `" + n + "` in constants (E0015)", pe);
                    pe.Fn = f;
                    return new RTy { Kind = RK.Fn, Items = f.Params, Ret = f.Ret, FnSym = f };
                }
                if (importedItems.TryGetValue(n, out var imp)) return ExternalItem(pe, imp.Module, imp.Name, segs[0]);
                if (n == "None") { pe.IsNone = true; return RTy.Option(NewVar(0, pe, "Option<_>")); }
                if (n == "Some") throw NotSupported("`Some` as a function value", pe);
                if (Array.IndexOf(GameApi.DirectionNames, n) >= 0 && FarmHas(n))
                {
                    pe.ConstCategory = "Directions"; pe.ConstName = n;
                    return RTy.Game("Direction");
                }
                var api = RustApi.Find(n);
                if (api != null && FarmHas(n) && (!segs[0].Raw || n == "move"))
                {
                    if (inConstInit) throw Err("cannot call non-const fn `" + n + "` in constants (E0015)", pe);
                    pe.Builtin = api.B.PythonName;
                    return BuiltinFnType(api, pe);
                }
                throw UnknownValue(n, pe);
            }
            // qualified paths
            int k = 0;
            if (segs[0].Name == "farm") k = 1;
            if (segs.Count - k == 2 && System.Array.IndexOf(RustApi.Enums, segs[k].Name) >= 0)
            {
                var en = segs[k].Name;
                if (k == 0 && !FarmHas(en)) throw Err("failed to resolve: use of undeclared type `" + en + "` (add `use farm::*;`) (E0433)", segs[k].Start, segs[k].End);
                var cat = en == "Direction" ? "Directions" : RustApi.CategoryOfEnum(en);
                pe.ConstCategory = cat; pe.ConstName = segs[k + 1].Name;
                return RTy.Game(en);
            }
            if (segs.Count - k == 2 && GameApi.GameTypeOfCategory(segs[k].Name) != null && segs[k].Name != GameApi.GameTypeOfCategory(segs[k].Name))
                throw Err("failed to resolve: use of undeclared type `" + segs[k].Name + "` (the game's " + segs[k].Name + " are `" + GameApi.GameTypeOfCategory(segs[k].Name) + "::" + segs[k + 1].Name + "` in Rust) (E0433)", segs[k].Start, segs[k].End);
            if (k == 1 && segs.Count == 2)
            {
                var api = RustApi.Find(segs[1].Name);
                if (api != null) { pe.Builtin = api.B.PythonName; return BuiltinFnType(api, pe); }
                throw Err("cannot find value `" + segs[1].Name + "` in crate `farm` (E0425)", segs[1].Start, segs[1].End);
            }
            if (segs.Count == 2 && (segs[1].Name == "MAX" || segs[1].Name == "MIN") && RustParser.IntSuffixes.Contains(segs[0].Name))
            {
                pe.Builtin = "const:" + segs[0].Name + "::" + segs[1].Name;
                return RTy.Int(segs[0].Name);
            }
            int m = 0;
            if (segs[0].Name == "crate") m = 1;
            if (segs.Count - m == 1)
            {
                // crate::item: an item of this file
                var inner = new RPathExpr { Start = segs[m].Start, End = segs[m].End };
                inner.Segs.Add(segs[m]);
                var t = PathValue(inner, forWrite);
                pe.Var = inner.Var; pe.Fn = inner.Fn; pe.Builtin = inner.Builtin; pe.ConstCategory = inner.ConstCategory; pe.ConstName = inner.ConstName;
                return t;
            }
            if (segs.Count - m == 2)
            {
                var mod = segs[m].Name;
                string actual;
                if (ModuleAliases.TryGetValue(mod, out actual) || (m == 1 && ModuleExists(mod, out actual)))
                    return ExternalItem(pe, actual, segs[m + 1].Name, segs[m + 1]);
                if (ModuleExists(mod, out _))
                    throw Err("failed to resolve: use of unresolved module `" + mod + "` (add `mod " + mod + ";` at the top of this file) (E0433)", segs[m].Start, segs[m].End);
            }
            if (segs[0].Name == "std" || segs[0].Name == "core") throw NotSupported("`" + string.Join("::", segs.Select(s => s.Name)) + "`", pe);
            throw Err("failed to resolve: use of undeclared type or module `" + segs[m].Name + "` (E0433)", segs[m].Start, segs[m].End);
        }

        RTy ExternalItem(RPathExpr pe, string module, string name, RPathSeg at)
        {
            var d = Decls(module);
            if (d.Error != null) throw Err("cannot use `" + module + "`: " + d.Error, at.Start, at.End);
            pe.Module = module;
            if (d.Fns.TryGetValue(name, out var f))
            {
                if (!f.Pub) throw Err("function `" + name + "` is private (make it `pub fn` in " + module + ") (E0603)", at.Start, at.End);
                pe.Fn = f;
                return new RTy { Kind = RK.Fn, Items = f.Params, Ret = f.Ret, FnSym = f };
            }
            if (d.Vars.TryGetValue(name, out var v))
            {
                if (!v.Pub) throw Err((v.Mut ? "static" : "constant") + " `" + name + "` is private (make it `pub` in " + module + ") (E0603)", at.Start, at.End);
                if (v.Mut) throw NotSupported("another file's `static mut`", pe);
                pe.Var = v;
                return v.Type;
            }
            throw Err("cannot find `" + name + "` in `" + module + "` (E0425)", at.Start, at.End);
        }

        RustAbort UnknownValue(string n, RNode at)
        {
            string hint = "";
            if (RustApi.Find(n) != null && !FarmHas(n)) hint = " (add `use farm::*;` at the top of the file)";
            else if (Array.IndexOf(GameApi.DirectionNames, n) >= 0) hint = " (add `use farm::*;` at the top of the file)";
            else if (n == "True" || n == "False") hint = " (Rust writes `" + n.ToLowerInvariant() + "`)";
            else if (n == "null" || n == "nil" || n == "NULL") hint = " (Rust uses `None`)";
            else if (GameApi.FromApiName(n, NamingStyle.Camel) != null) hint = " (Rust uses snake_case: `" + GameApi.FromApiName(n, NamingStyle.Camel) + "`)";
            return Err("cannot find value `" + n + "` in this scope" + hint + " (E0425)", at);
        }

        void NoteCapture(RVar v, int level)
        {
            if (v.IsGlobal) return;
            for (int i = fnStack.Count - 1; i >= 0; i--)
            {
                var ctx = fnStack[i];
                if (ctx.Closure == null) break;
                if (level < ctx.BaseScope && !ctx.Closure.Captures.Contains(v)) ctx.Closure.Captures.Add(v);
            }
        }

        void RequireInit(RVar v, RNode at)
        {
            if (v.IsGlobal || v.Kind == RVarKind.Param) return;
            if (flow.Init.Contains(v)) return;
            if (flow.Maybe.Contains(v)) throw Err("used binding `" + v.Name + "` is possibly-uninitialized (E0381)", at);
            throw Err("used binding `" + v.Name + "` isn't initialized (E0381)", at);
        }

        // ------------------------------------------------------------------ operators
        RTy Unary(RUnary u)
        {
            switch (u.Op)
            {
                case "-":
                    {
                        var t = CheckExpr(u.Operand);
                        var d = RTy.Deref(t);
                        if (u.Operand is RLit lit && lit.Kind == RLitKind.Int) negatedLits.Add(lit);
                        if (!d.IsNumeric) throw Err("cannot apply unary operator `-` to type " + Q(t) + " (E0600)", u);
                        if (d.IsUnsigned) throw Err("cannot apply unary operator `-` to type " + Q(d) + " (unsigned values cannot be negated) (E0600)", u);
                        return d;
                    }
                case "!":
                    {
                        var t = CheckExpr(u.Operand);
                        var d = RTy.Deref(t);
                        if (d.Kind == RK.Bool) return RTy.Bool;
                        if (d.Kind == RK.Var && d.Class == 0 && Unify(d, RTy.Bool)) return RTy.Bool;
                        if (d.IsIntLike) throw NotSupported("the bitwise not operator `!` on integers", u);
                        throw Err("cannot apply unary operator `!` to type " + Q(t) + " (E0600)", u);
                    }
                case "*":
                    {
                        var t = RTy.Prune(CheckExpr(u.Operand));
                        if (t.Kind == RK.Ref) return t.A;
                        if (t.Kind == RK.Var && t.Class == 0) throw Err("type annotations needed (E0282)", u.Operand);
                        throw Err("type " + Q(t) + " cannot be dereferenced (E0614)", u);
                    }
                case "&":
                case "&mut":
                    {
                        if (u.Op == "&mut") RequireMutable(u.Operand, "borrow");
                        var t = CheckExpr(u.Operand);
                        if (u.Operand is RPathExpr pe && pe.Var != null && pe.Var.Mut && pe.Var.Kind == RVarKind.Static)
                            throw Err("creating a " + (u.Op == "&" ? "shared" : "mutable") + " reference to mutable static is not allowed in Rust 2024", u);
                        return RTy.Ref(t, u.Op == "&mut");
                    }
            }
            throw NotSupported("the operator `" + u.Op + "`", u);
        }

        static string OpVerb(string op, RTy l, RTy r)
        {
            string L = Q(l), Rr = Q(r);
            switch (op)
            {
                case "+": return "cannot add " + Rr + " to " + L;
                case "-": return "cannot subtract " + Rr + " from " + L;
                case "*": return "cannot multiply " + L + " by " + Rr;
                case "/": return "cannot divide " + L + " by " + Rr;
                default: return "cannot calculate the remainder of " + L + " divided by " + Rr;
            }
        }

        RTy Binary(RBinary b)
        {
            var op = b.Op;
            if (op == "&&" || op == "||")
            {
                var l = CheckExpr(b.Left);
                if (!Unify(RTy.Bool, l)) throw Mismatch(RTy.Bool, l, b.Left);
                var saveFlow = flow.Clone();
                var r = CheckExpr(b.Right);
                if (!Unify(RTy.Bool, r)) throw Mismatch(RTy.Bool, r, b.Right);
                flow = saveFlow;
                return RTy.Bool;
            }
            var lt = CheckExpr(b.Left);
            var rt = CheckExpr(b.Right);
            if (op == "+" || op == "-" || op == "*" || op == "/" || op == "%")
            {
                var ld = RTy.Deref(lt);
                if (op == "+" && (ld.Kind == RK.String || ld.Kind == RK.Str))
                {
                    var lp = RTy.Prune(lt);
                    if (lp.Kind != RK.String)
                        throw Err("cannot add " + Q(rt) + " to " + Q(lt) + " (use `.to_string()` on the left: `" + "a.to_string() + b`) (E0369)", b.OpStart, b.OpEnd);
                    if (!Coerces(RTy.StrRef, rt))
                    {
                        var rp = RTy.Prune(rt);
                        if (rp.Kind == RK.String) throw Mismatch(RTy.StrRef, rt, b.Right, " (consider borrowing here: `&`)");
                        throw Mismatch(RTy.StrRef, rt, b.Right);
                    }
                    return RTy.String;
                }
                var rd = RTy.Deref(rt);
                if (ld.Kind == RK.Var && ld.Class == 0) Unify(ld, rd);
                else if (rd.Kind == RK.Var && rd.Class == 0) Unify(rd, ld);
                ld = RTy.Prune(ld); rd = RTy.Prune(rd);
                if (!ld.IsNumeric || !rd.IsNumeric)
                {
                    string opname = op == "+" ? "cannot add" : op == "-" ? "cannot subtract" : op == "*" ? "cannot multiply" : op == "/" ? "cannot divide" : "cannot calculate the remainder of";
                    if (!ld.IsNumeric) throw Err("binary operation `" + op + "` cannot be applied to type " + Q(lt) + " (E0369)", b.OpStart, b.OpEnd);
                    throw Err(OpVerb(op, ld, rd) + " (E0277)", b.OpStart, b.OpEnd);
                }
                if (!Unify(ld, rd))
                {
                    if (ld.Kind == RK.Var && ld.Class == 1 && rd.IsFloatLike && rd.Kind == RK.Var)
                        throw Err((op == "+" ? "cannot add a float to an integer" : OpVerb(op, ld, rd)) + " (E0277)", b.OpStart, b.OpEnd);
                    throw Err(OpVerb(op, ld, rd) + " (E0277)", b.OpStart, b.OpEnd);
                }
                return ld;
            }
            // comparisons
            {
                var ld = RTy.Deref(lt); var rd = RTy.Deref(rt);
                bool ls = ld.Kind == RK.String || ld.Kind == RK.Str, rs = rd.Kind == RK.String || rd.Kind == RK.Str;
                if (ls && rs) return RTy.Bool;
                int dl = RTy.RefDepth(lt), dr = RTy.RefDepth(rt);
                if (dl != dr)
                {
                    if (!(ld.Kind == RK.Var || rd.Kind == RK.Var) || Unify(ld, rd))
                        throw Err("can't compare " + Q(lt) + " with " + Q(rt) + (dl > dr ? " (dereference the left side with `*`)" : " (borrow the right side with `&`)") + " (E0277)", b.OpStart, b.OpEnd);
                }
                if (!Unify(ld, rd))
                {
                    if (ld.Kind == RK.Option && RTy.Prune(rd).Kind != RK.Option && Unify(RTy.Prune(ld).A, rd))
                        throw Mismatch(ld, rd, b.Right, " (compare with `Some(...)`)");
                    throw Mismatch(ld, rd, b.Right);
                }
                var pl = RTy.Prune(ld);
                if (pl.Kind == RK.Closure || pl.Kind == RK.Fn || pl.Kind == RK.Drone || pl.Kind == RK.Range || pl.Kind == RK.Iter)
                    throw Err("binary operation `" + op + "` cannot be applied to type " + Q(lt) + " (E0369)", b.OpStart, b.OpEnd);
                if (pl.Kind == RK.Float && false) { }
                return RTy.Bool;
            }
        }

        RTy Cast(RCast c)
        {
            var t = CheckExpr(c.Operand);
            var to = ResolveType(c.To, false);
            var from = RTy.Prune(t);
            var tp = RTy.Prune(to);
            if (!tp.IsNumeric)
            {
                if (tp.Kind == RK.Bool) throw Err("cannot cast " + Q(t) + " as `bool` (compare with zero instead: `x != 0`) (E0054)", c);
                throw Err("non-primitive cast: " + Q(t) + " as " + Q(to) + " (E0605)", c);
            }
            if (from.Kind == RK.Bool) throw NotSupported("casting a `bool` to a number", c);
            if (!from.IsNumeric)
            {
                if (from.Kind == RK.Ref && RTy.Prune(from.A).IsNumeric) throw Err("casting " + Q(t) + " as " + Q(to) + " is invalid (dereference it first: `*x as " + RTy.Show(to) + "`) (E0606)", c);
                if (from.Kind == RK.Var && from.Class == 0) throw Err("type annotations needed (E0282)", c.Operand);
                throw Err("non-primitive cast: " + Q(t) + " as " + Q(to) + " (E0605)", c);
            }
            return to;
        }

        RTy Field(RField f)
        {
            var t = CheckExpr(f.Recv);
            var d = RTy.Deref(t);
            if (f.TupleIndex >= 0)
            {
                if (d.Kind == RK.Tuple)
                {
                    if (f.TupleIndex >= d.Items.Count) throw Err("no field `" + f.TupleIndex + "` on type " + Q(d) + " (E0609)", f.NameStart, f.NameEnd);
                    return d.Items[f.TupleIndex];
                }
                if (d.Kind == RK.Var) throw Err("type annotations needed (E0282)", f.Recv);
                throw Err("no field `" + f.Name + "` on type " + Q(d) + " (E0609)", f.NameStart, f.NameEnd);
            }
            if (d.Kind == RK.Var) throw Err("type annotations needed (E0282)", f.Recv);
            string hint = "";
            if (d.Kind == RK.Tuple && (f.Name == "x" || f.Name == "y")) hint = " (a Position is a tuple: use `.0` for x and `.1` for y)";
            else if (d.Kind == RK.Vec && f.Name == "length" || f.Name == "size" || f.Name == "len" || f.Name == "length") hint = " (use the method `.len()`)";
            throw Err("no field `" + f.Name + "` on type " + Q(d) + hint + " (E0609)", f.NameStart, f.NameEnd);
        }

        RTy Index(RIndex ix)
        {
            var t = CheckExpr(ix.Target);
            if (ix.Index is RRange rr) throw NotSupported("slicing (`v[a..b]`)", ix);
            var it = CheckExpr(ix.Index);
            var d = RTy.Deref(t);
            switch (d.Kind)
            {
                case RK.Vec: case RK.Slice: case RK.Array:
                    {
                        var ip = RTy.Prune(it);
                        if (!Unify(RTy.USize, it))
                        {
                            string seq = d.Kind == RK.Vec || d.Kind == RK.Slice ? "[" + RTy.Show(d.A) + "]" : RTy.Show(d);
                            throw Err("the type `" + seq + "` cannot be indexed by " + Q(it) + " (use `[i as usize]`) (E0277)", ix.Index);
                        }
                        return d.A;
                    }
                case RK.Map:
                    if (!Coerces(RTy.Ref(d.A, false), it))
                    {
                        if (Unify(d.A, RTy.Deref(it))) throw Err("the type " + Q(d) + " cannot be indexed by " + Q(it) + " (borrow the key: `[&key]`) (E0277)", ix.Index);
                        throw Mismatch(RTy.Ref(d.A, false), it, ix.Index);
                    }
                    return d.B;
                case RK.String: case RK.Str:
                    throw Err("the type `str` cannot be indexed by " + Q(it) + " (strings cannot be indexed in Rust) (E0277)", ix.Index);
                case RK.Var: throw Err("type annotations needed (E0282)", ix.Target);
                case RK.Tuple: throw Err("cannot index into a value of type " + Q(d) + " (use `.0`, `.1` ...) (E0608)", ix);
            }
            throw Err("cannot index into a value of type " + Q(d) + " (E0608)", ix);
        }

        RTy ArrayExpr(RArray arr)
        {
            if (arr.RepeatValue != null)
            {
                var v = CheckExpr(arr.RepeatValue);
                var n = CheckExpr(arr.RepeatCount);
                if (!Unify(RTy.USize, n)) throw Mismatch(RTy.USize, n, arr.RepeatCount, " (the count must be a `usize`: write `n as usize`)");
                if (!arr.IsVecMacro)
                {
                    if (!(arr.RepeatCount is RLit)) throw NotSupported("an array length that is not a number (use vec![x; n])", arr.RepeatCount);
                    return RTy.Array(v, (long)((RLit)arr.RepeatCount).I);
                }
                return RTy.Vec(v);
            }
            RTy elem = null;
            foreach (var it in arr.Items)
            {
                var t = CheckExpr(it);
                if (elem == null) elem = t;
                else if (!Unify(elem, t)) throw Mismatch(elem, t, it);
            }
            if (elem == null) elem = NewVar(0, arr, arr.IsVecMacro ? "Vec<_>" : "[_; 0]");
            return arr.IsVecMacro ? RTy.Vec(elem) : RTy.Array(elem, arr.Items.Count);
        }

        // ------------------------------------------------------------------ assignment
        void CheckAssign(RAssign a)
        {
            if (inConstInit) throw NotSupported("an assignment in a constant", a);
            if (a.Target is RTuple tup)
            {
                if (a.Op != "=") throw Err("invalid left-hand side of assignment (E0067)", a.Target);
                var vt = RTy.Prune(CheckExpr(a.Value));
                if (vt.Kind != RK.Tuple || vt.Items.Count != tup.Items.Count) throw Mismatch(RTy.Tuple(tup.Items.Select(_ => NewVar()).ToArray()), vt, a.Value);
                for (int i = 0; i < tup.Items.Count; i++) AssignPlace(tup.Items[i], vt.Items[i], false, a);
                tup.Type = vt;
                return;
            }
            if (a.Op == "=")
            {
                var vt = CheckExpr(a.Value);
                AssignPlace(a.Target, vt, false, a);
                return;
            }
            // compound: target is read and written
            var tt = PlaceType(a.Target, a, true);
            a.Target.Type = tt;
            var v2 = CheckExpr(a.Value);
            var tp = RTy.Prune(tt);
            if (a.Op == "+=" && tp.Kind == RK.String)
            {
                if (!Coerces(RTy.StrRef, v2)) throw Mismatch(RTy.StrRef, v2, a.Value, RTy.Prune(v2).Kind == RK.String ? " (consider borrowing here: `&`)" : null);
                return;
            }
            var vd = RTy.Deref(v2);
            if (!tp.IsNumeric)
            {
                if (tp.Kind == RK.Var && tp.Class == 0 && Unify(tp, vd)) return;
                throw Err("binary assignment operation `" + a.Op + "` cannot be applied to type " + Q(tt) + " (E0368)", a.Target);
            }
            if (!Unify(tp, vd))
                throw Err(OpVerb(a.Op.Substring(0, 1), tp, vd).Replace("cannot add ", "cannot add-assign ").Replace("cannot subtract ", "cannot subtract-assign ").Replace("cannot multiply ", "cannot multiply-assign ").Replace("cannot divide ", "cannot divide-assign ") + " (E0277)", a.OpStart, a.OpEnd);
        }

        /// <summary>Type of a place that is read and written (compound assignment): initialised, mutable.</summary>
        RTy PlaceType(RExpr target, RAssign a, bool compound)
        {
            if (target is RPathExpr pe)
            {
                var t = PathValue(pe, forWrite: true);
                CheckWritableVar(pe, compound, a);
                if (pe.Var != null && !pe.Var.IsGlobal) RequireInit(pe.Var, pe);
                return t;
            }
            return WritablePlace(target);
        }

        void AssignPlace(RExpr target, RTy value, bool compound, RAssign a)
        {
            if (target is RPathExpr pe)
            {
                if (pe.Segs.Count == 1 && pe.Segs[0].Name == "_") { pe.Type = value; return; }
                var t = PathValue(pe, forWrite: true);
                pe.Type = t;
                CheckWritableVar(pe, compound, a);
                Coerce(t, value, a.Value);
                if (pe.Var != null && !pe.Var.IsGlobal) { flow.Init.Add(pe.Var); flow.Maybe.Add(pe.Var); }
                return;
            }
            var pt = WritablePlace(target);
            Coerce(pt, value, a.Value);
        }

        void CheckWritableVar(RPathExpr pe, bool compound, RAssign a)
        {
            var v = pe.Var;
            if (v == null)
            {
                if (pe.Fn != null || pe.Builtin != null) throw Err("invalid left-hand side of assignment: cannot assign to a function (E0070)", pe);
                if (pe.ConstCategory != null || pe.IsNone) throw Err("invalid left-hand side of assignment (E0070)", pe);
                throw Err("invalid left-hand side of assignment (E0070)", pe);
            }
            switch (v.Kind)
            {
                case RVarKind.Const: throw Err("invalid left-hand side of assignment: cannot assign to the constant `" + v.Name + "` (E0070)", pe);
                case RVarKind.External: throw NotSupported("assigning another file's variable", pe);
                case RVarKind.Static:
                    if (!v.Mut) throw Err("cannot assign to immutable static item `" + v.Name + "` (E0594)", a);
                    if (unsafeDepth == 0) throw Err("use of mutable static is unsafe and requires unsafe block (E0133)", pe);
                    return;
            }
            if (fnStack.Count > 0 && Fn.Closure != null && v.FnId != Fn.Id)
                throw NotSupported("assigning to a captured variable inside a closure (game Python closures cannot assign outer variables; pass the value in and return the result)", a);
            if (!v.Mut)
            {
                bool twice = compound || flow.Maybe.Contains(v) || (fnStack.Count > 0 && Fn.LoopDepth > v.LoopDepth) || v.Kind == RVarKind.Param;
                if (twice)
                {
                    if (v.Kind == RVarKind.Param) throw Err("cannot assign to immutable argument `" + v.Name + "` (declare it as `mut " + v.Name + "`) (E0384)", a);
                    throw Err("cannot assign twice to immutable variable `" + v.Name + "` (make it mutable: `let mut " + v.Name + "`) (E0384)", a);
                }
            }
        }

        /// <summary>v[i] / t.0 / *r as an assignment target: checks mutability, returns the element type.</summary>
        RTy WritablePlace(RExpr target)
        {
            switch (target)
            {
                case RIndex ix:
                    {
                        var t = CheckExpr(ix);
                        var d = RTy.Deref(ix.Target.Type);
                        if (d.Kind == RK.Map) throw Err("cannot assign to data in an index of " + Q(d) + " (use `.insert(key, value)`) (E0594)", target);
                        RequireMutable(ix.Target, "borrow");
                        return t;
                    }
                case RField f:
                    CheckExpr(f);
                    throw NotSupported("assigning to a tuple field (game tuples cannot be changed; build a new tuple)", target);
                case RUnary u when u.Op == "*":
                    {
                        var t = CheckExpr(u);
                        if (u.Operand is RMethodCall oi && oi.Resolved == "map.entry") return t;
                        throw NotSupported("assigning through a reference (`*x = ...`)", target);
                    }
                case RParenLike _:
                    break;
            }
            CheckExpr(target);
            throw Err("invalid left-hand side of assignment (E0070)", target);
        }

        sealed class RParenLike : RExpr { }

        /// <summary>The place is mutable: a `let mut` binding, a `&amp;mut` reference, or an element of one.</summary>
        void RequireMutable(RExpr e, string what)
        {
            switch (e)
            {
                case RPathExpr pe:
                    {
                        if (pe.Segs.Count != 1) return;
                        var v = Lookup(pe.Segs[0].Name, out _);
                        if (v == null) { if (Globals.TryGetValue(pe.Segs[0].Name, out var g)) { if (!g.Mut) throw Err("cannot borrow immutable static item `" + g.Name + "` as mutable (E0596)", e); } return; }
                        var t = RTy.Prune(v.Type);
                        if (t.Kind == RK.Ref)
                        {
                            if (!t.Mut) throw Err("cannot borrow `*" + v.Name + "` as mutable, as it is behind a `&` reference (E0596)", e);
                            return;
                        }
                        if (!v.Mut) throw Err("cannot borrow `" + v.Name + "` as mutable, as it is not declared as mutable (make it `let mut " + v.Name + "`) (E0596)", e);
                        return;
                    }
                case RIndex ix: RequireMutable(ix.Target, what); return;
                case RField f: RequireMutable(f.Recv, what); return;
                case RUnary u when u.Op == "*":
                    {
                        var t = RTy.Prune(u.Operand.Type ?? RTy.Error);
                        if (t.Kind == RK.Ref && !t.Mut) throw Err("cannot borrow data in a `&` reference as mutable (E0596)", e);
                        return;
                    }
            }
        }

        // ------------------------------------------------------------------ control flow
        sealed class LoopInfo { public bool HasBreak; }
        readonly List<LoopInfo> loopStack = new List<LoopInfo>();

        RTy If(RIf i)
        {
            if (inConstInit) throw NotSupported("`if` in a constant", i);
            Flow afterCond;
            RTy thenT, elseT = RTy.Unit;
            bool dThen, dElse = false;
            Flow fThen, fElse;
            bool before = diverged;
            if (i.LetPat != null)
            {
                var st = CheckExpr(i.LetExpr);
                afterCond = flow.Clone();
                flow = afterCond.Clone();
                Push();
                CheckPattern(i.LetPat, st);
                diverged = false;
                thenT = CheckBlock(i.Then);
                Pop();
            }
            else
            {
                var ct = CheckExpr(i.Cond);
                if (!Unify(RTy.Bool, ct))
                {
                    var d = RTy.Prune(ct);
                    string hint = d.Kind == RK.Option ? " (Rust has no truthiness: use `.is_some()`)" : d.IsNumeric ? " (Rust has no truthiness: compare, e.g. `x != 0`)" : d.Kind == RK.Vec ? " (use `!v.is_empty()`)" : null;
                    throw Mismatch(RTy.Bool, ct, i.Cond, hint);
                }
                afterCond = flow.Clone();
                diverged = false;
                thenT = CheckBlock(i.Then);
            }
            dThen = diverged; fThen = flow;
            flow = afterCond.Clone();
            diverged = false;
            if (i.Else != null) { elseT = CheckExpr(i.Else); dElse = diverged || RTy.Prune(elseT).Kind == RK.Never; }
            fElse = flow;
            MergeFlows(fThen, dThen, fElse, dElse);
            diverged = before || (dThen && dElse);
            if (i.Else == null)
            {
                var tp = RTy.Prune(thenT);
                if (tp.Kind != RK.Unit && tp.Kind != RK.Never && !Unify(RTy.Unit, thenT))
                    throw Err("mismatched types: expected `()`, found " + Q(thenT) + " (`if` without an `else` cannot produce a value) (E0317)", i.Then.Tail ?? (RNode)i.Then);
                return RTy.Unit;
            }
            var a = RTy.Prune(thenT); var b = RTy.Prune(elseT);
            if (a.Kind == RK.Never) return elseT;
            if (b.Kind == RK.Never) return thenT;
            if (!Unify(thenT, elseT))
                throw Err("`if` and `else` have incompatible types: expected " + Q(thenT) + ", found " + Q(elseT) + " (E0308)", i.Else is RBlockExpr be && be.Block.Tail != null ? be.Block.Tail : (RNode)i.Else);
            return thenT;
        }

        void MergeFlows(Flow a, bool da, Flow b, bool db)
        {
            if (da && db) { flow = a; return; }
            if (da) { flow = b; return; }
            if (db) { flow = a; return; }
            var m = new Flow { Init = new HashSet<RVar>(a.Init), Maybe = new HashSet<RVar>(a.Maybe) };
            m.Init.IntersectWith(b.Init);
            m.Maybe.UnionWith(b.Maybe);
            flow = m;
        }

        RTy While(RWhile w)
        {
            if (inConstInit) throw NotSupported("a loop in a constant", w);
            var before = flow.Clone();
            bool div = diverged;
            Fn.LoopDepth++;
            loopStack.Add(new LoopInfo());
            Push();
            if (w.LetPat != null)
            {
                var st = CheckExpr(w.LetExpr);
                CheckPattern(w.LetPat, st);
            }
            else
            {
                var ct = CheckExpr(w.Cond);
                if (!Unify(RTy.Bool, ct)) throw Mismatch(RTy.Bool, ct, w.Cond);
            }
            var bt = CheckBlock(w.Body);
            if (w.Body.Tail != null && RTy.Prune(bt).Kind != RK.Unit && RTy.Prune(bt).Kind != RK.Never && !Unify(RTy.Unit, bt)) throw Mismatch(RTy.Unit, bt, w.Body.Tail);
            Pop();
            loopStack.RemoveAt(loopStack.Count - 1);
            Fn.LoopDepth--;
            before.Maybe.UnionWith(flow.Maybe);
            flow = before;
            diverged = div;
            return RTy.Unit;
        }

        RTy Loop(RLoop lp)
        {
            if (inConstInit) throw NotSupported("a loop in a constant", lp);
            var before = flow.Clone();
            bool div = diverged;
            Fn.LoopDepth++;
            var info = new LoopInfo();
            loopStack.Add(info);
            var bt = CheckBlock(lp.Body);
            if (lp.Body.Tail != null && RTy.Prune(bt).Kind != RK.Unit && RTy.Prune(bt).Kind != RK.Never && !Unify(RTy.Unit, bt)) throw Mismatch(RTy.Unit, bt, lp.Body.Tail);
            loopStack.RemoveAt(loopStack.Count - 1);
            Fn.LoopDepth--;
            before.Maybe.UnionWith(flow.Maybe);
            flow = before;
            if (!info.HasBreak) { diverged = true; return RTy.Never; }
            diverged = div;
            return RTy.Unit;
        }

        RTy For(RFor f)
        {
            if (inConstInit) throw NotSupported("a loop in a constant", f);
            var item = IterItem(f);
            var before = flow.Clone();
            bool div = diverged;
            Fn.LoopDepth++;
            loopStack.Add(new LoopInfo());
            Push();
            BindPattern(f.Pat, item, initialized: true);
            var bt = CheckBlock(f.Body);
            if (f.Body.Tail != null && RTy.Prune(bt).Kind != RK.Unit && RTy.Prune(bt).Kind != RK.Never && !Unify(RTy.Unit, bt)) throw Mismatch(RTy.Unit, bt, f.Body.Tail);
            Pop();
            loopStack.RemoveAt(loopStack.Count - 1);
            Fn.LoopDepth--;
            before.Maybe.UnionWith(flow.Maybe);
            flow = before;
            diverged = div;
            return RTy.Unit;
        }

        static RExpr Unparen(RExpr e) => e;

        /// <summary>Classifies the iterable of a for loop (sets Shape / Source / Range / Rev / Step) and returns the item type.</summary>
        RTy IterItem(RFor f)
        {
            var it = f.Iter;
            // range forms: a..b, (a..b).rev(), (a..b).step_by(n), (a..b).rev().step_by(n)
            RExpr core = it;
            RExpr step = null;
            bool rev = false;
            if (core is RMethodCall sb && sb.Name == "step_by" && sb.Args.Count == 1) { step = sb.Args[0]; core = sb.Recv; }
            if (core is RMethodCall rv && rv.Name == "rev" && rv.Args.Count == 0 && rv.Recv is RRange) { rev = true; core = rv.Recv; }
            if (core is RRange range && (step == null || core != it))
            {
                if (range.From == null || range.To == null) throw NotSupported("an open range in a `for` loop", range);
                var rt = CheckExpr(range);
                var elem = RTy.Prune(rt).A;
                if (RTy.Deref(elem).IsFloatLike) throw Err("the trait bound `" + RTy.Show(elem) + ": Step` is not satisfied (ranges of floats cannot be iterated) (E0277)", range);
                if (step != null)
                {
                    var stt = CheckExpr(step);
                    if (!Unify(RTy.USize, stt)) throw Mismatch(RTy.USize, stt, step, " (write `.step_by(n as usize)`)");
                    if (rev && !(step is RLit)) throw NotSupported("`.rev().step_by(n)` with a variable step", step);
                    if (step is RLit sl && sl.I == 0) throw Err("`step_by(0)` would loop forever (assertion failed: step != 0)", step);
                }
                MarkIterTypes(it, rt, step);
                f.Shape = "range"; f.Range = range; f.Rev = rev; f.Step = step; f.Source = range;
                return elem;
            }
            if (it is RMethodCall en && en.Name == "enumerate" && en.Args.Count == 0)
            {
                var inner = ElemsOf(en.Recv, out var src, out var itemT, requireIterator: true);
                if (inner != "elems") throw NotSupported("`.enumerate()` on this", en);
                en.Type = RTy.Iter(RTy.Tuple(RTy.USize, itemT));
                f.Shape = "enumerate"; f.Source = src;
                return RTy.Tuple(RTy.USize, itemT);
            }
            var shape = ElemsOf(it, out var source, out var item, requireIterator: false);
            f.Shape = shape; f.Source = source;
            return item;
        }

        void MarkIterTypes(RExpr it, RTy rangeT, RExpr step)
        {
            if (it is RMethodCall m) { m.Type = rangeT; MarkIterTypes(m.Recv, rangeT, step); }
        }

        /// <summary>
        /// for-loop sources: &amp;v, &amp;mut v (no), v, v.iter(), v.into_iter(), .copied()/.cloned(), &amp;m / m.iter() (pairs),
        /// m.keys(), m.values(), sets, array literals. Returns the shape and sets the source collection and item type.
        /// </summary>
        string ElemsOf(RExpr it, out RExpr source, out RTy item, bool requireIterator)
        {
            bool byRef = false;
            bool copied = false;
            RExpr e = it;
            string method = null;
            if (e is RMethodCall c1 && (c1.Name == "copied" || c1.Name == "cloned") && c1.Args.Count == 0) { copied = true; e = c1.Recv; }
            if (e is RMethodCall c2 && c2.Args.Count == 0 && (c2.Name == "iter" || c2.Name == "into_iter" || c2.Name == "keys" || c2.Name == "values" || c2.Name == "iter_mut" || c2.Name == "values_mut" || c2.Name == "chars" || c2.Name == "drain"))
            {
                method = c2.Name; e = c2.Recv;
                if (method == "iter_mut" || method == "values_mut") throw NotSupported("`." + method + "()` (change elements by index instead: `v[i] = x`)", c2.NameStart, c2.NameEnd);
                if (method == "chars") throw NotSupported("`.chars()` (strings cannot be iterated by character in JavaFarmer)", c2.NameStart, c2.NameEnd);
                if (method == "drain") throw NotSupported("`.drain()`", c2.NameStart, c2.NameEnd);
            }
            if (e is RUnary u && (u.Op == "&" || u.Op == "&mut") && method == null)
            {
                if (u.Op == "&mut") throw NotSupported("iterating with `&mut` (change elements by index instead: `v[i] = x`)", u);
                byRef = true; e = u.Operand;
            }
            var t = CheckExpr(e);
            var d = RTy.Prune(t);
            if (d.Kind == RK.Ref) { byRef = true; d = RTy.Deref(d); }
            source = e;
            if (method == "iter" || method == "keys" || method == "values") byRef = true;
            if (method == "into_iter" && RTy.Prune(t).Kind != RK.Ref) byRef = false;
            RTy Wrap(RTy x) => byRef && !copied ? RTy.Ref(x, false) : x;
            switch (d.Kind)
            {
                case RK.Vec: case RK.Slice: case RK.Array: case RK.Set:
                    if (method == "keys" || method == "values") throw Err("no method named `" + method + "` found for " + Q(d) + " in the current scope (E0599)", it);
                    if (requireIterator && method == null) throw Err("`" + RTy.Show(t) + "` is not an iterator (use `.iter()`) (E0599)", it);
                    item = Wrap(d.A);
                    if (it is RMethodCall mi) mi.Type = RTy.Iter(item);
                    return "elems";
                case RK.Map:
                    if (method == "keys") { item = Wrap(d.A); MarkIter(it, item); return "keys"; }
                    if (method == "values") { item = Wrap(d.B); MarkIter(it, item); return "values"; }
                    if (copied) throw NotSupported("`.copied()` on a map iterator", it);
                    item = byRef ? RTy.Tuple(RTy.Ref(d.A, false), RTy.Ref(d.B, false)) : RTy.Tuple(d.A, d.B);
                    MarkIter(it, item);
                    return "map";
                case RK.Range: throw NotSupported("this kind of range loop", it);
                case RK.String: case RK.Str: throw Err("`" + RTy.Show(t) + "` is not an iterator (strings cannot be iterated in JavaFarmer) (E0277)", it);
                case RK.Var: throw Err("type annotations needed (E0282)", e);
            }
            throw Err("`" + RTy.Show(t) + "` is not an iterator (E0277)", it);
        }

        static void MarkIter(RExpr it, RTy item) { if (it is RMethodCall m) m.Type = RTy.Iter(item); }

        // ------------------------------------------------------------------ match / patterns
        RTy Match(RMatch m)
        {
            if (inConstInit) throw NotSupported("`match` in a constant", m);
            var st = CheckExpr(m.Scrutinee);
            var afterScrut = flow.Clone();
            bool before = diverged;
            RTy result = null;
            Flow merged = null; bool mergedDiv = true;
            bool catchAll = false, hasNone = false, hasSomeAll = false, hasTrue = false, hasFalse = false;
            var dirs = new HashSet<string>();
            foreach (var arm in m.Arms)
            {
                flow = afterScrut.Clone();
                diverged = false;
                Push();
                if (arm.Guard != null) throw NotSupported("a `match` guard (`if` after the pattern)", arm.Guard);
                CheckPattern(arm.Pat, st);
                Classify(arm.Pat, ref catchAll, ref hasNone, ref hasSomeAll, ref hasTrue, ref hasFalse, dirs);
                var at = CheckExpr(arm.Body);
                Pop();
                bool d = diverged || RTy.Prune(at).Kind == RK.Never;
                if (merged == null) { merged = flow; mergedDiv = d; }
                else
                {
                    MergeFlows(merged, mergedDiv, flow, d);
                    merged = flow; mergedDiv = mergedDiv && d;
                }
                if (RTy.Prune(at).Kind == RK.Never) continue;
                if (result == null) result = at;
                else if (!Unify(result, at))
                    throw Err("`match` arms have incompatible types: expected " + Q(result) + ", found " + Q(at) + " (E0308)", arm.Body);
            }
            flow = merged ?? afterScrut;
            diverged = before || (m.Arms.Count > 0 && mergedDiv);
            var sd = RTy.Deref(st);
            bool exhaustive = catchAll
                || (sd.Kind == RK.Option && hasNone && hasSomeAll)
                || (sd.Kind == RK.Bool && hasTrue && hasFalse)
                || (sd.Kind == RK.Game && sd.Name == "Direction" && GameApi.DirectionNames.All(dirs.Contains));
            if (!exhaustive)
            {
                string missing = sd.Kind == RK.Option ? (hasNone ? "`Some(_)`" : "`None`") : sd.Kind == RK.Bool ? (hasTrue ? "`false`" : "`true`") : "`_`";
                throw Err("non-exhaustive patterns: " + missing + " not covered (add a `_ => ...` arm) (E0004)", m.Scrutinee);
            }
            return result ?? RTy.Never;
        }

        static bool Irrefutable(RPat p)
        {
            switch (p.Kind)
            {
                case RPatKind.Wild: return true;
                case RPatKind.Ident: return p.Var != null;
                case RPatKind.Ref: return Irrefutable(p.Inner);
                case RPatKind.Tuple: return p.Items.All(Irrefutable);
            }
            return false;
        }

        void Classify(RPat p, ref bool catchAll, ref bool hasNone, ref bool hasSomeAll, ref bool hasTrue, ref bool hasFalse, HashSet<string> dirs)
        {
            switch (p.Kind)
            {
                case RPatKind.Wild: catchAll = true; return;
                case RPatKind.Ident: if (p.Var != null) catchAll = true; else if (p.Name == "None") hasNone = true; else dirs.Add(p.Name); return;
                case RPatKind.Path:
                    if (p.Path.Count == 1 && p.Path[0] == "None") hasNone = true;
                    else if (p.Path.Count >= 2 && p.Path[p.Path.Count - 2] == "Direction") dirs.Add(p.Path[p.Path.Count - 1]);
                    return;
                case RPatKind.TupleStruct:
                    if (p.Items.Count == 1 && Irrefutable(p.Items[0])) hasSomeAll = true;
                    return;
                case RPatKind.Lit:
                    if (p.Lit is RLit l && l.Kind == RLitKind.Bool) { if (l.B) hasTrue = true; else hasFalse = true; }
                    return;
                case RPatKind.Or:
                    foreach (var a in p.Items) Classify(a, ref catchAll, ref hasNone, ref hasSomeAll, ref hasTrue, ref hasFalse, dirs);
                    return;
            }
        }

        /// <summary>Refutable pattern (match arm / if let / while let) against the scrutinee type; binds variables.</summary>
        void CheckPattern(RPat p, RTy t)
        {
            var tp = RTy.Prune(t);
            bool viaRef = tp.Kind == RK.Ref;
            var d = RTy.Deref(t);
            p.Type = t;
            switch (p.Kind)
            {
                case RPatKind.Wild: return;
                case RPatKind.Ident:
                    if (IsConstPatternName(p.Name))
                    {
                        p.Kind = RPatKind.Path;
                        p.Path = new List<string> { p.Name };
                        p.PathEnd = p.End;
                        CheckPattern(p, t);
                        return;
                    }
                    BindPattern(p, t, initialized: true);
                    return;
                case RPatKind.Lit:
                    {
                        var lt = CheckExpr(p.Lit);
                        var target = RTy.Prune(lt).Kind == RK.Ref ? t : d;
                        if (RTy.Prune(lt).Kind == RK.Ref && RTy.Prune(t).Kind == RK.String)
                            throw Mismatch(t, lt, p, " (match on `s.as_str()` to compare with string literals)");
                        if (!Unify(target, lt)) throw Mismatch(t, lt, p);
                        return;
                    }
                case RPatKind.Path:
                    {
                        var path = p.Path;
                        RTy pt;
                        int k = path[0] == "farm" ? 1 : 0;
                        if (path.Count == 1 && path[0] == "None") pt = RTy.Option(NewVar());
                        else if (path.Count == 2 && path[0] == "Option" && path[1] == "None") pt = RTy.Option(NewVar());
                        else if (path.Count - k == 2 && System.Array.IndexOf(RustApi.Enums, path[k]) >= 0)
                        {
                            if (k == 0 && !FarmHas(path[k])) throw Err("failed to resolve: use of undeclared type `" + path[k] + "` (add `use farm::*;`) (E0433)", p);
                            pt = RTy.Game(path[k]);
                        }
                        else if (path.Count == 1 && Array.IndexOf(GameApi.DirectionNames, path[0]) >= 0 && FarmHas(path[0])) pt = RTy.Game("Direction");
                        else if (path.Count == 1 && Globals.TryGetValue(path[0], out var g) && g.Kind == RVarKind.Const) pt = g.Type;
                        else if (path.Count - k == 2 && GameApi.GameTypeOfCategory(path[k]) != null)
                            throw Err("failed to resolve: use of undeclared type `" + path[k] + "` (use `" + GameApi.GameTypeOfCategory(path[k]) + "::" + path[k + 1] + "`) (E0433)", p);
                        else throw Err("cannot find unit struct, unit variant or constant `" + string.Join("::", path) + "` in this scope (E0531)", p);
                        if (!Unify(d, pt)) throw Mismatch(d, pt, p);
                        return;
                    }
                case RPatKind.TupleStruct:
                    {
                        var path = p.Path;
                        bool some = (path.Count == 1 && path[0] == "Some") || (path.Count == 2 && path[0] == "Option" && path[1] == "Some");
                        if (!some) throw NotSupported("the pattern `" + string.Join("::", path) + "(..)`", p);
                        if (p.Items.Count != 1) throw Err("this pattern has " + p.Items.Count + " fields, but the corresponding tuple variant has 1 field (E0023)", p);
                        var inner = NewVar();
                        if (!Unify(d, RTy.Option(inner))) throw Mismatch(d, RTy.Option(inner), p);
                        CheckPattern(p.Items[0], viaRef ? RTy.Ref(inner, tp.Mut) : inner);
                        return;
                    }
                case RPatKind.Tuple:
                    {
                        if (d.Kind != RK.Tuple)
                        {
                            var items = p.Items.Select(_ => NewVar()).ToArray();
                            if (!Unify(d, RTy.Tuple(items))) throw Err("mismatched types: expected " + Q(t) + ", found tuple (E0308)", p);
                            d = RTy.Prune(d);
                        }
                        if (d.Items.Count != p.Items.Count) throw Err("mismatched types: expected a tuple with " + d.Items.Count + " elements, found one with " + p.Items.Count + " elements (E0308)", p);
                        for (int i = 0; i < p.Items.Count; i++) CheckPattern(p.Items[i], viaRef ? RTy.Ref(d.Items[i], tp.Mut) : d.Items[i]);
                        return;
                    }
                case RPatKind.Or:
                    foreach (var a in p.Items)
                    {
                        if (a.Kind == RPatKind.Ident && !IsConstPatternName(a.Name)) throw NotSupported("a binding inside an or-pattern", a);
                        CheckPattern(a, t);
                    }
                    return;
                case RPatKind.Ref:
                    {
                        if (tp.Kind != RK.Ref) throw Err("mismatched types: expected " + Q(t) + ", found `&_` (E0308)", p);
                        CheckPattern(p.Inner, tp.A);
                        return;
                    }
            }
            throw NotSupported("this pattern", p);
        }

        // ------------------------------------------------------------------ closures
        RTy Closure(RClosure c)
        {
            if (inConstInit) throw NotSupported("a closure in a constant", c);
            var ret = c.Ret != null ? ResolveType(c.Ret, true) : NewVar();
            var ctx = new FnCtx { Ret = ret, Id = ++fnIds, BaseScope = scopes.Count, Closure = c };
            var saveFlow = flow;
            flow = flow.Clone();
            bool saveDiv = diverged;
            var saveLoops = loopStack.ToList();
            loopStack.Clear();
            fnStack.Add(ctx);
            Push();
            var ps = new List<RTy>();
            foreach (var p in c.Params)
            {
                var pt = p.Type != null ? ResolveType(p.Type, true) : NewVar(0, p, null);
                ps.Add(pt);
                BindPattern(p.Pat, pt, initialized: true, kind: RVarKind.Param);
            }
            diverged = false;
            var bt = CheckExpr(c.Body);
            if (RTy.Prune(bt).Kind != RK.Never) Coerce(ret, bt, c.Body is RBlockExpr be && be.Block.Tail != null ? be.Block.Tail : c.Body);
            Pop();
            fnStack.RemoveAt(fnStack.Count - 1);
            loopStack.Clear(); loopStack.AddRange(saveLoops);
            flow = saveFlow;
            diverged = saveDiv;
            return new RTy { Kind = RK.Closure, Items = ps, Ret = ret, Closure = c };
        }
    }
}
