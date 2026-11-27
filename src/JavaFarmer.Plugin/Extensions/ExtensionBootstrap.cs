// Entry point of the content extensions. The plugin calls ExtensionBootstrap.RegisterAll from Awake, after
// ExtensionRegistry.Init(Config) (partial class: other parts may add more registration steps).
using System;
using System.IO;
using HarmonyLib;

namespace JavaFarmer.Plugin.Extensions
{
    public static partial class ExtensionBootstrap
    {
        static bool registered;

        /// <summary>The language id docs / tooltips / unlock boxes use ("java", "python", "rust" ...). Set by the plugin.</summary>
        public static Func<string> CurrentLanguage
        {
            get => ExtensionContent.CurrentLanguage;
            set => ExtensionContent.CurrentLanguage = value ?? (() => SaveSidecar.Language);
        }

        /// <summary>Registers Cybersecurity and AI / ML and applies the extension patches (content = the plugin's content folder).</summary>
        public static void RegisterAll(Harmony harmony, string contentRoot)
        {
            if (registered) return;
            registered = true;
            try
            {
                ExtensionContent.Root = contentRoot ?? DefaultContentRoot();
                ExtensionHost.Register(new Cyber.CyberExtension());
                ExtensionHost.Register(new AiMl.AiExtension());
                SaveSidecar.Loaded += ExtensionPatches.OnSidecarLoaded;
                SaveSidecar.Saving += ExtensionPatches.OnSidecarSaving;
                int n = ExtensionPatches.Apply(harmony ?? new Harmony("javafarmer.plugin.extensions"));
                Log.Info("JavaFarmer extensions: " + ExtensionHost.Extensions.Count + " registered, " + n + "/" + ExtensionPatches.All().Count + " patches applied.");
            }
            catch (Exception e)
            {
                Log.Error("JavaFarmer extensions failed to start", e);
            }
        }

        public static void RegisterAll(Harmony harmony) => RegisterAll(harmony, null);

        public static void RegisterAll() => RegisterAll(null, null);

        public static string DefaultContentRoot()
        {
            try { return Path.Combine(Path.GetDirectoryName(typeof(ExtensionBootstrap).Assembly.Location) ?? ".", "content"); }
            catch { return "content"; }
        }
    }
}
