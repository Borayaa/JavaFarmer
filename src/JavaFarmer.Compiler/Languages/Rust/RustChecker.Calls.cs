using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed partial class RustChecker
    {
        // ------------------------------------------------------------------ neutral API types
        /// <summary>Neutral GameApi type text → Rust type (param: strings are &amp;str, lists &amp;[T]).</summary>
        RTy FromNeutral(string text, bool param, RExpr at, bool inner = false)
        {
            text = (text ?? "any").Replace(" ", "");
            if (text.Contains("|"))
            {
                var parts = RustApi.UnionMembers(text);
                var nonNull = parts.Where(x => x != "null" && x != "None" && x != "void").ToList();
                if (nonNull.Count == 1) return parts.Count > 1 ? RTy.Option(FromNeutral(nonNull[0], param, at, inner)) : FromNeutral(nonNull[0], param, at, inner);
                var v = NewVar();
                pendingTraits.Add((v, at, "union:" + string.Join("|", nonNull)));
                return v;
            }
            if (text.EndsWith("?")) return RTy.Option(FromNeutral(text.Substring(0, text.Length - 1), param, at, inner));
            int lt = text.IndexOf('<');
            if (lt > 0 && text.EndsWith(">"))
            {
                var head = text.Substring(0, lt);
                var args = SplitArgs(text.Substring(lt + 1, text.Length - lt - 2));
                RTy A(int i) => i < args.Count ? FromNeutral(args[i], param, at, true) : NewVar();
                switch (head)
                {
                    case "list": case "array": return param && !inner ? RTy.Ref(RTy.Slice(A(0)), false) : RTy.Vec(A(0));
                    case "set": return RTy.Set(A(0));
                    case "map": case "dict": return RTy.Map(A(0), A(1));
                    case "tuple": return RTy.Tuple(args.Select(a => FromNeutral(a, param, at, true)).ToArray());
                    case "func": return RTy.Fn(new List<RTy>(), A(0));
                }
                return NewVar();
            }
            switch (text)
            {
                case "int": return RTy.I32;
                case "float": case "double": return RTy.F64;
                case "number": return NewVar();
                case "bool": return RTy.Bool;
                case "string": case "str": return param ? RTy.StrRef : RTy.String;
                case "void": case "None": return RTy.Unit;
                case "any": case "null": return param ? NewVar() : NewVar(0, at, null);
                case "func": return RTy.Fn(new List<RTy>(), NewVar());
                case "Position": return RTy.Position;
                case "Companion": return RTy.Companion;
                case "Drone": return RTy.Drone(NewVar());
            }
            if (System.Array.IndexOf(RustApi.Enums, text) >= 0) return RTy.Game(text);
            return param ? NewVar() : NewVar(0, at, null);
        }

        static List<string> SplitArgs(string s)
        {
            var res = new List<string>();
            int depth = 0, start = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '<') depth++;
                else if (s[i] == '>') depth--;
                else if (s[i] == ',' && depth == 0) { res.Add(s.Substring(start, i - start)); start = i + 1; }
            }
            res.Add(s.Substring(start));
            return res;
        }

        /// <summary>Type of a builtin used as a value (fn item).</summary>
        RTy BuiltinFnType(RustApi.Entry api, RExpr at)
        {
            var b = api.B;
            if (b.PythonName == "spawn_drone" || b.PythonName == "wait_for" || b.PythonName == "has_finished" || b.PythonName == "measure" || b.PythonName == "min" || b.PythonName == "max" || b.PythonName == "abs")
                throw NotSupported("using the generic builtin `" + api.RustName + "` as a value", at);
            var ps = api.Params.Select(p => FromNeutral(p.TypeText, true, at)).ToList();
            return new RTy { Kind = RK.Fn, Items = ps, Ret = FromNeutral(b.ReturnText, false, null), Builtin = api.RustName };
        }

        static string Args(int n) => n == 1 ? "1 argument" : n + " arguments";

        RustAbort ArgCount(string what, int expected, int got, RNode at, string hint = null) =>
            Err("this " + what + " takes " + Args(expected) + " but " + Args(got) + (got == 1 ? " was" : " were") + " supplied" + (hint ?? "") + " (E0061)", at);

        // ------------------------------------------------------------------ calls
        RTy Call(RCall call)
        {
            if (call.Callee is RPathExpr pe)
            {
                var segs = pe.Segs;
                var names = segs.Select(s => s.Name).ToList();
                if (segs.Count == 1)
                {
                    var n = names[0];
                    var v = Lookup(n, out int lvl);
                    if (v == null && !Globals.ContainsKey(n) && !Fns.ContainsKey(n) && !importedItems.ContainsKey(n))
                    {
                        if (n == "Some")
                        {
                            if (call.Args.Count != 1) throw Err("this enum variant takes 1 argument but " + Args(call.Args.Count) + (call.Args.Count == 1 ? " was" : " were") + " supplied (E0061)", call);
                            call.Special = "some";
                            return RTy.Option(CheckExpr(call.Args[0]));
                        }
                        if ((n == "min" && stdCmpMin) || (n == "max" && stdCmpMax)) return MinMax(call, n, std: true);
                        if (n == "drop") throw NotSupported("`drop`", call.Callee);
                        var api = RustApi.Find(n);
                        if (api != null && FarmHas(n)) return CallBuiltin(call, api, pe);
                        if (n == "print" || n == "println" || n == "quick_print")
                            throw Err("expected function, found macro `" + n + "` (write `" + (n == "print" ? "println" : n) + "!(...)`) (E0423)", call.Callee);
                        throw UnknownFn(n, pe);
                    }
                }
                else
                {
                    int k = names[0] == "std" && names.Count >= 3 && names[1] == "collections" ? 2 : 0;
                    if (names.Count == 3 && names[0] == "std" && names[1] == "cmp" && (names[2] == "min" || names[2] == "max")) return MinMax(call, names[2], std: true);
                    if (names.Count == 2 && names[0] == "farm")
                    {
                        var api = RustApi.Find(names[1]);
                        if (api != null) return CallBuiltin(call, api, pe);
                        if (names[1] == "min" || names[1] == "max") return MinMax(call, names[1], std: false);
                    }
                    if (names.Count - k == 2)
                    {
                        var ty = names[k]; var fn = names[k + 1];
                        var gen = segs[k].Generics;
                        switch (ty)
                        {
                            case "Vec": return CtorVec(call, fn, gen, pe);
                            case "HashMap":
                                if (!hashMapImported && k == 0) throw Err("failed to resolve: use of undeclared type `HashMap` (add `use std::collections::HashMap;`) (E0433)", segs[0].Start, segs[0].End);
                                return CtorMap(call, fn, gen, pe);
                            case "HashSet":
                                if (!hashSetImported && k == 0) throw Err("failed to resolve: use of undeclared type `HashSet` (add `use std::collections::HashSet;`) (E0433)", segs[0].Start, segs[0].End);
                                return CtorSet(call, fn, gen, pe);
                            case "String":
                                if (fn == "new" && call.Args.Count == 0) { call.Special = "string.new"; return RTy.String; }
                                if (fn == "from" && call.Args.Count == 1)
                                {
                                    var at = CheckExpr(call.Args[0]);
                                    if (!Coerces(RTy.StrRef, at) && RTy.Prune(at).Kind != RK.String) throw Mismatch(RTy.StrRef, at, call.Args[0]);
                                    call.Special = "string.from";
                                    return RTy.String;
                                }
                                throw NotSupported("`String::" + fn + "`", call);
                            case "Option":
                                if (fn == "Some" && call.Args.Count == 1) { call.Special = "some"; return RTy.Option(CheckExpr(call.Args[0])); }
                                break;
                        }
                        if (RustParser.IntSuffixes.Contains(ty) || ty == "f64" || ty == "f32")
                            throw NotSupported("`" + ty + "::" + fn + "(...)` (use the method form: `x." + fn + "(...)`)", call.Callee);
                    }
                }
            }
            // user functions, other files, values
            var ct = CheckExpr(call.Callee);
            var t = RTy.Prune(ct);
            if (call.Callee is RPathExpr p2 && p2.Fn != null && call.Callee.ParenStart < 0)
            {
                var f = p2.Fn;
                call.Special = "fn";
                call.FnSym = f;
                if (call.Args.Count != f.Params.Count) throw ArgCount("function", f.Params.Count, call.Args.Count, call);
                for (int i = 0; i < call.Args.Count; i++) Coerce(f.Params[i], CheckExpr(call.Args[i]), call.Args[i]);
                return f.Ret;
            }
            if (call.Callee is RPathExpr p3 && p3.Builtin != null)
            {
                var api = RustApi.Find(p3.Builtin == "move" ? "move_to" : RustApi.BaseName(p3.Builtin)?.Replace("r#", ""));
                return CallBuiltin(call, api, p3);
            }
            if (t.Kind == RK.Fn || t.Kind == RK.Closure)
            {
                call.Special = "value";
                int n = t.Items?.Count ?? 0;
                if (call.Args.Count != n) throw ArgCount(t.Kind == RK.Closure ? "closure" : "function", n, call.Args.Count, call);
                for (int i = 0; i < n; i++) Coerce(t.Items[i], CheckExpr(call.Args[i]), call.Args[i]);
                return t.Ret ?? RTy.Unit;
            }
            if (t.Kind == RK.Var && t.Class == 0) throw Err("type annotations needed (E0282)", call.Callee);
            throw Err("expected function, found " + Q(ct) + " (E0618)", call.Callee);
        }

        RustAbort UnknownFn(string n, RNode at)
        {
            string hint = "";
            if (RustApi.Find(n) != null && !FarmHas(n)) hint = " (add `use farm::*;` at the top of the file)";
            else if (n == "move") hint = " (the game's move is `move_to(...)` or `r#move(...)`)";
            else if (GameApi.FromApiName(n, NamingStyle.Camel) != null && GameApi.FromApiName(n, NamingStyle.Camel) != n) hint = " (Rust uses snake_case: `" + RustApi.BaseName(GameApi.FromApiName(n, NamingStyle.Camel)) + "`)";
            else if (n == "len" || n == "str" || n == "range" || n == "int" || n == "float") hint = " (Python builtins do not exist in Rust)";
            return Err("cannot find function `" + n + "` in this scope" + hint + " (E0425)", at);
        }

        RTy CtorVec(RCall call, string fn, List<RTypeRef> gen, RNode at)
        {
            RTy Elem() => gen != null && gen.Count == 1 ? ResolveType(gen[0], true) : NewVar(0, call, "Vec<_>");
            switch (fn)
            {
                case "new":
                    if (call.Args.Count != 0) throw ArgCount("function", 0, call.Args.Count, call);
                    call.Special = "vec.new";
                    return RTy.Vec(Elem());
                case "with_capacity":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        var n = CheckExpr(call.Args[0]);
                        if (!Unify(RTy.USize, n)) throw Mismatch(RTy.USize, n, call.Args[0]);
                        call.Special = "vec.new";
                        return RTy.Vec(Elem());
                    }
                case "from":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        if (!(call.Args[0] is RArray arr) || arr.IsVecMacro) throw NotSupported("`Vec::from` of anything but an array literal (`Vec::from([1, 2])`)", call.Args[0]);
                        var t = RTy.Prune(CheckExpr(arr));
                        call.Special = "vec.from";
                        return RTy.Vec(t.A);
                    }
            }
            throw Err("no function or associated item named `" + fn + "` found for struct `Vec` in the current scope (E0599)", at);
        }

        RTy CtorMap(RCall call, string fn, List<RTypeRef> gen, RNode at)
        {
            switch (fn)
            {
                case "new":
                case "with_capacity":
                    {
                        if (fn == "new" && call.Args.Count != 0) throw ArgCount("function", 0, call.Args.Count, call);
                        if (fn == "with_capacity") { if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call); var n = CheckExpr(call.Args[0]); if (!Unify(RTy.USize, n)) throw Mismatch(RTy.USize, n, call.Args[0]); }
                        call.Special = "map.new";
                        if (gen != null && gen.Count == 2) return RTy.Map(ResolveType(gen[0], true), ResolveType(gen[1], true));
                        var k = NewVar(0, call, "HashMap<_, _>");
                        var v = NewVar(0, call, "HashMap<_, _>");
                        return RTy.Map(k, v);
                    }
                case "from":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        if (!(call.Args[0] is RArray arr) || arr.IsVecMacro || arr.RepeatValue != null || arr.Items.Any(i => !(i is RTuple tt && tt.Items.Count == 2)))
                            throw NotSupported("`HashMap::from` of anything but an array of pairs (`HashMap::from([(k, v), ...])`)", call.Args[0]);
                        var t = RTy.Prune(CheckExpr(arr));
                        var pair = RTy.Prune(t.A);
                        call.Special = "map.from";
                        if (pair.Kind != RK.Tuple) { var kk = NewVar(0, call, "HashMap<_, _>"); var vv = NewVar(0, call, "HashMap<_, _>"); Unify(pair, RTy.Tuple(kk, vv)); return RTy.Map(kk, vv); }
                        return RTy.Map(pair.Items[0], pair.Items[1]);
                    }
            }
            throw Err("no function or associated item named `" + fn + "` found for struct `HashMap` in the current scope (E0599)", at);
        }

        RTy CtorSet(RCall call, string fn, List<RTypeRef> gen, RNode at)
        {
            switch (fn)
            {
                case "new":
                case "with_capacity":
                    if (fn == "new" && call.Args.Count != 0) throw ArgCount("function", 0, call.Args.Count, call);
                    if (fn == "with_capacity") { if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call); var n = CheckExpr(call.Args[0]); if (!Unify(RTy.USize, n)) throw Mismatch(RTy.USize, n, call.Args[0]); }
                    call.Special = "set.new";
                    return RTy.Set(gen != null && gen.Count == 1 ? ResolveType(gen[0], true) : NewVar(0, call, "HashSet<_>"));
                case "from":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        if (!(call.Args[0] is RArray arr) || arr.IsVecMacro || arr.RepeatValue != null) throw NotSupported("`HashSet::from` of anything but an array literal (`HashSet::from([a, b])`)", call.Args[0]);
                        var t = RTy.Prune(CheckExpr(arr));
                        call.Special = "set.from";
                        return RTy.Set(t.A);
                    }
            }
            throw Err("no function or associated item named `" + fn + "` found for struct `HashSet` in the current scope (E0599)", at);
        }

        static bool Ordered(RTy t)
        {
            var d = RTy.Prune(t);
            switch (d.Kind)
            {
                case RK.Int: case RK.Float: case RK.Bool: case RK.String: case RK.Game: case RK.Var: return true;
                case RK.Ref: return Ordered(d.A);
                case RK.Str: return true;
                case RK.Tuple: return d.Items.All(Ordered);
                case RK.Option: case RK.Vec: return Ordered(d.A);
            }
            return false;
        }

        RTy MinMax(RCall call, string name, bool std)
        {
            call.Special = name;
            if (call.Args.Count != 2) throw ArgCount("function", 2, call.Args.Count, call);
            var a = CheckExpr(call.Args[0]);
            var b = CheckExpr(call.Args[1]);
            if (!Unify(a, b)) throw Mismatch(a, b, call.Args[1]);
            if (!Ordered(a)) throw Err("the trait bound " + Q(a) + ": PartialOrd` is not satisfied (E0277)".Replace("`" + RTy.Show(a) + "`: ", "`" + RTy.Show(a) + ": "), call.Args[0]);
            if (std && RTy.Deref(a).IsFloatLike) throw Err("the trait bound `" + RTy.Show(RTy.Deref(a)) + ": Ord` is not satisfied (floats are not `Ord`: use `a.min(b)` or farm's `min(a, b)`) (E0277)", call.Args[0]);
            if (!RTy.Deref(a).IsNumeric) throw NotSupported("`" + name + "` of non-numbers", call);
            return a;
        }

        RTy CallBuiltin(RCall call, RustApi.Entry api, RPathExpr callee)
        {
            if (api == null) throw UnknownFn(callee.Last, callee);
            if (inConstInit) throw Err("cannot call non-const fn `" + api.RustName + "` in constants (E0015)", call);
            var b = api.B;
            call.Special = "builtin";
            call.Api = api;
            callee.Builtin = b.PythonName;
            string py = b.PythonName;
            switch (py)
            {
                case "min": case "max": return MinMax(call, py, std: false);
                case "abs":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        var a = CheckExpr(call.Args[0]);
                        var d = RTy.Deref(a);
                        if (!d.IsNumeric) throw Err("the trait bound `" + RTy.Show(a) + ": Number` is not satisfied (abs needs a number) (E0277)", call.Args[0]);
                        if (d.IsUnsigned) throw Err("the trait bound `" + RTy.Show(d) + ": Number` is not satisfied (unsigned numbers have no abs) (E0277)", call.Args[0]);
                        return d;
                    }
                case "spawn_drone":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        var arg = call.Args[0];
                        var at = CheckExpr(arg);
                        var d = RTy.Prune(at);
                        RTy result;
                        if (d.Kind == RK.Closure)
                        {
                            if (d.Items.Count != 0) throw Err("closure is expected to take 0 arguments, but it takes " + Args(d.Items.Count) + " (E0593)", arg);
                            var cl = d.Closure;
                            if (cl != null && !cl.Move && cl.Captures.Count > 0)
                                throw Err("closure may outlive the current function, but it borrows `" + cl.Captures[0].Name + "`, which is owned by the current function (write `move ||`) (E0373)", arg is RClosure ? cl.Start : arg.Start, arg is RClosure ? cl.HeaderEnd : arg.End);
                            result = d.Ret;
                        }
                        else if (d.Kind == RK.Fn)
                        {
                            if ((d.Items?.Count ?? 0) != 0) throw Err("expected a function that takes 0 arguments, but `" + RTy.Show(d) + "` takes " + Args(d.Items.Count) + " (E0593)", arg);
                            result = d.Ret ?? RTy.Unit;
                        }
                        else throw Err("expected a closure or a function, found " + Q(at) + " (E0277)", arg);
                        return RTy.Option(RTy.Drone(result));
                    }
                case "wait_for":
                case "has_finished":
                    {
                        if (call.Args.Count != 1) throw ArgCount("function", 1, call.Args.Count, call);
                        var at = CheckExpr(call.Args[0]);
                        var r = NewVar();
                        if (!Unify(RTy.Drone(r), at))
                        {
                            var d = RTy.Prune(at);
                            if (d.Kind == RK.Option) throw Mismatch(RTy.Drone(r), at, call.Args[0], " (spawn_drone returns an Option: check it with `if let Some(d) = spawn_drone(...)`)");
                            throw Mismatch(RTy.Drone(r), at, call.Args[0]);
                        }
                        return py == "wait_for" ? r : RTy.Bool;
                    }
                case "measure":
                    {
                        int want = api.WithForm ? 1 : 0;
                        if (call.Args.Count != want) throw ArgCount("function", want, call.Args.Count, call, !api.WithForm && call.Args.Count == 1 ? " (to measure a neighbour use `measure_with(direction)`)" : null);
                        if (want == 1) Coerce(RTy.Game("Direction"), CheckExpr(call.Args[0]), call.Args[0]);
                        var r = NewVar(0, call, null);
                        pendingTraits.Add((r, call, "Measurement"));
                        return r;
                    }
            }
            var ps = api.Params;
            var variadic = ps.Count > 0 && ps[ps.Count - 1].Variadic ? ps[ps.Count - 1] : null;
            if (call.Args.Count != ps.Count)
            {
                string hint = null;
                if (!api.WithForm && RustApi.HasOptional(b) && call.Args.Count > ps.Count) hint = " (to pass the optional parameters use `" + RustApi.BaseName(py).Replace("r#", "") + "_with(...)`)";
                throw ArgCount("function", ps.Count, call.Args.Count, call, hint);
            }
            for (int i = 0; i < call.Args.Count; i++)
            {
                var p = ps[i];
                var arg = call.Args[i];
                if (p.Variadic)
                {
                    if (!(arg is RUnary u && u.Op == "&" && u.Operand is RArray arr && !arr.IsVecMacro && arr.RepeatValue == null))
                        throw NotSupported("passing anything but an array literal `&[a, b]` to the variadic parameter `" + p.Name + "`", arg);
                    call.SpreadArgs = call.SpreadArgs ?? new List<RExpr>();
                    var et = FromNeutral(p.TypeText, true, arg, true);
                    foreach (var it in arr.Items) { Coerce(et, CheckExpr(it), it); call.SpreadArgs.Add(it); }
                    arg.Type = RTy.Ref(RTy.Slice(et), false);
                    continue;
                }
                var pt = FromNeutral(p.TypeText, true, arg);
                var at = CheckExpr(arg);
                if (RTy.Prune(pt).Kind == RK.Var) { if (!Unify(pt, RTy.Deref(at)) && !Unify(pt, at)) throw Mismatch(pt, at, arg); }
                else Coerce(pt, at, arg);
            }
            return FromNeutral(b.ReturnText, false, call);
        }

        // ------------------------------------------------------------------ methods
        static readonly HashSet<string> NumericInherent = new HashSet<string> { "abs", "min", "max", "pow", "powi", "powf", "sqrt", "floor", "ceil", "round", "div_euclid", "rem_euclid", "signum" };

        RTy MethodCall(RMethodCall mc)
        {
            var name = mc.Name;
            if (inConstInit) throw Err("cannot call non-const method `" + name + "` in constants (E0015)", mc);
            // .chars().count()
            if (name == "count" && mc.Args.Count == 0 && mc.Recv is RMethodCall ch && ch.Name == "chars" && ch.Args.Count == 0)
            {
                var st = CheckExpr(ch.Recv);
                var sd = RTy.Deref(st);
                if (sd.Kind != RK.String && sd.Kind != RK.Str) throw Err("no method named `chars` found for " + Q(st) + " in the current scope (E0599)", ch.NameStart, ch.NameEnd);
                ch.Type = RTy.Iter(RTy.Char);
                mc.Resolved = "len";
                return RTy.USize;
            }
            // m.entry(k).or_insert(v)
            if (name == "or_insert" && mc.Recv is RMethodCall en && en.Name == "entry")
            {
                var mt = CheckExpr(en.Recv);
                var md = RTy.Deref(mt);
                if (md.Kind != RK.Map) throw Err("no method named `entry` found for " + Q(mt) + " in the current scope (E0599)", en.NameStart, en.NameEnd);
                if (en.Args.Count != 1) throw ArgCount("method", 1, en.Args.Count, en);
                if (mc.Args.Count != 1) throw ArgCount("method", 1, mc.Args.Count, mc);
                RequireMutable(en.Recv, "borrow");
                Coerce(md.A, CheckExpr(en.Args[0]), en.Args[0]);
                Coerce(md.B, CheckExpr(mc.Args[0]), mc.Args[0]);
                en.Type = RTy.Unit;
                mc.Resolved = "map.entry";
                return RTy.Ref(md.B, true);
            }
            var rt = CheckExpr(mc.Recv);
            var d = RTy.Deref(rt);
            if (d.Kind == RK.Var)
            {
                if (d.Class == 0) throw Err("type annotations needed (E0282)", mc.Recv);
                if (NumericInherent.Contains(name))
                    throw Err("can't call method `" + name + "` on ambiguous numeric type `" + (d.Class == 1 ? "{integer}" : "{float}") + "` (give it a type, e.g. `" + (d.Class == 1 ? "2_i32" : "2.0_f64") + "`) (E0689)", mc.NameStart, mc.NameEnd);
                Unify(d, d.Class == 1 ? RTy.I32 : RTy.F64);
                d = RTy.Prune(d);
            }
            int argc = mc.Args.Count;
            void Want(int n) { if (argc != n) throw ArgCount("method", n, argc, mc); }
            RTy A(int i) => CheckExpr(mc.Args[i]);
            void Mut() => RequireMutable(mc.Recv, "borrow");
            void RefArg(int i, RTy elem)
            {
                var at = A(i);
                if (!Coerces(RTy.Ref(elem, false), at))
                {
                    if (Unify(elem, at)) throw Mismatch(RTy.Ref(elem, false), at, mc.Args[i], " (consider borrowing here: `&`)");
                    throw Mismatch(RTy.Ref(elem, false), at, mc.Args[i]);
                }
            }
            RustAbort NoMethod(string kind)
            {
                string hint = "";
                if (name == "length" || name == "size" || name == "count") hint = " (use `.len()`)";
                else if (name == "add" || name == "append" || name == "push_back") hint = d.Kind == RK.Set ? " (use `.insert(x)`)" : " (use `.push(x)`)";
                else if (name == "put") hint = " (use `.insert(k, v)`)";
                else if (name == "pow" && d.Kind == RK.Float) hint = " (use `.powi(n)` or `.powf(x)`)";
                else if ((name == "sqrt" || name == "floor" || name == "ceil" || name == "round") && d.Kind == RK.Int) hint = " (convert first: `(x as f64)." + name + "()`)";
                return Err("no method named `" + name + "` found for " + kind + " " + Q(d) + " in the current scope" + hint + " (E0599)", mc.NameStart, mc.NameEnd);
            }
            RustAbort Unsup() => NotSupported("the method `." + name + "()` on " + Q(d), mc.NameStart, mc.NameEnd);
            if (name == "iter" || name == "into_iter" || name == "iter_mut" || name == "keys" || name == "values" || name == "enumerate" || name == "rev" || name == "step_by" || name == "copied" || name == "cloned" && d.Kind != RK.Option || name == "map" || name == "filter" || name == "collect" || name == "sum")
            {
                if ((d.Kind == RK.Vec || d.Kind == RK.Map || d.Kind == RK.Set || d.Kind == RK.Range || d.Kind == RK.Array || d.Kind == RK.Slice || d.Kind == RK.Iter))
                    throw NotSupported("iterator methods outside a `for` loop (`." + name + "()`)", mc.NameStart, mc.NameEnd);
            }
            switch (d.Kind)
            {
                case RK.Vec: case RK.Slice: case RK.Array:
                    switch (name)
                    {
                        case "push":
                            if (d.Kind != RK.Vec) throw NoMethod(d.Kind == RK.Array ? "array" : "slice");
                            Want(1); Mut(); Coerce(d.A, A(0), mc.Args[0]); mc.Resolved = "vec.push"; return RTy.Unit;
                        case "pop": if (d.Kind != RK.Vec) throw NoMethod("array"); Want(0); Mut(); mc.Resolved = "vec.pop"; return RTy.Option(d.A);
                        case "len": Want(0); mc.Resolved = "len"; return RTy.USize;
                        case "is_empty": Want(0); mc.Resolved = "is_empty"; return RTy.Bool;
                        case "insert":
                            {
                                if (d.Kind != RK.Vec) throw NoMethod("array");
                                Want(2); Mut();
                                var i = A(0); if (!Unify(RTy.USize, i)) throw Mismatch(RTy.USize, i, mc.Args[0]);
                                Coerce(d.A, A(1), mc.Args[1]); mc.Resolved = "vec.insert"; return RTy.Unit;
                            }
                        case "remove":
                            {
                                if (d.Kind != RK.Vec) throw NoMethod("array");
                                Want(1); Mut();
                                var i = A(0);
                                if (!Unify(RTy.USize, i)) throw Mismatch(RTy.USize, i, mc.Args[0], " (Vec::remove takes an index; to remove a value use `.remove_value(&x)`)");
                                mc.Resolved = "vec.remove"; return d.A;
                            }
                        case "contains": Want(1); RefArg(0, d.A); mc.Resolved = "contains"; return RTy.Bool;
                        case "clear": if (d.Kind != RK.Vec) throw NoMethod("array"); Want(0); Mut(); mc.Resolved = "clear"; return RTy.Unit;
                        case "clone": Want(0); mc.Resolved = d.Kind == RK.Vec ? "copy.list" : "copy.list"; return RTy.Prune(rt).Kind == RK.Ref && d.Kind == RK.Slice ? RTy.Vec(d.A) : d;
                        case "to_vec": Want(0); mc.Resolved = "copy.list"; return RTy.Vec(d.A);
                        case "swap":
                            {
                                Want(2); Mut();
                                for (int i = 0; i < 2; i++) { var it = A(i); if (!Unify(RTy.USize, it)) throw Mismatch(RTy.USize, it, mc.Args[i]); }
                                mc.Resolved = "vec.swap"; return RTy.Unit;
                            }
                        case "remove_value":
                            if (!FarmHas("FarmVec")) throw Err("no method named `remove_value` found for " + Q(d) + " in the current scope (it comes from the farm crate: add `use farm::*;`) (E0599)", mc.NameStart, mc.NameEnd);
                            Want(1); Mut(); RefArg(0, d.A); mc.Resolved = "vec.remove_value"; return RTy.Unit;
                        case "index_of":
                            if (!FarmHas("FarmVec")) throw Err("no method named `index_of` found for " + Q(d) + " in the current scope (it comes from the farm crate: add `use farm::*;`) (E0599)", mc.NameStart, mc.NameEnd);
                            Want(1); RefArg(0, d.A); mc.Resolved = "vec.index_of"; return RTy.I32;
                        case "first": case "last": case "get": case "get_mut": case "sort": case "sort_unstable": case "reverse": case "extend": case "join": case "retain": case "truncate":
                        case "dedup": case "append": case "split_off": case "drain": case "concat": case "windows": case "chunks": case "binary_search": case "fill": case "resize":
                            throw Unsup();
                    }
                    throw NoMethod(d.Kind == RK.Vec ? "struct" : d.Kind == RK.Array ? "array" : "slice");
                case RK.Map:
                    switch (name)
                    {
                        case "insert": Want(2); Mut(); Coerce(d.A, A(0), mc.Args[0]); Coerce(d.B, A(1), mc.Args[1]); mc.Resolved = "map.insert"; return RTy.Option(d.B);
                        case "get": Want(1); RefArg(0, d.A); mc.Resolved = "map.get"; return RTy.Option(RTy.Ref(d.B, false));
                        case "contains_key": Want(1); RefArg(0, d.A); mc.Resolved = "contains"; return RTy.Bool;
                        case "remove": Want(1); Mut(); RefArg(0, d.A); mc.Resolved = "map.remove"; return RTy.Option(d.B);
                        case "len": Want(0); mc.Resolved = "len"; return RTy.USize;
                        case "is_empty": Want(0); mc.Resolved = "is_empty"; return RTy.Bool;
                        case "clear": Want(0); Mut(); mc.Resolved = "clear"; return RTy.Unit;
                        case "clone": Want(0); mc.Resolved = "copy.map"; return d;
                        case "get_mut": case "entry": case "get_or_insert": case "retain": case "extend": case "drain": case "into_keys": case "into_values":
                            throw Unsup();
                    }
                    throw NoMethod("struct");
                case RK.Set:
                    switch (name)
                    {
                        case "insert": Want(1); Mut(); Coerce(d.A, A(0), mc.Args[0]); mc.Resolved = "set.insert"; return RTy.Bool;
                        case "contains": Want(1); RefArg(0, d.A); mc.Resolved = "contains"; return RTy.Bool;
                        case "remove": Want(1); Mut(); RefArg(0, d.A); mc.Resolved = "set.remove"; return RTy.Bool;
                        case "len": Want(0); mc.Resolved = "len"; return RTy.USize;
                        case "is_empty": Want(0); mc.Resolved = "is_empty"; return RTy.Bool;
                        case "clear": Want(0); Mut(); mc.Resolved = "clear"; return RTy.Unit;
                        case "clone": Want(0); mc.Resolved = "copy.set"; return d;
                        case "union": case "intersection": case "difference": case "is_subset": case "retain": case "extend": case "get": case "take": case "drain":
                            throw Unsup();
                    }
                    throw NoMethod("struct");
                case RK.Option:
                    switch (name)
                    {
                        case "unwrap": Want(0); mc.Resolved = "opt.unwrap"; return d.A;
                        case "expect":
                            {
                                Want(1);
                                var at = A(0);
                                if (!Coerces(RTy.StrRef, at)) throw Mismatch(RTy.StrRef, at, mc.Args[0]);
                                mc.Resolved = "opt.unwrap"; return d.A;
                            }
                        case "is_some": Want(0); mc.Resolved = "opt.is_some"; return RTy.Bool;
                        case "is_none": Want(0); mc.Resolved = "opt.is_none"; return RTy.Bool;
                        case "unwrap_or":
                            {
                                Want(1);
                                Coerce(d.A, A(0), mc.Args[0]);
                                mc.Resolved = "opt.unwrap_or"; return d.A;
                            }
                        case "copied": case "cloned":
                            {
                                Want(0);
                                var inner = RTy.Prune(d.A);
                                if (inner.Kind != RK.Ref) throw Err("the method `" + name + "` exists for " + Q(d) + ", but its trait bounds were not satisfied (it needs an `Option<&T>`) (E0599)", mc.NameStart, mc.NameEnd);
                                mc.Resolved = "opt.copied"; return RTy.Option(inner.A);
                            }
                        case "clone": Want(0); mc.Resolved = "same"; return d;
                        case "map": case "and_then": case "unwrap_or_else": case "unwrap_or_default": case "ok_or": case "take": case "filter": case "or": case "as_ref": case "is_some_and":
                            throw Unsup();
                    }
                    throw NoMethod("enum");
                case RK.String: case RK.Str:
                    switch (name)
                    {
                        case "len": Want(0); mc.Resolved = "len"; return RTy.USize;
                        case "is_empty": Want(0); mc.Resolved = "is_empty"; return RTy.Bool;
                        case "contains":
                            {
                                Want(1);
                                var at = A(0);
                                if (!Coerces(RTy.StrRef, at)) throw Mismatch(RTy.StrRef, at, mc.Args[0], RTy.Prune(at).Kind == RK.String ? " (consider borrowing here: `&`)" : null);
                                mc.Resolved = "contains"; return RTy.Bool;
                            }
                        case "push_str":
                            {
                                if (d.Kind != RK.String) throw NoMethod("reference");
                                Want(1); Mut();
                                var at = A(0);
                                if (!Coerces(RTy.StrRef, at)) throw Mismatch(RTy.StrRef, at, mc.Args[0], RTy.Prune(at).Kind == RK.String ? " (consider borrowing here: `&`)" : null);
                                mc.Resolved = "str.push_str"; return RTy.Unit;
                            }
                        case "to_string": case "to_owned": Want(0); mc.Resolved = "same"; return RTy.String;
                        case "clone": Want(0); mc.Resolved = "same"; return RTy.Prune(rt).Kind == RK.Ref && RTy.Prune(RTy.Prune(rt).A).Kind == RK.Str ? rt : RTy.String;
                        case "as_str": Want(0); mc.Resolved = "same"; return RTy.StrRef;
                        case "push": throw NotSupported("`String::push` (char is not supported; use `push_str(\"x\")`)", mc.NameStart, mc.NameEnd);
                        case "trim": case "to_uppercase": case "to_lowercase": case "split": case "parse": case "starts_with": case "ends_with": case "replace": case "chars": case "bytes":
                        case "lines": case "find": case "repeat": case "split_whitespace": case "insert": case "remove": case "pop": case "truncate": case "clear": case "get": case "into":
                            throw Unsup();
                    }
                    throw NoMethod(d.Kind == RK.String ? "struct" : "reference");
                case RK.Int: case RK.Float:
                    {
                        bool isF = d.Kind == RK.Float;
                        switch (name)
                        {
                            case "abs":
                                Want(0);
                                if (d.IsUnsigned) throw NoMethod("type");
                                mc.Resolved = "num.abs"; return d;
                            case "min": case "max": Want(1); { var at = A(0); var ad = RTy.Deref(at); if (!Unify(d, ad)) throw Mismatch(d, at, mc.Args[0]); } mc.Resolved = "num." + name; return d;
                            case "pow":
                                if (isF) throw NoMethod("type");
                                Want(1); { var at = A(0); if (!Unify(RTy.U32, at)) throw Mismatch(RTy.U32, at, mc.Args[0], " (write `.pow(n as u32)`)"); }
                                mc.Resolved = "num.pow"; return d;
                            case "powi":
                                if (!isF) throw NoMethod("type");
                                Want(1); { var at = A(0); if (!Unify(RTy.I32, at)) throw Mismatch(RTy.I32, at, mc.Args[0]); }
                                mc.Resolved = "num.pow"; return d;
                            case "powf":
                                if (!isF) throw NoMethod("type");
                                Want(1); { var at = A(0); if (!Unify(d, at)) throw Mismatch(d, at, mc.Args[0]); }
                                mc.Resolved = "num.pow"; return d;
                            case "sqrt": case "floor": case "ceil": case "round":
                                if (!isF) throw NoMethod("type");
                                Want(0); mc.Resolved = "num." + name; return d;
                            case "div_euclid": case "rem_euclid":
                                Want(1); { var at = A(0); var ad = RTy.Deref(at); if (!Unify(d, ad)) throw Mismatch(d, at, mc.Args[0]); }
                                mc.Resolved = "num." + name; return d;
                            case "to_string": Want(0); mc.Resolved = "num.to_string"; return RTy.String;
                            case "clone": Want(0); mc.Resolved = "same"; return d;
                            case "signum": case "trunc": case "fract": case "sin": case "cos": case "tan": case "ln": case "log10": case "log2": case "exp": case "hypot": case "atan2":
                            case "checked_add": case "wrapping_add": case "saturating_sub": case "is_positive": case "is_negative": case "clamp": case "to_radians": case "is_nan":
                                throw Unsup();
                        }
                        throw NoMethod("type");
                    }
                case RK.Bool:
                    if (name == "to_string") { Want(0); mc.Resolved = "num.to_string"; return RTy.String; }
                    if (name == "clone") { Want(0); mc.Resolved = "same"; return d; }
                    if (name == "then" || name == "then_some") throw Unsup();
                    throw NoMethod("type");
                case RK.Game:
                    if (name == "to_string") { Want(0); mc.Resolved = "num.to_string"; return RTy.String; }
                    if (name == "clone") { Want(0); mc.Resolved = "same"; return d; }
                    throw NoMethod("enum");
                case RK.Tuple: case RK.Drone:
                    if (name == "clone") { Want(0); mc.Resolved = "same"; return d; }
                    throw NoMethod(d.Kind == RK.Tuple ? "tuple" : "struct");
                case RK.Range:
                    if (name == "contains")
                    {
                        Want(1); RefArg(0, d.A);
                        if (!(mc.Recv is RRange)) throw NotSupported("`contains` on a range in a variable", mc.Recv);
                        mc.Resolved = "range.contains"; return RTy.Bool;
                    }
                    throw NoMethod("struct");
                case RK.Closure: case RK.Fn:
                    throw Err("no method named `" + name + "` found for " + Q(d) + " in the current scope (call it directly: `f()`) (E0599)", mc.NameStart, mc.NameEnd);
            }
            throw NoMethod("type");
        }

        // ------------------------------------------------------------------ macros
        RTy Macro(RMacro m)
        {
            switch (m.Name)
            {
                case "println": case "print": case "format": break;
                case "quick_print":
                    if (!FarmHas("quick_print")) throw Err("cannot find macro `quick_print` in this scope (add `use farm::*;`)", m.NameStart, m.NameEnd);
                    break;
                case "eprintln": case "eprint": throw NotSupported("`" + m.Name + "!` (use `println!`)", m.NameStart, m.BangEnd);
                case "panic": throw NotSupported("`panic!`", m.NameStart, m.BangEnd);
                default: throw Err("cannot find macro `" + m.Name + "` in this scope", m.NameStart, m.NameEnd);
            }
            if (inConstInit) throw NotSupported("a macro in a constant", m);
            if ((m.Name == "print" || m.Name == "format" || m.Name == "quick_print") && m.Format == null && m.Name != "quick_print")
                throw Err("requires at least a format string argument", m);
            foreach (var arg in m.Args)
                if (!m.Pieces.Any(pc => pc.Arg == arg)) CheckExpr(arg);
            foreach (var pc in m.Pieces)
            {
                if (pc.Arg == null) continue;
                var t = pc.Arg.Type ?? CheckExpr(pc.Arg);
                if (pc.Arg is RPathExpr pe && pe.Var != null && pe.Var.Kind == RVarKind.Static && pe.Var.Mut)
                    throw Err("creating a shared reference to mutable static is not allowed in Rust 2024 (copy it first: `let v = unsafe { " + pe.Var.Name + " };`)", pc.Arg);
                if (pc.Debug) { if (!DebugOk(t)) throw Err(Q(t) + " doesn't implement `Debug` (E0277)", pc.Arg); }
                else if (!DisplayOk(t))
                    throw Err(Q(t) + " doesn't implement `std::fmt::Display` (use `{:?}` to print it) (E0277)", pc.Arg);
            }
            return m.Name == "format" ? RTy.String : RTy.Unit;
        }

        static bool DisplayOk(RTy t)
        {
            var d = RTy.Prune(t);
            switch (d.Kind)
            {
                case RK.Int: case RK.Float: case RK.Bool: case RK.String: case RK.Str: case RK.Game: case RK.Char: case RK.Var: case RK.Error: return true;
                case RK.Ref: return DisplayOk(d.A);
            }
            return false;
        }

        static bool DebugOk(RTy t)
        {
            var d = RTy.Prune(t);
            switch (d.Kind)
            {
                case RK.Closure: case RK.Fn: case RK.Iter: case RK.Never: return false;
                case RK.Ref: case RK.Vec: case RK.Slice: case RK.Array: case RK.Set: case RK.Option: return DebugOk(d.A);
                case RK.Map: return DebugOk(d.A) && DebugOk(d.B);
                case RK.Tuple: return d.Items.All(DebugOk);
            }
            return true;
        }
    }
}
