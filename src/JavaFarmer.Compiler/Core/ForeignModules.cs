using System;
using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    // Cross-language imports (docs/LANGUAGE-GUIDE.md "Cross-language imports").
    // Every code window has its own language (chosen by its file extension). A file may use another file's functions and
    // globals whatever language that file is written in: the plugin passes the other files' texts (CompileOptions.OtherFiles)
    // and their language ids (CompileOptions.OtherFileLanguages). A frontend resolves a file written in ANOTHER language
    // through ForeignModules.Resolve, which reads it with that language's own frontend and returns a neutral signature list.

    /// <summary>The top-level declarations of one file, in neutral IR types (Unknown/Any where the language has no types).</summary>
    public sealed class ModuleSignature
    {
        /// <summary>Module (window) name, without extension: what other files write ("Utils" in Utils.f()).</summary>
        public string Module;
        public string LanguageId;
        /// <summary>"Rust", "Python" ... (for messages).</summary>
        public string LanguageName;
        /// <summary>False when the file could not be read; Error says why (ready to show to the player).</summary>
        public bool Ok;
        public string Error;
        public List<FunctionSignature> Functions = new List<FunctionSignature>();
        public List<GlobalSignature> Globals = new List<GlobalSignature>();

        public FunctionSignature Function(string name) => Functions.FirstOrDefault(f => f.Name == name);
        public GlobalSignature Global(string name) => Globals.FirstOrDefault(g => g.Name == name);

        public static ModuleSignature Failed(string module, string languageId, string error) =>
            new ModuleSignature { Module = module, LanguageId = languageId, LanguageName = Languages.Get(languageId)?.DisplayName ?? languageId, Ok = false, Error = error };
    }

    public sealed class FunctionSignature
    {
        /// <summary>Source name (as written in the other file).</summary>
        public string Name;
        /// <summary>Game Python name the other file's compiled code defines (use it for IrSymbol.PyName).</summary>
        public string PyName;
        public List<ParamSignature> Params = new List<ParamSignature>();
        public IrType ReturnType = IrType.Unknown;
        public bool IsPrivate;
        /// <summary>The other file's entry point (main): callable, but rarely meant to be.</summary>
        public bool IsEntry;
        public int MinArgs => Params.Count(p => !p.Optional);
    }

    public sealed class ParamSignature
    {
        public string Name;
        public IrType Type = IrType.Unknown;
        /// <summary>Has a default value (Python "def f(a = 1)").</summary>
        public bool Optional;
    }

    public sealed class GlobalSignature
    {
        public string Name;
        public string PyName;
        public IrType Type = IrType.Unknown;
        public bool IsConst;
        public bool IsPrivate;
    }

    /// <summary>
    /// Optional ILanguage extension: the top-level declarations of a source file WITHOUT checking function bodies.
    /// Cheaper than ToIr, and it does not need the file's own imports, so files that import each other (a cycle) can still
    /// see each other's declarations. Languages without it are read with ToIr (bodies checked; a cycle then fails cleanly).
    /// </summary>
    public interface ISignatureProvider
    {
        /// <summary>Never throws. Ok = false plus Error when the declarations cannot be read.</summary>
        ModuleSignature Signatures(string source, CompileOptions options);
    }

    /// <summary>Resolves other files of ANY language into signatures (cached by language + name + text). Thread-safe, never throws.</summary>
    public static class ForeignModules
    {
        static readonly Dictionary<string, ModuleSignature> cache = new Dictionary<string, ModuleSignature>();
        static readonly object gate = new object();
        [ThreadStatic] static HashSet<string> inProgress;

        /// <summary>Language id of another file (CompileOptions.OtherFileLanguages), or null when not given.</summary>
        public static string LanguageOf(CompileOptions options, string module)
        {
            if (options?.OtherFileLanguages == null || module == null) return null;
            return options.OtherFileLanguages.TryGetValue(module, out var id) && !string.IsNullOrEmpty(id) ? id : null;
        }

        /// <summary>
        /// True when the other file is written in a language different from ownLanguageId. A file without a language entry
        /// is treated as written in the caller's own language (single-language callers keep their old behaviour).
        /// </summary>
        public static bool IsForeign(CompileOptions options, string module, string ownLanguageId)
        {
            var id = LanguageOf(options, module);
            return id != null && !string.Equals(id, ownLanguageId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Declarations of another file (options = the CALLER's options: its OtherFiles / OtherFileLanguages must contain
        /// the module). Returns Ok = false with a clear message when the file is missing, its language is unknown or it
        /// does not compile.
        /// </summary>
        public static ModuleSignature Resolve(string module, CompileOptions options)
        {
            try
            {
                string source = null;
                if (options?.OtherFiles == null || !options.OtherFiles.TryGetValue(module ?? "", out source) || source == null)
                    return ModuleSignature.Failed(module, null, "cannot find file " + module);
                var id = LanguageOf(options, module) ?? "python";
                var others = new Dictionary<string, string>();
                var otherLangs = new Dictionary<string, string>();
                foreach (var kv in options.OtherFiles) if (kv.Key != module) others[kv.Key] = kv.Value;
                if (options.OtherFileLanguages != null)
                    foreach (var kv in options.OtherFileLanguages) if (kv.Key != module) otherLangs[kv.Key] = kv.Value;
                return Resolve(module, source, id, new CompileOptions { FileName = module, OtherFiles = others, OtherFileLanguages = otherLangs });
            }
            catch (Exception ex)
            {
                return ModuleSignature.Failed(module, null, "internal JavaFarmer error while reading " + module + ": " + ex.Message);
            }
        }

        /// <summary>Declarations of a source text in the given language (options.FileName = its module name).</summary>
        public static ModuleSignature Resolve(string module, string source, string languageId, CompileOptions options)
        {
            var lang = Languages.Get(languageId);
            if (lang == null)
                return ModuleSignature.Failed(module, languageId, module + " is written in '" + languageId + "', which this JavaFarmer build does not support");
            string key = lang.Id + "\0" + module + "\0" + (source ?? "");
            lock (gate)
            {
                if (cache.TryGetValue(key, out var hit)) return hit;
            }
            if (inProgress == null) inProgress = new HashSet<string>();
            if (!inProgress.Add(key))
                return ModuleSignature.Failed(module, lang.Id, module + " (" + lang.DisplayName + ") imports this file back: " + lang.DisplayName + " cannot read files that import each other yet");
            ModuleSignature sig;
            try
            {
                options = options ?? new CompileOptions { FileName = module };
                if (lang is ISignatureProvider sp) sig = sp.Signatures(source ?? "", options);
                else
                {
                    var ir = lang.ToIr(source ?? "", options, out var errs);
                    if (ir == null)
                    {
                        var d = errs?.FirstOrDefault();
                        sig = ModuleSignature.Failed(module, lang.Id, module + " (" + lang.DisplayName + ") does not compile" +
                            (d != null ? " (line " + LineOf(source, d.Start) + ": " + d.Message + ")" : ""));
                    }
                    else sig = FromIr(ir, lang);
                }
                if (sig == null) sig = ModuleSignature.Failed(module, lang.Id, "could not read " + module);
                sig.Module = module;
                sig.LanguageId = lang.Id;
                sig.LanguageName = lang.DisplayName;
            }
            catch (Exception ex)
            {
                sig = ModuleSignature.Failed(module, lang.Id, "internal JavaFarmer error while reading " + module + " (" + lang.DisplayName + "): " + ex.Message);
            }
            finally
            {
                inProgress.Remove(key);
            }
            lock (gate)
            {
                if (cache.Count > 128) cache.Clear();
                cache[key] = sig;
            }
            return sig;
        }

        /// <summary>Signatures of a lowered module (functions, IrGlobals and top-level script variables).</summary>
        public static ModuleSignature FromIr(IrModule m, ILanguage lang)
        {
            var sig = new ModuleSignature { Module = m?.Name, LanguageId = lang?.Id, LanguageName = lang?.DisplayName, Ok = m != null };
            if (m == null) return sig;
            bool native = lang?.IsNative == true;
            var fnNames = m.Functions.Where(f => f.Symbol != null).Select(f => f.Symbol.Name).ToList();
            var globals = new List<IrSymbol>();
            var seen = new HashSet<string>();
            foreach (var g in m.Globals) if (g.Symbol != null && seen.Add(g.Symbol.Name)) globals.Add(g.Symbol);
            foreach (var st in m.TopLevel)
            {
                IrSymbol s = null;
                if (st is IrVarDecl vd) s = vd.Symbol;
                else if (st is IrAssign a && a.Target is IrName n) s = n.Symbol;
                if (s != null && (s.Kind == IrSymbolKind.Global || s.Kind == IrSymbolKind.Local) && seen.Add(s.Name)) globals.Add(s);
            }
            var fnPy = PyNaming.ModuleLevel(fnNames, globals.Select(g => g.Name), out var globalPy);
            foreach (var f in m.Functions)
            {
                if (f.Symbol == null) continue;
                var fs = new FunctionSignature
                {
                    Name = f.Symbol.Name,
                    PyName = native ? (f.Symbol.PyName ?? f.Symbol.Name) : f.Symbol.PyName ?? fnPy[f.Symbol.Name],
                    ReturnType = f.ReturnType ?? IrType.Unknown,
                    IsPrivate = f.IsPrivate,
                    IsEntry = f.IsEntry,
                };
                foreach (var p in f.Params)
                    fs.Params.Add(new ParamSignature { Name = p.Symbol?.Name, Type = p.Symbol?.Type ?? IrType.Unknown, Optional = p.Default != null });
                sig.Functions.Add(fs);
            }
            foreach (var g in globals)
            {
                var decl = m.Globals.FirstOrDefault(x => x.Symbol == g);
                sig.Globals.Add(new GlobalSignature
                {
                    Name = g.Name,
                    PyName = native ? (g.PyName ?? g.Name) : g.PyName ?? globalPy[g.Name],
                    Type = g.Type ?? IrType.Unknown,
                    IsConst = g.IsConst,
                    IsPrivate = decl?.IsPrivate ?? false,
                });
            }
            return sig;
        }

        /// <summary>
        /// Fills Type / ParamTypes of the module's external symbols (calls into other files) from the other files'
        /// signatures, when the frontend left them Unknown. For dynamic frontends (Python, JS, R) before type inference.
        /// </summary>
        public static void AnnotateExternals(IrModule m, CompileOptions options)
        {
            if (m == null || options?.OtherFiles == null) return;
            try
            {
                var sigs = new Dictionary<string, ModuleSignature>();
                foreach (var n in IrWalk.Descendants(m))
                {
                    IrSymbol s = null;
                    if (n is IrName nm) s = nm.Symbol;
                    else if (n is IrCall c) s = c.Function;
                    if (s == null || !s.IsExternal || string.IsNullOrEmpty(s.Module) || !options.OtherFiles.ContainsKey(s.Module)) continue;
                    if (!sigs.TryGetValue(s.Module, out var sig)) sigs[s.Module] = sig = Resolve(s.Module, options);
                    if (sig == null || !sig.Ok) continue;
                    if (s.Kind == IrSymbolKind.ExternalFunction)
                    {
                        var f = sig.Function(s.Name);
                        if (f == null) continue;
                        if (s.Type == null || !s.Type.IsKnown || (s.Type.Kind == IrTypeKind.Func && !(s.Type.Elem?.IsKnown ?? false)))
                            s.Type = IrType.Func(f.ReturnType, f.Params.Select(p => p.Type).ToArray());
                        if (s.ParamTypes == null) s.ParamTypes = f.Params.Select(p => p.Type).ToList();
                    }
                    else
                    {
                        var g = sig.Global(s.Name);
                        if (g != null && (s.Type == null || !s.Type.IsKnown)) s.Type = g.Type;
                    }
                }
            }
            catch (Exception) { /* best effort */ }
        }

        static int LineOf(string src, int offset)
        {
            int line = 1;
            for (int i = 0; i < Math.Min(offset, (src ?? "").Length); i++) if (src[i] == '\n') line++;
            return line;
        }

        /// <summary>For tests.</summary>
        public static void ClearCache() { lock (gate) cache.Clear(); }
    }
}
