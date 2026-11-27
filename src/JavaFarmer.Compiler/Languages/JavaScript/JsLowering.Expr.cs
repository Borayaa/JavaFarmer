using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    /// <summary>Game API names in JavaScript: camelCase builtins, console.log for print, Math.min / max / abs / random.</summary>
    internal static class JsNames
    {
        static bool IsMathOrPrint(string py) => py == "print" || py == "min" || py == "max" || py == "abs" || py == "random";

        public static string ApiName(string pythonName)
        {
            if (pythonName == null || GameApi.Find(pythonName) == null) return null;
            if (pythonName == "print") return "console.log";
            if (pythonName == "min" || pythonName == "max" || pythonName == "abs" || pythonName == "random") return "Math." + pythonName;
            return GameApi.ToStyle(pythonName, NamingStyle.Camel);
        }

        public static string FromApiName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (name == "console.log") return "print";
            if (name.StartsWith("Math.", StringComparison.Ordinal))
            {
                var m = name.Substring(5);
                return (m == "min" || m == "max" || m == "abs" || m == "random") && GameApi.Find(m) != null ? m : null;
            }
            if (IsMathOrPrint(name)) return GameApi.Find(name) != null ? name : null;
            return GameApi.FromApiName(name, NamingStyle.Camel);
        }

        /// <summary>A builtin callable by its bare camelCase name (not print / min / max / abs / random); null otherwise.</summary>
        public static string BareBuiltin(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var py = GameApi.FromApiName(name, NamingStyle.Camel);
            if (py == null || IsMathOrPrint(py)) return null;
            return py;
        }
    }

    internal sealed partial class JsLowering
    {
        static readonly HashSet<string> UnsupportedGlobals = new HashSet<string>
        {
            "JSON", "Date", "Promise", "Symbol", "RegExp", "Error", "BigInt", "Boolean", "globalThis", "window", "document",
            "process", "require", "module", "setTimeout", "setInterval", "clearTimeout", "alert", "prompt", "Reflect", "Proxy",
            "WeakMap", "WeakSet", "eval", "arguments", "isNaN", "isFinite", "Intl", "structuredClone", "queueMicrotask",
        };

        static readonly HashSet<string> StringMethods = new HashSet<string>
        {
            "toUpperCase", "toLowerCase", "split", "trim", "trimStart", "trimEnd", "substring", "substr", "startsWith", "endsWith",
            "replace", "replaceAll", "repeat", "padStart", "padEnd", "charCodeAt", "codePointAt", "localeCompare", "match", "search",
            "normalize",
        };

        static readonly HashSet<string> HigherOrder = new HashSet<string>
        {
            "map", "filter", "forEach", "reduce", "reduceRight", "find", "findIndex", "findLast", "findLastIndex", "some", "every",
            "flatMap", "flat", "sort", "toSorted", "reverse", "toReversed", "join", "concat", "lastIndexOf", "copyWithin", "with",
        };

        static IrLiteral Lit(IrLiteral l, Span s) { l.Span = s; return l; }

        IrExpr Bad(Span s) => new IrLiteral { Kind = IrLitKind.Null, Type = IrType.Null, Span = s };

        // ================================================================== names
        void NoteUse(Binding b, bool assign, Span at)
        {
            if (b.ModuleScope && (b.Kind == "let" || b.Kind == "const" || b.Kind == "var"))
            {
                if (fn != null && (fn.InsideDecl || assign)) b.NeedsGlobal = true;
                return;
            }
            if (assign && b.Owner != fn && (b.Kind == "let" || b.Kind == "var" || b.Kind == "param" || b.Kind == "function"))
                Error("assigning a captured variable inside a closure is not supported in JavaFarmer", at);
        }

        void TdzCheck(Binding b, Span at)
        {
            if ((b.Kind == "let" || b.Kind == "const") && b.Owner == fn && at.Start < b.DeclPos)
                Error("Cannot access '" + b.Sym.Name + "' before initialization", at);
        }

        string DidYouMean(string name)
        {
            if (name == "True") return "true";
            if (name == "False") return "false";
            if (name == "None") return "null";
            if (name == "print" || name == "println" || name == "printf") return "console.log";
            if (GameApi.Find(name) != null) return JsNames.ApiName(name);
            foreach (var b in GameApi.Builtins)
            {
                var js = JsNames.ApiName(b.PythonName);
                if (js == null) continue;
                if (string.Equals(js, name, StringComparison.OrdinalIgnoreCase)) return js;
                if (js.StartsWith("Math.") && string.Equals(js.Substring(5), name, StringComparison.OrdinalIgnoreCase)) return js;
            }
            foreach (var d in GameApi.DirectionNames) if (string.Equals(d, name, StringComparison.OrdinalIgnoreCase)) return d;
            foreach (var c in GameApi.ConstantCategories) if (string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) return c;
            for (var s = scope; s != null; s = s.Parent)
                foreach (var k in s.Names.Keys) if (string.Equals(k, name, StringComparison.OrdinalIgnoreCase)) return k;
            return null;
        }

        void NotDefined(string name, Span at)
        {
            var dym = DidYouMean(name);
            Error(name + " is not defined" + (dym != null && dym != name ? " (did you mean " + dym + "?)" : ""), at);
        }

        IrSymbol ConstantBag(string name)
        {
            if (bags.TryGetValue(name, out var s)) return s;
            s = new IrSymbol(name, IrSymbolKind.Global, IrType.Any) { PyName = name, Tag = PyLowering.UndefinedTag, IsConst = true };
            bags[name] = s;
            return s;
        }

        IrExpr NameValue(JsIdent id)
        {
            var name = id.Name;
            if (Reserved(name)) { Error("identifiers starting with __j are reserved by JavaFarmer", id.Span); return Bad(id.Span); }
            var b = Lookup(name);
            if (b != null)
            {
                switch (b.Kind)
                {
                    case "namespace": Error(name + " is a module namespace: use " + name + ".name in JavaFarmer", id.Span); return Bad(id.Span);
                    case "farm": Error(name + " is the game API module: use " + name + ".name in JavaFarmer", id.Span); return Bad(id.Span);
                    case "fnexpr": Error("named function expressions are not supported in JavaFarmer: declare it with function " + name + "() { ... }", id.Span); return Bad(id.Span);
                    case "import":
                        if (b.Sym.Kind == IrSymbolKind.ExternalFunction) return new IrFuncRef { Function = b.Sym, NameSpan = id.Span, Span = id.Span };
                        return new IrName(b.Sym) { Span = id.Span, NameSpan = id.Span };
                }
                TdzCheck(b, id.Span);
                NoteUse(b, false, id.Span);
                if (b.Sym.Kind == IrSymbolKind.Function) return new IrFuncRef { Function = b.Sym, NameSpan = id.Span, Span = id.Span };
                return new IrName(b.Sym) { Span = id.Span, NameSpan = id.Span };
            }
            if (name == "undefined") return Lit(IrLiteral.Null(), id.Span);
            if (Array.IndexOf(GameApi.DirectionNames, name) >= 0) return new IrConst("Directions", name) { Span = id.Span };
            if (Array.IndexOf(GameApi.ConstantCategories, name) >= 0) return new IrName(ConstantBag(name)) { Span = id.Span, NameSpan = id.Span };
            var py = JsNames.BareBuiltin(name);
            if (py != null) return new IrFuncRef { Builtin = py, NameSpan = id.Span, Span = id.Span };
            if (name == "Number" || name == "parseInt" || name == "parseFloat") { Error(name + " is not supported in JavaFarmer (the game cannot convert text to numbers)", id.Span); return Bad(id.Span); }
            if (name == "Infinity" || name == "NaN") { Error(name + " is not supported in JavaFarmer (the game has no " + name + "; use a large number like 999999)", id.Span); return Bad(id.Span); }
            if (UnsupportedGlobals.Contains(name)) { Error(name + " is not supported in JavaFarmer", id.Span); return Bad(id.Span); }
            if (name == "Math" || name == "console" || name == "Object" || name == "Array" || name == "String" || name == "Map" || name == "Set")
            { Error(name + " cannot be used as a value in JavaFarmer", id.Span); return Bad(id.Span); }
            NotDefined(name, id.Span);
            return Bad(id.Span);
        }

        /// <summary>The symbol an assignment (or a for-of loop without declaration) writes.</summary>
        IrSymbol AssignTargetSymbol(JsIdent id)
        {
            var name = id.Name;
            var b = Lookup(name);
            if (b == null)
            {
                if (Reserved(name)) Error("identifiers starting with __j are reserved by JavaFarmer", id.Span);
                else NotDefined(name, id.Span);
                return new IrSymbol(name, IrSymbolKind.Local);
            }
            switch (b.Kind)
            {
                case "const": case "import": case "namespace": case "farm": case "fnexpr":
                    Error("Assignment to constant variable.", id.Span);
                    return b.Sym;
                case "function":
                    if (b.Sym.Kind == IrSymbolKind.Function) { Error("assigning to a function is not supported in JavaFarmer", id.Span); return b.Sym; }
                    break;
            }
            TdzCheck(b, id.Span);
            NoteUse(b, true, id.Span);
            return b.Sym;
        }

        IrExpr AssignTarget(JsExpr t)
        {
            switch (t)
            {
                case JsIdent id: return new IrName(AssignTargetSymbol(id)) { Span = id.Span, NameSpan = id.Span };
                case JsMember m:
                    {
                        if (IsNamespaceRef(m.Object)) { Error("Assignment to constant variable.", m.Span); return Bad(m.Span); }
                        if (m.Object is JsIdent ci && Array.IndexOf(GameApi.ConstantCategories, ci.Name) >= 0 && Lookup(ci.Name) == null)
                        { Error("game constants cannot be changed", m.Span); return Bad(m.Span); }
                        if (m.Name == "length" || m.Name == "size") { Error("changing ." + m.Name + " is not supported in JavaFarmer", m.Span); return Bad(m.Span); }
                        var recv = Expr(m.Object);
                        return new IrIndex { Target = recv, Index = Lit(IrLiteral.OfString(m.Name), m.NameSpan), Span = m.Span };
                    }
                case JsIndex ix:
                    return new IrIndex { Target = Expr(ix.Object), Index = Expr(ix.Index), Span = ix.Span };
                case JsArrayLit a:
                    {
                        var tl = new IrTupleLit { Span = a.Span };
                        foreach (var it in a.Items)
                        {
                            if (it == null) { Error("holes in destructuring are not supported in JavaFarmer", a.Span); continue; }
                            tl.Items.Add(AssignTarget(it));
                        }
                        return tl;
                    }
            }
            Error("Invalid left-hand side in assignment", t.Span);
            return Bad(t.Span);
        }

        static IrBinOp CompoundOp(string op)
        {
            switch (op)
            {
                case "+=": return IrBinOp.Add;
                case "-=": return IrBinOp.Sub;
                case "*=": return IrBinOp.Mul;
                case "/=": return IrBinOp.TrueDiv;
                case "%=": return IrBinOp.ModTrunc;
                default: return IrBinOp.Pow;
            }
        }

        // ================================================================== statement expressions
        void StmtExpr(JsExpr e, Span span, List<IrStmt> outList)
        {
            switch (e)
            {
                case JsAssign a:
                    {
                        if (a.Op == "=" && a.Target is JsMember lm && lm.Name == "length" && !IsNamespaceRef(lm.Object))
                        {
                            if (a.Value is JsNum zero && zero.Value == 0)
                            {
                                outList.Add(new IrExprStmt(new IrOpCall(IrOp.Clear, Expr(lm.Object)) { NameSpan = lm.NameSpan, Span = a.Span }) { Span = span });
                                return;
                            }
                            Error("changing .length is not supported in JavaFarmer (only list.length = 0 to empty a list)", a.Span);
                            return;
                        }
                        var value = a.Op == "=" && a.Target is JsArrayLit ? DestructureValue(a.Value) : Expr(a.Value);
                        var target = AssignTarget(a.Target);
                        var asg = new IrAssign { Target = target, Op = a.Op == "=" ? (IrBinOp?)null : CompoundOp(a.Op), Value = value, OpSpan = a.OpSpan, Span = span };
                        if (asg.Op == IrBinOp.Add) fixupCandidates.Add(asg);
                        outList.Add(asg);
                        return;
                    }
                case JsUpdate u:
                    {
                        var target = AssignTarget(u.Target);
                        var inc = new IrAssign
                        {
                            Target = target, Op = u.Op == "++" ? IrBinOp.Add : IrBinOp.Sub, Value = Lit(IrLiteral.OfInt(1), u.OpSpan),
                            OpSpan = u.OpSpan, IsIncDec = true, Prefix = u.Prefix, Span = span,
                        };
                        if (inc.Op == IrBinOp.Add) fixupCandidates.Add(inc);
                        outList.Add(inc);
                        return;
                    }
                case JsCall c:
                    {
                        var r = Call(c, outList, span);
                        if (r != null) outList.Add(new IrExprStmt(r) { Span = span });
                        return;
                    }
            }
            outList.Add(new IrExprStmt(Expr(e)) { Span = span });
        }

        // ================================================================== expressions
        IrExpr Expr(JsExpr e)
        {
            var r = ExprCore(e);
            if (r != null && !e.Paren.IsNone && r.ParenSpan.IsNone) r.ParenSpan = e.Paren;
            return r;
        }

        IrExpr StringLit(string value, Span s)
        {
            if (value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                Error("line breaks (\\n) inside strings are not supported in JavaFarmer", s);
            return Lit(IrLiteral.OfString(value), s);
        }

        IrExpr ExprCore(JsExpr e)
        {
            switch (e)
            {
                case JsNum n:
                    if (!n.IsFloat && n.Value == Math.Floor(n.Value) && Math.Abs(n.Value) <= 9007199254740992.0)
                        return Lit(IrLiteral.OfInt((long)n.Value), n.Span);
                    return Lit(IrLiteral.OfFloat(n.Value), n.Span);
                case JsStr s: return StringLit(s.Value, s.Span);
                case JsBool b: return Lit(IrLiteral.OfBool(b.Value), b.Span);
                case JsNull nl: return Lit(IrLiteral.Null(), nl.Span);
                case JsIdent id: return NameValue(id);
                case JsTemplate t: return Template(t);
                case JsArrayLit a: return ArrayLit(a);
                case JsObjectLit o: return ObjectLit(o);
                case JsFunctionExpr f: return LowerLambda(f);
                case JsSpread sp: Error("spread (...) is only supported as [...list] in JavaFarmer", sp.Span); return Bad(sp.Span);
                case JsUnary u:
                    {
                        var op = u.Op == "!" ? IrUnOp.Not : u.Op == "-" ? IrUnOp.Neg : IrUnOp.Plus;
                        return new IrUnary { Op = op, Operand = Expr(u.Operand), OpSpan = u.OpSpan, Span = u.Span };
                    }
                case JsUpdate up:
                    Error(up.Op + " can only be used as a separate statement in JavaFarmer", up.Span);
                    return Bad(up.Span);
                case JsAssign asg:
                    Error("using an assignment as a value is not supported in JavaFarmer", asg.Span);
                    return Bad(asg.Span);
                case JsBinary b: return Binary(b);
                case JsCond c:
                    return new IrConditional { Cond = Expr(c.Test), Then = Expr(c.Then), Else = Expr(c.Else), QSpan = c.QSpan, Span = c.Span };
                case JsCall call: return Call(call, null, call.Span);
                case JsNew nw: return New(nw);
                case JsMember m: return Member(m);
                case JsIndex ix:
                    {
                        // list.splice(i, 1)[0]: remove and return the i-th element
                        if (ix.Index is JsNum z && z.Value == 0 && ix.Object is JsCall sc && sc.Callee is JsMember sm && sm.Name == "splice"
                            && sc.Args.Count == 2 && sc.Args[1] is JsNum one && one.Value == 1 && !IsNamespaceRef(sm.Object))
                            return new IrOpCall(IrOp.ListPop, Expr(sm.Object), Expr(sc.Args[0])) { NameSpan = sm.NameSpan, Span = ix.Span };
                        if (ix.Object is JsIdent ci && Array.IndexOf(GameApi.ConstantCategories, ci.Name) >= 0 && Lookup(ci.Name) == null && ix.Index is JsStr cs)
                            return new IrConst(ci.Name, cs.Value) { Span = ix.Span };
                        var target = Expr(ix.Object);
                        return new IrIndex { Target = target, Index = Expr(ix.Index), Span = ix.Span };
                    }
            }
            Error("this expression is not supported in JavaFarmer", e.Span);
            return Bad(e.Span);
        }

        bool IsNamespaceRef(JsExpr e) => e is JsIdent id && Lookup(id.Name) is Binding b && (b.Kind == "namespace" || b.Kind == "farm");

        IrExpr Template(JsTemplate t)
        {
            if (t.Exprs.Count == 0) return StringLit(t.Parts[0], t.Span);
            // a lone substitution: String(x)
            if (t.Exprs.Count == 1 && t.Parts[0].Length == 0 && t.Parts[1].Length == 0)
                return new IrCast { Target = IrType.String, Operand = Expr(t.Exprs[0]), OpSpan = t.Span, Span = t.Span, Type = IrType.String };
            IrExpr acc = null;
            int accStart = t.Span.Start;
            void Add(IrExpr piece, Span opSpan)
            {
                if (acc == null) { acc = piece; return; }
                int end = Math.Max(opSpan.End, piece.Span.IsNone ? opSpan.End : piece.Span.End);
                acc = new IrBinary { Op = IrBinOp.Concat, Left = acc, Right = piece, OpSpan = opSpan, Span = new Span(accStart, end), Type = IrType.String };
            }
            for (int k = 0; k < t.Parts.Count; k++)
            {
                var ps = t.PartSpans[k];
                if (t.Parts[k].Length > 0) Add(StringLit(t.Parts[k], ps), ps);
                if (k < t.Exprs.Count)
                {
                    var ex = Expr(t.Exprs[k]);
                    var closeEnd = k + 1 < t.PartSpans.Count ? t.PartSpans[k + 1].Start : t.Span.End;
                    Add(ex, new Span(ps.End, Math.Max(ps.End + 2, closeEnd)));
                }
            }
            if (acc is IrBinary) acc.Span = t.Span;
            return acc;
        }

        IrExpr ArrayLit(JsArrayLit a)
        {
            if (a.Items.Count == 1 && a.Items[0] is JsSpread sp)
                return new IrOpCall(IrOp.ListCopy, Expr(sp.Arg)) { NameSpan = new Span(sp.Span.Start, sp.Span.Start + 3), Span = a.Span };
            if (a.Items.Any(x => x is JsSpread) && a.Items.All(x => x != null))
            {
                // [...a, x, ...b]: list concatenation a + [x] + b
                IrExpr acc = null;
                IrListLit run = null;
                void Push(IrExpr part, Span opSpan)
                {
                    acc = acc == null ? part : new IrBinary { Op = IrBinOp.Add, Left = acc, Right = part, OpSpan = opSpan, Span = a.Span };
                }
                foreach (var it in a.Items)
                {
                    if (it is JsSpread s2) { run = null; Push(Expr(s2.Arg), new Span(s2.Span.Start, s2.Span.Start + 3)); }
                    else
                    {
                        if (run == null) { run = new IrListLit { Span = it.Span }; Push(run, it.Span); }
                        run.Items.Add(Expr(it));
                        run.Span = Span.Cover(run.Span, it.Span);
                    }
                }
                return acc;
            }
            var l = new IrListLit { Span = a.Span };
            foreach (var it in a.Items)
            {
                if (it == null) { Error("holes in array literals are not supported in JavaFarmer", a.Span); continue; }
                if (it is JsSpread s2) { Error("spread (...) is only supported as [...list] in JavaFarmer", s2.Span); continue; }
                l.Items.Add(Expr(it));
            }
            return l;
        }

        IrExpr ObjectLit(JsObjectLit o)
        {
            var m = new IrMapLit { Span = o.Span };
            foreach (var p in o.Props)
            {
                m.Keys.Add(p.KeyExpr != null ? KeyExpr(p.KeyExpr) : StringLit(p.Key, p.KeySpan));
                m.Values.Add(Expr(p.Value));
            }
            MarkObject(m);
            return m;
        }

        static bool IsComparison(string op) => op == "<" || op == "<=" || op == ">" || op == ">=";

        IrExpr Binary(JsBinary b)
        {
            if (b.Op == "??")
            {
                if (b.Left is JsCall gc && gc.Callee is JsMember gm && gm.Name == "get" && gc.Args.Count == 1 && !IsNamespaceRef(gm.Object) && b.Left.Paren.IsNone)
                {
                    var recv = Expr(gm.Object);
                    return new IrOpCall(IrOp.MapGetOrDefault, recv, KeyExpr(gc.Args[0]), Expr(b.Right)) { NameSpan = gm.NameSpan, Span = b.Span };
                }
                Error("?? is only supported as map.get(key) ?? default in JavaFarmer", b.OpSpan);
                return Bad(b.Span);
            }
            if (IsComparison(b.Op) && b.Left is JsBinary lb && lb.Paren.IsNone && IsComparison(lb.Op))
            {
                Error("a " + lb.Op + " b " + b.Op + " c compares a boolean with c in JavaScript: write a " + lb.Op + " b && b " + b.Op + " c", b.OpSpan);
                return Bad(b.Span);
            }
            if (b.Op == "in")
                return new IrOpCall(IrOp.Contains, Expr(b.Right), KeyExpr(b.Left)) { NameSpan = b.OpSpan, Span = b.Span };
            IrBinOp op;
            switch (b.Op)
            {
                case "+": op = IrBinOp.Add; break;
                case "-": op = IrBinOp.Sub; break;
                case "*": op = IrBinOp.Mul; break;
                case "/": op = IrBinOp.TrueDiv; break;
                case "%": op = IrBinOp.ModTrunc; break;
                case "**": op = IrBinOp.Pow; break;
                case "===": case "==": op = IrBinOp.Eq; break;
                case "!==": case "!=": op = IrBinOp.Ne; break;
                case "<": op = IrBinOp.Lt; break;
                case "<=": op = IrBinOp.Le; break;
                case ">": op = IrBinOp.Gt; break;
                case ">=": op = IrBinOp.Ge; break;
                case "&&": op = IrBinOp.And; break;
                case "||": op = IrBinOp.Or; break;
                default: Error("the operator '" + b.Op + "' is not supported in JavaFarmer", b.OpSpan); return Bad(b.Span);
            }
            var left = Expr(b.Left);
            var right = Expr(b.Right);
            var bin = new IrBinary { Op = op, Left = left, Right = right, OpSpan = b.OpSpan, Span = b.Span };
            if (op == IrBinOp.Add) fixupCandidates.Add(bin);
            return bin;
        }

        IrExpr Member(JsMember m)
        {
            if (m.Object is JsIdent oi && Lookup(oi.Name) == null)
            {
                if (Array.IndexOf(GameApi.ConstantCategories, oi.Name) >= 0) return new IrConst(oi.Name, m.Name) { Span = m.Span };
                if (oi.Name == "Math")
                {
                    if (m.Name == "PI") return Lit(IrLiteral.OfFloat(Math.PI), m.Span);
                    if (m.Name == "E") return Lit(IrLiteral.OfFloat(Math.E), m.Span);
                    if (m.Name == "min" || m.Name == "max" || m.Name == "abs" || m.Name == "random")
                        return new IrFuncRef { Builtin = m.Name, NameSpan = m.Span, Span = m.Span };
                    Error("Math." + m.Name + " is not supported in JavaFarmer", m.Span);
                    return Bad(m.Span);
                }
                if (oi.Name == "console")
                {
                    if (m.Name == "log") return new IrFuncRef { Builtin = "print", NameSpan = m.Span, Span = m.Span };
                    Error("console." + m.Name + " is not supported in JavaFarmer (use console.log)", m.Span);
                    return Bad(m.Span);
                }
            }
            if (m.Object is JsIdent ni && Lookup(ni.Name) is Binding nb && (nb.Kind == "namespace" || nb.Kind == "farm"))
                return NamespaceMember(nb, ni, m.Name, m.NameSpan, m.Span, false, null);
            if (m.Name == "length" || m.Name == "size")
            {
                var len = new IrOpCall(IrOp.Len, Expr(m.Object)) { NameSpan = m.NameSpan, Span = m.Span };
                lenSource[len] = m.Name;
                return len;
            }
            return new IrIndex { Target = Expr(m.Object), Index = Lit(IrLiteral.OfString(m.Name), m.NameSpan), Span = m.Span };
        }

        /// <summary>Utils.x / Utils.f(...) of a namespace import, or farm.harvest() of the game API module.</summary>
        IrExpr NamespaceMember(Binding nb, JsIdent ni, string name, Span nameSpan, Span span, bool call, List<IrExpr> args)
        {
            if (nb.Kind == "farm")
            {
                if (Array.IndexOf(GameApi.DirectionNames, name) >= 0 && !call) return new IrConst("Directions", name) { Span = span };
                if (Array.IndexOf(GameApi.ConstantCategories, name) >= 0 && !call) return new IrName(ConstantBag(name)) { Span = span, NameSpan = span };
                var py = JsNames.BareBuiltin(name);
                if (py == null) { Error("The requested module './farm.js' does not provide an export named '" + name + "'", nameSpan); return Bad(span); }
                if (!call) return new IrFuncRef { Builtin = py, NameSpan = span, Span = span };
                return BuiltinCall(py, args, span, span);
            }
            if (nb.Info != null && !nb.Info.Exports.ContainsKey(name))
            {
                if (call) Error(ni.Name + "." + name + " is not a function (" + nb.Module + " does not export " + name + ")", span);
                else Error("'" + name + "' is not exported by " + nb.Module + " (add export to its declaration)", nameSpan);
                return Bad(span);
            }
            JsModuleInfo.Decl d = null;
            nb.Info?.Exports.TryGetValue(name, out d);
            if (call)
            {
                if (d != null && !d.IsFunction)
                {
                    var vc = new IrCall { Kind = IrCallKind.Value, Callee = new IrName(External(nb.Module, name, false, d.PyName)) { Span = span, QualifierSpan = ni.Span, NameSpan = nameSpan }, Span = span, NameSpan = nameSpan };
                    vc.Args.AddRange(args);
                    return vc;
                }
                if (d != null && (args.Count < d.MinArgs || args.Count > d.MaxArgs)) Error(ArityText(ni.Name + "." + name, d.MinArgs, d.MaxArgs, args.Count), span);
                var c = new IrCall { Kind = IrCallKind.Function, Function = External(nb.Module, name, true, d?.PyName), NameSpan = nameSpan, QualifierSpan = ni.Span, Span = span };
                c.Args.AddRange(args);
                return c;
            }
            if (d != null && d.IsFunction) return new IrFuncRef { Function = External(nb.Module, name, true, d.PyName), QualifierSpan = ni.Span, NameSpan = nameSpan, Span = span };
            return new IrName(External(nb.Module, name, false, d?.PyName)) { Span = span, QualifierSpan = ni.Span, NameSpan = nameSpan };
        }

        static string ArityText(string name, int min, int max, int got)
        {
            string want;
            if (min == max) want = min + " argument" + (min == 1 ? "" : "s");
            else if (max == int.MaxValue) want = "at least " + min + " argument" + (min == 1 ? "" : "s");
            else want = min + " to " + max + " arguments";
            return name + "() expects " + want + ", but got " + got;
        }

        readonly List<(IrCall Call, ApiBuiltin Api, string JsName)> builtinCalls = new List<(IrCall, ApiBuiltin, string)>();

        IrExpr BuiltinCall(string py, List<IrExpr> args, Span nameSpan, Span span)
        {
            var api = GameApi.Find(py);
            var jsName = JsNames.ApiName(py) ?? py;
            if (api != null && !spreadSeen && (args.Count < api.MinArgs || args.Count > api.MaxArgs)) Error(ArityText(jsName, api.MinArgs, api.MaxArgs, args.Count), span);
            var c = new IrCall { Kind = IrCallKind.Builtin, Builtin = py, NameSpan = nameSpan, Span = span };
            c.Args.AddRange(args);
            if (api != null) builtinCalls.Add((c, api, jsName));
            return c;
        }

        bool spreadSeen;

        List<IrExpr> Args(JsCall c)
        {
            var list = new List<IrExpr>();
            spreadSeen = false;
            foreach (var a in c.Args)
            {
                if (a is JsSpread sp) { Error("spread (...) in a call is not supported in JavaFarmer", sp.Span); spreadSeen = true; continue; }
                list.Add(Expr(a));
            }
            return list;
        }

        bool StmtOnly(string what, List<IrStmt> stmtCtx, Span at)
        {
            if (stmtCtx != null) return true;
            Error("'." + what + "()' can only be used as a separate statement in JavaFarmer", at);
            return false;
        }

        bool ArgCount(string name, int got, int min, int max, Span at)
        {
            if (got >= min && got <= max) return true;
            Error(ArityText("'." + name, min, max, got).Replace("() expects", "()' expects"), at);
            return false;
        }

        /// <summary>A call. stmtCtx != null: the call is a whole statement (it may add statements there and return null).</summary>
        IrExpr Call(JsCall c, List<IrStmt> stmtCtx, Span stmtSpan)
        {
            var span = c.Span;
            if (c.Callee is JsIdent id)
            {
                var b = Lookup(id.Name);
                if (b != null)
                {
                    if (b.Kind == "namespace" || b.Kind == "farm") { Error(id.Name + " is not a function", id.Span); return Bad(span); }
                    if (b.Kind == "fnexpr") { NameValue(id); return Bad(span); }
                    var args = Args(c);
                    if (b.Kind == "import")
                    {
                        JsModuleInfo.Decl d = null;
                        if (b.Info != null && b.Info.Exports.TryGetValue(b.Imported, out d) && d.IsFunction && (args.Count < d.MinArgs || args.Count > d.MaxArgs))
                            Error(ArityText(id.Name, d.MinArgs, d.MaxArgs, args.Count), span);
                        if (d == null || d.IsFunction)
                        {
                            var ec = new IrCall { Kind = IrCallKind.Function, Function = External(b.Module, b.Imported, true, d?.PyName), NameSpan = id.Span, Span = span };
                            ec.Args.AddRange(args);
                            return ec;
                        }
                        var evc = new IrCall { Kind = IrCallKind.Value, Callee = new IrName(b.Sym) { Span = id.Span, NameSpan = id.Span }, NameSpan = id.Span, Span = span };
                        evc.Args.AddRange(args);
                        return evc;
                    }
                    TdzCheck(b, id.Span);
                    NoteUse(b, false, id.Span);
                    if (b.Sym.Kind == IrSymbolKind.Function)
                    {
                        if (b.Decl != null)
                        {
                            int max = b.Decl.Params.Count, min = 0;
                            for (int k = 0; k < b.Decl.Params.Count; k++) if (b.Decl.Params[k].Default == null) min = k + 1;
                            if (!spreadSeen && (args.Count < min || args.Count > max)) Error(ArityText(id.Name, min, max, args.Count), span);
                        }
                        var fc = new IrCall { Kind = IrCallKind.Function, Function = b.Sym, NameSpan = id.Span, Span = span };
                        fc.Args.AddRange(args);
                        return fc;
                    }
                    var vc = new IrCall { Kind = IrCallKind.Value, Callee = new IrName(b.Sym) { Span = id.Span, NameSpan = id.Span }, NameSpan = id.Span, Span = span };
                    vc.Args.AddRange(args);
                    return vc;
                }
                if (Reserved(id.Name)) { Error("identifiers starting with __j are reserved by JavaFarmer", id.Span); return Bad(span); }
                switch (id.Name)
                {
                    case "String":
                        {
                            var args = Args(c);
                            if (args.Count != 1) { Error(ArityText("String", 1, 1, args.Count), span); return Bad(span); }
                            return new IrCast { Target = IrType.String, Operand = args[0], OpSpan = id.Span, Span = span, Type = IrType.String };
                        }
                    case "Array":
                        Error("Array(n) is only supported as Array(n).fill(value) in JavaFarmer", span);
                        return Bad(span);
                }
                var py = JsNames.BareBuiltin(id.Name);
                if (py != null) return BuiltinCall(py, Args(c), id.Span, span);
                if (Array.IndexOf(GameApi.DirectionNames, id.Name) >= 0 || Array.IndexOf(GameApi.ConstantCategories, id.Name) >= 0)
                { Error(id.Name + " is not a function", id.Span); return Bad(span); }
                NameValue(id);   // reports "is not defined" / "is not supported"
                return Bad(span);
            }
            if (c.Callee is JsMember m)
            {
                if (m.Object is JsIdent oi && Lookup(oi.Name) == null)
                {
                    bool reported;
                    var r = GlobalObjectCall(oi, m, c, out reported);
                    if (r != null) return r;
                    if (reported) return Bad(span);
                }
                if (m.Object is JsIdent ni && Lookup(ni.Name) is Binding nb && (nb.Kind == "namespace" || nb.Kind == "farm"))
                    return NamespaceMember(nb, ni, m.Name, m.NameSpan, span, true, Args(c));
                return MethodCall(m, c, stmtCtx, stmtSpan);
            }
            // (expr)(...), f()(...), list[0](...)
            var callee = Expr(c.Callee);
            var call = new IrCall { Kind = IrCallKind.Value, Callee = callee, Span = span };
            call.Args.AddRange(Args(c));
            return call;
        }

        /// <summary>console.log, Math.f, Object.keys, Array.from, Entities.X(...). Null: not one of those (reported: an error was raised).</summary>
        IrExpr GlobalObjectCall(JsIdent oi, JsMember m, JsCall c, out bool reported)
        {
            reported = false;
            var span = c.Span;
            var nameSpan = m.Span;
            switch (oi.Name)
            {
                case "console":
                    if (m.Name == "log") return BuiltinCall("print", Args(c), nameSpan, span);
                    Error("console." + m.Name + " is not supported in JavaFarmer (use console.log)", nameSpan);
                    reported = true;
                    return null;
                case "Math":
                    {
                        var args = Args(c);
                        switch (m.Name)
                        {
                            case "min": case "max": case "abs": case "random": return BuiltinCall(m.Name, args, nameSpan, span);
                            case "floor": case "ceil": case "sqrt": case "round":
                                {
                                    if (args.Count != 1) { Error(ArityText("Math." + m.Name, 1, 1, args.Count), span); return Bad(span); }
                                    var op = m.Name == "floor" ? IrOp.Floor : m.Name == "ceil" ? IrOp.Ceil : m.Name == "sqrt" ? IrOp.Sqrt : IrOp.Round;
                                    return new IrOpCall(op, args[0]) { NameSpan = nameSpan, Span = span };
                                }
                            case "trunc":
                                if (args.Count != 1) { Error(ArityText("Math.trunc", 1, 1, args.Count), span); return Bad(span); }
                                return new IrCast { Target = IrType.Int, Operand = args[0], OpSpan = nameSpan, Span = span, Type = IrType.Int };
                            case "pow":
                                if (args.Count != 2) { Error(ArityText("Math.pow", 2, 2, args.Count), span); return Bad(span); }
                                return new IrBinary { Op = IrBinOp.Pow, Left = args[0], Right = args[1], OpSpan = nameSpan, Span = span };
                        }
                        Error("Math." + m.Name + " is not supported in JavaFarmer", nameSpan);
                        reported = true;
                        return null;
                    }
                case "Object":
                    if (m.Name == "keys" || m.Name == "values")
                    {
                        var args = Args(c);
                        if (args.Count != 1) { Error(ArityText("Object." + m.Name, 1, 1, args.Count), span); return Bad(span); }
                        return new IrOpCall(m.Name == "keys" ? IrOp.MapKeys : IrOp.MapValues, args[0]) { NameSpan = nameSpan, Span = span };
                    }
                    if (m.Name == "entries") Error("Object.entries() is only supported in for (const [key, value] of Object.entries(obj)) in JavaFarmer", span);
                    else Error("Object." + m.Name + " is not supported in JavaFarmer", nameSpan);
                    reported = true;
                    return null;
                case "Array":
                    if (m.Name == "from") return ArrayFrom(c);
                    Error("Array." + m.Name + " is not supported in JavaFarmer", nameSpan);
                    reported = true;
                    return null;
                case "Number": case "JSON": case "String": case "Date": case "Promise": case "Reflect":
                    Error(oi.Name + "." + m.Name + " is not supported in JavaFarmer", nameSpan);
                    reported = true;
                    return null;
            }
            if (Array.IndexOf(GameApi.ConstantCategories, oi.Name) >= 0)
            {
                Error(oi.Name + "." + m.Name + " is not a function", nameSpan);
                reported = true;
            }
            return null;
        }

        IrExpr ArrayFrom(JsCall c)
        {
            var span = c.Span;
            var ns = ((JsMember)c.Callee).Span;
            if (c.Args.Count == 1 && !(c.Args[0] is JsObjectLit))
                return new IrOpCall(IrOp.ListCopy, Expr(c.Args[0])) { NameSpan = ns, Span = span };
            if (c.Args.Count == 2 && c.Args[0] is JsObjectLit o && o.Props.Count == 1 && o.Props[0].Key == "length" && o.Props[0].KeyExpr == null
                && c.Args[1] is JsFunctionExpr f && f.Params.Count == 0 && f.ExprBody != null)
            {
                var body = f.ExprBody;
                if (body is JsCall fc && IsFill(fc, out var size, out var fillValue))
                {
                    var n = Expr(o.Props[0].Value);
                    return new IrOpCall(IrOp.ArrayNew2, n, Expr(size), Expr(fillValue)) { NameSpan = ns, Span = span };
                }
                if (body is JsNum || body is JsStr || body is JsBool || body is JsNull || (body is JsUnary bu && bu.Operand is JsNum)
                    || (body is JsIdent bi && Array.IndexOf(GameApi.DirectionNames, bi.Name) >= 0)
                    || (body is JsMember bm && bm.Object is JsIdent bo && Array.IndexOf(GameApi.ConstantCategories, bo.Name) >= 0))
                {
                    var n = Expr(o.Props[0].Value);
                    return new IrOpCall(IrOp.ArrayNew, n, Expr(body)) { NameSpan = ns, Span = span };
                }
            }
            Error("Array.from(...) is only supported as Array.from(list) or Array.from({ length: n }, () => new Array(m).fill(value)) in JavaFarmer", span);
            return Bad(span);
        }

        /// <summary>new Array(n).fill(d) / Array(n).fill(d).</summary>
        bool IsFill(JsCall c, out JsExpr size, out JsExpr value)
        {
            size = value = null;
            if (!(c.Callee is JsMember m) || m.Name != "fill" || c.Args.Count != 1) return false;
            if (m.Object is JsNew n && n.Callee is JsIdent ni && ni.Name == "Array" && Lookup("Array") == null && n.Args.Count == 1) { size = n.Args[0]; value = c.Args[0]; return true; }
            if (m.Object is JsCall ac && ac.Callee is JsIdent ai && ai.Name == "Array" && Lookup("Array") == null && ac.Args.Count == 1) { size = ac.Args[0]; value = c.Args[0]; return true; }
            return false;
        }

        IrExpr MethodCall(JsMember m, JsCall c, List<IrStmt> stmtCtx, Span stmtSpan)
        {
            var span = c.Span;
            var name = m.Name;
            var ns = m.NameSpan;
            if (IsFill(c, out var size, out var fillValue))
                return new IrOpCall(IrOp.ArrayNew, Expr(size), Expr(fillValue)) { NameSpan = ns, Span = span };
            if (name == "fill") { Error("'.fill()' is only supported as new Array(n).fill(value) in JavaFarmer", ns); return Bad(span); }
            if (name == "entries") { Error("'.entries()' is only supported in for (const [key, value] of map.entries()) in JavaFarmer", ns); return Bad(span); }
            if (name == "at") { Error("'.at()' is not supported in JavaFarmer: use a[i] (a[a.length - 1] for the last item)", ns); return Bad(span); }
            if (name == "toFixed" || name == "toPrecision") { Error("'." + name + "()' is not supported in JavaFarmer (the game has no number formatting)", ns); return Bad(span); }
            if (StringMethods.Contains(name)) { Error("'." + name + "()' is not supported in JavaFarmer (the game has no string methods)", ns); return Bad(span); }
            if (HigherOrder.Contains(name))
            {
                var why = name == "sort" || name == "toSorted" ? "sort the list with your own loop" : name == "join" ? "build the text with a loop and +" : "use a for...of loop";
                Error("'." + name + "()' is not supported in JavaFarmer (" + why + ")", ns);
                return Bad(span);
            }
            switch (name)
            {
                case "push": case "pop": case "shift": case "unshift": case "splice": case "indexOf": case "includes": case "has":
                case "get": case "set": case "add": case "delete": case "clear": case "keys": case "values": case "slice": case "charAt":
                case "toString":
                    break;
                default:
                    Error("'." + name + "()' is not supported in JavaFarmer", ns);
                    return Bad(span);
            }
            if (stmtCtx == null && (name == "set" || name == "add"))
            {
                bool chained = m.Object is JsCall prev && prev.Callee is JsMember pm && pm.Name == name;
                Error(chained ? "chained ." + name + "() calls are not supported in JavaFarmer: write one ." + name + "() per statement" : "'." + name + "()' can only be used as a separate statement in JavaFarmer", span);
                return Bad(span);
            }
            var recv = Expr(m.Object);
            var keyed = name == "get" || name == "set" || name == "has" || name == "delete" || name == "add";
            var args = new List<IrExpr>();
            for (int k = 0; k < c.Args.Count; k++)
            {
                if (c.Args[k] is JsSpread sp) { Error("spread (...) in a call is not supported in JavaFarmer", sp.Span); continue; }
                args.Add(keyed && k == 0 ? KeyExpr(c.Args[k]) : Expr(c.Args[k]));
            }
            int n = args.Count;
            IrOpCall Op(IrOp op, params IrExpr[] a) => new IrOpCall(op, new[] { recv }.Concat(a).ToArray()) { NameSpan = ns, Span = span };
            switch (name)
            {
                case "push":
                    if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                    if (!ArgCount(name, n, 1, int.MaxValue, span)) return null;
                    for (int k = 0; k < n; k++)
                    {
                        var r = k == 0 ? recv : IrClone.Expr(recv);
                        var op = new IrOpCall(IrOp.ListAppend, r, args[k]) { NameSpan = ns, Span = n == 1 ? span : args[k].Span };
                        stmtCtx.Add(new IrExprStmt(op) { Span = n == 1 ? stmtSpan : args[k].Span });
                    }
                    return null;
                case "pop":
                    ArgCount(name, n, 0, 0, span);
                    return Op(IrOp.ListPop);
                case "shift":
                    ArgCount(name, n, 0, 0, span);
                    return Op(IrOp.ListPop, IrLiteral.OfInt(0));
                case "unshift":
                    if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                    return ArgCount(name, n, 1, 1, span) ? Op(IrOp.ListInsert, IrLiteral.OfInt(0), args[0]) : Bad(span);
                case "splice":
                    if (n == 2 && c.Args[1] is JsNum one && one.Value == 1)
                    {
                        if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                        return Op(IrOp.ListPop, args[0]);
                    }
                    if (n == 3 && c.Args[1] is JsNum zero && zero.Value == 0)
                    {
                        if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                        return Op(IrOp.ListInsert, args[0], args[2]);
                    }
                    Error("'.splice()' is only supported as list.splice(i, 1), list.splice(i, 1)[0] and list.splice(i, 0, x) in JavaFarmer", span);
                    return Bad(span);
                case "indexOf":
                    return ArgCount(name, n, 1, 1, span) ? Op(IrOp.ListIndexOf, args[0]) : Bad(span);
                case "includes":
                case "has":
                    return ArgCount(name, n, 1, 1, span) ? Op(IrOp.Contains, args[0]) : Bad(span);
                case "get":
                    return ArgCount(name, n, 1, 1, span) ? Op(IrOp.MapGetOrNull, args[0]) : Bad(span);
                case "set":
                    return ArgCount(name, n, 2, 2, span) ? Op(IrOp.MapPut, args[0], args[1]) : Bad(span);
                case "add":
                    return ArgCount(name, n, 1, 1, span) ? Op(IrOp.SetAdd, args[0]) : Bad(span);
                case "delete":
                    if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                    if (!ArgCount(name, n, 1, 1, span)) return Bad(span);
                    {
                        var del = Op(IrOp.MapRemoveIfPresent, args[0]);
                        fixupCandidates.Add(del);
                        return del;
                    }
                case "clear":
                    if (!StmtOnly(name, stmtCtx, span)) return Bad(span);
                    ArgCount(name, n, 0, 0, span);
                    return Op(IrOp.Clear);
                case "keys":
                    ArgCount(name, n, 0, 0, span);
                    return Op(IrOp.MapKeys);
                case "values":
                    ArgCount(name, n, 0, 0, span);
                    return Op(IrOp.MapValues);
                case "slice":
                    if (n != 0) { Error("'.slice()' with arguments is not supported in JavaFarmer (the game has no slicing): use slice() to copy a list", span); return Bad(span); }
                    return Op(IrOp.ListCopy);
                case "charAt":
                    return ArgCount(name, n, 1, 1, span) ? new IrIndex { Target = recv, Index = args[0], Span = span } : Bad(span);
                case "toString":
                    ArgCount(name, n, 0, 0, span);
                    return new IrCast { Target = IrType.String, Operand = recv, OpSpan = ns, Span = span, Type = IrType.String };
            }
            return Bad(span);
        }

        IrExpr New(JsNew n)
        {
            var span = n.Span;
            if (n.Callee is JsIdent ci && Lookup(ci.Name) == null)
            {
                foreach (var a in n.Args) if (a is JsSpread sp) { Error("spread (...) in a call is not supported in JavaFarmer", sp.Span); return Bad(span); }
                switch (ci.Name)
                {
                    case "Map":
                        if (n.Args.Count == 0) return new IrMapLit { Span = span };
                        if (n.Args.Count == 1 && n.Args[0] is JsArrayLit pairs && pairs.Items.All(x => x is JsArrayLit p && p.Items.Count == 2 && p.Items.All(y => y != null && !(y is JsSpread))))
                        {
                            var ml = new IrMapLit { Span = span };
                            foreach (var x in pairs.Items) { var p = (JsArrayLit)x; ml.Keys.Add(KeyExpr(p.Items[0])); ml.Values.Add(Expr(p.Items[1])); }
                            return ml;
                        }
                        if (n.Args.Count == 1) return new IrOpCall(IrOp.MapCopy, Expr(n.Args[0])) { NameSpan = n.KwSpan, Span = span };
                        break;
                    case "Set":
                        if (n.Args.Count == 0) return new IrSetLit { Span = span };
                        if (n.Args.Count == 1 && n.Args[0] is JsArrayLit items && items.Items.All(x => x != null && !(x is JsSpread)))
                        {
                            var sl = new IrSetLit { Span = span };
                            foreach (var x in items.Items) sl.Items.Add(KeyExpr(x));
                            return sl;
                        }
                        if (n.Args.Count == 1) return new IrOpCall(IrOp.SetCopy, Expr(n.Args[0])) { NameSpan = n.KwSpan, Span = span };
                        break;
                    case "Array":
                        if (n.Args.Count == 0) return new IrListLit { Span = span };
                        Error("new Array(n) is only supported as new Array(n).fill(value) in JavaFarmer", span);
                        return Bad(span);
                }
                Error("new " + ci.Name + "() is not supported in JavaFarmer (only new Map(), new Set() and new Array(n).fill(value))", span);
                return Bad(span);
            }
            Error("new is only supported for Map, Set and Array in JavaFarmer", span);
            return Bad(span);
        }

        /// <summary>A Map key / Set item: an array literal there is a game tuple (compared by value, like a Position).</summary>
        IrExpr KeyExpr(JsExpr e)
        {
            if (e is JsArrayLit a && a.Paren.IsNone && a.Items.Count >= 2 && a.Items.All(x => x != null && !(x is JsSpread)))
            {
                var t = new IrTupleLit { Span = a.Span };
                foreach (var x in a.Items) t.Items.Add(Expr(x));
                return t;
            }
            return Expr(e);
        }
    }
}
