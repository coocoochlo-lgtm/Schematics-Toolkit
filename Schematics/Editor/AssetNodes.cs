using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BlueGraph;
using BlueGraph.Editor;
using UnityEditor;

namespace Remedy.Schematics
{
    /// <summary>
    /// Gives assets editor tags: tags the editor works out itself (Audio's Music and SFX, from the folder a clip is in)
    /// rather than ones users make. They aren't shown or edited in the Project Manager's tag bars, and users can't make
    /// tags with their names. Derive from this anywhere in editor code; it's found automatically.
    /// </summary>
    public abstract class EditorTagProvider
    {
        /// <summary>Every tag this can give (reserved: users can't make tags with these names).</summary>
        public abstract IEnumerable<string> Names { get; }

        /// <summary>The tags an asset has from this provider.</summary>
        public abstract IEnumerable<string> TagsOf(UnityEngine.Object asset);
    }

    /// <summary>
    /// An asset's tags, whatever made them: editor tags (<see cref="EditorTagProvider"/>s) and the tags users put on
    /// assets in the Project Manager (plugged in by it through <see cref="UserTags"/>).
    /// </summary>
    public static class AssetTagIndex
    {
        private static List<EditorTagProvider> _providers;

        /// <summary>The names of an asset's user tags (set by the Project Manager, whose tags these are).</summary>
        public static Func<UnityEngine.Object, IEnumerable<string>> UserTags;

        private static List<EditorTagProvider> Providers
            => _providers ??= TypeCache.GetTypesDerivedFrom<EditorTagProvider>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (EditorTagProvider)Activator.CreateInstance(t))
                .ToList();

        /// <summary>The asset's editor tags.</summary>
        public static IEnumerable<string> EditorTagsOf(UnityEngine.Object asset)
            => asset == null ? Enumerable.Empty<string>() : Providers.SelectMany(p => p.TagsOf(asset) ?? Enumerable.Empty<string>());

        /// <summary>Every tag the asset has: editor tags and user tags.</summary>
        public static HashSet<string> TagsOf(UnityEngine.Object asset)
        {
            var tags = new HashSet<string>(EditorTagsOf(asset), StringComparer.OrdinalIgnoreCase);
            if (asset != null && UserTags != null)
                foreach (var tag in UserTags(asset) ?? Enumerable.Empty<string>())
                    tags.Add(tag);
            return tags;
        }

        /// <summary>Whether a name belongs to an editor tag (so users can't make a tag with it).</summary>
        public static bool IsEditorTag(string name)
        {
            name = (name ?? "").Trim();
            return name.Length > 0 && Providers.Any(p => p.Names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)));
        }
    }

    /// <summary>
    /// Which nodes an asset becomes when dropped into a Schematic Graph (<see cref="AssetNodeAttribute"/>), and making them.
    /// </summary>
    public static class AssetNodes
    {
        /// <summary>A node that fits an asset.</summary>
        public sealed class Match
        {
            public Type NodeType;
            public AssetNodeAttribute Attribute;
            public FieldInfo Field;
            public string Title(UnityEngine.Object asset)
                => string.IsNullOrEmpty(Attribute.Title) ? ObjectNames.NicifyVariableName(NodeType.Name) : string.Format(Attribute.Title, asset.name);
        }

        private static List<(Type node, AssetNodeAttribute attribute)> _all;

        private static List<(Type node, AssetNodeAttribute attribute)> All
            => _all ??= TypeCache.GetTypesWithAttribute<AssetNodeAttribute>()
                .Where(t => typeof(Node).IsAssignableFrom(t) && !t.IsAbstract)
                .SelectMany(t => t.GetCustomAttributes<AssetNodeAttribute>(false).Select(a => (t, a)))
                .ToList();

        /// <summary>The nodes a dropped asset can become, in menu order.</summary>
        public static List<Match> For(UnityEngine.Object asset)
        {
            var matches = new List<Match>();
            if (asset == null)
                return matches;
            HashSet<string> tags = null;
            foreach (var (node, attribute) in All)
            {
                if (attribute.AssetType == null || !attribute.AssetType.IsInstanceOfType(asset))
                    continue;
                if (attribute.Tags != null && attribute.Tags.Length > 0)
                {
                    tags ??= AssetTagIndex.TagsOf(asset);
                    bool fits = attribute.RequireAllTags ? attribute.Tags.All(tags.Contains) : attribute.Tags.Any(tags.Contains);
                    if (!fits)
                        continue;
                }
                var field = FieldFor(node, attribute);
                if (field == null)
                {
                    UnityEngine.Debug.LogWarning($"[Schematics] {node.Name} says it takes {attribute.AssetType.Name} assets, but has no field for one{(string.IsNullOrEmpty(attribute.Field) ? "" : $" named '{attribute.Field}'")}.");
                    continue;
                }
                matches.Add(new Match { NodeType = node, Attribute = attribute, Field = field });
            }
            return matches.OrderBy(m => m.Attribute.Order).ThenBy(m => m.NodeType.Name).ToList();
        }

        private static FieldInfo FieldFor(Type node, AssetNodeAttribute attribute)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            if (!string.IsNullOrEmpty(attribute.Field))
            {
                var named = node.GetField(attribute.Field, flags);
                return named != null && named.FieldType.IsAssignableFrom(attribute.AssetType) ? named : null;
            }
            return node.GetFields(flags).FirstOrDefault(f => f.FieldType.IsAssignableFrom(attribute.AssetType) && f.FieldType != typeof(object));
        }

        /// <summary>Makes the node with the asset in its field and its title set (not yet added to a graph).</summary>
        public static Node Create(Match match, UnityEngine.Object asset)
        {
            var reflected = NodeReflection.GetNodeType(match.NodeType);
            if (reflected == null)
                return null;
            var node = reflected.CreateInstance();
            match.Field.SetValue(node, asset);
            node.Name = match.Title(asset);
            return node;
        }
    }
}
