// Shared contract between the Mods menu (plugin-lang work) and the content extensions (extensions work).
// See docs/EXTENSIONS.md §2. Owners may ADD members; do not rename or remove these.
using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace JavaFarmer.Plugin.Extensions
{
    /// <summary>A content extension the player can turn on in the Mods menu. The extensions work adds more members.</summary>
    public interface IExtension
    {
        /// <summary>"cybersecurity", "ai_ml".</summary>
        string Id { get; }
        /// <summary>"Cybersecurity", "AI / ML".</summary>
        string DisplayName { get; }
        /// <summary>One line for the Mods panel.</summary>
        string Description { get; }
    }

    /// <summary>Which extensions exist and which are enabled (global setting, persisted in the BepInEx config).</summary>
    public static class ExtensionRegistry
    {
        private static readonly List<IExtension> all = new List<IExtension>();
        private static readonly Dictionary<string, ConfigEntry<bool>> enabled = new Dictionary<string, ConfigEntry<bool>>();
        private static ConfigFile config;

        public static IReadOnlyList<IExtension> All => all;

        /// <summary>Raised after SetEnabled changed a value. The Mods menu then saves and reloads the scene.</summary>
        public static event Action<string, bool> Changed;

        /// <summary>Called once from the plugin's Awake, before Register.</summary>
        public static void Init(ConfigFile cfg) { config = cfg; }

        public static void Register(IExtension ext)
        {
            if (ext == null || all.Exists(e => e.Id == ext.Id)) return;
            all.Add(ext);
            if (config != null)
                enabled[ext.Id] = config.Bind("Extensions", ext.Id, false, "Enable the " + ext.DisplayName + " extension (Mods menu).");
        }

        public static IExtension Get(string id) => all.Find(e => e.Id == id);

        public static bool IsEnabled(string id) => enabled.TryGetValue(id, out var e) && e.Value;

        public static void SetEnabled(string id, bool on)
        {
            if (!enabled.TryGetValue(id, out var e) || e.Value == on) return;
            e.Value = on;
            Changed?.Invoke(id, on);
        }
    }
}
