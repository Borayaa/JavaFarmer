using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JavaFarmer.Plugin.Extensions;

namespace JavaFarmer.Plugin
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("TheFarmerWasReplaced.exe")]
    public sealed class JavaFarmerPlugin : BaseUnityPlugin
    {
        public const string Guid = "javafarmer.plugin";
        public const string Name = "JavaFarmer";
        public const string Version = "0.1.0";

        private ConfigEntry<bool> enabledConfig;
        private ConfigEntry<bool> contentForAllLanguages;
        private Harmony harmony;

        private void Awake()
        {
            Log.InfoSink = s => Logger.LogInfo(s);
            Log.WarnSink = s => Logger.LogWarning(s);
            Log.ErrorSink = s => Logger.LogError(s);

            enabledConfig = Config.Bind("General", "Enabled", true,
                "Enable JavaFarmer (other programming languages, Mods menu). false = the game runs exactly as without the mod (nothing is patched).");
            contentForAllLanguages = Config.Bind("General", "ContentForAllLanguages", true,
                "Use the (English) docs and strings of the programming language for every game language. false = only when the game language is EN.");

            var languageConfig = Config.Bind(ActiveLanguage.Section, ActiveLanguage.Key, ActiveLanguage.DefaultId, ActiveLanguage.ConfigDescription());
            ActiveLanguage.Initialize(languageConfig.Value, id => languageConfig.Value = id);
            ExtensionRegistry.Init(Config);

            if (!enabledConfig.Value)
            {
                Logger.LogInfo("JavaFarmer is disabled in the config (General.Enabled = false). Nothing is patched.");
                return;
            }

            try
            {
                string dir = Path.GetDirectoryName(Info.Location) ?? ".";
                GamePatches.Content = new ContentStore(Path.Combine(dir, "content"));
                GamePatches.ContentForAllLanguages = contentForAllLanguages.Value;
                if (!GamePatches.Content.Exists)
                    Logger.LogWarning("content folder not found at " + GamePatches.Content.Root + ": docs and tooltips stay in Python.");

                JavaParseBridge.FormatMessage = s => "<noparse>" + (s ?? "").Replace("</noparse>", "") + "</noparse>";
                JavaParseBridge.Localize = s => Localizer.Localize(s);

                harmony = new Harmony(Guid);
                int n = PatchRegistry.ApplyAll(harmony);
                if (n < 0)
                {
                    Logger.LogError("JavaFarmer could not patch the game (game update?). The game runs unmodified.");
                    return;
                }
                Logger.LogInfo("JavaFarmer " + Version + ": " + n + " patches applied. Default language: " + ActiveLanguage.Current?.DisplayName +
                    ". Leaderboard score uploads are disabled while the mod is active.");
                try
                {
                    ExtensionBootstrap.CurrentLanguage = () => ActiveLanguage.Id;
                    ExtensionBootstrap.RegisterAll(harmony, Path.Combine(dir, "content"));
                }
                catch (Exception e) { Logger.LogError("registering the extensions failed: " + e); }
                Logger.LogInfo("extensions: " + ExtensionRegistry.All.Count + " registered");
                ActiveLanguage.Changed += (from, to) =>
                {
                    Logger.LogInfo("default language changed from " + from + " to " + to);
                    GamePatches.ReloadStrings();
                };
                GamePatches.ReloadStrings();
            }
            catch (Exception e)
            {
                Logger.LogError("JavaFarmer failed to start: " + e);
                try { harmony?.UnpatchSelf(); } catch { }
            }
        }
    }
}
