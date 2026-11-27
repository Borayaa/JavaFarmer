using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using JavaFarmer.Compiler;
using JavaFarmer.Plugin.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace JavaFarmer.Plugin
{
    /// <summary>
    /// The "Mods" button of the title page / pause menu and its panel (docs/EXTENSIONS.md §1, research §4.2-4.4):
    /// a clone of the Options panel with only our rows: Programming language (cycles through Languages.All) and one
    /// Enabled/Disabled row per ExtensionRegistry.All. Changes are pending until "apply": a warningPopup asks, then
    /// the game saves, the settings change and the scene reloads. The language row is the DEFAULT language (new files, docs):
    /// existing files keep their own language (their extension) and are never converted.
    /// Built again on every scene load (Menu.Start postfix).
    /// </summary>
    public static class ModsMenu
    {
        public const string ButtonName = "ModsButton";
        public const string PanelName = "ModsPanel";
        public const string ButtonLabel = "Mods";

        private static Menu menu;
        private static GameObject titlePage;
        private static GameObject panel;
        private static ColoredButton applyButton;
        private static ColoredButton languageButton;
        private static readonly Dictionary<string, ColoredButton> extensionButtons = new Dictionary<string, ColoredButton>();
        private static string pendingLanguage;
        private static readonly Dictionary<string, bool> pendingExtensions = new Dictionary<string, bool>();
        private static bool dumped;

        public static bool IsOpen => panel != null && panel.activeSelf;

        private static T Field<T>(object o, string name) where T : class
        {
            var f = AccessTools.Field(o.GetType(), name);
            return f?.GetValue(o) as T;
        }

        public static void Install(Menu m)
        {
            menu = m;
            panel = null;
            applyButton = null;
            languageButton = null;
            extensionButtons.Clear();
            titlePage = Field<GameObject>(m, "titlePage");
            var options = Field<OptionMenu>(m, "options");
            if (titlePage == null || options == null)
            {
                Log.Error("Mods menu: Menu.titlePage/options not found (game update?): no Mods button.");
                return;
            }
            if (!dumped)
            {
                dumped = true;
                Log.Info("Mods menu: title page = " + Describe(titlePage.transform, 1) + "\n options = " + Describe(options.transform, 3));
            }
            try { BuildPanel(options); }
            catch (Exception e) { Log.Error("Mods menu: could not build the panel", e); panel = null; }
            if (panel != null) AddButton();
        }

        // ------------------------------------------------------------------ title page button

        private static void AddButton()
        {
            var t = titlePage.transform;
            var old = t.Find(ButtonName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var optionsBtn = t.Find("OptionsButton") as RectTransform;
            if (optionsBtn == null)
            {
                foreach (Transform c in t)
                    if (c.GetComponent<ColoredButton>() != null && c.name.IndexOf("Option", StringComparison.OrdinalIgnoreCase) >= 0) { optionsBtn = c as RectTransform; break; }
            }
            if (optionsBtn == null) { Log.Error("Mods menu: OptionsButton not found on the title page."); return; }

            // the column of title buttons (same x), top to bottom, before the clone exists
            var column = new List<RectTransform>();
            foreach (Transform c in t)
            {
                var rt = c as RectTransform;
                if (rt == null || c.GetComponent<Button>() == null) continue;
                if (Mathf.Abs(rt.anchoredPosition.x - optionsBtn.anchoredPosition.x) < 1f) column.Add(rt);
            }
            column.Sort((a, b) => b.anchoredPosition.y.CompareTo(a.anchoredPosition.y));
            float step = 62.7f;
            if (column.Count > 1)
            {
                var gaps = new List<float>();
                for (int i = 1; i < column.Count; i++) gaps.Add(column[i - 1].anchoredPosition.y - column[i].anchoredPosition.y);
                gaps.Sort();
                step = gaps[gaps.Count / 2];
                if (step < 20f) step = 62.7f;
            }

            var clone = Object.Instantiate(optionsBtn.gameObject, t);
            clone.name = ButtonName;
            clone.transform.SetSiblingIndex(optionsBtn.GetSiblingIndex() + 1);
            var button = clone.GetComponent<Button>();
            DisablePersistent(button.onClick);
            button.onClick.AddListener(Open);
            var cb = clone.GetComponent<ColoredButton>();
            if (cb != null)
            {
                cb.Text = ButtonLabel;
                cb.tooltipDescription = "Programming language and content extensions (JavaFarmer).";
            }

            // reflow: Mods goes right below Options, the column stays centred where it was
            var rtClone = (RectTransform)clone.transform;
            int at = column.IndexOf(optionsBtn);
            float top = column.Count > 0 ? column[0].anchoredPosition.y : optionsBtn.anchoredPosition.y;
            column.Insert(at < 0 ? column.Count : at + 1, rtClone);
            top += step / 2f;
            for (int i = 0; i < column.Count; i++)
                column[i].anchoredPosition = new Vector2(column[i].anchoredPosition.x, top - i * step);
        }

        private static void DisablePersistent(UnityEventBase e)
        {
            if (e == null) return;
            for (int i = 0; i < e.GetPersistentEventCount(); i++) e.SetPersistentListenerState(i, UnityEventCallState.Off);
        }

        // ------------------------------------------------------------------ panel

        private static void BuildPanel(OptionMenu options)
        {
            var src = options.gameObject;
            panel = Object.Instantiate(src, src.transform.parent);
            panel.name = PanelName;
            panel.SetActive(false);
            var om = panel.GetComponent<OptionMenu>();
            var container = Field<RectTransform>(om, "optionContainer");
            var tabs = Field<RectTransform>(om, "tabsContainer");
            var tabPrefab = Field<ColoredButton>(om, "coloredButtonPrefab");
            Object.DestroyImmediate(om); // the clone must never behave as the options menu
            if (container == null || tabs == null || tabPrefab == null) throw new InvalidOperationException("OptionMenu fields not found");

            for (int i = container.childCount - 1; i >= 0; i--) Object.DestroyImmediate(container.GetChild(i).gameObject);

            Transform back = null;
            for (int i = tabs.childCount - 1; i >= 0; i--)
            {
                var c = tabs.GetChild(i);
                if (back == null && c.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) back = c;
                else Object.DestroyImmediate(c.gameObject);
            }
            if (back == null)
            {
                foreach (var b in panel.GetComponentsInChildren<Button>(true))
                    if (b.name.IndexOf("Back", StringComparison.OrdinalIgnoreCase) >= 0) { back = b.transform; break; }
            }
            if (back != null)
            {
                var b = back.GetComponent<Button>();
                if (b != null)
                {
                    DisablePersistent(b.onClick);
                    b.onClick.RemoveAllListeners();
                    b.onClick.AddListener(Back);
                }
            }
            else Log.Warn("Mods menu: no back button found in the cloned options panel.");

            var header = Object.Instantiate(tabPrefab, tabs);
            header.name = "ModsTab";
            header.Text = "mods";
            header.State = ColoredButton.ButtonState.disabled;
            applyButton = Object.Instantiate(tabPrefab, tabs);
            applyButton.name = "ApplyButton";
            applyButton.Text = "apply";
            applyButton.tooltipDescription = "Save the game and reload it with these settings.";
            applyButton.OnClick.AddListener(Apply);

            var rowPrefab = ResourceManager.GetAllOptions().OfType<CycleOptionSO>().Select(o => o.optionUI).FirstOrDefault(u => u != null);
            if (rowPrefab == null) throw new InvalidOperationException("no cycle option row prefab");

            languageButton = AddRow(rowPrefab, container, "default language",
                "Language of new files and of the docs, tooltips and unlock boxes. Every file has its own language, chosen by its extension: " +
                "rename a file to main.java, utils.rs or bot.py to change it. Changing the default never changes your existing files.",
                "for new files and the docs; a file's extension (main.java, bot.py) sets its own language", CycleLanguage);
            foreach (var ext in ExtensionRegistry.All)
            {
                var e = ext;
                extensionButtons[e.Id] = AddRow(rowPrefab, container, (e.DisplayName ?? e.Id).ToLowerInvariant(), e.Description ?? "", e.Description, () => ToggleExtension(e.Id));
            }
        }

        private static ColoredButton AddRow(OptionUI prefab, RectTransform container, string title, string tooltip, string description, Action onClick)
        {
            var ui = Object.Instantiate(prefab, container);
            var so = ScriptableObject.CreateInstance<CycleOptionSO>();
            so.name = "javafarmer " + title;
            so.optionName = title;
            so.tooltip = tooltip;
            so.category = "mods";
            so.options = new List<string> { "" };
            ui.Setup(so);
            var cb = AccessTools.Field(typeof(CycleOptionUI), "button")?.GetValue(ui) as ColoredButton;
            if (cb == null) cb = ui.GetComponentInChildren<ColoredButton>(true);
            var button = cb.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            DisablePersistent(button.onClick);
            button.onClick.AddListener(() => onClick());
            if (!string.IsNullOrEmpty(description))
            {
                var titleText = AccessTools.Field(typeof(OptionUI), "title")?.GetValue(ui) as TextMeshProUGUI;
                if (titleText != null)
                {
                    // the name, then the description in small grey text that wraps inside the row (the row's auto-size
                    // keeps both inside the row)
                    titleText.richText = true;
                    titleText.enableWordWrapping = true;
                    titleText.overflowMode = TextOverflowModes.Overflow;
                    titleText.text = title + "\n<size=55%><color=#b4b4b4>" + description.Replace("<", "&lt;") + "</color></size>";
                    AddFallbackFont(titleText, cb);
                }
            }
            return cb;
        }

        /// <summary>
        /// The option-row title font draws some capitals as blanks ("Insight" showed as "nsight"; a fallback font does not
        /// help because the glyph exists). The row's own button font renders every letter ("Disabled", "Java"): use it.
        /// </summary>
        private static void AddFallbackFont(TextMeshProUGUI text, ColoredButton button)
        {
            try
            {
                var label = button != null ? button.GetComponentInChildren<TextMeshProUGUI>(true) : null;
                var other = label?.font;
                if (other == null || ReferenceEquals(text.font, other)) return;
                text.font = other;
                if (label.fontSharedMaterial != null) text.fontSharedMaterial = label.fontSharedMaterial;
                text.SetAllDirty();
            }
            catch (Exception e)
            {
                Log.Warn("Mods menu: no fallback font: " + e.Message);
            }
        }

        // ------------------------------------------------------------------ state

        public static void Open()
        {
            try
            {
                if (panel == null || menu == null) return;
                pendingLanguage = ActiveLanguage.Id;
                pendingExtensions.Clear();
                foreach (var e in ExtensionRegistry.All) pendingExtensions[e.Id] = ExtensionRegistry.IsEnabled(e.Id);
                titlePage.SetActive(false);
                panel.SetActive(true);
                Refresh();
            }
            catch (Exception e)
            {
                Log.Error("Mods menu: open failed", e);
            }
        }

        public static void Hide()
        {
            if (panel != null && panel.activeSelf) panel.SetActive(false);
        }

        private static void Back()
        {
            Hide();
            menu?.Open();
        }

        private static void CycleLanguage()
        {
            var all = Languages.All;
            if (all.Count == 0) return;
            int i = 0;
            for (int k = 0; k < all.Count; k++) if (all[k].Id == pendingLanguage) { i = k; break; }
            pendingLanguage = all[(i + 1) % all.Count].Id;
            Refresh();
        }

        private static void ToggleExtension(string id)
        {
            pendingExtensions[id] = !(pendingExtensions.TryGetValue(id, out var on) && on);
            Refresh();
        }

        private static string NameOf(string id) => Languages.Get(id)?.DisplayName ?? id;

        public static bool HasChanges =>
            (pendingLanguage != null && pendingLanguage != ActiveLanguage.Id) ||
            pendingExtensions.Any(kv => kv.Value != ExtensionRegistry.IsEnabled(kv.Key));

        private static void Refresh()
        {
            if (languageButton != null) languageButton.Text = NameOf(pendingLanguage) + (pendingLanguage != ActiveLanguage.Id ? " *" : "");
            foreach (var kv in extensionButtons)
            {
                bool on = pendingExtensions.TryGetValue(kv.Key, out var v) && v;
                kv.Value.Text = (on ? "Enabled" : "Disabled") + (on != ExtensionRegistry.IsEnabled(kv.Key) ? " *" : "");
            }
            if (applyButton != null) applyButton.State = HasChanges ? ColoredButton.ButtonState.normal : ColoredButton.ButtonState.disabled;
        }

        /// <summary>The confirmation text for the pending changes (pure; unit tested).</summary>
        public static string ConfirmText(string fromLanguage, string toLanguage, IEnumerable<(string name, bool on)> extensionChanges)
        {
            var sb = new StringBuilder("Apply these changes?\n");
            if (toLanguage != null && toLanguage != fromLanguage)
            {
                string a = NameOf(fromLanguage), b = NameOf(toLanguage);
                sb.Append("\n- Default language: ").Append(a).Append(" -> ").Append(b).Append(". New files are ").Append(b)
                  .Append(" (f0").Append(FileLanguages.ExtensionOfLanguage(toLanguage)).Append("), and the docs show ").Append(b)
                  .Append(". Your existing files do not change: each file keeps the language of its extension.");
            }
            foreach (var (name, on) in extensionChanges ?? Enumerable.Empty<(string, bool)>())
                sb.Append("\n- ").Append(name).Append(": ").Append(on ? "enabled" : "disabled");
            sb.Append("\n\nThe game saves and reloads.");
            return sb.ToString();
        }

        private static void Apply()
        {
            try
            {
                if (!HasChanges) return;
                var popup = MainSim.Inst?.warningPopup;
                var changes = pendingExtensions.Where(kv => kv.Value != ExtensionRegistry.IsEnabled(kv.Key))
                    .Select(kv => (ExtensionRegistry.Get(kv.Key)?.DisplayName ?? kv.Key, kv.Value)).ToList();
                string text = ConfirmText(ActiveLanguage.Id, pendingLanguage, changes);
                if (popup == null) { DoApply(); return; }
                popup.ShowPopup("<noparse>" + text + "</noparse>", new List<WarningPopup.ButtonData>
                {
                    new WarningPopup.ButtonData("apply", DoApply),
                    new WarningPopup.ButtonData("cancel", popup.Close),
                });
            }
            catch (Exception e)
            {
                Log.Error("Mods menu: apply failed", e);
            }
        }

        private static void DoApply()
        {
            try
            {
                var sim = MainSim.Inst;
                sim?.warningPopup?.Close();
                string lang = pendingLanguage;
                var ext = pendingExtensions.ToList();
                Saver.StopFileWatcher();
                // files keep their own language (extension); the default only changes new files and the docs
                if (sim != null) Saver.Save(sim);
                foreach (var kv in ext)
                    if (kv.Value != ExtensionRegistry.IsEnabled(kv.Key)) ExtensionRegistry.SetEnabled(kv.Key, kv.Value);
                if (lang != null && lang != ActiveLanguage.Id) ActiveLanguage.Set(lang);
                Log.Info("Mods menu: applied (language " + ActiveLanguage.Id + "), reloading the scene");
                SceneManager.LoadScene(0);
            }
            catch (Exception e)
            {
                Log.Error("Mods menu: applying the changes failed", e);
            }
        }

        // ------------------------------------------------------------------ diagnostics

        private static string Describe(Transform t, int depth)
        {
            var sb = new StringBuilder();
            void Walk(Transform x, int d, string indent)
            {
                var rt = x as RectTransform;
                sb.Append('\n').Append(indent).Append(x.name);
                if (rt != null) sb.Append(" @").Append(rt.anchoredPosition.x.ToString("0.#")).Append(',').Append(rt.anchoredPosition.y.ToString("0.#"));
                sb.Append(" [").Append(string.Join(",", x.GetComponents<Component>().Where(c => c != null && !(c is Transform)).Select(c => c.GetType().Name))).Append(']');
                if (d <= 0) return;
                foreach (Transform c in x) Walk(c, d - 1, indent + "  ");
            }
            Walk(t, depth, "  ");
            return sb.ToString();
        }
    }
}
