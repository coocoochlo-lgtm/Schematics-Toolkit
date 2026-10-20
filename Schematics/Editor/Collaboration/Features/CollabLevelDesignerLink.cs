using System;
using System.IO;
using System.Reflection;

namespace Remedy.Schematics.Collaboration
{
    /// <summary>
    /// Talks to the Level Designer (Features/Level Designer) when it's in the project, by reflection so collaboration
    /// doesn't depend on it: a level is a blockout scene with variant scenes loaded additively on top, and the Level
    /// Designer knows which scene is which and how to open a variant properly (blockout + that variant).
    /// API used: Remedy.LevelDesigner.Editor.LevelDesigner.OpenVariant(string scenePath) and
    /// TryGetLevelOf(string scenePath, out string levelName, out bool isBlockout).
    /// </summary>
    internal static class CollabLevelDesignerLink
    {
        private static bool _looked;
        private static MethodInfo _openVariant, _tryGetLevelOf;

        private static void Look()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                var type = Type.GetType("Remedy.LevelDesigner.Editor.LevelDesigner, Remedy.LevelDesigner.Editor", false);
                if (type == null) return;
                _openVariant = type.GetMethod("OpenVariant", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                _tryGetLevelOf = type.GetMethod("TryGetLevelOf", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(string).MakeByRefType(), typeof(bool).MakeByRefType() }, null);
            }
            catch (Exception e) { CollabLog.Exception("Looking for the Level Designer failed", e); }
        }

        /// <summary>The level a scene belongs to, if the Level Designer knows it.</summary>
        public static bool TryGetLevelOf(string scenePath, out string levelName, out bool isBlockout)
        {
            levelName = null;
            isBlockout = false;
            Look();
            if (_tryGetLevelOf == null || string.IsNullOrEmpty(scenePath)) return false;
            try
            {
                var args = new object[] { scenePath, null, false };
                if (!(_tryGetLevelOf.Invoke(null, args) is bool found) || !found) return false;
                levelName = args[1] as string;
                isBlockout = args[2] is bool b && b;
                return true;
            }
            catch (Exception e)
            {
                CollabLog.Exception("Asking the Level Designer about a scene failed", e);
                return false;
            }
        }

        /// <summary>True when the Level Designer knows the scene as a variant (not a blockout).</summary>
        public static bool IsVariant(string scenePath) => TryGetLevelOf(scenePath, out _, out bool isBlockout) && !isBlockout;

        /// <summary>Opens a variant the Level Designer's way (it asks to save first). False if it can't (or isn't there).</summary>
        public static bool OpenVariant(string scenePath)
        {
            Look();
            if (_openVariant == null || !IsVariant(scenePath)) return false;
            try { return _openVariant.Invoke(null, new object[] { scenePath }) is bool ok && ok; }
            catch (Exception e)
            {
                CollabLog.Exception("Opening the variant with the Level Designer failed", e);
                return false;
            }
        }

        /// <summary>"Level · Variant" for a variant, "Level (blockout)" for a blockout, else the scene's file name.</summary>
        public static string DisplayName(string scenePath)
        {
            // Drawn every repaint: remembered for a few seconds.
            double now = UnityEditor.EditorApplication.timeSinceStartup;
            string key = scenePath ?? "";
            if (_names.TryGetValue(key, out var cached) && now - cached.at < 5.0) return cached.name;
            string name = ComputeDisplayName(scenePath);
            _names[key] = (name, now);
            return name;
        }

        private static readonly System.Collections.Generic.Dictionary<string, (string name, double at)> _names = new();

        private static string ComputeDisplayName(string scenePath)
        {
            string file = Path.GetFileNameWithoutExtension(scenePath ?? "");
            if (!TryGetLevelOf(scenePath, out string level, out bool isBlockout) || string.IsNullOrEmpty(level)) return file;
            if (isBlockout) return level + " (blockout)";
            // Variant scenes are usually named "<Level>_<Variant>".
            string variant = file.StartsWith(level + "_", StringComparison.OrdinalIgnoreCase) ? file.Substring(level.Length + 1) : file;
            return level + " · " + variant;
        }
    }
}
