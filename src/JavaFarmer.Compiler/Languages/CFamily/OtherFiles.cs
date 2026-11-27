using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CFamily
{
    /// <summary>
    /// Declarations of the other code windows (functions and globals they define), parsed once per source text.
    /// A prototype / extern declaration in this file resolves against them (like the linker would).
    /// </summary>
    internal sealed class OtherFiles
    {
        internal sealed class Decls
        {
            public Dictionary<string, FnSig> Functions = new Dictionary<string, FnSig>();
            public Dictionary<string, CT> Globals = new Dictionary<string, CT>();
            public Dictionary<string, string> FnPy, GlobalPy;
            /// <summary>Display name of the language when the window is not C / C++ of this dialect (null: same language).</summary>
            public string Foreign;
        }

        static readonly object gate = new object();
        static readonly Dictionary<string, Decls> cache = new Dictionary<string, Decls>();

        readonly Dialect d;
        readonly List<(string Module, Decls D)> files = new List<(string, Decls)>();

        /// <summary>Other windows written in another language that could not be read: module → message for the player.</summary>
        public readonly List<string> ForeignErrors = new List<string>();

        public OtherFiles(Dialect dialect, CompileOptions options)
        {
            d = dialect;
            var others = options?.OtherFiles;
            if (others == null) return;
            var own = d == Dialect.C ? "c" : "cpp";
            foreach (var kv in others.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Key == options.FileName || kv.Value == null) continue;
                if (ForeignModules.IsForeign(options, kv.Key, own))
                {
                    // LANGUAGE-GUIDE §9: a window in another language is read by its own frontend, never parsed as C / C++
                    var sig = ForeignModules.Resolve(kv.Key, options);
                    if (sig == null || !sig.Ok) { ForeignErrors.Add(sig?.Error ?? ("cannot read " + kv.Key)); continue; }
                    files.Add((kv.Key, FromSignature(sig, dialect)));
                    continue;
                }
                files.Add((kv.Key, Get(kv.Key, kv.Value)));
            }
        }

        /// <summary>Declarations of a foreign window. Types the other language does not know (Python, JS, R) are Any: the prototype decides.</summary>
        static Decls FromSignature(ModuleSignature sig, Dialect d)
        {
            var res = new Decls { FnPy = new Dictionary<string, string>(), GlobalPy = new Dictionary<string, string>(), Foreign = sig.LanguageName ?? sig.LanguageId };
            foreach (var f in sig.Functions)
            {
                if (f.IsEntry || f.Name == null) continue;
                var fs = new FnSig { Name = f.Name, Ret = Foreign(f.ReturnType, d), Defined = true };
                foreach (var p in f.Params) { fs.Params.Add(Foreign(p.Type, d)); fs.Defaults.Add(null); }
                fs.ForeignMinArgs = f.MinArgs;
                res.Functions[f.Name] = fs;
                res.FnPy[f.Name] = f.PyName ?? f.Name;
            }
            foreach (var g in sig.Globals)
            {
                if (g.Name == null) continue;
                res.Globals[g.Name] = Foreign(g.Type, d);
                res.GlobalPy[g.Name] = g.PyName ?? g.Name;
            }
            return res;
        }

        static CT Foreign(IrType t, Dialect d)
        {
            if (t == null || !t.IsKnown || t.Kind == IrTypeKind.Any || t.Kind == IrTypeKind.Null) return CT.Any;
            if (t.Kind == IrTypeKind.Void) return CT.Void;
            var c = CT.FromIr(t, d);
            return c ?? CT.Any;
        }

        Decls Get(string module, string src)
        {
            var key = (d == Dialect.C ? "c:" : "cpp:") + module.Length + ":" + module + ":" + src;
            lock (gate) { if (cache.TryGetValue(key, out var x)) return x; }
            var res = new Decls();
            try
            {
                var lex = Lexer.Lex(src, d, false);
                var unit = Parser.ParseUnit(lex, src, d, out _);
                var b = new Binder(d, src, new CompileOptions { FileName = module }, null);
                b.Declarations(unit, res);
                res.FnPy = PyNaming.ModuleLevel(res.Functions.Keys, res.Globals.Keys, out var gp);
                res.GlobalPy = gp;
            }
            catch { }
            lock (gate)
            {
                if (cache.Count > 128) cache.Clear();
                cache[key] = res;
            }
            return res;
        }

        public string ForeignOf(string module) { foreach (var (m, dd) in files) if (m == module) return dd.Foreign; return null; }

        public (FnSig Sig, string Module, string PyName)? FindFunction(string name, string self, Binder from)
        {
            foreach (var (m, dd) in files)
                if (dd.Functions.TryGetValue(name, out var s)) return (s, m, dd.FnPy != null && dd.FnPy.TryGetValue(name, out var p) ? p : null);
            return null;
        }

        public (CT Type, string Module, string PyName)? FindGlobal(string name, string self, Binder from)
        {
            foreach (var (m, dd) in files)
                if (dd.Globals.TryGetValue(name, out var t)) return (t, m, dd.GlobalPy != null && dd.GlobalPy.TryGetValue(name, out var p) ? p : null);
            return null;
        }
    }

    internal sealed partial class Binder
    {
        /// <summary>Declarations-only pass for another code window: exported function signatures and globals.</summary>
        public void Declarations(Unit u, OtherFiles.Decls into)
        {
            foreach (var it in u.Items)
            {
                switch (it)
                {
                    case Include inc: IncludeDirective(inc); break;
                    case UsingNamespace un: if (un.Ns == "std") usingStd = true; break;
                    case FuncDecl f:
                        {
                            if (f.Body == null || f.Static || f.Name == "main") break;
                            var ret = Resolve(f.Ret);
                            if (ret.IsError) break;
                            var sig = new FnSig { Name = f.Name, Ret = ret, Defined = true };
                            bool ok = true;
                            foreach (var p in f.Params)
                            {
                                var pt = Resolve(p.Type, p.D, true);
                                if (pt.IsError) { ok = false; break; }
                                sig.Params.Add(pt);
                                sig.Defaults.Add(null);
                            }
                            if (ok) into.Functions[f.Name] = sig;
                            break;
                        }
                    case VarDecl vd:
                        if (vd.Extern || vd.Static) break;
                        foreach (var dc in vd.Vars)
                        {
                            if (dc.Name == null) continue;
                            var t = Resolve(vd.Type, dc);
                            if (t.IsError || t.Kind == CK.Any) continue;
                            into.Globals[dc.Name] = t;
                        }
                        break;
                }
            }
            Errors.Clear();
        }
    }
}
