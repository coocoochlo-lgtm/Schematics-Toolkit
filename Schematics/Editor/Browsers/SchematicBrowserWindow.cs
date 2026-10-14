using System;
using System.Linq;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The shared shape of every Schematics browser window: a header (a hint on the left, the window's own controls,
    /// then buttons to jump between the browsers and the Level Designer) above a <see cref="SchematicTemplateGrid"/>
    /// (search, thumbnail size, chips and the tiles). Styled by one shared stylesheet, <c>SchematicBrowser.uss</c>, so
    /// every browser looks alike and restyling them is a one-file change.
    /// </summary>
    /// <remarks>
    /// These are ordinary <see cref="EditorWindow"/>s: they open as tabs and dock anywhere, next to the Project window
    /// by default. To make another browser, derive from this, give it a <see cref="PrefsKey"/>, set up the grid in
    /// <see cref="ConfigureGrid"/> (its Source, chips and right-click menu), say what a double-click does in
    /// <see cref="OnPicked"/>, and add an Open method that calls <see cref="OpenWindow{T}"/>.
    /// </remarks>
    public abstract class SchematicBrowserWindow : EditorWindow
    {
        /// <summary>The shared stylesheet's file name (found anywhere in the project).</summary>
        public const string StyleSheetName = "SchematicBrowser";

        private static StyleSheet _styleSheet;

        /// <summary>The grid of tiles (null until the window's UI is built).</summary>
        protected SchematicTemplateGrid Grid { get; private set; }

        /// <summary>The header row; <see cref="BuildHeader"/> fills its middle.</summary>
        protected VisualElement Header { get; private set; }

        /// <summary>EditorPrefs prefix for this browser's grid (tile size, category).</summary>
        protected abstract string PrefsKey { get; }

        /// <summary>The tab's title.</summary>
        protected abstract string Title { get; }

        /// <summary>The tab's icon (a built-in editor icon name), or null.</summary>
        protected virtual string IconName => null;

        /// <summary>The short hint at the left of the header.</summary>
        protected virtual string Hint => null;

        /// <summary>Set up the grid: its Source, chips, filter, right-click menu, "+" tile…</summary>
        protected virtual void ConfigureGrid(SchematicTemplateGrid grid) { }

        /// <summary>Add this window's own controls to the header (toggles, dropdowns, buttons).</summary>
        protected virtual void BuildHeader(VisualElement header) { }

        /// <summary>A tile was double-clicked, or Enter was pressed on it.</summary>
        protected abstract void OnPicked(SchematicTemplate item);

        /// <summary>
        /// Opens (or focuses) a browser as a dockable tab, next to the Project window when there is one.
        /// </summary>
        protected static T OpenWindow<T>() where T : SchematicBrowserWindow
        {
            var dockNextTo = new[]
            {
                typeof(EditorWindow).Assembly.GetType("UnityEditor.ProjectBrowser"),
                typeof(SceneView),
            }.Where(t => t != null).ToArray();

            var window = GetWindow<T>(null, true, dockNextTo);
            window.titleContent = new GUIContent(window.Title, SchematicBrowsers.Icon(window.IconName));
            window.Show();
            window.Focus();
            return window;
        }

        protected virtual void OnEnable()
        {
            titleContent = new GUIContent(Title, SchematicBrowsers.Icon(IconName));
            minSize = new Vector2(260f, 200f);
        }

        protected virtual void OnDisable() { }

        /// <summary>Builds the UI. Override <see cref="BuildHeader"/> and <see cref="ConfigureGrid"/> rather than this.</summary>
        protected virtual void CreateGUI()
        {
            var root = rootVisualElement;
            var sheet = StyleSheet;
            if (sheet != null)
                root.styleSheets.Add(sheet);
            root.AddToClassList("schematic-browser");
            root.AddToClassList(EditorGUIUtility.isProSkin ? "schematic-browser--dark" : "schematic-browser--light");

            Header = new VisualElement { name = "header" };
            Header.AddToClassList("schematic-browser__header");
            root.Add(Header);

            if (!string.IsNullOrEmpty(Hint))
            {
                var hint = new Label(Hint);
                hint.AddToClassList("schematic-browser__hint");
                Header.Add(hint);
            }

            var controls = new VisualElement();
            controls.AddToClassList("schematic-browser__controls");
            Header.Add(controls);
            BuildHeader(controls);

            Header.Add(BuildNavigation());

            Grid = new SchematicTemplateGrid(PrefsKey);
            Grid.AddToClassList("schematic-browser__grid");
            Grid.Picked += OnPicked;
            root.Add(Grid);
            ConfigureGrid(Grid);
        }

        // Buttons to the other Schematics windows, so they feel like one tool. The current one is highlighted.
        private VisualElement BuildNavigation()
        {
            var nav = new VisualElement();
            nav.AddToClassList("schematic-browser__nav");
            AddNavButton(nav, "Templates", "Prefab Icon", "Template Browser: premade Schematic Objects to start from.",
                typeof(TemplateBrowserWindow), SchematicBrowsers.OpenTemplates);
            AddNavButton(nav, "Objects", "PrefabVariant Icon", "Schematic Objects: every Schematic prefab in the project.",
                typeof(SchematicObjectsBrowserWindow), SchematicBrowsers.OpenSchematicObjects);
            if (SchematicBrowsers.HasLevelDesigner)
                AddNavButton(nav, "Level Designer", "SceneAsset Icon", "The Level Designer window: blockouts, art designs and character metrics.",
                    SchematicBrowsers.LevelDesignerType, SchematicBrowsers.OpenLevelDesigner);
            return nav;
        }

        private void AddNavButton(VisualElement nav, string text, string icon, string tooltip, Type windowType, Action open)
        {
            var button = new Button(open) { tooltip = tooltip };
            button.AddToClassList("schematic-browser__nav-button");
            if (windowType != null && windowType.IsInstanceOfType(this))
                button.AddToClassList("schematic-browser__nav-button--current");

            var image = SchematicBrowsers.Icon(icon);
            if (image != null)
            {
                var iconElement = new Image { image = image, pickingMode = PickingMode.Ignore };
                iconElement.AddToClassList("schematic-browser__nav-icon");
                button.Add(iconElement);
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("schematic-browser__nav-label");
            button.Add(label);
            nav.Add(button);
        }

        /// <summary>A header toggle styled like the others, remembered in EditorPrefs under this browser's key.</summary>
        protected Toggle HeaderToggle(string label, string tooltip, string prefKey, bool fallback, Action<bool> changed)
        {
            string key = $"{PrefsKey}.{prefKey}";
            var toggle = new Toggle(label) { tooltip = tooltip, value = EditorPrefs.GetBool(key, fallback) };
            toggle.AddToClassList("schematic-browser__toggle");
            toggle.RegisterValueChangedCallback(evt =>
            {
                EditorPrefs.SetBool(key, evt.newValue);
                changed?.Invoke(evt.newValue);
            });
            return toggle;
        }

        /// <summary>The shared stylesheet (null if it's missing: the browsers still work, just plainer).</summary>
        protected static StyleSheet StyleSheet
        {
            get
            {
                if (_styleSheet != null)
                    return _styleSheet;
                foreach (var guid in AssetDatabase.FindAssets($"{StyleSheetName} t:StyleSheet"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != StyleSheetName)
                        continue;
                    _styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                    if (_styleSheet != null)
                        break;
                }
                return _styleSheet;
            }
        }

        /// <summary>Opens a prefab in Prefab Mode.</summary>
        protected static void OpenPrefab(GameObject prefab)
        {
            if (prefab != null)
                AssetDatabase.OpenAsset(prefab);
        }

        /// <summary>Pings and selects an asset in the Project window.</summary>
        protected static void Ping(UnityEngine.Object asset)
        {
            if (asset == null) return;
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }
    }
}
