using System;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#else
using System.Reflection;
using UnityEditor.UIElements;
using UnityEngine.UIElements;
#endif

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// Buttons on Unity's main toolbar (the bar with Play and Pause) for the Level Designer, the Template Browser and the
    /// Schematic Objects browser, so they're one click away instead of in a menu.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Unity 6.3 and newer: added with Unity's own main toolbar API (<c>[MainToolbarElement]</c>), docked on the right.
    /// Like every custom toolbar element, they can be shown, hidden and moved from the toolbar's right-click menu; if
    /// you don't see them, right-click an empty part of the main toolbar and turn on the "Remedy" ones.
    /// </para>
    /// <para>
    /// Older Unity 6 versions have no public API for this, so the buttons are slipped into the toolbar's right-hand zone
    /// (the one holding Layers and Layout) through reflection. If a Unity update changes the toolbar's insides, that just
    /// stops working: one message in the Console, and the same windows are still under Window > Remedy.
    /// </para>
    /// </remarks>
    public static class SchematicMainToolbar
    {
        internal const string LevelDesignerTooltip = "Open the Level Designer (blockouts, art designs, character metrics).";
        internal const string TemplatesTooltip = "Open the Template Browser: premade Schematic Objects to start from.";
        internal const string ObjectsTooltip = "Open the Schematic Objects browser: every Schematic prefab in the project.";

#if UNITY_6000_3_OR_NEWER
        [MainToolbarElement("Remedy/Level Designer", defaultDockPosition = MainToolbarDockPosition.Right)]
        public static MainToolbarElement LevelDesignerButton()
            => new MainToolbarButton(new MainToolbarContent("Level Designer", SchematicBrowsers.Icon("SceneAsset Icon"), LevelDesignerTooltip),
                SchematicBrowsers.OpenLevelDesigner);

        [MainToolbarElement("Remedy/Templates", defaultDockPosition = MainToolbarDockPosition.Right)]
        public static MainToolbarElement TemplatesButton()
            => new MainToolbarButton(new MainToolbarContent("Templates", SchematicBrowsers.Icon("Prefab Icon"), TemplatesTooltip),
                SchematicBrowsers.OpenTemplates);

        [MainToolbarElement("Remedy/Schematic Objects", defaultDockPosition = MainToolbarDockPosition.Right)]
        public static MainToolbarElement SchematicObjectsButton()
            => new MainToolbarButton(new MainToolbarContent("Objects", SchematicBrowsers.Icon("PrefabVariant Icon"), ObjectsTooltip),
                SchematicBrowsers.OpenSchematicObjects);
#endif
    }

#if !UNITY_6000_3_OR_NEWER
    /// <summary>
    /// Unity 6.0 to 6.2: puts the Remedy buttons into the main toolbar's right-hand zone by reflection (see
    /// <see cref="SchematicMainToolbar"/>). Checks about once a second that they're still there (a layout change
    /// rebuilds the toolbar) and puts them back. Gives up quietly, with one Console message, if the toolbar can't be found.
    /// </summary>
    [InitializeOnLoad]
    internal static class SchematicMainToolbarInjector
    {
        private const string ContainerName = "RemedySchematicsToolbar";
        private const string ZoneName = "ToolbarZoneRightAlign";
        private const int MaxAttempts = 30;

        private static VisualElement _container;
        private static double _nextCheck;
        private static int _attempts;
        private static bool _gaveUp;

        static SchematicMainToolbarInjector()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (_gaveUp || EditorApplication.timeSinceStartup < _nextCheck)
                return;
            _nextCheck = EditorApplication.timeSinceStartup + 1.0;
            if (_container != null && _container.panel != null)
                return;

            try
            {
                if (TryInject())
                {
                    _attempts = 0;
                    return;
                }
                if (++_attempts >= MaxAttempts)
                    GiveUp("the main toolbar wasn't found");
            }
            catch (Exception exception)
            {
                GiveUp(exception.GetBaseException().Message);
            }
        }

        private static void GiveUp(string reason)
        {
            _gaveUp = true;
            EditorApplication.update -= Update;
            Debug.Log($"[Schematics] Couldn't add the Level Designer / Templates / Objects buttons to the main toolbar ({reason}). They're under Window > Remedy.");
        }

        private static bool TryInject()
        {
            var toolbarType = typeof(EditorWindow).Assembly.GetType("UnityEditor.Toolbar");
            if (toolbarType == null)
                throw new MissingMemberException("UnityEditor.Toolbar");
            var rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (rootField == null)
                throw new MissingFieldException("UnityEditor.Toolbar", "m_Root");

            var toolbars = Resources.FindObjectsOfTypeAll(toolbarType);
            if (toolbars == null || toolbars.Length == 0)
                return false; // not made yet (early in startup)

            if (rootField.GetValue(toolbars[0]) is not VisualElement root)
                return false;
            var zone = root.Q(ZoneName);
            if (zone == null)
                return false;

            // A leftover from before a reload or a layout change.
            zone.Q(ContainerName)?.RemoveFromHierarchy();

            _container = Build();
            zone.Insert(0, _container);
            return true;
        }

        private static VisualElement Build()
        {
            var container = new VisualElement { name = ContainerName };
            container.style.flexDirection = FlexDirection.Row;
            container.style.alignItems = Align.Center;
            container.style.marginRight = 8;

            if (SchematicBrowsers.HasLevelDesigner)
                container.Add(Button("Level Designer", "SceneAsset Icon", SchematicMainToolbar.LevelDesignerTooltip, SchematicBrowsers.OpenLevelDesigner));
            container.Add(Button("Templates", "Prefab Icon", SchematicMainToolbar.TemplatesTooltip, SchematicBrowsers.OpenTemplates));
            container.Add(Button("Objects", "PrefabVariant Icon", SchematicMainToolbar.ObjectsTooltip, SchematicBrowsers.OpenSchematicObjects));
            return container;
        }

        private static VisualElement Button(string text, string iconName, string tooltip, Action click)
        {
            var button = new ToolbarButton(click) { tooltip = tooltip };
            button.AddToClassList("unity-editor-toolbar-element");
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.height = 20;
            button.style.marginLeft = 2;
            button.style.marginRight = 2;
            button.style.paddingLeft = 5;
            button.style.paddingRight = 6;
            button.style.borderTopLeftRadius = button.style.borderTopRightRadius = 3;
            button.style.borderBottomLeftRadius = button.style.borderBottomRightRadius = 3;
            button.style.borderLeftWidth = button.style.borderRightWidth = 1;
            button.style.borderTopWidth = button.style.borderBottomWidth = 1;

            var icon = SchematicBrowsers.Icon(iconName);
            if (icon != null)
            {
                var image = new Image { image = icon, pickingMode = PickingMode.Ignore };
                image.style.width = 16;
                image.style.height = 16;
                image.style.marginRight = 3;
                button.Add(image);
            }
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.Add(label);
            return button;
        }
    }
#endif
}
