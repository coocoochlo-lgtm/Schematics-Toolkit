using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Surfaces
{
    /// <summary>
    /// Surface Materials in the editor: finding them all, keeping the <see cref="SurfaceLibrary"/>'s list and Default
    /// up to date, and making new ones (in Assets/Content/Surfaces).
    /// </summary>
    public static class SurfaceLibraryEditor
    {
        public const string Folder = "Assets/Content/Surfaces";

        /// <summary>Every Surface Material in the project, by name.</summary>
        public static List<SurfaceMaterial> AllMaterials()
            => AssetDatabase.FindAssets("t:" + nameof(SurfaceMaterial))
                .Select(guid => AssetDatabase.LoadAssetAtPath<SurfaceMaterial>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m != null)
                .OrderBy(m => m.name)
                .ToList();

        /// <summary>
        /// Makes sure there's a Default material (made, gray, the first time) and that the library lists every
        /// material. Returns the library.
        /// </summary>
        public static SurfaceLibrary Sync()
        {
            var library = SurfaceLibrary.Instance;
            if (library == null)
                return null;
            bool changed = false;
            if (library.Default == null)
            {
                library.Default = AllMaterials().FirstOrDefault(m => m.name == "Default") ?? Create("Default", new Color(0.55f, 0.55f, 0.55f));
                changed = true;
            }
            var all = AllMaterials();
            if (!library.Materials.SequenceEqual(all))
            {
                library.Materials = all;
                changed = true;
            }
            if (changed)
            {
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
            }
            return library;
        }

        /// <summary>A new Surface Material asset (in <see cref="Folder"/>), with a unique name.</summary>
        public static SurfaceMaterial Create(string name, Color color)
        {
            EnsureFolder(Folder);
            var material = ScriptableObject.CreateInstance<SurfaceMaterial>();
            material.PaintColor = color;
            AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{name}.asset"));
            AssetDatabase.SaveAssets();
            return material;
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
