using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler.CSharp
{
    internal sealed class CLocal
    {
        public string Name;
        public CType Type;
        public bool IsConst;
        public bool IsParam;
        public bool IsForEach;          // foreach iteration variable (read-only)
        public bool TopLevel;           // declared directly in the top-level statements
        public IrSymbol Ir;
        public FuncCtx Owner;
        public int DeclStart, DeclEnd;
        public long? ConstInt;          // const int value (switch labels)
        public string ConstStr;
        // KeyValuePair loop variable: kv.Key / kv.Value
        public IrSymbol KeyIr, ValueIr;
        public bool ValueUsed;
    }

    internal sealed class CFieldSym
    {
        public string Name;
        public CType Type;
        public bool IsConst, IsReadonly, IsAccessible;
        public string FileName;
        public string ClassName;
        public IrSymbol Ir;
        public string PyName;
        public long? ConstInt;
        public string ConstStr;
    }

    internal sealed class CMethodSym
    {
        public string Name;
        public List<CType> ParamTypes = new List<CType>();
        public List<string> ParamNames = new List<string>();
        public List<bool> HasDefault = new List<bool>();
        public CType ReturnType;
        public bool IsAccessible, IsLocal, IsStaticLocal;
        public string FileName;
        public string ClassName;
        public IrSymbol Ir;
        public string PyName;
        public CMethod Decl;
        public int Required => HasDefault.Count(h => !h);
        public string Display => Name + "(" + string.Join(", ", ParamTypes) + ")";
    }

    /// <summary>Declarations of one file (own or another window).</summary>
    internal sealed class CFileDecls
    {
        public string FileName;
        public bool IsClass;           // class form (else top-level statements)
        public string ClassName;
        public bool HasMain;
        /// <summary>Written in another language ("Python", "Java" ...); null for C# windows.</summary>
        public string ForeignLanguage;
        /// <summary>Why a foreign window could not be read (shown at the use site).</summary>
        public string LoadError;
        public Dictionary<string, CFieldSym> Fields = new Dictionary<string, CFieldSym>();
        public Dictionary<string, CMethodSym> Methods = new Dictionary<string, CMethodSym>();
    }

    internal sealed class BreakTarget { public CNode Node; public bool IsSwitch; public bool HasBreak; }

    internal sealed class FuncCtx
    {
        public FuncCtx Parent;
        public CMethodSym Method;
        public CLambda Lambda;
        public bool IsTopLevel;        // the top-level statements
        public bool IsFieldInit;
        public bool IsStatic;          // static local function / static lambda: no captures
        public CType ReturnType;       // null: infer (lambda)
        public List<CType> InferredReturns;
        public string Display;
        public List<BreakTarget> Targets = new List<BreakTarget>();
    }

    internal sealed class CScope
    {
        public readonly CScope Parent;
        public readonly Dictionary<string, CLocal> Vars = new Dictionary<string, CLocal>();
        /// <summary>Names declared later in this block (CS0841 "used before declared").</summary>
        public HashSet<string> Later;
        public readonly FuncCtx Fn;
        public CScope(CScope parent, FuncCtx fn) { Parent = parent; Fn = fn; }
        public CLocal Lookup(string name)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.Vars.TryGetValue(name, out var v)) return v;
            return null;
        }
        public bool DeclaredLater(string name)
        {
            for (var s = this; s != null; s = s.Parent)
                if (s.Later != null && s.Later.Contains(name)) return true;
            return false;
        }
    }

    /// <summary>Caches the declarations of other windows (they rarely change between keystrokes).</summary>
    internal static class CsDeclCache
    {
        static readonly Dictionary<string, CFileDecls> cache = new Dictionary<string, CFileDecls>();
        static readonly object gate = new object();
        static int apiVersion = -1;

        public static CFileDecls Get(string name, string source)
        {
            var key = name + "\0" + source;
            lock (gate)
            {
                if (apiVersion != GameApi.Version) { cache.Clear(); apiVersion = GameApi.Version; }
                if (cache.TryGetValue(key, out var fs)) return fs;
            }
            CFileDecls result;
            var cu = CsParser.Parse(source, out _);
            if (cu == null) result = new CFileDecls { FileName = name };
            else
            {
                try
                {
                    var b = new CsBinder(cu, name, null, source);
                    b.DeclareOnly();
                    result = b.Own;
                }
                catch { result = new CFileDecls { FileName = name }; }
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
