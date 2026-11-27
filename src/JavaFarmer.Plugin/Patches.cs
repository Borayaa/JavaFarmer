using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using JavaFarmer.Compiler;
using Steamworks;
using TMPro;

namespace JavaFarmer.Plugin
{
    /// <summary>One Harmony patch: target method + prefix/postfix.</summary>
    public sealed class PatchSpec
    {
        public string Name;
        public Func<MethodBase> Target;
        public MethodInfo Prefix;
        public MethodInfo Postfix;
        public MethodInfo Transpiler;
        /// <summary>The target calls Unity internals that CoreCLR cannot re-JIT: tests on .NET 8 skip it (Unity's Mono patches it fine).</summary>
        public bool HeadlessUnsafe;
        /// <summary>If a required patch fails, every patch is removed and the game stays vanilla.</summary>
        public bool Required;
        public override string ToString() => Name;
    }

    /// <summary>All patches the plugin applies (SPEC §9). Tests resolve every target from this list.</summary>
    public static class PatchRegistry
    {
        private static MethodInfo M(string name) => AccessTools.Method(typeof(GamePatches), name);

        public static List<PatchSpec> All()
        {
            var list = new List<PatchSpec>
            {
                new PatchSpec { Name = "Parser.Parse", Required = true,
                    Target = () => AccessTools.Method(typeof(Parser), nameof(Parser.Parse), new[] { typeof(TokenStream), typeof(CodeWindow) }),
                    Prefix = M(nameof(GamePatches.ParsePrefix)) },
                new PatchSpec { Name = "CodeWindow.Parse",
                    Target = () => AccessTools.Method(typeof(CodeWindow), nameof(CodeWindow.Parse), Type.EmptyTypes),
                    Prefix = M(nameof(GamePatches.CodeWindowParsePrefix)) },
                new PatchSpec { Name = "CallNode.Execute", Required = true,
                    Target = () => AccessTools.Method(typeof(CallNode), nameof(CallNode.Execute), new[] { typeof(ProgramState), typeof(Execution), typeof(int) }),
                    Postfix = M(nameof(GamePatches.CallExecutePostfix)) },
                new PatchSpec { Name = "Node.InsertBreakpointsRec",
                    Target = () => AccessTools.Method(typeof(Node), nameof(Node.InsertBreakpointsRec), new[] { typeof(HashSet<int>) }),
                    Prefix = M(nameof(GamePatches.InsertBreakpointsPrefix)) },
                new PatchSpec { Name = "CodeUtilities.SyntaxColor2",
                    Target = () => AccessTools.Method(typeof(CodeUtilities), nameof(CodeUtilities.SyntaxColor2), new[] { typeof(string), typeof(string), typeof(int) }),
                    Prefix = M(nameof(GamePatches.SyntaxColorPrefix)) },
                new PatchSpec { Name = "CodeWindow.OnCodeInserted",
                    Target = () => AccessTools.Method(typeof(CodeWindow), "OnCodeInserted", new[] { typeof(string) }),
                    Postfix = M(nameof(GamePatches.OnCodeInsertedPostfix)) },
                new PatchSpec { Name = "CodeWindow.GetWordList",
                    Target = () => AccessTools.Method(typeof(CodeWindow), "GetWordList", Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.GetWordListPostfix)) },
                new PatchSpec { Name = "CodeWindow.GetSubWordList",
                    Target = () => AccessTools.Method(typeof(CodeWindow), "GetSubWordList", new[] { typeof(string) }),
                    Postfix = M(nameof(GamePatches.GetSubWordListPostfix)) },
                new PatchSpec { Name = "TooltipUtils.GetWordTooltip",
                    Target = () => AccessTools.Method(typeof(TooltipUtils), nameof(TooltipUtils.GetWordTooltip), new[] { typeof(string), typeof(CodeWindow) }),
                    Prefix = M(nameof(GamePatches.WordTooltipPrefix)), Postfix = M(nameof(GamePatches.WordTooltipPostfix)) },
                new PatchSpec { Name = "Workspace.OpenCodeWindow",
                    Target = () => AccessTools.Method(typeof(Workspace), nameof(Workspace.OpenCodeWindow)),
                    Prefix = M(nameof(GamePatches.OpenCodeWindowPrefix)), Postfix = M(nameof(GamePatches.OpenCodeWindowPostfix)) },
                new PatchSpec { Name = "Localizer.LoadDoc",
                    Target = () => AccessTools.Method(typeof(Localizer), nameof(Localizer.LoadDoc), new[] { typeof(string) }),
                    Postfix = M(nameof(GamePatches.LoadDocPostfix)) },
                new PatchSpec { Name = "Localizer.LoadLang",
                    Target = () => AccessTools.Method(typeof(Localizer), nameof(Localizer.LoadLang), new[] { typeof(string) }),
                    Postfix = M(nameof(GamePatches.LoadLangPostfix)) },
                new PatchSpec { Name = "UnlockBox.SetupRec",
                    Target = () => AccessTools.Method(typeof(UnlockBox), nameof(UnlockBox.SetupRec)),
                    Prefix = M(nameof(GamePatches.UnlockBoxPrefix)) },
                new PatchSpec { Name = "CodeUtilities.LocalizeAndFormat",
                    Target = () => AccessTools.Method(typeof(CodeUtilities), nameof(CodeUtilities.LocalizeAndFormat), new[] { typeof(string), typeof(object[]) }),
                    Prefix = M(nameof(GamePatches.LocalizeAndFormatPrefix)) },
                new PatchSpec { Name = "SteamLeaderboard.LoadLeaderboard", Required = true,
                    Target = () => AccessTools.Method(typeof(SteamLeaderboard), nameof(SteamLeaderboard.LoadLeaderboard)),
                    Prefix = M(nameof(GamePatches.LoadLeaderboardPrefix)) },
                new PatchSpec { Name = "SteamUserStats.UploadLeaderboardScore", Required = true,
                    Target = () => AccessTools.Method(typeof(SteamUserStats), nameof(SteamUserStats.UploadLeaderboardScore)),
                    Prefix = M(nameof(GamePatches.UploadScorePrefix)) },
                new PatchSpec { Name = "MarkdownText.GenerateBuiltinsTOC",
                    Target = () => AccessTools.Method(typeof(MarkdownText), "GenerateBuiltinsTOC", Type.EmptyTypes),
                    Prefix = M(nameof(GamePatches.BuiltinsTocPrefix)) },
                new PatchSpec { Name = "CodeUtilities.ToNiceString",
                    Target = () => AccessTools.Method(typeof(CodeUtilities), nameof(CodeUtilities.ToNiceString), new[] { typeof(object), typeof(int), typeof(HashSet<object>), typeof(bool) }),
                    Prefix = M(nameof(GamePatches.ToNiceStringPrefix)) },
                new PatchSpec { Name = "MainSim.EvaluateName",
                    Target = () => AccessTools.Method(typeof(MainSim), nameof(MainSim.EvaluateName), new[] { typeof(string), typeof(IPyObject).MakeByRefType() }),
                    Prefix = M(nameof(GamePatches.EvaluateNamePrefix)) },
                new PatchSpec { Name = "ProgramState.GetTrace",
                    Target = () => AccessTools.Method(typeof(ProgramState), nameof(ProgramState.GetTrace), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.GetTracePostfix)) },
                new PatchSpec { Name = "MainSim.GetCallStack",
                    Target = () => AccessTools.Method(typeof(MainSim), nameof(MainSim.GetCallStack), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.GetCallStackPostfix)) },
                new PatchSpec { Name = "MainSim.BlinkEffect",
                    Target = () => AccessTools.Method(typeof(MainSim), nameof(MainSim.BlinkEffect), new[] { typeof(Node) }),
                    Prefix = M(nameof(GamePatches.BlinkEffectPrefix)) },
                new PatchSpec { Name = "Execution.ApplySideEffect",
                    Target = () => AccessTools.Method(typeof(Execution), "ApplySideEffect", new[] { typeof(int) }),
                    Prefix = M(nameof(GamePatches.ApplySideEffectPrefix)), Postfix = M(nameof(GamePatches.ExecutionContextPostfix)) },
                new PatchSpec { Name = "FunctionNode.GetSignature",
                    Target = () => AccessTools.Method(typeof(FunctionNode), nameof(FunctionNode.GetSignature), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.GetSignaturePostfix)) },
                // code files keep their real extension on disk (main.java, utils.rs, bot.py)
                new PatchSpec { Name = "Saver.WriteCodeFiles", Required = true,
                    Target = () => AccessTools.Method(typeof(Saver), "WriteCodeFiles"),
                    Prefix = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.WriteCodeFilesPrefix)),
                    Postfix = M(nameof(GamePatches.WriteCodeFilesPostfix)) },
                new PatchSpec { Name = "Saver.Load", Required = true,
                    Target = () => AccessTools.Method(typeof(Saver), nameof(Saver.Load), new[] { typeof(MainSim) }),
                    Prefix = M(nameof(GamePatches.SaverLoadPrefix)), Postfix = M(nameof(GamePatches.SaverLoadPostfix)) },
                new PatchSpec { Name = "Saver.OnFileChanged",
                    Target = () => AccessTools.Method(typeof(Saver), "OnFileChanged", new[] { typeof(object), typeof(System.IO.FileSystemEventArgs) }),
                    Prefix = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.OnFileChangedPrefix)) },
                new PatchSpec { Name = "CodeWindow.Rename",
                    Target = () => AccessTools.Method(typeof(CodeWindow), nameof(CodeWindow.Rename), new[] { typeof(string) }),
                    Prefix = M(nameof(GamePatches.CodeWindowRenamePrefix)) },
                new PatchSpec { Name = "Workspace.IsValidFileName",
                    Target = () => AccessTools.Method(typeof(Workspace), nameof(Workspace.IsValidFileName), new[] { typeof(string) }),
                    Postfix = M(nameof(GamePatches.IsValidFileNamePostfix)) },
                // calls without a window argument (colouring, value text, error text) learn the window / program they serve
                new PatchSpec { Name = "CodeWindow.UpdateSearch",
                    Target = () => AccessTools.Method(typeof(CodeWindow), nameof(CodeWindow.UpdateSearch), new[] { typeof(string), typeof(int) }),
                    Prefix = M(nameof(GamePatches.WindowContextPrefix)), Postfix = M(nameof(GamePatches.WindowContextPostfix)) },
                new PatchSpec { Name = "CodeWindow.UpdateTokens", HeadlessUnsafe = true,
                    Target = () => AccessTools.Method(typeof(CodeWindow), "UpdateTokens", Type.EmptyTypes),
                    Prefix = M(nameof(GamePatches.WindowContextPrefix)), Postfix = M(nameof(GamePatches.WindowContextPostfix)) },
                new PatchSpec { Name = "CodeWindow.GetTooltipInfo",
                    Target = () => AccessTools.Method(typeof(CodeWindow), nameof(CodeWindow.GetTooltipInfo), new[] { typeof(Action) }),
                    Prefix = M(nameof(GamePatches.WindowContextPrefix)), Postfix = M(nameof(GamePatches.WindowContextPostfix)) },
                new PatchSpec { Name = "ProgramState.PerformExecutionStep",
                    Target = () => AccessTools.Method(typeof(ProgramState), nameof(ProgramState.PerformExecutionStep)),
                    Prefix = M(nameof(GamePatches.ExecutionContextPrefix)), Postfix = M(nameof(GamePatches.ExecutionContextPostfix)) },
                new PatchSpec { Name = "Menu.Start",
                    Target = () => AccessTools.Method(typeof(Menu), "Start", Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.MenuStartPostfix)) },
                new PatchSpec { Name = "Menu.Open",
                    Target = () => AccessTools.Method(typeof(Menu), nameof(Menu.Open), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.MenuHideModsPostfix)) },
                new PatchSpec { Name = "Menu.Play",
                    Target = () => AccessTools.Method(typeof(Menu), nameof(Menu.Play), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.MenuHideModsPostfix)) },
                new PatchSpec { Name = "Menu.Options",
                    Target = () => AccessTools.Method(typeof(Menu), nameof(Menu.Options), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.MenuHideModsPostfix)) },
                new PatchSpec { Name = "Menu.ChooseSave",
                    Target = () => AccessTools.Method(typeof(Menu), nameof(Menu.ChooseSave), Type.EmptyTypes),
                    Postfix = M(nameof(GamePatches.MenuHideModsPostfix)) },
            };
            // Saver's ".py" checks (Load, WriteCodeFiles cleanup): any code file of a known extension
            var sites = SaveFiles.ExtensionCheckSites();
            if (sites.Count == 0)
                list.Add(new PatchSpec { Name = "Saver .py checks", Required = true, Target = () => null, Transpiler = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.ExtensionCheckTranspiler)) });
            foreach (var site in sites)
            {
                var s = site;
                list.Add(new PatchSpec { Name = "Saver .py check in " + s.DeclaringType.Name + "." + s.Name, Required = true, Target = () => s,
                    Transpiler = AccessTools.Method(typeof(SaveFiles), nameof(SaveFiles.ExtensionCheckTranspiler)) });
            }
            foreach (var t in DependencyNodeTypes())
            {
                var type = t;
                list.Add(new PatchSpec
                {
                    Name = type.Name + ".CheckDependencies", Required = true,
                    Target = () => DeclaredCheckDependencies(type),
                    Prefix = M(nameof(GamePatches.CheckDependenciesPrefix)),
                });
            }
            return list;
        }

        /// <summary>Node and every Node subclass in Core that declares its own CheckDependencies.</summary>
        public static List<Type> DependencyNodeTypes()
        {
            var result = new List<Type>();
            Type[] types;
            try { types = typeof(Node).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = Array.FindAll(e.Types, t => t != null); }
            foreach (var t in types)
            {
                if (!typeof(Node).IsAssignableFrom(t)) continue;
                if (DeclaredCheckDependencies(t) != null)
                    result.Add(t);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return result;
        }

        /// <summary>
        /// The CheckDependencies(ProgramState, Execution) that the type itself declares, or null. Plain reflection on purpose:
        /// AccessTools.DeclaredMethod logs a HarmonyX warning for every type that does not override it.
        /// </summary>
        public static MethodInfo DeclaredCheckDependencies(Type t)
        {
            try
            {
                return t.GetMethod(nameof(Node.CheckDependencies),
                    BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(ProgramState), typeof(Execution) }, null);
            }
            catch (AmbiguousMatchException) { return null; }
        }

        /// <summary>Applies every patch; on a failed required patch removes them all. Returns the number applied.</summary>
        public static int ApplyAll(Harmony harmony, IEnumerable<PatchSpec> specs = null)
        {
            int ok = 0;
            foreach (var spec in specs ?? All())
            {
                try
                {
                    var target = spec.Target();
                    if (target == null) throw new MissingMethodException("target not found");
                    harmony.Patch(target,
                        prefix: spec.Prefix != null ? new HarmonyMethod(spec.Prefix) : null,
                        postfix: spec.Postfix != null ? new HarmonyMethod(spec.Postfix) : null,
                        transpiler: spec.Transpiler != null ? new HarmonyMethod(spec.Transpiler) : null);
                    ok++;
                }
                catch (Exception e)
                {
                    Log.Error("patch " + spec.Name + " failed: " + e.Message);
                    if (spec.Required)
                    {
                        Log.Error("a required patch failed; JavaFarmer removes all its patches and the game stays vanilla (Python).");
                        try { harmony.UnpatchSelf(); } catch (Exception e2) { Log.Error("unpatch failed", e2); }
                        return -1;
                    }
                }
            }
            return ok;
        }
    }

    /// <summary>Harmony patch bodies. Every body catches its own exceptions and falls back to the game's behaviour.</summary>
    public static class GamePatches
    {
        /// <summary>Content folder (docs/strings). Set by the plugin.</summary>
        public static ContentStore Content = new ContentStore(null);
        /// <summary>When false, content overrides only apply while the game language is EN.</summary>
        public static bool ContentForAllLanguages = true;

        /// <summary>Reads the Java text of a window (tests replace it: the headless CodeWindow has no input field).</summary>
        public static Func<CodeWindow, string> JavaTextProvider = DefaultJavaText;
        public static Func<CodeWindow, IReadOnlyDictionary<string, string>> OtherFilesProvider = DefaultOtherFiles;
        /// <summary>Other windows' language ids (by extension). Tests replace it.</summary>
        public static Func<CodeWindow, IReadOnlyDictionary<string, string>> OtherLanguagesProvider = DefaultOtherLanguages;

        public static IReadOnlyDictionary<string, string> DefaultOtherLanguages(CodeWindow f)
        {
            var ws = ReferenceEquals(f, null) ? null : f.workspace;
            if (ReferenceEquals(ws, null) || ws.codeWindows == null) return null;
            var d = new Dictionary<string, string>();
            foreach (var cw in ws.codeWindows.Values)
            {
                if (ReferenceEquals(cw, null) || ReferenceEquals(cw, f) || string.IsNullOrEmpty(cw.fileName)) continue;
                d[cw.fileName] = FileLanguages.LanguageIdOf(cw.fileName);
            }
            return d;
        }

        private static readonly HashSet<string> loggedOnce = new HashSet<string>();

        private static void LogOnce(string where, Exception e)
        {
            lock (loggedOnce)
            {
                if (!loggedOnce.Add(where)) return;
            }
            Log.Error(where, e);
        }

        public static string DefaultJavaText(CodeWindow f)
        {
            if (ReferenceEquals(f, null)) return null;
            var ci = f.CodeInput;
            return ReferenceEquals(ci, null) ? null : ci.text;
        }

        public static IReadOnlyDictionary<string, string> DefaultOtherFiles(CodeWindow f)
        {
            var ws = ReferenceEquals(f, null) ? null : f.workspace;
            if (ReferenceEquals(ws, null) || ws.codeWindows == null) return null;
            var d = new Dictionary<string, string>();
            foreach (var cw in ws.codeWindows.Values)
            {
                if (ReferenceEquals(cw, null) || ReferenceEquals(cw, f)) continue;
                string name = cw.fileName;
                if (string.IsNullOrEmpty(name) || name == f.fileName) continue;
                var ci = cw.CodeInput;
                if (ReferenceEquals(ci, null)) continue;
                d[name] = ci.text ?? "";
            }
            return d;
        }

        // ------------------------------------------------------------------ 1. parse hook

        public static bool ParsePrefix(CodeWindow f, ref Program __result)
        {
            if (JavaParseBridge.InOriginalParse) return true;
            string langId = FileLanguages.LanguageIdOf(ReferenceEquals(f, null) ? null : f.fileName);
            var lang = Languages.Get(langId);
            if (lang != null && lang.IsNative) return true; // a .py window: the game's own parser, nothing is compiled
            string java;
            IReadOnlyDictionary<string, string> others = null, otherLanguages = null;
            try
            {
                java = JavaTextProvider(f);
                if (java == null) return true;
                if (lang == null)
                    throw new ParseException(JavaParseBridge.FormatMessage("this file is " + FileLanguages.DisplayName(f.fileName) +
                        ", but this JavaFarmer build has no " + langId + " support. Rename it with another extension (" + FileLanguages.ExtensionList() + ")."), 0, 0);
                try { others = OtherFilesProvider(f); otherLanguages = OtherLanguagesProvider(f); } catch (Exception e) { LogOnce("OtherFiles", e); }
            }
            catch (ParseException)
            {
                throw;
            }
            catch (Exception e)
            {
                LogOnce("ParsePrefix", e);
                return true;
            }
            try
            {
                __result = JavaParseBridge.Parse(java, f, others, lang.Id == "java" ? null : lang, otherLanguages);
            }
            catch (ParseException)
            {
                throw;
            }
            catch (Exception e)
            {
                Log.Error("JavaFarmer parse failed", e);
                throw new ParseException(JavaParseBridge.FormatMessage("internal JavaFarmer error: " + e.Message), 0, 0);
            }
            return false;
        }

        private static readonly ConditionalWeakTable<CodeWindow, StrongBox<long>> otherFilesFingerprint = new ConditionalWeakTable<CodeWindow, StrongBox<long>>();
        private static AccessTools.FieldRef<CodeWindow, bool> dirtyRef;

        /// <summary>Java resolves other files at compile time: re-parse a window when another window's text changed.</summary>
        public static void CodeWindowParsePrefix(CodeWindow __instance)
        {
            try
            {
                var own = FileLanguages.LanguageOf(__instance);
                if (own != null && own.IsNative) return;
                long fp = Fingerprint(OtherFilesProvider(__instance)) * 31 + Fingerprint(OtherLanguagesProvider(__instance));
                var box = otherFilesFingerprint.GetValue(__instance, _ => new StrongBox<long>(fp));
                if (box.Value != fp)
                {
                    box.Value = fp;
                    if (dirtyRef == null) dirtyRef = AccessTools.FieldRefAccess<CodeWindow, bool>("dirty");
                    dirtyRef(__instance) = true;
                }
            }
            catch (Exception e)
            {
                LogOnce("CodeWindowParsePrefix", e);
            }
        }

        public static long Fingerprint(IReadOnlyDictionary<string, string> files)
        {
            if (files == null) return 0;
            long acc = files.Count;
            foreach (var kv in files)
            {
                unchecked
                {
                    long h = (kv.Key?.GetHashCode() ?? 0) * 397L ^ (kv.Value?.GetHashCode() ?? 0) ^ ((long)(kv.Value?.Length ?? 0) << 32);
                    acc += h * 31 + 17;
                }
            }
            return acc;
        }

        // ------------------------------------------------------------------ 2. dependency policy

        public static bool CheckDependenciesPrefix(Node __instance, ProgramState state)
        {
            try
            {
                return DependencyPolicies.BeforeCheck(__instance.boxedParams, state?.currentDependencies);
            }
            catch (Exception e)
            {
                LogOnce("CheckDependenciesPrefix", e);
                return true;
            }
        }

        public static void CallExecutePostfix(CallNode __instance, ProgramState state, ref IEnumerable<double> __result)
        {
            try
            {
                var bp = __instance.boxedParams;
                var policy = DependencyPolicies.Get(bp);
                if (policy == null || state == null) return;
                var st = state;
                __result = DependencyPolicies.WrapCall(__result, bp, () => st.currentDependencies, policy);
            }
            catch (Exception e)
            {
                LogOnce("CallExecutePostfix", e);
            }
        }

        public static bool InsertBreakpointsPrefix(Node __instance, HashSet<int> breakpointCharPositions)
        {
            try
            {
                if (!JavaParseBridge.IsJavaTree(__instance)) return true;
                JavaParseBridge.InsertBreakpoints(__instance, breakpointCharPositions);
                return false;
            }
            catch (Exception e)
            {
                LogOnce("InsertBreakpointsPrefix", e);
                return true;
            }
        }

        // ------------------------------------------------------------------ 3. editor

        public static bool SyntaxColorPrefix(string code, string searchWord, int searchIndex, ref string __result)
        {
            try
            {
                // the window being drawn (UpdateSearch / UpdateTokens / its tooltip), else the default language (docs, unlock boxes)
                var lang = LanguageContext.ForUi;
                if (lang == null || lang.IsNative) return true; // Python: the game's own colouring
                __result = lang.Id == "java" ? JavaHighlighter.Color(code, searchWord, searchIndex) : JavaHighlighter.Color(code, searchWord, searchIndex, lang);
                return false;
            }
            catch (Exception e)
            {
                LogOnce("SyntaxColorPrefix", e);
                return true;
            }
        }

        public static void OnCodeInsertedPostfix(CodeWindow __instance, string inserted)
        {
            try
            {
                if (string.IsNullOrEmpty(inserted) || inserted.Length > 2 || __instance.isExecuting) return;
                var lang = FileLanguages.LanguageOf(__instance);
                if (lang == null || lang.IsNative) return; // the game indents after ':' itself
                var rules = lang.Editor ?? EditorSupport.JavaRules;
                var ci = __instance.CodeInput;
                if (ReferenceEquals(ci, null)) return;
                string text = ci.text;
                int caret = ci.stringPosition;
                string newText; int newCaret;
                bool changed;
                if (inserted.IndexOf('\n') >= 0 || inserted.IndexOf('\r') >= 0)
                {
                    string unit = OptionHolder.GetString("tabs to spaces") == "enabled" ? "    " : "\t";
                    changed = EditorSupport.AfterNewline(text, caret, unit, rules, out newText, out newCaret);
                }
                else if (EditorSupport.IsDedentChar(inserted, rules))
                {
                    changed = EditorSupport.AfterDedentChar(text, caret, rules, out newText, out newCaret);
                }
                else return;
                if (!changed) return;
                ci.text = newText;
                ci.stringPosition = newCaret;
            }
            catch (Exception e)
            {
                LogOnce("OnCodeInsertedPostfix", e);
            }
        }

        public static void GetWordListPostfix(CodeWindow __instance, ref List<string> __result)
        {
            try
            {
                var lang = FileLanguages.LanguageOf(__instance);
                if (lang == null || lang.IsNative) return;
                if (lang.Id == "java")
                    __result = EditorSupport.JavaWordList(__result,
                        w => BuiltinFunctions.Functions.ContainsKey(w),
                        u => MainSim.Inst != null && MainSim.Inst.IsUnlocked(u));
                else
                    __result = EditorSupport.LanguageWordList(__result,
                        w => BuiltinFunctions.Functions.ContainsKey(w),
                        u => MainSim.Inst != null && MainSim.Inst.IsUnlocked(u), lang);
            }
            catch (Exception e)
            {
                LogOnce("GetWordListPostfix", e);
            }
        }

        private static readonly HashSet<string> enumDomains = new HashSet<string> { "Entities", "Grounds", "Items", "Unlocks", "Leaderboards", "Hats" };

        public static void GetSubWordListPostfix(CodeWindow __instance, string domain, ref List<string> __result)
        {
            try
            {
                if (domain != null && enumDomains.Contains(domain)) return;
                var lang = FileLanguages.LanguageOf(__instance);
                if (lang == null || lang.IsNative) return;
                bool isModule = false;
                var ws = __instance.workspace;
                if (!ReferenceEquals(ws, null))
                    foreach (var cw in ws.codeWindows.Values)
                        if (!ReferenceEquals(cw, null) && cw.fileName == domain) { isModule = true; break; }
                __result = isModule || lang.Id != "java" ? EditorSupport.FilterHelpers(__result) : EditorSupport.JavaMemberList(domain);
            }
            catch (Exception e)
            {
                LogOnce("GetSubWordListPostfix", e);
            }
        }

        private static bool HasParsedFunction(CodeWindow context, string name)
        {
            var ws = ReferenceEquals(context, null) ? null : context.workspace;
            if (ReferenceEquals(ws, null) || ws.codeWindows == null) return false;
            foreach (var cw in ws.codeWindows.Values)
                if (!ReferenceEquals(cw, null) && cw.parsedFunctions != null && cw.parsedFunctions.ContainsKey(name)) return true;
            return false;
        }

        public static bool WordTooltipPrefix(ref string word, CodeWindow context, out string[] __state, ref TooltipInfo __result)
        {
            __state = null;
            try
            {
                // the hovered window's own language (its extension), not the default language
                var lang = FileLanguages.LanguageOf(context) ?? ActiveLanguage.Current;
                if (lang == null) return true;
                if (lang.IsNative) { __state = new[] { null, word, lang.Id }; return true; }
                if (lang.Id != "java")
                {
                    bool r = OtherLanguageTooltip(lang, ref word, context, out __state, ref __result);
                    if (r) __state = new[] { __state?[0], __state?[1] ?? word, lang.Id };
                    return r;
                }
                __state = new[] { null, word, lang.Id };
                if (word != null && EditorSupport.JavaKeywordGates.TryGetValue(word, out var gate))
                {
                    string key = "code_tooltip_" + word;
                    string text = TooltipBody(key, lang);
                    if (text != null && MainSim.Inst != null && MainSim.Inst.IsUnlocked(gate))
                    {
                        __result = new TooltipInfo("## `" + word + "`\n" + text, 0f, default, TooltipInfo.Anchor.Auto, TooltipUtils.GetKeywordDocsPage(gate));
                        return false;
                    }
                }
                if (EditorSupport.IsPythonOnlyWord(word))
                {
                    // e.g. a variable or method the player called "list" or "max": never the Python builtin's tooltip
                    foreach (var c in RuntimeNames.PythonCandidates(word))
                        if (HasParsedFunction(context, c)) { word = c; return true; }
                    __result = null;
                    return false;
                }
                string py = EditorSupport.HoverWordToPython(word);
                if (py != null)
                {
                    __state = new[] { word, py, lang.Id };
                    word = py;
                }
                else if (word != null)
                {
                    // a user method the lowering renamed (list -> list_j): its signature tooltip is keyed by the Python name
                    foreach (var c in RuntimeNames.PythonCandidates(word))
                        if (HasParsedFunction(context, c)) { word = c; break; }
                }
            }
            catch (Exception e)
            {
                LogOnce("WordTooltipPrefix", e);
            }
            return true;
        }

        /// <summary>Keyword → the game word whose unlock shows its tooltip (languages other than Java).</summary>
        private static readonly Dictionary<string, string> keywordGates = new Dictionary<string, string>
        {
            { "fn", "def" }, { "function", "def" }, { "func", "def" }, { "void", "def" }, { "static", "def" }, { "pub", "def" },
            { "let", "variables" }, { "mut", "variables" }, { "var", "variables" }, { "const", "variables" }, { "auto", "variables" },
            { "int", "variables" }, { "double", "variables" }, { "float", "variables" }, { "bool", "variables" }, { "string", "variables" },
            { "loop", "while" }, { "repeat", "while" }, { "do", "while" }, { "switch", "if" }, { "match", "if" }, { "case", "if" },
            { "new", "lists" },
            // type words (Rust and others): the unlock of the Python feature they stand for
            { "i32", "variables" }, { "i64", "variables" }, { "f64", "variables" }, { "usize", "variables" }, { "str", "variables" },
            { "String", "variables" }, { "char", "variables" }, { "long", "variables" }, { "boolean", "variables" },
            { "Vec", "lists" }, { "HashMap", "dicts" }, { "HashSet", "dicts" }, { "Option", "None" }, { "Some", "None" },
            { "move", "def" },
        };

        /// <summary>
        /// The string key of a keyword / type word's own tooltip in a language, or null. code_tooltip_keyword_&lt;word&gt; first
        /// (a keyword that has the name of a game builtin: Rust's closure keyword move vs the builtin move, which Rust calls
        /// move_to); code_tooltip_&lt;word&gt; only when it is not the tooltip of a game builtin spelled differently in the language.
        /// </summary>
        public static string KeywordTooltipKey(ILanguage lang, string word, Func<string, bool> hasKey, Func<string, bool> gameHasKey = null)
        {
            if (lang == null || string.IsNullOrEmpty(word)) return null;
            var kind = ActiveLanguage.WordKind(lang, word);
            if (kind != TokenKind.Keyword && kind != TokenKind.Type && kind != TokenKind.Constant) return null;
            // constants the game itself explains (North, None, True ...) keep the game's tooltip path
            if (kind == TokenKind.Constant && gameHasKey != null && gameHasKey("code_tooltip_" + word)) return null;
            if (hasKey("code_tooltip_keyword_" + word)) return "code_tooltip_keyword_" + word;
            if (!hasKey("code_tooltip_" + word)) return null;
            string api = null;
            try { if (GameApi.Find(word) != null) api = lang.ApiName(word); } catch { }
            if (api != null && api != word) return null;   // e.g. Rust "move": that string documents move_to
            return "code_tooltip_" + word;
        }

        /// <summary>Hover tooltips in a language other than Java (Python names under the hood, the language's names shown).</summary>
        private static bool OtherLanguageTooltip(ILanguage lang, ref string word, CodeWindow context, out string[] __state, ref TooltipInfo __result)
        {
            __state = null;
            if (word == null) return true;
            // a keyword or type word with its own tooltip in this language's strings (content/<lang>/Strings: code_tooltip_<word>)
            var raw = Content.RawStrings(lang.Id);
            var game = GameRawStrings();
            string key = KeywordTooltipKey(lang, word, raw.ContainsKey, game.ContainsKey);
            if (key != null)
            {
                string gate = keywordGates.TryGetValue(word, out var g) ? g : word;
                string text = TooltipBody(key, lang);
                if (text != null && MainSim.Inst != null && MainSim.Inst.IsUnlocked(gate))
                {
                    __result = new TooltipInfo("## `" + word + "`\n" + text, 0f, default, TooltipInfo.Anchor.Auto, TooltipUtils.GetKeywordDocsPage(gate));
                    return false;
                }
            }
            string py = EditorSupport.HoverWordToPython(word, lang);
            if (py != null)
            {
                __state = new[] { word, py, lang.Id };
                word = py;
                return true;
            }
            foreach (var c in RuntimeNames.PythonCandidates(word))
                if (HasParsedFunction(context, c)) { word = c; return true; }
            if (EditorSupport.IsForeignBuiltinWord(word, lang, w => BuiltinFunctions.Functions.ContainsKey(w)))
            {
                __result = null;
                return false;
            }
            return true;
        }

        public static void WordTooltipPostfix(string[] __state, TooltipInfo __result)
        {
            try
            {
                if (__state == null || __result == null || __result.text == null) return;
                // the game localised the builtin's tooltip in the default language: show the hovered window's language
                if (__state.Length > 2 && __state[1] != null && __state[2] != ActiveLanguage.Id)
                {
                    string key = "code_tooltip_" + __state[1];
                    string shown = Localizer.Localize(key);
                    string mine = shown == key ? null : TooltipBody(key, Languages.Get(__state[2]));
                    if (mine != null && shown != mine)
                    {
                        int at = __result.text.IndexOf(shown, StringComparison.Ordinal);
                        if (at >= 0) __result.text = __result.text.Substring(0, at) + mine + __result.text.Substring(at + shown.Length);
                    }
                }
                if (__state[0] == null) return;
                string from = "`" + __state[1] + "`", to = "`" + __state[0] + "`";
                int i = __result.text.IndexOf(from, StringComparison.Ordinal);
                if (i >= 0) __result.text = __result.text.Substring(0, i) + to + __result.text.Substring(i + from.Length);
            }
            catch (Exception e)
            {
                LogOnce("WordTooltipPostfix", e);
            }
        }

        /// <summary>
        /// A string (tooltip) for a language other than the default one, tagged for display: the language's own content
        /// string, else the game's string with its code translated (Python: the game's string). Null when there is none.
        /// </summary>
        public static string TooltipBody(string key, ILanguage lang)
        {
            if (lang == null) return null;
            if (lang.Id == ActiveLanguage.Id)
            {
                string t = Localizer.Localize(key);
                return t == key ? null : t;
            }
            string raw = null;
            if (!lang.IsNative && Content.StringsFor(lang.Id).TryGetValue(key, out var own)) raw = own;
            else if (lang.IsNative && Content.StringsFor(lang.Id).TryGetValue(key, out var py)) raw = py;
            else if (GameRawStrings().TryGetValue(key, out var game)) raw = lang.IsNative ? game : Content.TranslateGameString(key, game, lang.Id);
            return raw == null ? null : Tag(raw);
        }

        private static Dictionary<string, string> gameRaw;
        private static string gameRawLang;

        /// <summary>The game's own (untagged) strings of the UI language, from StreamingAssets (cached).</summary>
        public static Dictionary<string, string> GameRawStrings()
        {
            string lang = Localizer.Lang ?? "EN";
            if (gameRaw != null && gameRawLang == lang) return gameRaw;
            var d = new Dictionary<string, string>();
            try
            {
                string dir = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Languages", lang, "Strings");
                if (Directory.Exists(dir))
                    foreach (var f in Directory.GetFiles(dir, "*.txt"))
                        foreach (var kv in ContentStore.ParseKeyValueFile(File.ReadAllText(f))) d[kv.Key] = kv.Value;
            }
            catch (Exception e) { LogOnce("GameRawStrings", e); }
            gameRawLang = lang;
            return gameRaw = d;
        }

        // ------------------------------------------------------------------ 4. new windows: extension, title, template, rename

        /// <summary>A new window gets its extension (from the disk while a save loads, else the default language's) and the template of its language.</summary>
        public static void OpenCodeWindowPrefix(Workspace __instance, string fileName, ref string code)
        {
            try
            {
                if (!ReferenceEquals(__instance, null) && __instance.openWindows != null && __instance.openWindows.ContainsKey(fileName)) return;
                FileLanguages.Set(fileName, SaveFiles.ExtensionForNewWindow(fileName));
                if (!string.IsNullOrEmpty(code)) return;
                var lang = FileLanguages.LanguageOf(fileName);
                if (lang == null || lang.IsNative) return;
                code = lang.Id == "java" ? EditorSupport.NewFileTemplate : (lang.NewFileTemplate(fileName, out _) ?? "");
            }
            catch (Exception e)
            {
                LogOnce("OpenCodeWindowPrefix", e);
            }
        }

        // ------------------------------------------------------------------ 5. docs and strings

        private static bool ContentApplies()
        {
            if (ContentForAllLanguages) return true;
            try { return Localizer.Lang == null || Localizer.Lang == "EN"; } catch { return true; }
        }

        /// <summary>Canonical (Python) extra section appended to the docs home page in every language (written by extensions).</summary>
        public const string HomeSectionDoc = "docs/mods/home_section.md";

        public static void LoadDocPostfix(string doc, ref string __result)
        {
            try
            {
                if (!ContentApplies()) return;
                string lang = ActiveLanguage.Id;
                __result = ResolveDoc(Content, doc, __result, lang);
            }
            catch (Exception e)
            {
                LogOnce("LoadDocPostfix", e);
            }
        }

        /// <summary>
        /// The docs resolution chain (testable): content/&lt;lang&gt;/doc → content/python/doc translated → the game's page
        /// translated; plus the extensions' home section on docs/home.md.
        /// </summary>
        public static string ResolveDoc(ContentStore content, string doc, string gameText, string lang)
        {
            var language = ActiveLanguage.Resolve(lang);
            bool native = language == null || language.IsNative;
            string text;
            if (content.TryGetDoc(doc, lang, out var own)) text = own;
            else text = native ? gameText : content.TranslateGameDoc(doc, gameText, lang);
            if (doc != null && doc.Replace('\\', '/').TrimStart('/') == "docs/home.md" && content.TryGetDoc(HomeSectionDoc, lang, out var section)
                && !string.IsNullOrEmpty(section) && (text ?? "").IndexOf(section.Trim(), StringComparison.Ordinal) < 0)
                text = (text ?? "").TrimEnd('\n') + "\n\n" + section;
            return text;
        }

        private static FieldInfo languageField;

        public static void LoadLangPostfix()
        {
            try
            {
                MergeStrings();
            }
            catch (Exception e)
            {
                LogOnce("LoadLangPostfix", e);
            }
        }

        public static int MergeStrings()
        {
            if (!ContentApplies()) return 0;
            if (languageField == null) languageField = AccessTools.Field(typeof(Localizer), "language");
            var dict = languageField?.GetValue(null) as Dictionary<string, string>;
            if (dict == null) return 0;
            int n = 0;
            string lang = ActiveLanguage.Id;
            var overrides = Content.StringsFor(lang);
            foreach (var kv in overrides)
            {
                dict[kv.Key] = Tag(kv.Value);
                n++;
            }
            if (!ActiveLanguage.IsNative)
            {
                // game strings with code (builtin tooltips ...) that this language does not override: translate the code spans
                foreach (var kv in GameStringsWithCode())
                {
                    if (overrides.ContainsKey(kv.Key)) continue;
                    string t = Content.TranslateGameString(kv.Key, kv.Value, lang);
                    if (t == kv.Value) continue;
                    dict[kv.Key] = Tag(t);
                    n++;
                }
            }
            return n;
        }

        private static string Tag(string value)
        {
            try { return CodeUtilities.ApplyCodeTags(value); } catch { return value; }
        }

        /// <summary>Files of the game's Strings folder whose code spans are translated for languages without their own text.</summary>
        public static readonly string[] TranslatedGameStringFiles = { "code_tooltips.txt", "unlock_tooltips.txt", "item_tooltips.txt" };

        /// <summary>The raw (untagged) game strings of the current UI language that contain code spans.</summary>
        private static List<KeyValuePair<string, string>> GameStringsWithCode()
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                string dir = Path.Combine(UnityEngine.Application.streamingAssetsPath, "Languages", Localizer.Lang ?? "EN", "Strings");
                foreach (var name in TranslatedGameStringFiles)
                {
                    string f = Path.Combine(dir, name);
                    if (!File.Exists(f)) continue;
                    foreach (var kv in ContentStore.ParseKeyValueFile(File.ReadAllText(f)))
                        if (kv.Value.IndexOf('`') >= 0) result.Add(kv);
                }
            }
            catch (Exception e)
            {
                LogOnce("GameStringsWithCode", e);
            }
            return result;
        }

        /// <summary>Loads the game's strings again (after a language switch) so the overrides of the new language apply.</summary>
        public static void ReloadStrings()
        {
            try
            {
                if (languageField == null) languageField = AccessTools.Field(typeof(Localizer), "language");
                if (languageField?.GetValue(null) == null) return;
                string lang = Localizer.Lang;
                AccessTools.Property(typeof(Localizer), nameof(Localizer.Lang))?.SetValue(null, null);
                Localizer.LoadLang(lang ?? OptionHolder.GetString("language", "EN"));
            }
            catch (Exception e)
            {
                Log.Warn("could not reload strings: " + e.Message);
            }
        }

        private sealed class DisplayCodeState { public string Original; public string Language; }
        private static readonly ConditionalWeakTable<UnlockSO, DisplayCodeState> displayCodeState = new ConditionalWeakTable<UnlockSO, DisplayCodeState>();

        /// <summary>
        /// Unlock boxes show UnlockSO.displayCode (canonical Python). The original is remembered per SO, so the text can
        /// follow a language switch (the SOs live for the whole process). Runs every frame: cheap when nothing changed.
        /// </summary>
        public static void UnlockBoxPrefix(UnlockBox __instance)
        {
            try
            {
                var so = __instance.unlockSO;
                if (ReferenceEquals(so, null)) return;
                var st = displayCodeState.GetValue(so, s => new DisplayCodeState { Original = s.displayCode });
                string lang = ActiveLanguage.Id;
                if (st.Language == lang) return;
                st.Language = lang;
                so.displayCode = Content.ResolveDisplayCode(so.unlockName, st.Original, lang) ?? st.Original;
            }
            catch (Exception e)
            {
                LogOnce("UnlockBoxPrefix", e);
            }
        }

        // ------------------------------------------------------------------ 6. runtime error text

        public static void LocalizeAndFormatPrefix(string s, object[] args)
        {
            try
            {
                ErrorText.MapArgs(s, args, LanguageContext.ForValues);
            }
            catch (Exception e)
            {
                LogOnce("LocalizeAndFormatPrefix", e);
            }
        }

        // ------------------------------------------------------------------ 6b. Java text for values, traces, docs index

        public static bool BuiltinsTocPrefix(ref string __result)
        {
            try
            {
                if (ActiveLanguage.IsNative) return true;
                __result = JavaText.BuiltinsToc(BuiltinFunctions.Functions.Keys, ActiveLanguage.Current);
                return false;
            }
            catch (Exception e)
            {
                LogOnce("BuiltinsTocPrefix", e);
                return true;
            }
        }

        public static bool ToNiceStringPrefix(object obj, ref string __result)
        {
            try
            {
                // the running program's file, the hovered window, else the default language (docs)
                var lang = LanguageContext.ForValues;
                if (lang == null || lang.IsNative) return true; // Python: the game's own format
                __result = lang.FormatValue(GameValueConverter.Convert(obj));
                return false;
            }
            catch (Exception e)
            {
                LogOnce("ToNiceStringPrefix", e);
                return true;
            }
        }

        [ThreadStatic] private static bool inEvaluateName;

        /// <summary>Hover value tooltip: a Java name the lowering renamed (list -> list_j, count -> count_l) is looked up by its Python name.</summary>
        public static bool EvaluateNamePrefix(MainSim __instance, string name, ref IPyObject value, ref bool __result)
        {
            if (inEvaluateName) return true;
            try
            {
                var candidates = RuntimeNames.PythonCandidates(name);
                if (candidates.Count == 0) return true;
                inEvaluateName = true;
                try
                {
                    foreach (var c in candidates)
                    {
                        if (__instance.EvaluateName(c, out var v))
                        {
                            value = v;
                            __result = true;
                            return false;
                        }
                    }
                }
                finally { inEvaluateName = false; }
            }
            catch (Exception e)
            {
                LogOnce("EvaluateNamePrefix", e);
            }
            return true;
        }

        public static void GetTracePostfix(ref string __result)
        {
            try
            {
                __result = JavaText.RewriteTrace(__result);
            }
            catch (Exception e)
            {
                LogOnce("GetTracePostfix", e);
            }
        }

        /// <summary>The call-stack line highlight skips the synthetic main() call (it would light up the "void main() {" line).</summary>
        public static void GetCallStackPostfix(List<(FunctionNode func, Node callNode)> __result)
        {
            try
            {
                __result?.RemoveAll(e => !ReferenceEquals(e.callNode, null) && JavaParseBridge.IsHidden(e.callNode.boxedParams));
            }
            catch (Exception e)
            {
                LogOnce("GetCallStackPostfix", e);
            }
        }

        public static bool BlinkEffectPrefix(Node node)
        {
            try
            {
                var bp = node?.boxedParams;
                return bp == null || bp.wordEnd != bp.wordStart || !JavaParseBridge.IsHidden(bp);
            }
            catch (Exception e)
            {
                LogOnce("BlinkEffectPrefix", e);
                return true;
            }
        }

        /// <summary>
        /// A runtime error inside a JavaFarmer helper (e.g. integer division by zero in __jdiv) is reported at the Java
        /// expression that called the helper instead of at the start of the file.
        /// </summary>
        public static void ApplySideEffectPrefix(Execution __instance, int droneId)
        {
            try
            {
                var states = __instance?.States;
                if (states == null || droneId < 0 || droneId >= states.Count) return;
                var st = states[droneId];
                LanguageContext.EnterExecution(st);
                if (st == null || st.currentSideEffect != SideEffect.Error || st.moduleState == null) return;
                var node = st.CurrentExecutingNode;
                if (ReferenceEquals(node, null) || !JavaParseBridge.IsHelperCode(node.boxedParams)) return;
                var stack = st.moduleState.callStack;
                if (stack == null) return;
                foreach (var scope in stack)
                {
                    var cn = scope?.callNode;
                    if (!ReferenceEquals(cn, null) && cn.boxedParams != null && !JavaParseBridge.IsHelperCode(cn.boxedParams))
                    {
                        st.CurrentExecutingNode = cn;
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                LogOnce("ApplySideEffectPrefix", e);
            }
        }

        public static void GetSignaturePostfix(FunctionNode __instance, ref string __result)
        {
            try
            {
                var bp = __instance?.boxedParams;
                var cw = bp?.codeWindow;
                if (ReferenceEquals(cw, null)) return;
                var ci = cw.CodeInput;
                if (ReferenceEquals(ci, null)) return;
                var lang = FileLanguages.LanguageOf(cw);
                if (lang == null || lang.IsNative) return;
                __result = JavaText.SignatureWithComments(ci.text, bp.wordStart, bp.wordEnd, lang.Editor ?? EditorSupport.JavaRules) ?? __result;
            }
            catch (Exception e)
            {
                LogOnce("GetSignaturePostfix", e);
            }
        }

        // ------------------------------------------------------------------ 7. leaderboards

        public static void LoadLeaderboardPrefix(string leaderboardName, ref int score)
        {
            try
            {
                if (score != 0)
                {
                    Log.Info("Leaderboard '" + leaderboardName + "': score " + score + " NOT uploaded (JavaFarmer disables leaderboard uploads for modded runs). The leaderboard is still shown.");
                    score = 0;
                }
            }
            catch (Exception e)
            {
                LogOnce("LoadLeaderboardPrefix", e);
            }
        }

        /// <summary>Safety net: block any score upload that could change a leaderboard entry.</summary>
        public static bool UploadScorePrefix(ELeaderboardUploadScoreMethod __1, int __2, ref SteamAPICall_t __result)
        {
            try
            {
                if (__1 == ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodNone && __2 == 0) return true;
                Log.Info("Steam leaderboard upload skipped (score " + __2 + "): leaderboard uploads are disabled while JavaFarmer is active.");
                __result = SteamAPICall_t.Invalid;
                return false;
            }
            catch (Exception e)
            {
                LogOnce("UploadScorePrefix", e);
                return false; // never upload on error
            }
        }

        // ------------------------------------------------------------------ 9. API stub for external editors

        public static void WriteCodeFilesPostfix(Workspace workspace, string savePath, string backupPath)
        {
            if (string.IsNullOrEmpty(savePath)) return;
            try
            {
                // external-editor API files for every language that has a file in the save
                var langs = new List<ILanguage>();
                if (!ReferenceEquals(workspace, null) && workspace.codeWindows != null)
                    foreach (var cw in workspace.codeWindows.Values)
                        if (!ReferenceEquals(cw, null)) { var l = FileLanguages.LanguageOf(cw); if (l != null) langs.Add(l); }
                ApiStubs.Write(savePath, langs);
            }
            catch (Exception e)
            {
                LogOnce("WriteCodeFilesPostfix.stub", e);
            }
            try
            {
                SaveSidecar.Language = ActiveLanguage.Id;
                SaveSidecar.FileVersion = SaveSidecar.FormatVersion;
                SaveSidecar.Write(savePath, backupPath);
            }
            catch (Exception e)
            {
                LogOnce("WriteCodeFilesPostfix.sidecar", e);
            }
        }

        // ------------------------------------------------------------------ 10. save files with their real extension

        /// <summary>Before the game reads a save: sidecar, one-time migration, duplicate names, extensions on disk.</summary>
        public static void SaverLoadPrefix()
        {
            try
            {
                SaveFiles.BeforeLoad(Saver.GetPathOfSaveDirectory(OptionHolder.GetString("activeSave", "Save0")), DateTime.Now);
            }
            catch (Exception e)
            {
                LogOnce("SaverLoadPrefix", e);
            }
        }

        /// <summary>After loading: tell the player what the migration did.</summary>
        public static void SaverLoadPostfix()
        {
            SaveFiles.AfterLoad();
            try { ReparseCompiledWindows(MainSim.Inst == null ? null : MainSim.Inst.workspace); }
            catch (Exception e) { LogOnce("ReparseCompiledWindows", e); }
            try
            {
                if (SaveFiles.LoadNotes.Count == 0 || MainSim.Inst == null || MainSim.Inst.warningPopup == null) return;
                var popup = MainSim.Inst.warningPopup;
                popup.ShowPopup("<noparse>" + string.Join("\n\n", SaveFiles.LoadNotes) + "</noparse>",
                    new List<WarningPopup.ButtonData> { new WarningPopup.ButtonData("ok", popup.Close) });
                SaveFiles.LoadNotes.Clear();
            }
            catch (Exception e)
            {
                LogOnce("SaverLoadPostfix", e);
            }
        }

        private static MethodInfo closeError;
        private static FieldInfo parseExceptionField;

        /// <summary>
        /// Windows open one by one while a save loads, so a window that uses a window opened after it (js.js importing
        /// pyhelp.py) was compiled without it and shows "cannot find module". Compile every non-Python window again once
        /// all are open (CodeWindowParsePrefix sees the changed set of files); a window that compiles now loses its old error.
        /// </summary>
        public static void ReparseCompiledWindows(Workspace ws)
        {
            if (ReferenceEquals(ws, null) || ws.codeWindows == null) return;
            if (closeError == null) closeError = AccessTools.Method(typeof(CodeWindow), "CloseError");
            if (parseExceptionField == null) parseExceptionField = AccessTools.Field(typeof(CodeWindow), "parseException");
            foreach (var cw in new List<CodeWindow>(ws.codeWindows.Values))
            {
                if (ReferenceEquals(cw, null)) continue;
                var lang = FileLanguages.LanguageOf(cw);
                if (lang != null && lang.IsNative) continue;
                try
                {
                    bool hadError = parseExceptionField != null && parseExceptionField.GetValue(cw) != null;
                    cw.Parse();
                    if (hadError && parseExceptionField.GetValue(cw) == null) closeError?.Invoke(cw, null);
                }
                catch (Exception e) { LogOnce("ReparseCompiledWindows", e); }
            }
        }

        private static AccessTools.FieldRef<CodeWindow, TMP_InputField> fileNameTextRef;
        private static AccessTools.FieldRef<CodeWindow, string> prevTextRef;
        private static MethodInfo updateTokens;

        private static void ShowTitle(CodeWindow cw)
        {
            if (fileNameTextRef == null) fileNameTextRef = AccessTools.FieldRefAccess<CodeWindow, TMP_InputField>("fileNameText");
            var t = fileNameTextRef(cw);
            if (!ReferenceEquals(t, null)) t.text = FileLanguages.DisplayName(cw.fileName);
        }

        /// <summary>The title shows the file name with its extension ("main.java").</summary>
        public static void OpenCodeWindowPostfix(Workspace __instance, string fileName)
        {
            try
            {
                if (__instance?.codeWindows != null && __instance.codeWindows.TryGetValue(fileName, out var cw) && !ReferenceEquals(cw, null)) ShowTitle(cw);
            }
            catch (Exception e) { LogOnce("OpenCodeWindowPostfix", e); }
        }

        /// <summary>
        /// Replaces CodeWindow.Rename: "utils.rs" renames the module to "utils" and makes the file Rust; a name without an
        /// extension keeps the current one. Module names stay unique across extensions (other files import by them).
        /// </summary>
        public static bool CodeWindowRenamePrefix(CodeWindow __instance, string newName)
        {
            var cw = __instance;
            try
            {
                string old = cw.fileName, oldExt = FileLanguages.ExtensionOf(old);
                string error = RenameCheck(cw, newName, out string baseName, out string ext);
                if (error != null)
                {
                    ShowTitle(cw);
                    var popup = MainSim.Inst?.warningPopup;
                    popup?.ShowPopup("<noparse>" + error + "</noparse>", new List<WarningPopup.ButtonData> { new WarningPopup.ButtonData("ok", popup.Close) });
                    return false;
                }
                if (baseName != old) cw.GetComponent<Window>().Rename(baseName);
                cw.fileName = baseName;
                FileLanguages.Move(old, baseName, ext);
                ShowTitle(cw);
                if (!string.Equals(ext, oldExt, StringComparison.OrdinalIgnoreCase)) Recolor(cw);
                if (MainSim.Inst != null) MainSim.Inst.dirty = true;
            }
            catch (Exception e)
            {
                LogOnce("CodeWindowRenamePrefix", e);
                try { ShowTitle(cw); } catch { }
            }
            return false;
        }

        /// <summary>Null when the rename is fine (baseName / ext set), else the message for the player.</summary>
        public static string RenameCheck(CodeWindow cw, string typed, out string baseName, out string ext)
        {
            string old = cw.fileName;
            if (!FileLanguages.TrySplitName(typed, out baseName, out ext, out var why)) return why;
            if (ext == null) ext = FileLanguages.ExtensionOf(old);
            if (FileLanguages.StubFileNames.Contains(baseName + ext))
                return baseName + ext + " is the name of JavaFarmer's API file for external editors. Choose another name.";
            if (FileLanguages.ToolFileNames.Contains(baseName + ext))
                return baseName + ext + " is the name of the " + (ext.Equals(".zig", StringComparison.OrdinalIgnoreCase) ? "Zig" : "Cargo") + " build file: the game ignores files with this name. Choose another name.";
            if (string.Equals(baseName, old, StringComparison.Ordinal)) return null;
            var ws = cw.workspace;
            if (ReferenceEquals(ws, null)) return null;
            foreach (var other in ws.openWindows.Keys)
            {
                if (!string.Equals(other, baseName, StringComparison.OrdinalIgnoreCase) || string.Equals(other, old, StringComparison.Ordinal)) continue;
                bool isCode = ws.codeWindows.ContainsKey(other);
                return isCode
                    ? "There is already a file named " + FileLanguages.DisplayName(other) + ". Two files cannot have the same name, even with different extensions: other files use a file by its name without the extension (" + other + ".f())."
                    : "There is already a window named " + other + ".";
            }
            if (!ws.IsValidFileName(baseName) && !string.Equals(baseName, old, StringComparison.OrdinalIgnoreCase))
                return "\"" + baseName + "\" cannot be used as a file name.";
            return null;
        }

        /// <summary>Colours and parses the window again (its language changed).</summary>
        private static void Recolor(CodeWindow cw)
        {
            if (prevTextRef == null) prevTextRef = AccessTools.FieldRefAccess<CodeWindow, string>("prevText");
            if (updateTokens == null) updateTokens = AccessTools.Method(typeof(CodeWindow), "UpdateTokens");
            prevTextRef(cw) = null;
            updateTokens?.Invoke(cw, null);
        }

        /// <summary>Window names are module names: no dots (the extension is kept apart).</summary>
        public static void IsValidFileNamePostfix(string fileName, ref bool __result)
        {
            if (__result && fileName != null && fileName.IndexOf('.') >= 0) __result = false;
        }

        // ------------------------------------------------------------------ language context for calls without a window

        public static void WindowContextPrefix(CodeWindow __instance, out object __state) => __state = LanguageContext.EnterWindow(__instance);
        public static void WindowContextPostfix(object __state) => LanguageContext.Restore(__state);
        public static void ExecutionContextPrefix(ProgramState __instance) => LanguageContext.EnterExecution(__instance);
        public static void ExecutionContextPostfix() => LanguageContext.LeaveExecution();

        // ------------------------------------------------------------------ 11. Mods menu

        public static void MenuStartPostfix(Menu __instance)
        {
            try
            {
                ModsMenu.Install(__instance);
            }
            catch (Exception e)
            {
                LogOnce("MenuStartPostfix", e);
            }
        }

        public static void MenuHideModsPostfix()
        {
            try
            {
                ModsMenu.Hide();
            }
            catch (Exception e)
            {
                LogOnce("MenuHideModsPostfix", e);
            }
        }
    }
}
