using System;
using JavaFarmer.Compiler;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// Which language a game call without a window argument is about. CodeUtilities.SyntaxColor2, ToNiceString and
    /// LocalizeAndFormat have no window parameter, so the patches around a window's own work (colouring its text, its hover
    /// tooltip) set the UI window, and the execution patches set the running program. Otherwise: the default language
    /// (docs, unlock boxes, tooltips of the docs).
    /// </summary>
    public static class LanguageContext
    {
        [ThreadStatic] private static CodeWindow window;
        [ThreadStatic] private static bool windowSet;
        [ThreadStatic] private static ProgramState executing;

        /// <summary>Sets the UI window for the current thread; returns the previous state for Restore.</summary>
        public static object EnterWindow(CodeWindow w)
        {
            var prev = windowSet ? (object)new Box { Window = window } : null;
            window = w;
            windowSet = true;
            return prev;
        }

        public static void Restore(object state)
        {
            if (state is Box b) { window = b.Window; windowSet = true; }
            else { window = null; windowSet = false; }
        }

        private sealed class Box { public CodeWindow Window; }

        public static void EnterExecution(ProgramState state) => executing = state;
        public static void LeaveExecution() => executing = null;

        /// <summary>The language of the window being drawn (default language outside a window).</summary>
        public static ILanguage ForUi => windowSet && !ReferenceEquals(window, null) ? FileLanguages.LanguageOf(window) ?? ActiveLanguage.Current : ActiveLanguage.Current;

        /// <summary>Running program's window, else the UI window, else the default language.</summary>
        public static ILanguage ForValues
        {
            get
            {
                var st = executing;
                if (st != null)
                {
                    try
                    {
                        var cw = st.CurrentExecutingNode?.boxedParams?.codeWindow;
                        if (!ReferenceEquals(cw, null)) return FileLanguages.LanguageOf(cw) ?? ActiveLanguage.Current;
                    }
                    catch { }
                }
                return ForUi;
            }
        }

        /// <summary>For tests.</summary>
        public static void Reset() { window = null; windowSet = false; executing = null; }
    }
}
