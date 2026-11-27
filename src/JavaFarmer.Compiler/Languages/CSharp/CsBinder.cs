using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CSharp
{
    /// <summary>
    /// Name resolution, type checking, flow analysis (definite assignment, reachability, switch fall-through) and IR
    /// lowering in one pass over the parsed C# file. Diagnostics imitate Roslyn (CSxxxx: message); constructs outside the
    /// subset report "X is not supported in JavaFarmer". The IR is only used when there are no errors.
    /// </summary>
    internal sealed partial class CsBinder
    {
        readonly CUnit cu;
        readonly string fileName;
        readonly IReadOnlyDictionary<string, string> otherSources;
        readonly string source;
        public readonly List<Diagnostic> Errors = new List<Diagnostic>();
        public CFileDecls Own;
        readonly Dictionary<string, CFileDecls> others = new Dictionary<string, CFileDecls>();

        FuncCtx fn;
        CScope scope;
        HashSet<CLocal> da = new HashSet<CLocal>();
        bool reachable = true;
        bool usingStaticMath;
        CMethod mainDecl;
        /// <summary>Console.Write buffer (a synthetic module global) when the file uses Console.Write.</summary>
        IrSymbol bufSym;
        public const string BufferName = "__jcout";
        /// <summary>IrSymbol.Tag of char variables (the IR has strings only; the printer prints them as char again).</summary>
        public const string CharTag = "csharp:type:char";

        /// <summary>IrSymbol.Tag "csharp:type:&lt;C# type&gt;" for types the IR cannot tell apart from string ones (char, char[], List&lt;char&gt; ...).</summary>
        internal static string TypeTag(CType t)
        {
            bool HasChar(CType x) => x != null && (x.Kind == CK.Char || HasChar(x.Elem) || HasChar(x.Key) || (x.Params != null && x.Params.Any(HasChar)));
            return HasChar(t) ? "csharp:type:" + t : null;
        }
        readonly List<CLocal> topLocals = new List<CLocal>();

        public CsBinder(CUnit cu, string fileName, IReadOnlyDictionary<string, string> others, string source)
        {
            this.cu = cu; this.fileName = string.IsNullOrEmpty(fileName) ? "main" : fileName; otherSources = others; this.source = source ?? "";
        }

        bool IsClassForm => cu.Class != null;
        string ClassName => cu.Class?.Name ?? fileName;

        void Error(string msg, int s, int e) { if (Errors.Count < 50) Errors.Add(new Diagnostic(msg, s, e)); }
        void Error(string msg, CNode n) => Error(msg, n.Start, n.End);

        static Span Sp(CNode n) => n == null ? Span.None : new Span(n.Start, n.End);
        static Span Sp(int s, int e) => new Span(s, e);

        // ================================================================== entry points
        public void DeclareOnly() => Declare();

        public IrModule Run()
        {
            Declare();
            var mod = new IrModule { Name = fileName, SourceLanguage = "csharp", Span = new Span(0, source.Length) };
            if (UsesConsoleWrite())
            {
                bufSym = new IrSymbol(BufferName, IrSymbolKind.Global, IrType.String);
                var g = new IrGlobal { Symbol = bufSym, Init = IrLiteral.OfString(""), Synthetic = true, TypeExplicit = false };
                g.Init.Synthetic = true;
                mod.Items.Add(g);
            }
            if (IsClassForm) BindClass(mod);
            else BindTopLevel(mod);
            return mod;
        }

        bool UsesConsoleWrite()
        {
            int i = 0;
            while (true)
            {
                i = source.IndexOf("Write", i, StringComparison.Ordinal);
                if (i < 0) return false;
                int j = i + 5;
                if (j < source.Length && source[j] == 'L') { i = j; continue; }
                int k = i - 1;
                while (k >= 0 && char.IsWhiteSpace(source[k])) k--;
                if (k >= 0 && source[k] == '.')
                {
                    k--;
                    while (k >= 0 && char.IsWhiteSpace(source[k])) k--;
                    int e = k + 1;
                    while (k >= 0 && CsLexer.IsIdentPart(source[k])) k--;
                    if (source.Substring(k + 1, e - k - 1) == "Console") return true;
                }
                i = j;
            }
        }

        // ================================================================== declarations
        void Declare()
        {
            Own = new CFileDecls { FileName = fileName, IsClass = IsClassForm, ClassName = ClassName };
            foreach (var u in cu.Usings) CheckUsing(u);
            if (IsClassForm) DeclareClass(cu.Class);
            else
            {
                foreach (var s in cu.TopLevel)
                    if (s is CLocalFuncStmt lf) DeclareMethod(lf.Method, true);
            }
        }

        void CheckUsing(CUsing u)
        {
            if (u.Alias != null) { Error("using aliases are not supported in JavaFarmer", u); return; }
            var n = u.Name;
            if (u.IsStatic)
            {
                if (n == "Farm") return;
                if (n == "System.Math" || n == "Math") { usingStaticMath = true; return; }
                if (n == "System.Console" || n == "Console") { Error("using static System.Console is not supported in JavaFarmer (write Console.WriteLine)", u); return; }
                Error("using static " + n + " is not supported in JavaFarmer", u);
                return;
            }
            switch (n)
            {
                case "System": case "System.Collections.Generic": return;
                case "System.Linq": Error("LINQ (using System.Linq) is not supported in JavaFarmer", u); return;
            }
            if (otherSources != null && otherSources.ContainsKey(n)) { Error("other files are used by their class name (" + n + ".Name); 'using " + n + "' is not supported in JavaFarmer", u); return; }
            Error("using " + n + " is not supported in JavaFarmer (only System and System.Collections.Generic are available)", u);
        }

        void CheckIdentifier(string name, int s, int e)
        {
            if (name.StartsWith("__j", StringComparison.Ordinal)) Error("identifiers starting with __j are reserved by JavaFarmer", s, e);
        }

        static bool Accessible(CMods m) => (m & (CMods.Public | CMods.Internal)) != 0;

        void DeclareClass(CClass c)
        {
            CheckIdentifier(c.Name, c.NameStart, c.NameEnd);
            bool staticClass = (c.Mods & CMods.Static) != 0;
            if ((c.Mods & (CMods.Private | CMods.Protected)) != 0) Error("CS1527: Elements defined in a namespace cannot be explicitly declared as private, protected, protected internal, or private protected", c.NameStart, c.NameEnd);
            foreach (var f in c.Fields)
            {
                bool isConst = (f.Mods & CMods.Const) != 0;
                if (!isConst && (f.Mods & CMods.Static) == 0)
                {
                    if (staticClass) Error("CS0708: '" + c.Name + "." + f.Vars[0].Name + "': cannot declare instance members in a static class", f.Vars[0].NameStart, f.Vars[0].NameEnd);
                    else Error("instance fields are not supported in JavaFarmer: declare it static", f.Vars[0].NameStart, f.Vars[0].NameEnd);
                }
                if (isConst && (f.Mods & CMods.Static) != 0) Error("CS0504: The constant '" + c.Name + "." + f.Vars[0].Name + "' cannot be marked static", f.Vars[0].NameStart, f.Vars[0].NameEnd);
                CType ft;
                if (f.Type.Name == "var" && f.Type.Args == null) { Error("CS0825: The contextual keyword 'var' may only appear within a local variable declaration or in script code", f.Type); ft = CType.Error; }
                else ft = ResolveType(f.Type);
                foreach (var v in f.Vars)
                {
                    CheckIdentifier(v.Name, v.NameStart, v.NameEnd);
                    if (Own.Fields.ContainsKey(v.Name) || Own.Methods.ContainsKey(v.Name)) { Error("CS0102: The type '" + c.Name + "' already contains a definition for '" + v.Name + "'", v.NameStart, v.NameEnd); continue; }
                    var fs = new CFieldSym
                    {
                        Name = v.Name, Type = ft, IsConst = isConst, IsReadonly = (f.Mods & CMods.Readonly) != 0, IsAccessible = Accessible(f.Mods),
                        FileName = fileName, ClassName = c.Name,
                    };
                    if (isConst)
                    {
                        if (v.Init == null) Error("CS0145: A const field requires a value to be provided", v.NameStart, v.NameEnd);
                        else ConstValueOf(v.Init, out fs.ConstInt, out fs.ConstStr);
                    }
                    fs.Ir = new IrSymbol(v.Name, IrSymbolKind.Global, ft.Ir()) { IsConst = isConst || fs.IsReadonly, DeclSpan = Sp(v.NameStart, v.NameEnd), Tag = TypeTag(ft) };
                    Own.Fields[v.Name] = fs;
                }
            }
            foreach (var m in c.Methods)
            {
                if ((m.Mods & CMods.Static) == 0)
                {
                    if (staticClass) Error("CS0708: '" + c.Name + "." + m.Name + "()': cannot declare instance members in a static class", m.NameStart, m.NameEnd);
                    else Error("instance methods are not supported in JavaFarmer: declare it static", m.NameStart, m.NameEnd);
                }
                if ((m.Mods & (CMods.Const | CMods.Readonly)) != 0) Error("CS0106: The modifier '" + ((m.Mods & CMods.Const) != 0 ? "const" : "readonly") + "' is not valid for this item", m.NameStart, m.NameEnd);
                DeclareMethod(m, false);
            }
            if (mainDecl == null && c.Name != fileName)
                Error("class " + c.Name + " must be named like its file (" + fileName + ") so that other files can use it as " + fileName + ".Name (JavaFarmer)", c.NameStart, c.NameEnd);
            // PyNames of this file's declarations (used by other files)
            var fnNames = PyNaming.ModuleLevel(Own.Methods.Keys, Own.Fields.Keys, out var gNames);
            foreach (var ms in Own.Methods.Values) ms.PyName = fnNames[ms.Name];
            foreach (var fs in Own.Fields.Values) fs.PyName = gNames[fs.Name];
        }

        void DeclareMethod(CMethod m, bool local)
        {
            CheckIdentifier(m.Name, m.NameStart, m.NameEnd);
            var ms = new CMethodSym
            {
                Name = m.Name, IsLocal = local, IsStaticLocal = local && (m.Mods & CMods.Static) != 0, Decl = m, FileName = fileName, ClassName = ClassName,
                IsAccessible = Accessible(m.Mods),
            };
            ms.ReturnType = ResolveType(m.ReturnType, allowVoid: true);
            foreach (var p in m.Params)
            {
                ms.ParamTypes.Add(p.Type.Name == "var" && p.Type.Args == null ? ErrVar(p.Type) : ResolveType(p.Type));
                ms.ParamNames.Add(p.Name);
                ms.HasDefault.Add(p.Default != null);
            }
            for (int i = 1; i < ms.HasDefault.Count; i++)
                if (ms.HasDefault[i - 1] && !ms.HasDefault[i]) { Error("CS1737: Optional parameters must appear after all required parameters", m.Params[i]); break; }
            m.Symbol = ms;
            if (Own.Methods.TryGetValue(m.Name, out var prev))
            {
                if (local) Error("CS0128: A local variable or function named '" + m.Name + "' is already defined in this scope", m.NameStart, m.NameEnd);
                else if (prev.Display == ms.Display) Error("CS0111: Type '" + ClassName + "' already defines a member called '" + m.Name + "' with the same parameter types", m.NameStart, m.NameEnd);
                else Error("method overloading is not supported in JavaFarmer: there is already a method named " + m.Name, m.NameStart, m.NameEnd);
                return;
            }
            if (Own.Fields.ContainsKey(m.Name)) { Error("CS0102: The type '" + ClassName + "' already contains a definition for '" + m.Name + "'", m.NameStart, m.NameEnd); return; }
            ms.Ir = new IrSymbol(m.Name, IrSymbolKind.Function, IrType.Func(ms.ReturnType.Ir())) { ParamTypes = ms.ParamTypes.Select(t => t.Ir()).ToList(), DeclSpan = Sp(m.NameStart, m.NameEnd) };
            Own.Methods[m.Name] = ms;
            if (m.Name == "Main" && !local)
            {
                bool okParams = m.Params.Count == 0 || (m.Params.Count == 1 && ms.ParamTypes[0].Kind == CK.Array && ms.ParamTypes[0].Elem.Kind == CK.String);
                if (!okParams || ms.ReturnType.Kind != CK.Void)
                    Error("Main must be declared as static void Main() or static void Main(string[] args) in JavaFarmer", m.HeaderStart, m.HeaderEnd);
                else { mainDecl = m; Own.HasMain = true; }
            }
        }

        CType ErrVar(CTypeRef t) { Error("CS0825: The contextual keyword 'var' may only appear within a local variable declaration or in script code", t); return CType.Error; }

        /// <summary>Declarations of another window (parsed and cached; errors there are ignored here).</summary>
        internal CFileDecls GetOther(string name)
        {
            if (name == fileName || otherSources == null) return null;
            if (others.TryGetValue(name, out var cached)) return cached;
            if (!otherSources.TryGetValue(name, out var src) || src == null) return null;
            // a window written in another language (its file extension): read it with that language's own frontend
            var fs = Options != null && ForeignModules.IsForeign(Options, name, "csharp")
                ? FromSignature(ForeignModules.Resolve(name, Options), name)
                : CsDeclCache.Get(name, src);
            others[name] = fs;
            return fs;
        }

        /// <summary>The caller's options (OtherFileLanguages for cross-language imports); null in declaration-only reads.</summary>
        internal CompileOptions Options;

        /// <summary>C# view of a file in another language: a static class whose members are all accessible.</summary>
        internal static CFileDecls FromSignature(ModuleSignature sig, string name)
        {
            var fs = new CFileDecls { FileName = name, IsClass = true, ClassName = name, ForeignLanguage = sig?.LanguageName ?? "another language" };
            if (sig == null || !sig.Ok) { fs.LoadError = "cannot use " + name + ": " + (sig?.Error ?? "it could not be read"); return fs; }
            foreach (var f in sig.Functions)
            {
                if (f.Name == null || fs.Methods.ContainsKey(f.Name)) continue;
                // the other language's privacy (Rust fn without pub, JS without export) is not C#'s: the game shares every module-level name
                var ms = new CMethodSym { Name = f.Name, PyName = f.PyName ?? PyNaming.Ident(f.Name), ReturnType = Foreign(f.ReturnType, true), IsAccessible = true, FileName = name, ClassName = name };
                foreach (var p in f.Params) { ms.ParamTypes.Add(Foreign(p.Type, false)); ms.ParamNames.Add(p.Name ?? "arg"); ms.HasDefault.Add(p.Optional); }
                fs.Methods[f.Name] = ms;
                if (f.IsEntry) fs.HasMain = true;
            }
            foreach (var g in sig.Globals)
            {
                if (g.Name == null || fs.Fields.ContainsKey(g.Name) || fs.Methods.ContainsKey(g.Name)) continue;
                fs.Fields[g.Name] = new CFieldSym { Name = g.Name, PyName = g.PyName ?? PyNaming.Ident(g.Name), Type = Foreign(g.Type, false), IsConst = false, IsReadonly = false, IsAccessible = true, FileName = name, ClassName = name };
            }
            return fs;
        }

        /// <summary>Neutral type → C#. Unknown / dynamic values (Python, JavaScript, R) are object: cast them.</summary>
        static CType Foreign(IrType t, bool isReturn)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any || t.Kind == IrTypeKind.Null) return CType.Object.WithNullable(true);
            if (t.Kind == IrTypeKind.Void) return isReturn ? CType.Void : CType.Object;
            var c = CType.FromIr(t);
            bool unknownInside(IrType x) => x != null && (!x.IsKnown || unknownInside(x.Elem) || unknownInside(x.Key));
            if ((t.Kind == IrTypeKind.List || t.Kind == IrTypeKind.Set || t.Kind == IrTypeKind.Map || t.Kind == IrTypeKind.Array) && unknownInside(t))
            {
                CType E(IrType x) => x == null || !x.IsKnown || x.Kind == IrTypeKind.Any ? CType.Object : CType.FromIr(x);
                switch (t.Kind)
                {
                    case IrTypeKind.List: return CType.ListOf(E(t.Elem));
                    case IrTypeKind.Set: return CType.SetOf(E(t.Elem));
                    case IrTypeKind.Array: return CType.ArrayOf(E(t.Elem));
                    default: return CType.MapOf(E(t.Key), E(t.Elem));
                }
            }
            return c;
        }

        /// <summary>A foreign window that cannot be read: reports why at the use site. True when it did.</summary>
        bool ForeignFailed(CFileDecls o, CNode at)
        {
            if (o?.LoadError == null) return false;
            Error(o.LoadError, at);
            return true;
        }

        // ================================================================== types
        static readonly HashSet<string> UnsupportedCollections = new HashSet<string>
        {
            "Queue", "Stack", "LinkedList", "SortedDictionary", "SortedList", "SortedSet", "IEnumerable", "IList", "ICollection",
            "IDictionary", "ISet", "IReadOnlyList", "IReadOnlyDictionary", "ArrayList", "Hashtable", "PriorityQueue", "Span", "ReadOnlySpan",
        };

        internal CType ResolveType(CTypeRef t, bool allowVoid = false)
        {
            var r = ResolveTypeCore(t, allowVoid);
            t.Resolved = r;
            return r;
        }

        CType ResolveTypeCore(CTypeRef t, bool allowVoid)
        {
            CType baseT = ResolveBase(t, allowVoid);
            if (t.Nullable && !baseT.IsError)
            {
                if (baseT.IsValueType) { Error("nullable value types (" + baseT + "?) are not supported in JavaFarmer", t); return CType.Error; }
                baseT = baseT.WithNullable(true);
            }
            if (t.Ranks.Count > 0)
            {
                if (t.Ranks.Count > 2 || t.Ranks.Any(r => r > 2) || (t.Ranks.Count == 2 && t.Ranks.Any(r => r != 1)))
                { Error("arrays with more than 2 dimensions are not supported in JavaFarmer", t); return CType.Error; }
                if (t.Ranks.Count == 2) return CType.ArrayOf(CType.ArrayOf(baseT));
                return CType.ArrayOf(baseT, t.Ranks[0]);
            }
            return baseT;
        }

        CType ResolveBase(CTypeRef t, bool allowVoid)
        {
            CType Arg(int i) => ResolveType(t.Args[i]);
            bool NeedArgs(int n)
            {
                if (t.Args == null) { Error("CS0305: Using the generic type '" + t.Name + "<" + new string(',', n - 1) + ">' requires " + n + " type arguments", t.NameStart, t.NameEnd); return false; }
                if (t.Args.Count != n) { Error("CS0305: Using the generic type '" + t.Name + "<" + new string(',', n - 1) + ">' requires " + n + " type arguments", t.NameStart, t.NameEnd); return false; }
                return true;
            }
            if (t.IsTuple) return CType.TupleOf(t.Args.Select(a => ResolveType(a)));
            switch (t.Name)
            {
                case "int": return CType.Int;
                case "double": return CType.Double;
                case "bool": return CType.Bool;
                case "string": case "String": return CType.String;
                case "object": case "Object": return CType.Object;
                case "char": return CType.Char;
                case "void":
                    if (!allowVoid) Error("CS1547: Keyword 'void' cannot be used in this context", t);
                    return CType.Void;
                case "long": case "float": case "decimal": case "byte": case "sbyte": case "short": case "ushort": case "uint": case "ulong":
                    Error(t.Name + " is not supported in JavaFarmer; use int or double", t.NameStart, t.NameEnd);
                    return t.Name == "float" || t.Name == "decimal" ? CType.Double : CType.Int;
                case "dynamic": Error("dynamic is not supported in JavaFarmer", t); return CType.Error;
                case "Random": Error("System.Random is not supported in JavaFarmer; call Random() (the game's random number in [0, 1))", t.NameStart, t.NameEnd); return CType.Error;
                case "var": Error("CS0825: The contextual keyword 'var' may only appear within a local variable declaration or in script code", t); return CType.Error;
                case "Int32": return CType.Int;
                case "Double": return CType.Double;
                case "Boolean": return CType.Bool;
                case "List": return NeedArgs(1) ? CType.ListOf(Arg(0)) : CType.Error;
                case "HashSet": return NeedArgs(1) ? CType.SetOf(Arg(0)) : CType.Error;
                case "Dictionary": return NeedArgs(2) ? CType.MapOf(Arg(0), Arg(1)) : CType.Error;
                case "KeyValuePair": return NeedArgs(2) ? CType.KeyValueOf(Arg(0), Arg(1)) : CType.Error;
                case "Action":
                    if (t.Args == null) return CType.Action;
                    return CType.DelegateOf(CType.Void, t.Args.Select(a => ResolveType(a)));
                case "Func":
                    {
                        if (t.Args == null || t.Args.Count == 0) { Error("CS0305: Using the generic type 'Func<TResult>' requires 1 type arguments", t.NameStart, t.NameEnd); return CType.Error; }
                        var all = t.Args.Select(a => ResolveType(a)).ToList();
                        if (all[all.Count - 1].Kind == CK.Void) Error("CS1547: Keyword 'void' cannot be used in this context", t.Args[t.Args.Count - 1]);
                        return CType.DelegateOf(all[all.Count - 1], all.Take(all.Count - 1));
                    }
            }
            if (CType.GameTypeNames.Contains(t.Name))
            {
                if (t.Args != null) Error("CS0308: The non-generic type '" + t.Name + "' cannot be used with type arguments", t);
                return CType.GameType(t.Name);
            }
            if (UnsupportedCollections.Contains(t.Name))
            {
                Error(t.Name + " is not supported in JavaFarmer; use List<T>, HashSet<T> or Dictionary<TKey, TValue>", t.NameStart, t.NameEnd);
                return CType.Error;
            }
            if (t.Name == ClassName || (otherSources != null && otherSources.ContainsKey(t.Name)) || t.Name == "Farm" || t.Name == "Entities" || t.Name == "Items")
            {
                Error("objects of class " + t.Name + " are not supported in JavaFarmer (only static members)", t.NameStart, t.NameEnd);
                return CType.Error;
            }
            Error("CS0246: The type or namespace name '" + t.Name + "' could not be found (are you missing a using directive or an assembly reference?)", t.NameStart, t.NameEnd);
            return CType.Error;
        }

        // ================================================================== class form
        void BindClass(IrModule mod)
        {
            var c = cu.Class;
            foreach (var member in c.Members)
            {
                if (member is CField f)
                {
                    foreach (var v in f.Vars)
                    {
                        if (!Own.Fields.TryGetValue(v.Name, out var fs) || fs.Ir == null) continue;
                        var g = new IrGlobal { Symbol = fs.Ir, Span = Sp(v), NameSpan = Sp(v.NameStart, v.NameEnd), TypeExplicit = true, IsPrivate = !fs.IsAccessible };
                        if (v.Init != null)
                        {
                            fn = new FuncCtx { IsFieldInit = true, Display = c.Name };
                            scope = new CScope(null, fn);
                            da = new HashSet<CLocal>();
                            reachable = true;
                            g.Init = Initializer(v.Init, fs.Type);
                        }
                        else
                        {
                            g.Init = DefaultLiteral(fs.Type, Sp(v));
                            g.ImplicitInit = true;
                        }
                        mod.Items.Add(g);
                    }
                }
                else if (member is CMethod m && m.Symbol != null && Own.Methods.TryGetValue(m.Name, out var ms) && ms == m.Symbol)
                {
                    mod.Items.Add(BindMethod(m, null));
                }
            }
        }

        static IrLiteral DefaultLiteral(CType t, Span span)
        {
            IrLiteral lit;
            switch (t.Kind)
            {
                case CK.Bool: lit = IrLiteral.OfBool(false); break;
                case CK.Int: lit = IrLiteral.OfInt(0); break;
                case CK.Double: lit = new IrLiteral { Kind = IrLitKind.Int, Int = 0, Type = IrType.Float }; break;
                case CK.Char: lit = IrLiteral.OfString(""); break;
                default: lit = IrLiteral.Null(); break;
            }
            lit.Span = span;
            return lit;
        }

        IrFunction BindMethod(CMethod m, CScope outer)
        {
            var ms = m.Symbol;
            var f = new IrFunction
            {
                Symbol = ms.Ir, ReturnType = ms.ReturnType.Ir(), IsEntry = m == mainDecl, IsPrivate = !ms.IsAccessible && !ms.IsLocal,
                Span = Sp(m), HeaderSpan = Sp(m.HeaderStart, m.HeaderEnd), NameSpan = Sp(m.NameStart, m.NameEnd),
            };
            ms.Ir.Function = f;
            var oldFn = fn; var oldScope = scope; var oldDa = da; var oldReach = reachable;
            fn = new FuncCtx { Parent = outer?.Fn, Method = ms, ReturnType = ms.ReturnType, Display = ms.IsLocal ? ms.Name + "(" + string.Join(", ", ms.ParamTypes) + ")" : ClassName + "." + ms.Name + "(" + string.Join(", ", ms.ParamTypes) + ")", IsStatic = ms.IsStaticLocal };
            scope = new CScope(outer, fn);
            da = new HashSet<CLocal>(oldDa ?? new HashSet<CLocal>());
            reachable = true;
            for (int i = 0; i < m.Params.Count; i++)
            {
                var p = m.Params[i];
                CheckIdentifier(p.Name, p.NameStart, p.NameEnd);
                if (scope.Vars.ContainsKey(p.Name)) { Error("CS0100: The parameter name '" + p.Name + "' is a duplicate", p.NameStart, p.NameEnd); continue; }
                if (outer != null && outer.Lookup(p.Name) != null) { }
                var sym = DeclareLocal(p.Name, ms.ParamTypes[i], false, p.NameStart, p.NameEnd, IrSymbolKind.Param);
                sym.IsParam = true;
                da.Add(sym);
                var ip = new IrParam { Symbol = sym.Ir, Span = Sp(p), NameSpan = Sp(p.NameStart, p.NameEnd) };
                if (p.Default != null)
                {
                    if (!IsConstantExpr(p.Default)) Error("CS1736: Default parameter value for '" + p.Name + "' must be a compile-time constant", p.Default);
                    var dv = X(p.Default, ms.ParamTypes[i]);
                    RequireAssignable(p.Default.Type, ms.ParamTypes[i], p.Default);
                    ip.Default = dv;
                }
                f.Params.Add(ip);
            }
            if (m.ExprBody != null)
            {
                var body = new IrBlock { Span = Sp(m.ExprBody) };
                if (ms.ReturnType.Kind == CK.Void) body.Stmts.AddRange(ExprStatement(m.ExprBody, Sp(m.ExprBody)));
                else body.Stmts.AddRange(ReturnValue(m.ExprBody, Sp(m.ExprBody)));
                reachable = false;
                f.Body = body;
            }
            else
            {
                f.Body = BindBlockBody(m.Body);
                if (reachable && ms.ReturnType.Kind != CK.Void && !ms.ReturnType.IsError)
                    Error("CS0161: '" + fn.Display + "': not all code paths return a value", m.NameStart, m.NameEnd);
            }
            fn = oldFn; scope = oldScope; da = oldDa; reachable = oldReach;
            return f;
        }

        // ================================================================== top-level statements
        void BindTopLevel(IrModule mod)
        {
            fn = new FuncCtx { IsTopLevel = true, ReturnType = CType.Void, Display = "<top-level-statements-entry-point>" };
            scope = new CScope(null, fn);
            scope.Later = new HashSet<string>(cu.TopLevel.OfType<CLocalVar>().SelectMany(v => v.Vars.Select(x => x.Name)));
            da = new HashSet<CLocal>();
            reachable = true;
            var functions = new List<IrFunction>();
            var stmts = new List<IrNode>();
            foreach (var s in cu.TopLevel)
            {
                if (s is CLocalFuncStmt lf)
                {
                    if (lf.Method.Symbol == null || !Own.Methods.TryGetValue(lf.Method.Name, out var ms) || ms != lf.Method.Symbol) continue;
                    bool r = reachable;
                    functions.Add(BindMethod(lf.Method, scope));
                    reachable = r;
                    continue;
                }
                if (s is CReturn) { Error("return in top-level statements is not supported in JavaFarmer", s); continue; }
                var list = new List<IrStmt>();
                Stmt(s, list, true);
                foreach (var st in list) stmts.Add(st);
            }
            // locals of the top-level statements that a local function or lambda uses become module globals
            foreach (var l in topLocals)
                if (l.Ir.Kind == IrSymbolKind.Global) { }
            var items = new List<IrNode>();
            foreach (var st in stmts)
            {
                if (st is IrVarDecl v && v.Symbol != null && v.Symbol.Kind == IrSymbolKind.Global)
                {
                    var g = new IrGlobal { Symbol = v.Symbol, Init = v.Init, NameSpan = v.NameSpan, TypeExplicit = v.TypeExplicit, Span = v.Span };
                    if (g.Init == null) { g.Init = DefaultLiteral(CType.FromIr(v.Symbol.Type), v.Span); g.ImplicitInit = true; }
                    items.Add(g);
                }
                else items.Add(st);
            }
            // local functions are hoisted in C#: define them before the statements run
            foreach (var f in functions) mod.Items.Add(f);
            foreach (var it in items) mod.Items.Add(it);
        }

        // ================================================================== locals
        CLocal DeclareLocal(string name, CType type, bool isConst, int s, int e, IrSymbolKind kind = IrSymbolKind.Local)
        {
            var sym = new CLocal { Name = name, Type = type, IsConst = isConst, DeclStart = s, DeclEnd = e, Owner = fn, TopLevel = fn.IsTopLevel && scope.Parent == null };
            sym.Ir = new IrSymbol(name, kind, type.Ir()) { IsConst = isConst, DeclSpan = Sp(s, e), Tag = TypeTag(type) };
            scope.Vars[name] = sym;
            if (sym.TopLevel) topLocals.Add(sym);
            return sym;
        }

        /// <summary>CS0128 / CS0136 checks before declaring a local.</summary>
        bool CheckRedeclare(string name, int start, int end)
        {
            int s = start, e = end;
            if (scope.Vars.ContainsKey(name) || (scope.Parent == null && fn.IsTopLevel && Own.Methods.ContainsKey(name)))
            {
                Error("CS0128: A local variable or function named '" + name + "' is already defined in this scope", s, e);
                return true;
            }
            // locals of an enclosing function may be shadowed by a local function / lambda (C# 8+), not by nested blocks
            CLocal outer = null;
            for (var sc = scope.Parent; sc != null && sc.Fn == fn; sc = sc.Parent)
                if (sc.Vars.TryGetValue(name, out outer)) break;
            if (outer != null)
            {
                Error("CS0136: A local or parameter named '" + name + "' cannot be declared in this scope because that name is used in an enclosing local scope to define a local or parameter", s, e);
                return true;
            }
            return false;
        }

        void PushScope() => scope = new CScope(scope, fn);
        void PopScope() => scope = scope.Parent;

        static HashSet<CLocal> Meet(HashSet<CLocal> a, bool ra, HashSet<CLocal> b, bool rb)
        {
            if (!ra) return b;
            if (!rb) return a;
            var r = new HashSet<CLocal>(a);
            r.IntersectWith(b);
            return r;
        }

        // ================================================================== statements
        IrBlock BindBlockBody(CBlock b)
        {
            var blk = new IrBlock { Span = Sp(b) };
            scope.Later = LaterNames(b.Stmts);
            foreach (var st in b.Stmts) Stmt(st, blk.Stmts, true);
            return blk;
        }

        static HashSet<string> LaterNames(List<CStmt> stmts)
        {
            HashSet<string> res = null;
            foreach (var s in stmts)
                if (s is CLocalVar lv) foreach (var v in lv.Vars) (res ?? (res = new HashSet<string>())).Add(v.Name);
            return res;
        }

        IrBlock Block(CStmt s)
        {
            PushScope();
            IrBlock blk;
            if (s is CBlock b)
            {
                blk = new IrBlock { Span = Sp(b) };
                scope.Later = LaterNames(b.Stmts);
                foreach (var st in b.Stmts) Stmt(st, blk.Stmts, true);
            }
            else
            {
                blk = new IrBlock { Span = Sp(s) };
                if (s != null) Stmt(s, blk.Stmts, false);
            }
            PopScope();
            return blk;
        }

        void Stmt(CStmt s, List<IrStmt> outList, bool inBlock)
        {
            switch (s)
            {
                case CBlock b:
                    {
                        PushScope();
                        var blk = new IrBlock { Span = Sp(b) };
                        scope.Later = LaterNames(b.Stmts);
                        foreach (var st in b.Stmts) Stmt(st, blk.Stmts, true);
                        PopScope();
                        outList.Add(blk);
                        return;
                    }
                case CEmpty _: return;
                case CLocalVar lv: LocalVar(lv, outList); return;
                case CDeconstruct dc: Deconstruct(dc, outList); return;
                case CExprStmt es: outList.AddRange(ExprStatement(es.E, Sp(es))); return;
                case CLocalFuncStmt lf:
                    Error("local functions inside methods or blocks are not supported in JavaFarmer", lf.Method.NameStart, lf.Method.NameEnd);
                    return;
                case CIf ifs: outList.AddRange(If(ifs)); return;
                case CWhile w:
                    {
                        var cond = Cond(w.Cond);
                        var target = new BreakTarget { Node = w };
                        fn.Targets.Add(target);
                        var da0 = new HashSet<CLocal>(da);
                        var body = Block(w.Body);
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        da = da0;
                        reachable = !IsTrue(w.Cond) || target.HasBreak;
                        outList.Add(new IrWhile { Cond = cond, Body = body, KwSpan = Sp(w.Start, w.KwEnd), Span = Sp(w) });
                        return;
                    }
                case CDoWhile dw:
                    {
                        var target = new BreakTarget { Node = dw };
                        fn.Targets.Add(target);
                        var body = Block(dw.Body);
                        fn.Targets.RemoveAt(fn.Targets.Count - 1);
                        reachable = true;
                        var cond = Cond(dw.Cond);
                        reachable = !IsTrue(dw.Cond) || target.HasBreak;
                        outList.Add(new IrDoWhile { Body = body, Cond = cond, KwSpan = Sp(dw.Start, dw.Start + 2), WhileSpan = Sp(dw.WhileStart, dw.WhileEnd), Span = Sp(dw) });
                        return;
                    }
                case CFor f: outList.Add(For(f)); return;
                case CForEach fe: outList.Add(ForEach(fe)); return;
                case CBreak br:
                    {
                        if (fn.Targets.Count == 0) { Error("CS0139: No enclosing loop out of which to break or continue", br); reachable = false; return; }
                        var t = fn.Targets[fn.Targets.Count - 1];
                        t.HasBreak = true;
                        if (t.IsSwitch && !br.Strip) Error("break inside a switch section is only supported as the last statement of the section in JavaFarmer", br);
                        if (!br.Strip) outList.Add(new IrBreak { Span = Sp(br) });
                        reachable = false;
                        return;
                    }
                case CContinue co:
                    if (!fn.Targets.Any(t => !t.IsSwitch)) Error("CS0139: No enclosing loop out of which to break or continue", co);
                    outList.Add(new IrContinue { Span = Sp(co) });
                    reachable = false;
                    return;
                case CReturn r:
                    outList.AddRange(Return(r));
                    reachable = false;
                    return;
                case CSwitch sw: outList.Add(Switch(sw)); return;
            }
            Error("unsupported statement", s);
        }

        static bool IsTrue(CExpr e)
        {
            while (e is CParen p) e = p.Inner;
            return e is CLiteral l && l.Kind == CLitKind.Bool && l.BoolValue;
        }

        static bool IsTryGetValue(CExpr e)
        {
            e = Unparen(e);
            if (e is CUnary u && u.Op == "!") e = Unparen(u.Operand);
            return e is CCall c && c.Callee is CMember m && m.Name == "TryGetValue";
        }

        /// <summary>if / else if chain. if (d.TryGetValue(k, out var v)): v is assigned before the if.</summary>
        List<IrStmt> If(CIf ifs)
        {
            var res = new List<IrStmt>();
            var oldPre = preStmts;
            preStmts = IsTryGetValue(ifs.Cond) ? res : null;
            var cond = Cond(ifs.Cond);
            preStmts = oldPre;
            var da0 = new HashSet<CLocal>(da);
            var then = Block(ifs.Then);
            var daThen = da; bool rThen = reachable;
            da = new HashSet<CLocal>(da0); reachable = true;
            IrStmt els = null;
            if (ifs.Else != null)
            {
                if (ifs.Else is CIf next)
                {
                    var chain = If(next);
                    els = chain.Count == 1 ? chain[0] : new IrBlock(chain) { Span = Sp(next) };
                }
                else els = Block(ifs.Else);
            }
            var daElse = da; bool rElse = reachable;
            da = Meet(daThen, rThen, daElse, rElse);
            reachable = rThen || rElse;
            res.Add(new IrIf { Cond = cond, Then = then, Else = els, KwSpan = Sp(ifs.Start, ifs.KwEnd), Span = Sp(ifs) });
            return res;
        }

        IrStmt For(CFor f)
        {
            PushScope();
            var cf = new IrClassicFor { KwSpan = Sp(f.Start, f.KwEnd), HeaderSpan = Sp(f.Start, f.HeaderEnd), Span = Sp(f) };
            foreach (var init in f.Init)
            {
                if (init is CLocalVar lv) LocalVar(lv, cf.Init);
                else if (init is CExprStmt es) cf.Init.AddRange(ExprStatement(es.E, Sp(es)));
            }
            if (f.Cond != null) cf.Cond = Cond(f.Cond);
            var da0 = new HashSet<CLocal>(da);
            var target = new BreakTarget { Node = f };
            fn.Targets.Add(target);
            cf.Body = Block(f.Body);
            reachable = true;
            foreach (var u in f.Update) cf.Update.AddRange(ExprStatement(u, Sp(u)));
            fn.Targets.RemoveAt(fn.Targets.Count - 1);
            PopScope();
            da = da0;
            reachable = (f.Cond != null && !IsTrue(f.Cond)) || target.HasBreak;
            return cf;
        }

        IrStmt ForEach(CForEach fe)
        {
            var it = X(fe.Iterable);
            var itT = fe.Iterable.Type ?? CType.Error;
            CType elem = CType.Error;
            bool isMap = false;
            switch (itT.Kind)
            {
                case CK.Array: elem = itT.Rank == 2 ? CType.Error : itT.Elem; if (itT.Rank == 2) Error("foreach over a 2-dimensional array is not supported in JavaFarmer", fe.Iterable); break;
                case CK.List: case CK.Set: elem = itT.Elem; break;
                case CK.String: elem = CType.Char; break;
                case CK.Map: elem = CType.KeyValueOf(itT.Key, itT.Elem); isMap = true; break;
                case CK.Error: break;
                default: Error("CS1579: foreach statement cannot operate on variables of type '" + itT + "' because '" + itT + "' does not contain a public instance or extension definition for 'GetEnumerator'", fe.Iterable); break;
            }
            PushScope();
            var res = new IrForEach { Iterable = it, KwSpan = Sp(fe.Start, fe.KwEnd), Span = Sp(fe) };
            CLocal kv = null;
            if (fe.Deconstruct != null)
            {
                var items = fe.Deconstruct.Items;
                CType[] parts = null;
                if (isMap) parts = new[] { itT.Key, itT.Elem };
                else if (elem.Kind == CK.Tuple) parts = elem.Params.ToArray();
                else if (elem.IsGame("Position")) parts = new[] { CType.Int, CType.Int };
                else if (!elem.IsError) Error("CS8129: No suitable 'Deconstruct' instance or extension method was found for type '" + elem + "', with " + items.Count + " out parameters and a void return type.", fe.Deconstruct);
                if (parts != null && parts.Length != items.Count) { Error("CS8132: Cannot deconstruct a tuple of '" + parts.Length + "' elements into '" + items.Count + "' variables.", fe.Deconstruct); parts = null; }
                if (!isMap)
                {
                    // foreach (var (a, b) in pairs): for <it> in pairs: a, b = <it>   (like Python's for a, b in pairs)
                    var syms = new List<CLocal>();
                    var unpackTarget = DeclareDeconstruct(fe.Deconstruct, elem, fe.Deconstruct, syms);
                    foreach (var s in syms) { s.IsForEach = true; da.Add(s); }
                    res.Var = new IrSymbol(string.Join("_", syms.Select(s => s.Name)), IrSymbolKind.Local, elem.Ir()) { Tag = PyLowering.TupleForTag, DeclSpan = Sp(fe.Deconstruct) };
                    res.VarSpan = Sp(fe.Deconstruct);
                    var daT = new HashSet<CLocal>(da);
                    var targetT = new BreakTarget { Node = fe };
                    fn.Targets.Add(targetT);
                    res.Body = Block(fe.Body);
                    fn.Targets.RemoveAt(fn.Targets.Count - 1);
                    res.Body.Stmts.Insert(0, new IrAssign { Target = unpackTarget, Value = new IrName(res.Var) { Span = Sp(fe.Deconstruct), Synthetic = true }, Synthetic = true, Span = Sp(fe.Deconstruct) });
                    PopScope();
                    da = daT;
                    reachable = true;
                    return res;
                }
                for (int i = 0; i < items.Count && i < 2; i++)
                {
                    if (!(items[i] is CName n)) { if (!(items[i] is CDiscard)) Error("nested deconstruction is not supported here in JavaFarmer", items[i]); continue; }
                    CheckIdentifier(n.Name, n.Start, n.End);
                    CheckRedeclare(n.Name, n.Start, n.End);
                    var t = parts != null && i < parts.Length ? parts[i] : CType.Error;
                    var sym = DeclareLocal(n.Name, t, false, n.Start, n.End);
                    sym.IsForEach = true;
                    da.Add(sym);
                    if (i == 0) { res.Var = sym.Ir; res.VarSpan = Sp(n); }
                    else { res.ValueVar = sym.Ir; res.ValueVarSpan = Sp(n); }
                }
                if (res.Var == null) res.Var = new IrSymbol("_", IrSymbolKind.Local, parts != null ? parts[0].Ir() : IrType.Unknown);
            }
            else
            {
                CheckIdentifier(fe.Name, fe.NameStart, fe.NameEnd);
                CType vt;
                if (fe.VarType.Name == "var" && fe.VarType.Args == null && fe.VarType.Ranks.Count == 0) vt = elem;
                else
                {
                    vt = ResolveType(fe.VarType);
                    if (!elem.IsError && !vt.IsError && !CType.Assignable(elem, vt))
                    {
                        if (elem.Kind == CK.Char && vt.Kind == CK.String) Error("CS0030: Cannot convert type 'char' to 'string'", fe.VarType);
                        else if (!(elem.Kind == CK.Double && vt.Kind == CK.Int)) Error("CS0030: Cannot convert type '" + elem + "' to '" + vt + "'", fe.VarType);
                        else Error("foreach with an int variable over doubles is not supported in JavaFarmer (it truncates)", fe.VarType);
                    }
                }
                CheckRedeclare(fe.Name, fe.NameStart, fe.NameEnd);
                var sym = DeclareLocal(fe.Name, vt, false, fe.NameStart, fe.NameEnd);
                sym.IsForEach = true;
                da.Add(sym);
                if (vt.Kind == CK.KeyValue)
                {
                    kv = sym;
                    sym.KeyIr = new IrSymbol(fe.Name + "Key", IrSymbolKind.Local, vt.Key.Ir()) { DeclSpan = Sp(fe.NameStart, fe.NameEnd) };
                    sym.ValueIr = new IrSymbol(fe.Name + "Value", IrSymbolKind.Local, vt.Elem.Ir()) { DeclSpan = Sp(fe.NameStart, fe.NameEnd) };
                    res.Var = sym.KeyIr;
                }
                else res.Var = sym.Ir;
                res.VarSpan = Sp(fe.NameStart, fe.NameEnd);
            }
            var da0 = new HashSet<CLocal>(da);
            var target = new BreakTarget { Node = fe };
            fn.Targets.Add(target);
            res.Body = Block(fe.Body);
            fn.Targets.RemoveAt(fn.Targets.Count - 1);
            PopScope();
            da = da0;
            reachable = true;
            if (kv != null && kv.ValueUsed) { res.ValueVar = kv.ValueIr; res.ValueVarSpan = res.VarSpan; }
            return res;
        }

        void LocalVar(CLocalVar lv, List<IrStmt> outList)
        {
            bool isVar = lv.Type.Name == "var" && lv.Type.Args == null && lv.Type.Ranks.Count == 0 && !lv.Type.Nullable && !lv.Type.IsTuple;
            CType declared = isVar ? null : ResolveType(lv.Type);
            if (isVar && lv.IsConst) Error("CS0822: Implicitly-typed variables cannot be constant", lv.Type);
            if (isVar && lv.Vars.Count > 1) Error("CS0819: Implicitly-typed variables cannot have multiple declarators", lv.Type);
            foreach (var v in lv.Vars)
            {
                CheckIdentifier(v.Name, v.NameStart, v.NameEnd);
                CType t = declared;
                IrExpr init = null;
                List<IrStmt> switchStmts = null;
                CSwitchExpr swx = Unparen(v.Init) as CSwitchExpr;
                if (isVar && v.Init == null) { Error("CS0818: Implicitly-typed variables must be initialized", v.NameStart, v.NameEnd); t = CType.Error; }
                if (lv.IsConst && v.Init == null) Error("CS0145: A const field requires a value to be provided", v.NameStart, v.NameEnd);
                if (v.Init != null)
                {
                    if (v.Init is CArrayInit ai && isVar) { Error("CS0820: Cannot initialize an implicitly-typed variable with an array initializer", v.NameStart, v.NameEnd); t = CType.Error; }
                    else if (swx != null) { /* bound after the declaration below */ }
                    else
                    {
                        init = Initializer(v.Init, t);
                        if (isVar)
                        {
                            t = v.Init.Type ?? CType.Error;
                            if (t.Kind == CK.Null) { Error("CS0815: Cannot assign <null> to an implicitly-typed variable", v.NameStart, v.Init.End); t = CType.Error; }
                            else if (t.Kind == CK.Void) { Error("CS0815: Cannot assign void to an implicitly-typed variable", v.NameStart, v.Init.End); t = CType.Error; }
                            else if (t.Kind == CK.Delegate && v.Init is CLambda == false && false) { }
                        }
                        if (lv.IsConst && !IsConstantExpr(v.Init)) Error("CS0133: The expression being assigned to '" + v.Name + "' must be constant", v.Init);
                    }
                }
                CheckRedeclare(v.Name, v.NameStart, v.NameEnd);
                if (swx != null && isVar) t = SwitchExprType(swx, null);
                var sym = DeclareLocal(v.Name, t ?? CType.Error, lv.IsConst, v.NameStart, v.NameEnd);
                if (lv.IsConst && v.Init != null) ConstValueOf(v.Init, out sym.ConstInt, out sym.ConstStr);
                var d = new IrVarDecl { Symbol = sym.Ir, NameSpan = Sp(v.NameStart, v.NameEnd), Span = Sp(v), TypeExplicit = !isVar, Init = init };
                outList.Add(d);
                if (swx != null)
                {
                    switchStmts = SwitchExprAssign(swx, t ?? CType.Error, e => new IrAssign { Target = new IrName(sym.Ir) { Span = Sp(v.NameStart, v.NameEnd) }, Value = e, OpSpan = Sp(v.NameStart, v.NameEnd), Span = e.Span }, true);
                    outList.AddRange(switchStmts);
                }
                if (v.Init != null) da.Add(sym);
            }
        }

        static CExpr Unparen(CExpr e) { while (e is CParen p) e = p.Inner; return e; }

        /// <summary>An initializer / assigned value against a target type (null target = infer).</summary>
        IrExpr Initializer(CExpr init, CType target)
        {
            if (init is CArrayInit ai)
            {
                if (target == null || target.IsError) { foreach (var it in ai.Items) X(it); return IrLiteral.Null(); }
                return ArrayInit(ai, target);
            }
            if (Unparen(init) is CSwitchExpr sx)
            {
                Error("a switch expression is only supported as a whole value (variable initializer, assignment or return) in JavaFarmer", sx.KwStart, sx.KwEnd);
                return IrLiteral.Null();
            }
            var e = X(init, target);
            if (target != null) RequireAssignable(init.Type, target, init);
            return e;
        }

        void RequireAssignable(CType from, CType to, CNode at)
        {
            if (from == null || to == null || from.IsError || to.IsError) return;
            if (from.Kind == CK.Void) { Error("CS0029: Cannot implicitly convert type 'void' to '" + to + "'", at); return; }
            if (!CType.Assignable(from, to)) Error(IncompatibleMsg(from, to), at);
        }

        internal static string IncompatibleMsg(CType from, CType to)
        {
            if (from.Kind == CK.Null) return "CS0037: Cannot convert null to '" + to + "' because it is a non-nullable value type";
            if (from.Kind == CK.Char && to.IsNumeric) return "converting a char to a number is not supported in JavaFarmer";
            if ((from.Kind == CK.Double && to.Kind == CK.Int) || (from.Kind == CK.Object && !to.IsError))
                return "CS0266: Cannot implicitly convert type '" + from + "' to '" + to + "'. An explicit conversion exists (are you missing a cast?)";
            return "CS0029: Cannot implicitly convert type '" + from + "' to '" + to + "'";
        }

        IrExpr Cond(CExpr e)
        {
            if (Unparen(e) is CAssign ca && ca.Op == "=")
            {
                ResolveTarget(ca.Target, false, out _);
                X(ca.Value);
                var tt = Unparen(ca.Target).Type ?? CType.Error;
                if (!tt.IsError && tt.Kind != CK.Bool) { Error("CS0029: Cannot implicitly convert type '" + tt + "' to 'bool' (to compare, write ==)", e); return null; }
                Error("using an assignment as a value is not supported in JavaFarmer; to compare, write ==", e);
                return null;
            }
            var x = X(e, CType.Bool);
            var t = e.Type ?? CType.Error;
            if (!t.IsError && t.Kind != CK.Bool)
                Error("CS0029: Cannot implicitly convert type '" + t + "' to 'bool'", e);
            return x;
        }

        List<IrStmt> Return(CReturn r)
        {
            var res = new List<IrStmt>();
            if (fn.IsFieldInit) { Error("CS1525: Invalid expression term 'return'", r); return res; }
            if (fn.IsTopLevel) { Error("return in top-level statements is not supported in JavaFarmer", r); return res; }
            if (fn.Lambda != null && fn.ReturnType == null)
            {
                if (r.Value != null)
                {
                    if (Unparen(r.Value) is CSwitchExpr sx0) { Error("a switch expression is only supported as a whole value (variable initializer, assignment or return) in JavaFarmer", sx0.KwStart, sx0.KwEnd); return res; }
                    var v = X(r.Value);
                    if (r.Value.Type.Kind == CK.Void) Error("CS8030: Anonymous function converted to a void returning delegate cannot return a value", r.Value);
                    fn.InferredReturns.Add(r.Value.Type);
                    res.Add(new IrReturn { Value = v, Span = Sp(r) });
                }
                else res.Add(new IrReturn { Span = Sp(r) });
                return res;
            }
            var rt = fn.ReturnType ?? CType.Void;
            if (rt.Kind == CK.Void)
            {
                if (r.Value != null)
                {
                    X(r.Value);
                    Error(fn.Lambda != null ? "CS8030: Anonymous function converted to a void returning delegate cannot return a value" : "CS0127: Since '" + fn.Display + "' returns void, a return keyword must not be followed by an object expression", r.Start, r.Start + 6);
                }
                res.Add(new IrReturn { Span = Sp(r) });
                return res;
            }
            if (r.Value == null) { Error("CS0126: An object of a type convertible to '" + rt + "' is required", r); return res; }
            return ReturnValue(r.Value, Sp(r));
        }

        /// <summary>"return value;" (also expression-bodied members): a switch expression becomes a switch of returns.</summary>
        List<IrStmt> ReturnValue(CExpr value, Span span)
        {
            var rt = fn.ReturnType ?? CType.Void;
            if (Unparen(value) is CSwitchExpr sx)
                return SwitchExprAssign(sx, rt, e => new IrReturn { Value = e, Span = e.Span }, false);
            var v = X(value, rt);
            RequireAssignable(value.Type, rt, value);
            return new List<IrStmt> { new IrReturn { Value = v, Span = span } };
        }

        // ------------------------------------------------------------------ switch statement
        IrSwitch Switch(CSwitch sw)
        {
            var sel = X(sw.Selector);
            var st = sw.Selector.Type ?? CType.Error;
            bool selOk = st.IsError || st.Kind == CK.Int || st.Kind == CK.String || st.Kind == CK.Char || st.Kind == CK.Bool || st.Kind == CK.Game;
            if (!selOk) Error("switch on a value of type '" + st + "' is not supported in JavaFarmer (use int, string, char or Direction)", sw.Selector);
            var res = new IrSwitch { Selector = sel, KwSpan = Sp(sw.Start, sw.KwEnd), Span = Sp(sw), Style = IrSwitchStyle.Colon };
            foreach (var sec in sw.Sections) MarkTailBreak(sec.Body);
            var labels = new HashSet<string>();
            bool hasDefault = false;
            var da0 = new HashSet<CLocal>(da);
            var target = new BreakTarget { Node = sw, IsSwitch = true };
            fn.Targets.Add(target);
            HashSet<CLocal> daOut = null;
            bool anyCompletes = false;
            PushScope();   // all sections share one declaration space
            for (int i = 0; i < sw.Sections.Count; i++)
            {
                var sec = sw.Sections[i];
                var ic = new IrCase { Span = Sp(sec), LabelSpan = Sp(sec.Labels[0].Start, sec.Labels[sec.Labels.Count - 1].LabelEnd) };
                foreach (var lab in sec.Labels)
                {
                    if (lab.IsDefault)
                    {
                        if (hasDefault) Error("CS0152: The switch statement contains multiple cases with the label value 'default'", lab.Start, lab.LabelEnd);
                        hasDefault = true;
                        ic.IsDefault = true;
                        continue;
                    }
                    foreach (var l in lab.Values)
                    {
                        var lx = Label(l, st, labels);
                        if (lx != null) ic.Labels.Add(lx);
                    }
                }
                da = new HashSet<CLocal>(da0);
                reachable = true;
                ic.Body = new IrBlock { Span = Sp(sec) };
                foreach (var s in sec.Body) Stmt(s, ic.Body.Stmts, true);
                bool endsWithBreak = LastIsStrippedBreak(sec.Body);
                if (reachable && !endsWithBreak)
                {
                    var lab = sec.Labels[sec.Labels.Count - 1];
                    var text = source.Substring(lab.Start, Math.Max(0, lab.LabelEnd - lab.Start));
                    if (i < sw.Sections.Count - 1) Error("CS0163: Control cannot fall through from one case label ('" + text + "') to another", lab.Start, lab.LabelEnd);
                    else Error("CS8070: Control cannot fall out of switch from final case label ('" + text + "')", lab.Start, lab.LabelEnd);
                }
                if (endsWithBreak) { anyCompletes = true; daOut = daOut == null ? new HashSet<CLocal>(da) : new HashSet<CLocal>(daOut.Intersect(da)); }
                res.Cases.Add(ic);
            }
            PopScope();
            fn.Targets.RemoveAt(fn.Targets.Count - 1);
            reachable = !hasDefault || anyCompletes || target.HasBreak || sw.Sections.Count == 0;
            da = hasDefault && daOut != null ? daOut : da0;
            return res;
        }

        IrExpr Label(CExpr l, CType st, HashSet<string> seen)
        {
            var lx = X(l, st);
            var lt = l.Type ?? CType.Error;
            if (!IsConstantExpr(l)) { Error(st.IsError ? "CS0150: A constant value is expected" : "CS9135: A constant value of type '" + st + "' is expected" + (st.Kind == CK.Game && st.Game != "Direction" ? " (game constants like Entities.Carrot are not compile-time constants in C#: use if / else if)" : ""), l); return null; }
            if (!lt.IsError && !st.IsError && !CType.Assignable(lt, st) && !(lt.IsNumeric && st.IsNumeric))
            { Error("CS0029: Cannot implicitly convert type '" + lt + "' to '" + st + "'", l); return null; }
            var key = LabelKey(l);
            if (key != null && !seen.Add(key))
            {
                Error("CS0152: The switch statement contains multiple cases with the label value '" + source.Substring(l.Start, l.End - l.Start) + "'", l);
                return null;
            }
            return lx;
        }

        static void MarkTailBreak(List<CStmt> body)
        {
            if (body.Count == 0) return;
            var last = body[body.Count - 1];
            if (last is CBreak br) br.Strip = true;
            else if (last is CBlock b) MarkTailBreak(b.Stmts);
        }

        static bool LastIsStrippedBreak(List<CStmt> body)
        {
            if (body.Count == 0) return false;
            var last = body[body.Count - 1];
            if (last is CBreak br) return br.Strip;
            if (last is CBlock b) return LastIsStrippedBreak(b.Stmts);
            return false;
        }

        /// <summary>Compile-time constants: literals, -literal, const locals / fields, Direction members.</summary>
        bool IsConstantExpr(CExpr e)
        {
            e = Unparen(e);
            switch (e)
            {
                case CLiteral _: return true;
                case CUnary u: return (u.Op == "-" || u.Op == "+") && IsConstantExpr(u.Operand);
                case CBinary b: return b.Op != "&&" && b.Op != "||" && IsConstantExpr(b.Left) && IsConstantExpr(b.Right);
                case CInterp ip: return ip.Parts.All(p => p is string);
                case CName n:
                    {
                        if (CsBinderConst.Directions.Contains(n.Name) && scope?.Lookup(n.Name) == null) return true;
                        var l = scope?.Lookup(n.Name);
                        if (l != null) return l.IsConst;
                        if (Own.Fields.TryGetValue(n.Name, out var f)) return f.IsConst;
                        return false;
                    }
                case CMember m:
                    {
                        if (m.Target is CName q)
                        {
                            if ((q.Name == "Direction" || q.Name == "Farm") && CsBinderConst.Directions.Contains(m.Name)) return true;
                            if (q.Name == "int" || q.Name == "Math") return true;
                            if (q.Name == ClassName && Own.Fields.TryGetValue(m.Name, out var f)) return f.IsConst;
                            var o = GetOther(q.Name);
                            if (o != null && o.Fields.TryGetValue(m.Name, out var of)) return of.IsConst;
                        }
                        if (m.Target is CPredefined) return true;
                        return false;
                    }
            }
            return false;
        }

        static string LabelKey(CExpr e)
        {
            e = Unparen(e);
            switch (e)
            {
                case CLiteral l:
                    switch (l.Kind)
                    {
                        case CLitKind.Int: return "n" + l.IntValue;
                        case CLitKind.Double: return "n" + l.DoubleValue;
                        case CLitKind.String: return "s" + l.StrValue;
                        case CLitKind.Char: return "s" + l.StrValue;
                        case CLitKind.Bool: return "b" + l.BoolValue;
                    }
                    return null;
                case CUnary u when u.Op == "-" && u.Operand is CLiteral ul && ul.Kind == CLitKind.Int: return "n-" + ul.IntValue;
                case CMember m when m.Target is CName tn && (tn.Name == "Direction" || tn.Name == "Farm"): return "d" + m.Name;
                case CName n when CsBinderConst.Directions.Contains(n.Name): return "d" + n.Name;
                case CName n: return "v" + n.Name;
            }
            return null;
        }

        void ConstValueOf(CExpr e, out long? i, out string s)
        {
            i = null; s = null;
            e = Unparen(e);
            if (e is CLiteral l)
            {
                if (l.Kind == CLitKind.Int) i = l.IntValue;
                else if (l.Kind == CLitKind.String) s = l.StrValue;
            }
            else if (e is CUnary u && u.Op == "-" && u.Operand is CLiteral ul && ul.Kind == CLitKind.Int) i = -ul.IntValue;
        }
    }

    internal static class CsBinderConst
    {
        public static readonly HashSet<string> Directions = new HashSet<string> { "North", "East", "South", "West" };
        public static readonly Dictionary<string, string> ConstBags = new Dictionary<string, string>
        {
            { "Entities", "Entity" }, { "Items", "Item" }, { "Grounds", "Ground" }, { "Unlocks", "Unlock" },
            { "Hats", "Hat" }, { "Leaderboards", "Leaderboard" },
        };
    }
}
