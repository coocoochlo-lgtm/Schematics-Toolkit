using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Remedy.Schematics.Templates;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Browsers
{
    /// <summary>
    /// The kinds of Schematic the Project Manager lists, found from <see cref="SchematicTypeAttribute"/> on graph types,
    /// and the graph assets of each kind (kept until the project changes).
    /// </summary>
    public static class SchematicTypes
    {
        /// <summary>One kind of Schematic.</summary>
        public sealed class Info
        {
            public Type GraphType;
            public SchematicTypeAttribute Attribute;
            public MethodInfo Creator;

            public string Name => Attribute.Name;
            public string Key => GraphType.FullName;
            public bool ListsPrefabs => Attribute.ListsPrefabs;
            public string DefaultFolder => !string.IsNullOrEmpty(Attribute.DefaultFolder)
                ? Attribute.DefaultFolder.TrimEnd('/')
                : "Assets/Content/Schematics/" + Attribute.Name;
            public string NewAssetName => !string.IsNullOrEmpty(Attribute.NewAssetName)
                ? Attribute.NewAssetName
                : "New " + (Attribute.Name.EndsWith("s") ? Attribute.Name.Substring(0, Attribute.Name.Length - 1) : Attribute.Name);
        }

        private static List<Info> _all;
        private static Dictionary<string, List<string>> _assetsByType;

        /// <summary>Forgets the graph asset lists (they're found again when next asked for).</summary>
        public static void Invalidate() => _assetsByType = null;

        /// <summary>Raised when graph assets were added, removed or moved.</summary>
        public static event Action Changed;

        /// <summary>Every kind, in sidebar order.</summary>
        public static IReadOnlyList<Info> All
        {
            get
            {
                if (_all != null)
                    return _all;
                var creators = TypeCache.GetMethodsWithAttribute<SchematicTypeCreatorAttribute>()
                    .Where(m => m.IsStatic && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)
                        && typeof(UnityEngine.Object).IsAssignableFrom(m.ReturnType))
                    .ToDictionary(m => m.GetCustomAttribute<SchematicTypeCreatorAttribute>().GraphType, m => m);
                _all = TypeCache.GetTypesWithAttribute<SchematicTypeAttribute>()
                    .Where(t => typeof(SchematicGraph).IsAssignableFrom(t) && !t.IsAbstract)
                    .Select(t => new Info
                    {
                        GraphType = t,
                        Attribute = t.GetCustomAttribute<SchematicTypeAttribute>(false),
                        Creator = creators.TryGetValue(t, out var creator) ? creator : null,
                    })
                    .Where(i => i.Attribute != null)
                    .OrderBy(i => i.Attribute.Order).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return _all;
            }
        }

        /// <summary>The kind a graph type belongs to: its own attribute, else its nearest base type's.</summary>
        public static Info Of(Type graphType)
        {
            for (var type = graphType; type != null && type != typeof(object); type = type.BaseType)
            {
                var info = All.FirstOrDefault(i => i.GraphType == type);
                if (info != null)
                    return info;
            }
            return null;
        }

        public static Info Find(string key) => All.FirstOrDefault(i => i.Key == key);

        /// <summary>The paths of the graph assets of a kind (not for prefab kinds, whose list is the Schematic Object index).</summary>
        public static IReadOnlyList<string> AssetPaths(Info info)
        {
            if (info == null || info.ListsPrefabs)
                return Array.Empty<string>();
            if (_assetsByType == null)
            {
                _assetsByType = new Dictionary<string, List<string>>();
                foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(SchematicGraph), new[] { "Assets" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                    var kind = type != null ? Of(type) : null;
                    if (kind == null || kind.ListsPrefabs)
                        continue;
                    if (!_assetsByType.TryGetValue(kind.Key, out var list))
                        _assetsByType[kind.Key] = list = new List<string>();
                    list.Add(path);
                }
            }
            return _assetsByType.TryGetValue(info.Key, out var paths) ? paths : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>
        /// Makes a new Schematic of a kind in a folder: the kind's creator method when available, an empty Schematic
        /// Object prefab for generic prefab kinds, else an empty graph asset. Returns the new asset (null if it failed).
        /// </summary>
        public static UnityEngine.Object Create(Info info, string folder)
        {
            if (info == null)
                return null;
            folder = string.IsNullOrEmpty(folder) ? info.DefaultFolder : folder.TrimEnd('/');
            ProjectFolderTree.CreateFolderPath(folder);

            if (info.Creator != null)
            {
                try
                {
                    return info.Creator.Invoke(null, new object[] { folder }) as UnityEngine.Object;
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[Project Manager] Couldn't make a new {info.Name} item: {exception.GetBaseException().Message}");
                    return null;
                }
            }
            if (info.ListsPrefabs)
            {
                string prefabPath = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{info.NewAssetName}.prefab");
                return SchematicObjectFactory.CreateEmptySchematicPrefab(prefabPath);
            }

            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{info.NewAssetName}.asset");
            var graph = ScriptableObject.CreateInstance(info.GraphType);
            graph.name = Path.GetFileNameWithoutExtension(path);
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            return graph;
        }

        internal sealed class Watcher : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
            {
                if (_assetsByType == null)
                    return;
                bool relevant = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                    .Any(p => p.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || AssetDatabase.IsValidFolder(p) || !Path.HasExtension(p));
                if (!relevant)
                    return;
                _assetsByType = null;
                EditorApplication.delayCall -= RaiseChanged;
                EditorApplication.delayCall += RaiseChanged;
            }

            private static void RaiseChanged() => Changed?.Invoke();
        }
    }
}
