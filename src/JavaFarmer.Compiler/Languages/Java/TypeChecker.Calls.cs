using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed partial class Checker
    {
        string ArgTypesText(List<Expr> args)
        {
            if (args.Count == 0) return "no arguments";
            return string.Join(",", args.Select(a => (a.Type ?? JType.Error).ToString()));
        }

        void CheckArgsLoose(CallExpr c)
        {
            foreach (var a in c.Args)
                if (a.Type == null) CheckExpr(a, a is LambdaExpr || a is MethodRefExpr ? InferFunctional : null);
        }

        /// <summary>Checks arguments against a fixed parameter list (javac-style messages).</summary>
        bool CheckArgsAgainst(CallExpr c, IList<JType> ps, string what)
        {
            if (c.Args.Count != ps.Count)
            {
                CheckArgsLoose(c);
                Error(what + " cannot be applied to given types; required: " + (ps.Count == 0 ? "no arguments" : string.Join(",", ps)) + "; found: " + ArgTypesText(c.Args), c.NameStart, c.End);
                return false;
            }
            bool ok = true;
            for (int i = 0; i < ps.Count; i++)
            {
                var t = CheckExpr(c.Args[i], ps[i]);
                if (t.Kind == TK.Void) { Error("'void' type not allowed here", c.Args[i]); ok = false; }
                else if (!JType.Assignable(t, ps[i])) { Error(IncompatibleMsg(t, ps[i]), c.Args[i]); ok = false; }
            }
            return ok;
        }

        JType CheckCall(CallExpr c, bool asStatement, JType expected)
        {
            c.ValueUsed = !asStatement;
            var t = CheckCallCore(c, expected) ?? JType.Error;
            c.Type = t;
            if (c.ValueUsed && c.Member != null && StatementOnlyMembers.Contains(c.Member))
                Error(c.Name + "(...) can only be used as a separate statement in JavaFarmer", c);
            if (c.ValueUsed && t.Kind == TK.Void && !asStatement)
                Error("'void' type not allowed here", c);
            return t;
        }

        JType CheckCallCore(CallExpr c, JType expected)
        {
            if (c.Target == null)
            {
                if (Own.Methods.TryGetValue(c.Name, out var ms)) return UserCall(c, ms, false);
                var overloads = GameApi.Overloads(c.Name);
                if (overloads.Count > 0) return GameCall(c, overloads);
                CheckArgsLoose(c);
                var lower = Own.Methods.Keys.Concat(GameApi.AllDefs.Select(d => d.JavaName)).FirstOrDefault(n => string.Equals(n, c.Name, System.StringComparison.OrdinalIgnoreCase));
                Error("cannot find symbol: method " + c.Name + "(" + (c.Args.Count == 0 ? "" : ArgTypesText(c.Args)) + ")" + (lower != null ? " (did you mean " + lower + "?)" : ""), c.NameStart, c.NameEnd);
                return JType.Error;
            }
            // System.out.println / print
            if (c.Target is FieldAccessExpr sys && sys.Name == "out" && sys.Target is NameExpr sn && sn.Name == "System" && !IsVariableName("System"))
            {
                sys.IsSystemOut = true; sys.Type = JType.Error; sn.Kind = NameKind.BuiltinClass; sn.Type = JType.Error;
                if (c.Name == "println" || c.Name == "print")
                {
                    c.Kind = CallKind.Print;
                    foreach (var a in c.Args)
                    {
                        var at = CheckExpr(a);
                        if (at.Kind == TK.Void) Error("'void' type not allowed here", a);
                    }
                    if (c.Args.Count > 1 || (c.Name == "print" && c.Args.Count == 0))
                        Error("no suitable method found for " + c.Name + "(" + ArgTypesText(c.Args) + ")", c.NameStart, c.End);
                    return JType.Void;
                }
                CheckArgsLoose(c);
                Error("System.out." + c.Name + " is not supported in JavaFarmer (use System.out.println)", c.NameStart, c.NameEnd);
                return JType.Error;
            }
            if (c.Target is ThisExpr)
            {
                c.Target.Type = JType.Error;
                if (cu.IsClass) { CheckArgsLoose(c); Error("non-static variable this cannot be referenced from a static context", c.Target); return JType.Error; }
                if (Own.Methods.TryGetValue(c.Name, out var ms)) return UserCall(c, ms, false);
                CheckArgsLoose(c);
                Error("cannot find symbol: method " + c.Name, c.NameStart, c.NameEnd);
                return JType.Error;
            }
            var q = QualifierKind(c.Target);
            if (q != null)
            {
                switch (q)
                {
                    case "own":
                        if (Own.Methods.TryGetValue(c.Name, out var ms)) return UserCall(c, ms, false);
                        break;
                    case "other":
                        {
                            var name = ((NameExpr)c.Target).Name;
                            NoteModuleUse(name, c.Target.Start, c.Target.End);
                            var other = GetOther(name);
                            if (other.LoadError != null) { CheckArgsLoose(c); Error(other.LoadError, c.Target.Start, c.Target.End); return JType.Error; }
                            if (other.Methods.TryGetValue(c.Name, out var oms))
                            {
                                if (oms.IsPrivate) Error(c.Name + "(" + string.Join(",", oms.ParamTypes) + ") has private access in " + name, c.NameStart, c.NameEnd);
                                return UserCall(c, oms, true);
                            }
                            CheckArgsLoose(c);
                            Error("cannot find symbol: method " + c.Name + " in " + name, c.NameStart, c.NameEnd);
                            return JType.Error;
                        }
                    case "Math": return MathCall(c);
                    case "String":
                        if (c.Name == "valueOf" && c.Args.Count == 1)
                        {
                            c.Kind = CallKind.StringValueOf;
                            var at = CheckExpr(c.Args[0]);
                            if (at.Kind == TK.Void) Error("'void' type not allowed here", c.Args[0]);
                            return JType.String;
                        }
                        break;
                    case "List": case "Set":
                        if (c.Name == "of") return CollectionOf(c, q, expected);
                        break;
                    case "Map":
                        if (c.Name == "of") return MapOf(c, expected);
                        break;
                    case "Integer": case "Double": case "Boolean":
                        if (c.Name == "valueOf" && c.Args.Count == 1)
                        {
                            var target = q == "Integer" ? JType.Int : q == "Double" ? JType.Double : JType.Boolean;
                            var at = CheckExpr(c.Args[0], target);
                            if (at.Kind == TK.String) { Error(q + ".valueOf(String) is not supported in JavaFarmer", c); return JType.Error; }
                            RequireAssignable(at, target, c.Args[0]);
                            c.Kind = CallKind.Member; c.Member = "box";
                            return target.BoxedVersion;
                        }
                        break;
                }
                CheckArgsLoose(c);
                Error("cannot find symbol: method " + c.Name + " in " + q, c.NameStart, c.NameEnd);
                return JType.Error;
            }
            var tt = CheckExpr(c.Target);
            if (tt.IsError) { CheckArgsLoose(c); return JType.Error; }
            return MemberCall(c, tt);
        }

        JType UserCall(CallExpr c, MethodSymbol ms, bool other)
        {
            c.Kind = other ? CallKind.OtherMethod : CallKind.UserMethod;
            c.Method = ms;
            CheckArgsAgainst(c, ms.ParamTypes, "method " + ms.Name + " in class " + ms.FileName);
            return ms.ReturnType;
        }

        // ------------------------------------------------------------------ game builtins
        JType ApiType(string s)
        {
            switch (s)
            {
                case "boolean": return JType.Boolean;
                case "void": return JType.Void;
                case "int": return JType.Int;
                case "double": return JType.Double;
                case "String": return JType.String;
                case "Object": return JType.Object;
                case "Runnable": return JType.Runnable;
                case "Map<Item,Double>": return JType.MapOf(JType.GameType("Item"), JType.DoubleBox);
                case "Map<String,Object>": return JType.MapOf(JType.String, JType.Object);
            }
            if (JType.GameTypeNames.Contains(s)) return JType.GameType(s);
            return JType.Object;
        }

        JType GameCall(CallExpr c, List<ApiFunctionDef> overloads)
        {
            c.Kind = CallKind.GameApi;
            var byArity = overloads.Where(o => o.ParamTypes.Length == c.Args.Count || (o.ParamTypes.Length == 1 && o.ParamTypes[0].EndsWith("...") && c.Args.Count >= 1)).ToList();
            if (byArity.Count == 0)
            {
                CheckArgsLoose(c);
                if (overloads.Count == 1)
                {
                    var o = overloads[0];
                    Error("method " + c.Name + " in class " + ClassName + " cannot be applied to given types; required: " +
                          (o.ParamTypes.Length == 0 ? "no arguments" : string.Join(",", o.ParamTypes)) + "; found: " + ArgTypesText(c.Args), c.NameStart, c.End);
                }
                else Error("no suitable method found for " + c.Name + "(" + ArgTypesText(c.Args) + ")", c.NameStart, c.End);
                return ApiType(overloads[0].ReturnType);
            }
            var def = byArity[0];
            c.Api = def;
            if (c.Name == "spawnDrone")
            {
                var a = c.Args[0];
                var at = CheckExpr(a, InferFunctional);
                if (!at.IsError && at.Kind != TK.Runnable && at.Kind != TK.Supplier)
                {
                    if (at.Kind == TK.String) Error("spawnDrone(String) is not supported: the game only accepts a function; pass a lambda like () -> work() or a method reference like " + (cu.IsClass ? ClassName : "this") + "::work", a);
                    else Error("incompatible types: " + at + " cannot be converted to Runnable", a);
                }
                return JType.GameType("Drone");
            }
            for (int i = 0; i < c.Args.Count; i++)
            {
                var spec = def.ParamTypes[System.Math.Min(i, def.ParamTypes.Length - 1)];
                var a = c.Args[i];
                if (spec == "Object...")
                {
                    var t = CheckExpr(a);
                    if (t.Kind == TK.Void) Error("'void' type not allowed here", a);
                    continue;
                }
                if (spec.Contains("|"))
                {
                    var t = CheckExpr(a);
                    var options = spec.Split('|');
                    if (!t.IsError && !(t.Kind == TK.Game && options.Contains(t.Game)) && !(t.Kind == TK.Null))
                        Error("incompatible types: " + t + " cannot be converted to " + string.Join(" or ", options), a);
                    continue;
                }
                var pt = def.IrParamTypes != null && i < def.IrParamTypes.Length ? FromIr(def.IrParamTypes[i]) : ApiType(spec);
                var at = CheckExpr(a, pt);
                if (at.Kind == TK.Void) Error("'void' type not allowed here", a);
                else if (!JType.Assignable(at, pt)) Error(IncompatibleMsg(at, pt), a);
            }
            return def.IrReturnType != null ? FromIr(def.IrReturnType) : ApiType(def.ReturnType);
        }

        /// <summary>Neutral IR type → Java type (registered extension builtins).</summary>
        internal static JType FromIr(IrType t)
        {
            switch (t.Kind)
            {
                case IrTypeKind.Int: return JType.Int;
                case IrTypeKind.Float: return JType.Double;
                case IrTypeKind.Bool: return JType.Boolean;
                case IrTypeKind.String: return JType.String;
                case IrTypeKind.Void: return JType.Void;
                case IrTypeKind.List: return JType.ListOf(FromIr(t.Elem).BoxedVersion);
                case IrTypeKind.Array: return JType.ArrayOf(FromIr(t.Elem));
                case IrTypeKind.Set: return JType.SetOf(FromIr(t.Elem).BoxedVersion);
                case IrTypeKind.Map: return JType.MapOf(FromIr(t.Key).BoxedVersion, FromIr(t.Elem).BoxedVersion);
                case IrTypeKind.Game: return JType.GameType(t.GameName);
                case IrTypeKind.Func: return t.Elem == null || t.Elem.Kind == IrTypeKind.Void ? JType.Runnable : JType.SupplierOf(FromIr(t.Elem).BoxedVersion);
            }
            return JType.Object;
        }

        JType MathCall(CallExpr c)
        {
            c.Kind = CallKind.MathFn;
            List<JType> NumArgs(int n)
            {
                var ts = c.Args.Select(a => CheckExpr(a)).ToList();
                if (c.Args.Count != n)
                {
                    Error("method " + c.Name + " in class Math cannot be applied to given types; required: " + (n == 0 ? "no arguments" : string.Join(",", Enumerable.Repeat("double", n))) + "; found: " + ArgTypesText(c.Args), c.NameStart, c.End);
                    return null;
                }
                for (int i = 0; i < ts.Count; i++)
                    if (!ts[i].IsError && !ts[i].IsNumeric) { Error("incompatible types: " + ts[i] + " cannot be converted to double", c.Args[i]); return null; }
                return ts;
            }
            switch (c.Name)
            {
                case "min": case "max":
                    {
                        var ts = NumArgs(2);
                        if (ts == null) return JType.Error;
                        return ts.All(t => t.Kind == TK.Int) ? JType.Int : JType.Double;
                    }
                case "abs":
                    {
                        var ts = NumArgs(1);
                        if (ts == null) return JType.Error;
                        return ts[0].IsError ? JType.Error : ts[0].Unboxed;
                    }
                case "random": return NumArgs(0) == null ? JType.Error : JType.Double;
                case "floor": case "ceil": case "sqrt": return NumArgs(1) == null ? JType.Error : JType.Double;
                case "round": return NumArgs(1) == null ? JType.Error : JType.Int;
                case "pow": return NumArgs(2) == null ? JType.Error : JType.Double;
                case "floorDiv": case "floorMod":
                    {
                        var ts = NumArgs(2);
                        if (ts == null) return JType.Error;
                        for (int i = 0; i < ts.Count; i++)
                            if (!ts[i].IsError && ts[i].Kind != TK.Int) { Error("incompatible types: possible lossy conversion from double to int", c.Args[i]); return JType.Error; }
                        return JType.Int;
                    }
            }
            CheckArgsLoose(c);
            Error("Math." + c.Name + " is not supported in JavaFarmer", c.NameStart, c.NameEnd);
            return JType.Error;
        }

        JType CollectionOf(CallExpr c, string q, JType expected)
        {
            c.Kind = q == "List" ? CallKind.ListOf : CallKind.SetOf;
            JType elem = null;
            if (expected != null && (expected.Kind == TK.List || expected.Kind == TK.Set || expected.Kind == TK.Collection)) elem = expected.Elem;
            var ts = c.Args.Select(a => CheckExpr(a, elem)).ToList();
            if (elem == null)
            {
                elem = ts.Count == 0 ? JType.Object : ts[0].BoxedVersion;
                if (ts.Any(t => !t.IsError && !JType.Assignable(t, elem))) elem = ts.All(t => t.IsNumeric) ? JType.DoubleBox : JType.Object;
            }
            for (int i = 0; i < ts.Count; i++)
            {
                if (ts[i].Kind == TK.Null) { Error("null elements are not allowed in " + q + ".of", c.Args[i]); continue; }
                if (!JType.Assignable(ts[i], elem)) Error(IncompatibleMsg(ts[i], elem), c.Args[i]);
            }
            return q == "List" ? JType.ListOf(elem) : JType.SetOf(elem);
        }

        JType MapOf(CallExpr c, JType expected)
        {
            c.Kind = CallKind.MapOf;
            JType k = null, v = null;
            if (expected != null && expected.Kind == TK.Map) { k = expected.Key; v = expected.Elem; }
            if (c.Args.Count % 2 != 0 || c.Args.Count > 20)
            {
                CheckArgsLoose(c);
                Error("no suitable method found for of(" + ArgTypesText(c.Args) + ")", c.NameStart, c.End);
                return JType.Error;
            }
            var ts = new List<JType>();
            for (int i = 0; i < c.Args.Count; i++) ts.Add(CheckExpr(c.Args[i], i % 2 == 0 ? k : v));
            if (k == null) k = ts.Count == 0 ? JType.Object : ts[0].BoxedVersion;
            if (v == null) v = ts.Count < 2 ? JType.Object : ts[1].BoxedVersion;
            for (int i = 0; i < ts.Count; i++)
            {
                var want = i % 2 == 0 ? k : v;
                if (!JType.Assignable(ts[i], want)) Error(IncompatibleMsg(ts[i], want), c.Args[i]);
            }
            return JType.MapOf(k, v);
        }

        // ------------------------------------------------------------------ members
        JType MemberCall(CallExpr c, JType tt)
        {
            c.Kind = CallKind.Member;
            string where = "class " + tt;
            JType Args(string member, JType ret, params JType[] ps)
            {
                c.Member = member;
                CheckArgsAgainst(c, ps, "method " + c.Name + " in " + where);
                return ret;
            }
            JType NotFound()
            {
                CheckArgsLoose(c);
                Error("cannot find symbol: method " + c.Name + "(" + (c.Args.Count == 0 ? "" : ArgTypesText(c.Args)) + ") in " + tt, c.NameStart, c.NameEnd);
                return JType.Error;
            }
            if (c.Name == "equals" && c.Args.Count == 1 && tt.IsReference) return Args("any.equals", JType.Boolean, JType.Object);
            if (c.Name == "toString" && c.Args.Count == 0 && tt.IsReference) return Args("any.toString", JType.String);
            if (tt.IsPrimitive)
            {
                CheckArgsLoose(c);
                Error(tt + " cannot be dereferenced", c.Target);
                return JType.Error;
            }
            switch (tt.Kind)
            {
                case TK.String:
                    switch (c.Name)
                    {
                        case "length": return Args("str.length", JType.Int);
                        case "isEmpty": return Args("str.isEmpty", JType.Boolean);
                        case "charAt": return Args("str.charAt", JType.String, JType.Int);
                        case "contains": return Args("str.contains", JType.Boolean, JType.String);
                    }
                    return NotFound();
                case TK.List:
                    switch (c.Name)
                    {
                        case "add":
                            if (c.Args.Count == 2) return Args("list.addAt", JType.Void, JType.Int, tt.Elem);
                            return Args("list.add", JType.Boolean, tt.Elem);
                        case "get": return Args("list.get", tt.Elem, JType.Int);
                        case "set": return Args("list.set", tt.Elem, JType.Int, tt.Elem);
                        case "remove":
                            {
                                if (c.Args.Count != 1) return Args("list.removeAt", tt.Elem, JType.Int);
                                var at = CheckExpr(c.Args[0]);
                                if (at.Kind == TK.Int && at.IsPrimitive) { c.Member = "list.removeAt"; return tt.Elem; }
                                if (at.Kind == TK.Double && at.IsPrimitive) { Error("incompatible types: possible lossy conversion from double to int", c.Args[0]); return JType.Error; }
                                c.Member = "list.removeObj";
                                return JType.Boolean;
                            }
                        case "size": return Args("list.size", JType.Int);
                        case "isEmpty": return Args("list.isEmpty", JType.Boolean);
                        case "contains": return Args("list.contains", JType.Boolean, JType.Object);
                        case "indexOf": return Args("list.indexOf", JType.Int, JType.Object);
                        case "clear": return Args("list.clear", JType.Void);
                    }
                    return NotFound();
                case TK.Set:
                    switch (c.Name)
                    {
                        case "add": return Args("set.add", JType.Boolean, tt.Elem);
                        case "remove": return Args("set.remove", JType.Boolean, JType.Object);
                        case "contains": return Args("set.contains", JType.Boolean, JType.Object);
                        case "size": return Args("set.size", JType.Int);
                        case "isEmpty": return Args("set.isEmpty", JType.Boolean);
                        case "clear": return Args("set.clear", JType.Void);
                    }
                    return NotFound();
                case TK.Collection:
                    switch (c.Name)
                    {
                        case "size": return Args("set.size", JType.Int);
                        case "isEmpty": return Args("set.isEmpty", JType.Boolean);
                        case "contains": return Args("set.contains", JType.Boolean, JType.Object);
                    }
                    return NotFound();
                case TK.Map:
                    switch (c.Name)
                    {
                        case "put": return Args("map.put", tt.Elem, tt.Key, tt.Elem);
                        case "get": return Args("map.get", tt.Elem, JType.Object);
                        case "getOrDefault": return Args("map.getOrDefault", tt.Elem, JType.Object, tt.Elem);
                        case "containsKey": return Args("map.containsKey", JType.Boolean, JType.Object);
                        case "remove": return Args("map.remove", tt.Elem, JType.Object);
                        case "keySet": return Args("map.keySet", JType.SetOf(tt.Key));
                        case "values": return Args("map.values", JType.CollectionOf(tt.Elem));
                        case "size": return Args("map.size", JType.Int);
                        case "isEmpty": return Args("map.isEmpty", JType.Boolean);
                        case "clear": return Args("map.clear", JType.Void);
                    }
                    return NotFound();
                case TK.Game:
                    if (tt.Game == "Position")
                    {
                        if (c.Name == "x") return Args("pos.x", JType.Int);
                        if (c.Name == "y") return Args("pos.y", JType.Int);
                    }
                    if (tt.Game == "Companion")
                    {
                        if (c.Name == "entity") return Args("comp.entity", JType.GameType("Entity"));
                        if (c.Name == "position") return Args("comp.position", JType.GameType("Position"));
                        if (c.Name == "x") return Args("comp.x", JType.Int);
                        if (c.Name == "y") return Args("comp.y", JType.Int);
                    }
                    return NotFound();
                case TK.Runnable:
                    if (c.Name == "run") { c.Kind = CallKind.FunctionalRun; CheckArgsAgainst(c, new JType[0], "method run in interface Runnable"); return JType.Void; }
                    return NotFound();
                case TK.Supplier:
                    if (c.Name == "get") { c.Kind = CallKind.FunctionalRun; CheckArgsAgainst(c, new JType[0], "method get in interface Supplier"); return tt.Elem; }
                    return NotFound();
                case TK.Int: case TK.Double: case TK.Boolean:
                    if (c.Name == "intValue" && tt.Kind == TK.Int) return Args("unbox", JType.Int);
                    if (c.Name == "doubleValue" && tt.IsNumeric) return Args("unbox", JType.Double);
                    if (c.Name == "booleanValue" && tt.Kind == TK.Boolean) return Args("unbox", JType.Boolean);
                    return NotFound();
                case TK.Array:
                    if (c.Name == "length") { CheckArgsLoose(c); Error("cannot find symbol: method length() (arrays use .length without parentheses)", c.NameStart, c.End); return JType.Error; }
                    return NotFound();
            }
            return NotFound();
        }

        internal void DeclareOnly() => DeclareOwn();
    }

    /// <summary>Caches the declarations of other windows (they rarely change between keystrokes).</summary>
    /// <summary>Java view of a file in another language (ForeignModules signatures → FileSymbols).</summary>
    internal static class ForeignSymbols
    {
        public static FileSymbols From(ModuleSignature sig, string name)
        {
            var fs = new FileSymbols { FileName = name, ForeignLanguage = sig?.LanguageName ?? "another language" };
            if (sig == null || !sig.Ok)
            {
                fs.LoadError = "cannot use " + name + ": " + (sig?.Error ?? "it could not be read");
                return fs;
            }
            foreach (var f in sig.Functions)
            {
                if (fs.Methods.ContainsKey(f.Name)) continue;
                // other languages' privacy (Rust fn without pub, JS without export ...) is not Java's: the game shares every module-level name
                var ms = new MethodSymbol { Name = f.Name, PyName = f.PyName ?? PyNames.Ident(f.Name), ReturnType = J(f.ReturnType, true), IsPrivate = false, IsStatic = true, FileName = name };
                foreach (var p in f.Params) { ms.ParamNames.Add(p.Name ?? "arg"); ms.ParamTypes.Add(J(p.Type, false)); }
                fs.Methods[f.Name] = ms;
                if (f.IsEntry) fs.HasMain = true;
            }
            foreach (var g in sig.Globals)
            {
                if (fs.Fields.ContainsKey(g.Name)) continue;
                fs.Fields[g.Name] = new FieldSymbol { Name = g.Name, PyName = g.PyName ?? PyNames.Ident(g.Name), Type = J(g.Type, false), IsFinal = g.IsConst, IsPrivate = false, IsStatic = true, FileName = name };
            }
            return fs;
        }

        /// <summary>Neutral type → Java type. Unknown / dynamic values (Python, JavaScript, R) become Object: cast them.</summary>
        static JType J(IrType t, bool isReturn)
        {
            if (t == null) return JType.Object;
            switch (t.Kind)
            {
                case IrTypeKind.Int: return JType.Int;
                case IrTypeKind.Float: return JType.Double;
                case IrTypeKind.Bool: return JType.Boolean;
                case IrTypeKind.String: return JType.String;
                case IrTypeKind.Void: return isReturn ? JType.Void : JType.Object;
                case IrTypeKind.Array: return JType.ArrayOf(J(t.Elem, false));
                case IrTypeKind.List: return JType.ListOf(Box(J(t.Elem, false)));
                case IrTypeKind.Set: return JType.SetOf(Box(J(t.Elem, false)));
                case IrTypeKind.Map: return JType.MapOf(Box(J(t.Key, false)), Box(J(t.Elem, false)));
                case IrTypeKind.Game: return JType.GameTypeNames.Contains(t.GameName) ? JType.GameType(t.GameName) : JType.Object;
                case IrTypeKind.Func:
                    return t.Elem == null || t.Elem.Kind == IrTypeKind.Void ? JType.Runnable : JType.SupplierOf(Box(J(t.Elem, false)));
            }
            return JType.Object;
        }

        static JType Box(JType t) => t.Kind == TK.Void ? JType.Object : t.BoxedVersion;
    }

    internal static class DeclCache
    {
        static readonly Dictionary<string, FileSymbols> cache = new Dictionary<string, FileSymbols>();
        static readonly object gate = new object();

        public static FileSymbols Get(string name, string source)
        {
            var key = name + "\0" + source;
            lock (gate)
            {
                if (cache.TryGetValue(key, out var fs)) return fs;
            }
            FileSymbols result;
            var cu = Parser.Parse(source, out var err);
            if (cu == null) result = new FileSymbols { FileName = name };
            else
            {
                var chk = new Checker(cu, name, null);
                chk.DeclareOnly();
                result = chk.Own;
            }
            lock (gate)
            {
                if (cache.Count > 64) cache.Clear();
                cache[key] = result;
            }
            return result;
        }
    }
}
