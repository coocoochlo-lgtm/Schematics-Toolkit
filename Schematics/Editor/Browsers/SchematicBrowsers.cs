using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// One place that routes the legacy browser shortcuts into Project Manager's matching section, with an optional
    /// Level Designer discovered by type name so the browser assembly does not depend on that feature.
    /// </summary>
    /// <remarks>
    /// The Level Designer is a separate feature (Features/Level Designer) that this assembly deliberately doesn't
    /// reference, so it's found by type name at runtime. If it isn't in the project, its button simply says so.
    /// </remarks>
    public static class SchematicBrowsers
    {
        /// <summary>The Level Designer window's full type name (with its assembly), looked up by reflection.</summary>
        public const string LevelDesignerTypeName = "Remedy.LevelDesigner.Editor.LevelDesignerWindow, Remedy.LevelDesigner.Editor";

        /// <summary>The Level Designer's own menu item, used if its type can't be opened directly.</summary>
        public const string LevelDesignerMenuPath = "Window/Remedy/Level Designer";

        private static Type _levelDesignerType;
        private static bool _levelDesignerLooked;

        /// <summary>Opens the non-dockable template chooser to create a Schematic Object prefab.</summary>
        public static void OpenTemplates() => TemplateBrowserWindow.Open();

        /// <summary>Opens Project Manager on the Schematic Objects section.</summary>
        public static void OpenSchematicObjects() => SchematicObjectsBrowserWindow.Open();

        /// <summary>The Level Designer window type, or null when that feature isn't in the project.</summary>
        public static Type LevelDesignerType
        {
            get
            {
                if (!_levelDesignerLooked)
                {
                    _levelDesignerLooked = true;
                    _levelDesignerType = Type.GetType(LevelDesignerTypeName, false);
                }
                return _levelDesignerType;
            }
        }

        /// <summary>Whether the Level Designer is in the project.</summary>
        public static bool HasLevelDesigner => LevelDesignerType != null;

        /// <summary>
        /// Opens (or focuses) the Level Designer: its own static <c>Open()</c> when there is one (it sets the title and
        /// size), else <c>EditorWindow.GetWindow</c> on its type, else its menu item.
        /// </summary>
        public static void OpenLevelDesigner()
        {
            if (HasLevelDesigner)
            {
                ProjectManagerWindow.OpenLevelDesigner();
                return;
            }
            var type = LevelDesignerType;
            if (type != null)
            {
                try
                {
                    var open = type.GetMethod("Open", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null);
                    if (open != null)
                        open.Invoke(null, null);
                    else
                        EditorWindow.GetWindow(type, false, "Level Designer", true);
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Schematics] Couldn't open the Level Designer directly ({exception.GetBaseException().Message}); trying its menu item.");
                }
            }

            if (!EditorApplication.ExecuteMenuItem(LevelDesignerMenuPath))
                Debug.LogWarning("[Schematics] The Level Designer isn't in this project (Features/Level Designer).");
        }

        #region Menus

        [MenuItem("Window/Remedy/Template Browser", false, 100)]
        private static void TemplatesMenu() => OpenTemplates();

        [MenuItem("Window/Remedy/Schematic Objects", false, 101)]
        private static void SchematicObjectsMenu() => OpenSchematicObjects();

        [MenuItem("Tools/Remedy/Schematics/Templates/Template Browser", false, 0)]
        private static void TemplatesToolsMenu() => OpenTemplates();

        [MenuItem("Tools/Remedy/Schematics/Schematic Objects", false, 1)]
        private static void SchematicObjectsToolsMenu() => OpenSchematicObjects();

        #endregion

        /// <summary>A built-in editor icon by name, or null (without Unity's "Unable to load the icon" warning).</summary>
        internal static Texture2D Icon(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var texture = EditorGUIUtility.FindTexture(name);
            if (texture == null && EditorGUIUtility.isProSkin)
                texture = EditorGUIUtility.FindTexture("d_" + name);
            return texture;
        }
    }
}
