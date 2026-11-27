using System.Collections.Generic;
using System.Linq;

namespace JavaFarmer.Compiler
{
    internal sealed class LocalSymbol
    {
        public string Name;
        public string PyName;
        public JType Type;
        public bool IsFinal;
        public bool HasInit;
        public bool IsParam;
        public int Reassignments;
        public int DeclStart, DeclEnd;
        public object Owner;        // the FunctionCtx that declared it
        public bool EffectivelyFinal => IsFinal || Reassignments == 0;
    }

    internal sealed class FieldSymbol
    {
        public string Name;
        public string PyName;
        public JType Type;
        public bool IsFinal;
        public bool IsPrivate;
        public bool IsStatic;
        public string FileName;
        public VarDeclarator Declarator;
    }

    internal sealed class MethodSymbol
    {
        public string Name;
        public string PyName;
        public List<JType> ParamTypes = new List<JType>();
        public List<string> ParamNames = new List<string>();
        public JType ReturnType;
        public bool IsPrivate;
        public bool IsStatic;
        public string FileName;
        public MethodDecl Decl;

        public string Display => Name + "(" + string.Join(",", ParamTypes.Select(t => t.ToString())) + ")";
    }

    /// <summary>Declarations of one file (own or other window).</summary>
    internal sealed class FileSymbols
    {
        public string FileName;
        public bool IsClass;
        public bool HasMain;
        public Dictionary<string, FieldSymbol> Fields = new Dictionary<string, FieldSymbol>();
        public Dictionary<string, MethodSymbol> Methods = new Dictionary<string, MethodSymbol>();
        /// <summary>A file in another language (cross-language import): its language's name ("Rust"), else null.</summary>
        public string ForeignLanguage;
        /// <summary>Set when the other file could not be read (its message is reported at the use site).</summary>
        public string LoadError;
    }

    /// <summary>Python identifier rules: reserved names get a suffix so Java names never clash with game names.</summary>
    internal static class PyNames
    {
        public static readonly HashSet<string> Reserved = new HashSet<string>
        {
            // python / game keywords
            "def", "while", "for", "if", "else", "elif", "and", "or", "not", "True", "False", "None", "pass", "break",
            "continue", "return", "global", "import", "from", "in", "is", "lambda", "class", "try", "except", "finally",
            "raise", "with", "as", "del", "assert", "yield", "nonlocal", "async", "await", "__name__", "__builtins__", "builtins",
            // game constants
            "Entities", "Grounds", "Items", "Unlocks", "Leaderboards", "Hats", "North", "South", "East", "West",
            // builtin methods
            "append", "add", "remove", "pop", "insert",
            // helper prefix is handled separately
        };

        static PyNames()
        {
            foreach (var f in GameApi.AllPythonBuiltins) Reserved.Add(f);
        }

        public static string Ident(string javaName)
        {
            if (Reserved.Contains(javaName)) return javaName + "_j";
            // the game tokenizer only starts identifiers with [a-zA-Z_]
            char c = javaName.Length > 0 ? javaName[0] : 'x';
            bool asciiStart = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_';
            return asciiStart ? javaName : "j_" + javaName;
        }
    }
}
