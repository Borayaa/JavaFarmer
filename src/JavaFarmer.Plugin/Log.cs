using System;

namespace JavaFarmer.Plugin
{
    /// <summary>Logging facade. The plugin points it at the BepInEx log; tests leave the console defaults.</summary>
    public static class Log
    {
        public static Action<string> InfoSink = s => Console.WriteLine("[JavaFarmer] " + s);
        public static Action<string> WarnSink = s => Console.WriteLine("[JavaFarmer WARN] " + s);
        public static Action<string> ErrorSink = s => Console.WriteLine("[JavaFarmer ERROR] " + s);

        public static void Info(string s) { try { InfoSink?.Invoke(s); } catch { } }
        public static void Warn(string s) { try { WarnSink?.Invoke(s); } catch { } }
        public static void Error(string s) { try { ErrorSink?.Invoke(s); } catch { } }
        public static void Error(string where, Exception e) => Error(where + ": " + e);
    }
}
