using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using JavaFarmer.Compiler.ZigImpl;

namespace JavaFarmer.Compiler
{
    /// <summary>
    /// Zig 0.17 as a JavaFarmer language (docs/languages/zig.md): pub fn main() void, const farm = @import("farm"),
    /// farm.canHarvest() / farm.plant(.Carrot), std.debug.print, std.ArrayList / std.AutoHashMap.
    /// </summary>
    public sealed class ZigLanguage : LanguageBase, IPrinterDiagnostics
    {
        public const string StubFileName = "farm.zig";
        public const string Template = "const std = @import(\"std\");\nconst farm = @import(\"farm\");\n\npub fn main() void {\n\t\n}\n";

        public override string Id => "zig";
        public override string DisplayName => "Zig";
        public override string FileExtension => ".zig";
        protected override NamingStyle ApiNaming => NamingStyle.Camel;

        static readonly EditorRules rules = new EditorRules
        {
            IndentAfter = new[] { "{" },
            DedentOn = new[] { "}" },
            LineComment = "//",
            BlockCommentStart = null,
            BlockCommentEnd = null,
            IndentUnit = "\t",
            Brackets = new[] { "{}", "()", "[]" },
        };
        public override EditorRules Editor => rules;

        // ------------------------------------------------------------------ compiler
        protected override IrModule Frontend(string source, CompileOptions options, List<Diagnostic> errors)
        {
            var file = ZigParser.Parse(source, out var perr);
            if (perr != null) { errors.Add(perr); return null; }
            var name = string.IsNullOrEmpty(options?.FileName) ? "main" : options.FileName;
            var m = ZigChecker.Check(file, name, options ?? new CompileOptions { FileName = name }, errors);
            if (m == null) return null;
            if (file.Comments.Count > 0)
            {
                var comments = new List<IrComment>();
                foreach (var t in file.Comments)
                {
                    var text = t.Text;
                    bool doc = (text.StartsWith("///") && !text.StartsWith("////")) || text.StartsWith("//!");
                    comments.Add(new IrComment { Text = text.Substring(doc ? 3 : 2), IsDoc = doc, Span = new Span(t.Start, t.End) });
                }
                IrComments.Attach(m, comments, source);
                IrComments.Tidy(m);
            }
            return m;
        }

        /// <summary>
        /// Compile = ToIr + IrEmitter, except that a container-level var initialised with undefined gets no assignment
        /// (the IR keeps its IrGlobal for printers): "a = 0" would replace a value farm.simulate passed in.
        /// </summary>
        public override CompileResult Compile(string source, CompileOptions options)
        {
            source = source ?? "";
            try
            {
                var ir = ToIr(source, options, out var errors);
                if (ir == null || errors.Count > 0) return CompileResults.Fail(source, errors.Count > 0 ? errors.ToList() : new List<Diagnostic> { new Diagnostic("compilation failed", 0, Math.Min(1, source.Length)) });
                ir.Items.RemoveAll(i => i is IrGlobal g && g.Init == null && g.Symbol?.Tag is ZigTag zt && zt.Undefined);
                return IrEmitter.Compile(ir, source, options);
            }
            catch (Exception ex)
            {
                return CompileResults.Fail(source, new List<Diagnostic> { CompileResults.Internal(ex, source) });
            }
        }

        protected override string PrintImpl(IrModule module, PrintOptions options) => ZigPrinter.Print(module, options, out _);

        public string Print(IrModule module, PrintOptions options, out IReadOnlyList<string> problems)
        {
            try { return ZigPrinter.Print(module, options ?? new PrintOptions(), out problems); }
            catch (Exception ex) { problems = new[] { "internal printer error: " + ex.Message }; return "// JavaFarmer could not print this code: " + ex.Message + "\n"; }
        }

        // ------------------------------------------------------------------ editor
        static readonly HashSet<string> TypeWords = new HashSet<string>(StringComparer.Ordinal)
        {
            "bool", "void", "noreturn", "type", "f16", "f32", "f64", "f80", "f128", "usize", "isize", "comptime_int", "comptime_float", "anyerror", "anyopaque",
            "Entity", "Item", "Ground", "Unlock", "Hat", "Leaderboard", "Direction", "Position", "Companion", "Drone", "ArrayList", "AutoHashMap", "StringHashMap", "Allocator",
        };
        static readonly HashSet<string> ConstWords = new HashSet<string>(StringComparer.Ordinal) { "true", "false", "null", "undefined" };

        public override IReadOnlyList<(int Start, int Length, TokenKind Kind)> Highlight(string source)
        {
            var res = new List<(int, int, TokenKind)>();
            if (string.IsNullOrEmpty(source)) return res;
            try
            {
                var toks = new ZigLexer(source).LexAll();
                ZToken prev = null, prev2 = null;
                foreach (var t in toks)
                {
                    if (t.Kind == ZTk.Eof || t.End <= t.Start) continue;
                    TokenKind k;
                    switch (t.Kind)
                    {
                        case ZTk.Whitespace: k = TokenKind.Whitespace; break;
                        case ZTk.Comment: k = TokenKind.Comment; break;
                        case ZTk.String: case ZTk.MultiString: case ZTk.Char: k = TokenKind.String; break;
                        case ZTk.Int: case ZTk.Float: k = TokenKind.Number; break;
                        case ZTk.Keyword: k = TokenKind.Keyword; break;
                        case ZTk.Builtin: k = TokenKind.Keyword; break;
                        case ZTk.Ident:
                            if (ConstWords.Contains(t.Text)) k = TokenKind.Constant;
                            else if (prev != null && prev.Kind == ZTk.Op && prev.Text == "." && (prev2 == null || !(prev2.Kind == ZTk.Ident || prev2.Kind == ZTk.Op && (prev2.Text == ")" || prev2.Text == "]"))))
                                k = TokenKind.Constant;   // .North enum literal
                            else if (prev != null && prev.Kind == ZTk.Op && prev.Text == "." && prev2 != null && prev2.Kind == ZTk.Ident && IsEnumType(prev2.Text))
                                k = TokenKind.Constant;   // farm.Entity.Carrot
                            else if (TypeWords.Contains(t.Text) || ZType.IntT(t.Text) != null) k = TokenKind.Type;
                            else k = TokenKind.Identifier;
                            break;
                        case ZTk.Op: k = IsPunct(t.Text) ? TokenKind.Punctuation : TokenKind.Operator; break;
                        default: k = TokenKind.Unknown; break;
                    }
                    res.Add((t.Start, t.End - t.Start, k));
                    if (t.Kind != ZTk.Whitespace && t.Kind != ZTk.Comment) { prev2 = prev; prev = t; }
                }
            }
            catch (Exception) { }
            // never leave gaps
            var fixedList = new List<(int, int, TokenKind)>();
            int pos = 0;
            foreach (var (s, l, k) in res.OrderBy(x => x.Item1))
            {
                if (s < pos) continue;
                if (s > pos) fixedList.Add((pos, s - pos, TokenKind.Unknown));
                fixedList.Add((s, l, k));
                pos = s + l;
            }
            if (pos < source.Length) fixedList.Add((pos, source.Length - pos, TokenKind.Unknown));
            return fixedList;
        }

        static bool IsEnumType(string n) => n == "Entity" || n == "Item" || n == "Ground" || n == "Unlock" || n == "Hat" || n == "Leaderboard" || n == "Direction";

        static bool IsPunct(string s)
        {
            switch (s) { case "(": case ")": case "{": case "}": case "[": case "]": case ";": case ",": case ".": case ":": return true; }
            return false;
        }

        static readonly IReadOnlyCollection<string> keywords = ZigLexer.Keywords.OrderBy(x => x, StringComparer.Ordinal).ToList();
        public override IReadOnlyCollection<string> Keywords => keywords;

        public override string NewFileTemplate(string fileName, out int caret)
        {
            caret = Template.IndexOf("\t\n", StringComparison.Ordinal) + 1;
            return Template;
        }

        // ------------------------------------------------------------------ naming
        /// <summary>"can_harvest" → "canHarvest" (used as farm.canHarvest); print → std.debug.print, min/max/abs → @min/@max/@abs.</summary>
        public override string ApiName(string pythonName)
        {
            try
            {
                if (pythonName == null || GameApi.Find(pythonName) == null) return null;
                if (ZigApi.Special.TryGetValue(pythonName, out var sp)) return sp;
                var fs = ZigApi.OfPython(pythonName);
                return fs.Count > 0 ? fs[0].ZigName : GameApi.ToStyle(pythonName, NamingStyle.Camel);
            }
            catch { return null; }
        }

        public override string FromApiName(string name)
        {
            try
            {
                if (string.IsNullOrEmpty(name)) return null;
                if (name.StartsWith("farm.")) name = name.Substring(5);
                foreach (var kv in ZigApi.Special) if (kv.Value == name) return kv.Key;
                if (name == "print" || name == "debug.print") return "print";
                if (name == "quickPrint") return "quick_print";
                var f = ZigApi.Find(name);
                return f?.Builtin.PythonName;
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------ values
        protected override ValueFormat CreateValueFormat() => new ZigValueFormat();

        sealed class ZigValueFormat : ValueFormat
        {
            public ZigValueFormat()
            {
                NullText = "null"; TrueText = "true"; FalseText = "false";
                ListOpen = "{ "; ListClose = " }"; SetOpen = "{ "; SetClose = " }"; MapOpen = "{ "; MapClose = " }";
                ItemSep = ", "; KeyValueSep = ": ";
                QuoteNestedStrings = true; Quote = '"';
                TupleOpen = ".{ "; TupleClose = " }";
                PositionFormat = ".{{ .x = {0}, .y = {1} }}";
                DroneFormat = ".{{ .id = {0} }}";
                ConstantText = (c, n) => "." + n;
                FunctionName = n => n != null && n.StartsWith("__j", StringComparison.Ordinal) ? "fn" : n;
            }

            protected override string FormatTuple(GameTuple t, int depth, HashSet<object> seen)
            {
                var e = t.Items;
                if (e.Count == 2 && e[1] is GameTuple p && p.Items.Count == 2 && IsNum(p.Items[0]) && IsNum(p.Items[1]))
                    return ".{ .entity = " + Format(e[0], depth + 1, seen, true) + ", .x = " + Format(p.Items[0], depth + 1, seen, true) + ", .y = " + Format(p.Items[1], depth + 1, seen, true) + " }";
                return base.FormatTuple(t, depth, seen);
            }

            static bool IsNum(object o) => o is double || o is int || o is long || o is float;
        }

        // ------------------------------------------------------------------ stub
        public override string ApiStubFile(out string fileName)
        {
            fileName = StubFileName;
            try { return ZigApi.Stub(ZigApi.KnownMembers); }
            catch (Exception ex) { return "// farm.zig could not be generated: " + ex.Message + "\n"; }
        }

        /// <summary>The stub with extra constant members (the conformance tests add the members their sources use).</summary>
        public static string BuildStub(IDictionary<string, IEnumerable<string>> extraMembers)
        {
            var all = new Dictionary<string, IEnumerable<string>>();
            foreach (var kv in ZigApi.KnownMembers) all[kv.Key] = kv.Value;
            if (extraMembers != null)
                foreach (var kv in extraMembers)
                    all[kv.Key] = (all.TryGetValue(kv.Key, out var v) ? v : Enumerable.Empty<string>()).Concat(kv.Value).Distinct().ToList();
            return ZigApi.Stub(all);
        }
    }
}
