using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Assistant
{
    /// <summary>Something Sprocket was dropped on: what to call it, what to say, and where it is on screen.</summary>
    internal sealed class InspectTarget
    {
        public string Title;
        public string Text;
        public Rect ScreenRect;
        public EditorWindow Window;
    }

    /// <summary>
    /// "What is this?": finds what's under a screen point (a graph node, an IO Dock row, an object in the Scene view,
    /// or for windows without a way to point at items, what's selected) and looks it up in the documentation.
    /// </summary>
    internal static class SprocketInspect
    {
        #region Finding what's there

        /// <summary>
        /// What's at a screen point in a window. Scene view picks arrive a moment later, so this calls back.
        /// </summary>
        public static void At(EditorWindow window, Vector2 screen, Action<InspectTarget> done)
        {
            if (window is SceneView sceneView)
            {
                PickInScene(sceneView, screen, done);
                return;
            }
            done(window is SchematicGraphEditorWindow graph ? InGraph(graph, screen) : InWindow(window));
        }

        private static InspectTarget InGraph(SchematicGraphEditorWindow window, Vector2 screen)
        {
            var root = window.rootVisualElement;
            var world = root.LocalToWorld(screen - window.position.position);
            var picked = root.panel?.Pick(world);
            for (var element = picked; element != null; element = element.parent)
            {
                // IO Dock rows carry their item.
                if (element.userData is DockItem item)
                    return Target(window, element, DockKey(item), DockTitle(item), DockFallback(item));
                if (element is NodeView view && view.Target != null)
                {
                    var node = view.Target;
                    string key = NodeKey(node, out string title);
                    return Target(window, view, key, title ?? node.Name, $"A {Nicify(node.GetType().Name)} node.");
                }
            }
            return Target(window, null, "window:" + window.GetType().Name, "The Schematic Graph", null);
        }

        private static void PickInScene(SceneView view, Vector2 screen, Action<InspectTarget> done)
        {
            // Picking needs the Scene view's GUI, so it happens on its next repaint.
            void Pick(SceneView sceneView)
            {
                if (sceneView != view) return;
                SceneView.duringSceneGui -= Pick;
                GameObject picked = null;
                try
                {
                    var guiPoint = GUIUtility.ScreenToGUIPoint(screen);
                    picked = HandleUtility.PickGameObject(guiPoint, false);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("[Assistant] Couldn't pick in the Scene view: " + exception.Message);
                }
                var target = picked != null ? ForGameObject(picked) : InWindow(view);
                target.Window = view;
                target.ScreenRect = new Rect(screen - new Vector2(30, 30), new Vector2(60, 60));
                done(target);
            }
            SceneView.duringSceneGui += Pick;
            view.Repaint();
        }

        // Windows with no public way to ask what's under a point (Hierarchy, Project, Inspector): what's selected.
        private static InspectTarget InWindow(EditorWindow window)
        {
            var selected = Selection.activeObject;
            InspectTarget target;
            if (selected is GameObject gameObject)
                target = ForGameObject(gameObject);
            else if (selected != null)
                target = ForAsset(selected);
            else
                target = Window(window);
            target.Window = window;
            target.ScreenRect = window.position;
            return target;
        }

        private static InspectTarget Window(EditorWindow window)
        {
            var entry = Docs.Find("window:" + window.GetType().Name);
            return new InspectTarget
            {
                Title = entry?.Title ?? window.titleContent.text,
                Text = entry?.Text ?? "An editor window. Drop me on a node, an IO Dock row or something in the Scene view and I'll tell you what it is.",
                Window = window,
                ScreenRect = window.position,
            };
        }

        private static InspectTarget ForGameObject(GameObject gameObject)
        {
            var text = new StringBuilder();
            var schematic = gameObject.GetComponentInParent<SchematicObject>();
            if (schematic != null)
            {
                text.Append(schematic.gameObject == gameObject ? "A Schematic Object" : $"Part of the Schematic Object \"{schematic.name}\"");
                if (schematic.SchematicGraph != null) text.Append($", run by the graph \"{schematic.SchematicGraph.name}\"");
                text.Append('.');
                if (schematic.Behaviors.TryGetValue(gameObject, out var list) && list != null)
                    foreach (var attachment in list.Items)
                    {
                        var type = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                        if (type == null) continue;
                        var entry = Docs.Find("behavior:" + type.FullName);
                        text.Append($"\n• <b>{Nicify(type.Name)}</b>: {FirstSentence(entry?.Text) ?? "a Behavior."}");
                    }
            }
            else
            {
                var components = gameObject.GetComponents<Component>().Where(c => c != null && !(c is Transform)).ToList();
                text.Append(components.Count == 0 ? "An empty object (just a position in the world)." : "An object with:");
                foreach (var component in components.Take(5))
                {
                    var entry = Docs.Find("component:" + component.GetType().FullName);
                    text.Append($"\n• <b>{Nicify(component.GetType().Name)}</b>{(entry != null ? ": " + FirstSentence(entry.Text) : "")}");
                }
            }
            return new InspectTarget { Title = gameObject.name, Text = text.ToString() };
        }

        private static InspectTarget ForAsset(UnityEngine.Object asset)
        {
            string key = asset is MonoScript script && script.GetClass() != null
                ? (typeof(SchematicBehavior).IsAssignableFrom(script.GetClass()) ? "behavior:" : "node:") + script.GetClass().FullName
                : "component:" + asset.GetType().FullName;
            var entry = Docs.Find(key);
            return new InspectTarget
            {
                Title = asset.name,
                Text = entry?.Text ?? $"A {Nicify(asset.GetType().Name)} asset.",
            };
        }

        private static InspectTarget Target(EditorWindow window, VisualElement element, string key, string title, string fallback)
        {
            var entry = Docs.Find(key);
            var bounds = element != null ? element.worldBound : window.rootVisualElement.worldBound;
            var local = window.rootVisualElement.WorldToLocal(bounds.position);
            return new InspectTarget
            {
                Title = entry?.Title ?? title,
                Text = entry?.Text ?? fallback ?? "I don't know much about this one yet. Its documentation entry can be written in the Sprocket Docs asset.",
                Window = window,
                ScreenRect = new Rect(window.position.position + local, bounds.size),
            };
        }

        private static string NodeKey(Node node, out string title)
        {
            title = null;
            switch (node)
            {
                case GetBehaviorVariableNode get when get.Target?.Type != null:
                    title = $"{Nicify(get.Variable)} ({Nicify(get.Target.Type.Name)})";
                    return $"variable:{get.Target.Type.FullName}.{get.Variable}";
                case SetBehaviorVariableNode set when set.Target?.Type != null:
                    title = $"{Nicify(set.Variable)} ({Nicify(set.Target.Type.Name)})";
                    return $"variable:{set.Target.Type.FullName}.{set.Variable}";
                case BehaviorReferenceNode behavior when behavior.Target?.Type != null:
                    return "behavior:" + behavior.Target.Type.FullName;
            }
            return "node:" + node.GetType().FullName;
        }

        private static string DockKey(DockItem item) => item.Kind switch
        {
            DockItem.ItemKind.Behavior => "behavior:" + item.Behavior?.FullName,
            DockItem.ItemKind.Variable => $"variable:{item.Behavior?.FullName}.{item.Name}",
            DockItem.ItemKind.Signal => $"signal:{item.Behavior?.FullName}.{item.Name}",
            _ => "window:GameObject",
        };

        private static string DockTitle(DockItem item) => item.Kind switch
        {
            DockItem.ItemKind.Behavior => Nicify(item.Behavior?.Name),
            DockItem.ItemKind.GameObject => item.GameObject != null ? item.GameObject.name : "Object",
            _ => Nicify(item.Name),
        };

        private static string DockFallback(DockItem item) => item.Kind switch
        {
            DockItem.ItemKind.Variable => $"A Variable of {Nicify(item.Behavior?.Name)}: a value it keeps for each object. Drag it into the graph to Get or Set it.",
            DockItem.ItemKind.Signal => $"A Signal of {Nicify(item.Behavior?.Name)}. Drag it into the graph to send it or react to it.",
            DockItem.ItemKind.GameObject => "One of this prefab's objects. Behaviors attach to it; drag it into the graph to refer to it.",
            _ => null,
        };

        #endregion

        #region Documentation

        private static SprocketDocs _docs;

        /// <summary>The documentation (generated the first time it's needed).</summary>
        public static SprocketDocs Docs
        {
            get
            {
                if (_docs != null) return _docs;
                _docs = Resources.Load<SprocketDocs>("SprocketDocs");
                if (_docs == null)
                    _docs = Generate();
                return _docs;
            }
        }

        [MenuItem("Tools/Remedy/Schematics/Assistant/Regenerate Documentation", priority = 30)]
        private static void RegenerateMenu()
        {
            var docs = Generate();
            Selection.activeObject = docs;
            Debug.Log($"[Assistant] Documentation regenerated: {docs.Entries.Count} entries. Write a Description on any entry to use your own words.");
        }

        /// <summary>
        /// Builds (or refreshes) the documentation asset from the code: summaries from the toolkit's source comments,
        /// node help, and Tooltips. Hand-written Descriptions are kept.
        /// </summary>
        public static SprocketDocs Generate()
        {
            string path = AssetPath();
            var docs = AssetDatabase.LoadAssetAtPath<SprocketDocs>(path);
            bool created = docs == null;
            if (created)
                docs = ScriptableObject.CreateInstance<SprocketDocs>();

            var keep = docs.Entries.Where(e => e != null && !string.IsNullOrEmpty(e.Key)).ToDictionary(e => e.Key, e => e.Description);
            var summaries = SourceSummaries();
            var entries = new Dictionary<string, SprocketDocs.Entry>();

            void Add(string key, string title, string summary)
            {
                entries[key] = new SprocketDocs.Entry { Key = key, Title = title, Summary = summary ?? "", Description = keep.TryGetValue(key, out var d) ? d : "" };
            }

            foreach (var type in NodeReflection.GetNodeTypes().Values)
            {
                if (type?.Type == null) continue;
                summaries.TryGetValue(type.Type.Name, out string summary);
                var text = new StringBuilder(summary ?? type.Help ?? "");
                var inputs = type.Ports.Where(p => p.Direction == PortDirection.Input && p.Type != typeof(ActionPort)).Select(p => p.Name).ToList();
                var outputs = type.Ports.Where(p => p.Direction == PortDirection.Output && p.Type != typeof(ActionPort)).Select(p => p.Name).ToList();
                if (inputs.Count > 0) text.Append($"\nIn: {string.Join(", ", inputs)}.");
                if (outputs.Count > 0) text.Append($"\nOut: {string.Join(", ", outputs)}.");
                Add("node:" + type.Type.FullName, string.IsNullOrEmpty(type.Name) ? Nicify(type.Type.Name) : type.Name, text.ToString().Trim());
            }

            foreach (var type in TypeCache.GetTypesDerivedFrom<SchematicBehavior>())
            {
                if (type.IsAbstract) continue;
                summaries.TryGetValue(type.Name, out string summary);
                Add("behavior:" + type.FullName, Nicify(type.Name), summary ?? $"The {Nicify(type.Name)} Behavior.");
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    string fieldType = field.FieldType.Name;
                    bool signal = fieldType.StartsWith("SignalHandle");
                    bool variable = fieldType.StartsWith("VariableHandle");
                    if (!signal && !variable) continue;
                    string tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
                    summaries.TryGetValue(type.Name + "." + field.Name, out string fieldSummary);
                    Add($"{(signal ? "signal" : "variable")}:{type.FullName}.{field.Name}", $"{Nicify(field.Name)} ({Nicify(type.Name)})",
                        tooltip ?? fieldSummary ?? (signal ? $"A Signal of {Nicify(type.Name)}." : $"A Variable of {Nicify(type.Name)}."));
                }
            }

            Add("window:SchematicGraphEditorWindow", "The Schematic Graph",
                "Where an object's logic lives. Events (On Start, On Signal Received…) start flows that run through action nodes along the arrows. The IO Dock on the left lists the prefab's objects with their Behaviors, Variables and Signals; drag any of them in to use them.");
            Add("window:SceneView", "The Scene View", "Your level. Drop me on an object here and I'll tell you what it is and which Behaviors it has. In Prefab Mode, it shows the prefab you're editing.");
            Add("window:GameView", "The Game View", "What the player sees through the game's camera. Press Play to run the game here.");
            Add("window:InspectorWindow", "The Inspector", "The settings of whatever is selected. Select something and drop me here, and I'll tell you about it.");
            Add("window:SceneHierarchyWindow", "The Hierarchy", "Every object in the open scenes, as a tree (children under their parents). Select one and drop me here to hear about it.");
            Add("window:ProjectBrowser", "The Project Window", "All of the project's files: prefabs, graphs, scripts, art. Select one and drop me here to hear about it.");
            Add("window:GameObject", "Prefab Object", "One of this prefab's objects. Behaviors attach to it; drag it into the graph to refer to it.");

            docs.Entries = entries.Values.OrderBy(e => e.Key, StringComparer.Ordinal).ToList();
            docs.Invalidate();
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                AssetDatabase.CreateAsset(docs, path);
            }
            EditorUtility.SetDirty(docs);
            AssetDatabase.SaveAssetIfDirty(docs);
            _docs = docs;
            return docs;
        }

        // Next to this script: .../Assistant/Resources/SprocketDocs.asset
        private static string AssetPath()
        {
            foreach (var guid in AssetDatabase.FindAssets("SprocketInspect t:MonoScript"))
            {
                string script = AssetDatabase.GUIDToAssetPath(guid);
                if (script.EndsWith("/SprocketInspect.cs"))
                    return Path.GetDirectoryName(script).Replace('\\', '/') + "/Resources/SprocketDocs.asset";
            }
            return "Assets/Sprocket/Resources/SprocketDocs.asset";
        }

        private static readonly Regex _summaryBefore = new(
            @"///\s*<summary>(?<text>(?:(?!</summary>).)*)</summary>(?<between>(?:\s*(?:///[^\n]*|\[[^\n]*\]))*)\s*(?:public|internal|protected|private)?\s*(?:static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+)*(?:(?<kind>class|struct)\s+(?<type>\w+)|[\w<>,\s\[\]]+?\s(?<field>\w+)\s*[=;])",
            RegexOptions.Singleline | RegexOptions.Compiled);

        /// <summary>Summary comments from the toolkit's own source: by class name, and "Class.Field" for fields.</summary>
        private static Dictionary<string, string> SourceSummaries()
        {
            var summaries = new Dictionary<string, string>(StringComparer.Ordinal);
            string root = ToolkitRoot();
            if (root == null) return summaries;
            foreach (var file in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                if (file.Contains("~") || file.Contains("/Dependencies/") || file.Contains("\\Dependencies\\")) continue;
                string source;
                try { source = File.ReadAllText(file); }
                catch { continue; }
                string currentClass = null;
                foreach (Match match in _summaryBefore.Matches(source))
                {
                    string text = Clean(match.Groups["text"].Value);
                    if (match.Groups["type"].Success)
                    {
                        currentClass = match.Groups["type"].Value;
                        summaries.TryAdd(currentClass, text);
                    }
                    else if (match.Groups["field"].Success && currentClass != null)
                        summaries.TryAdd(currentClass + "." + match.Groups["field"].Value, text);
                }
            }
            return summaries;
        }

        private static string ToolkitRoot()
        {
            foreach (var guid in AssetDatabase.FindAssets("SprocketInspect t:MonoScript"))
            {
                string script = Path.GetFullPath(AssetDatabase.GUIDToAssetPath(guid));
                for (var dir = new DirectoryInfo(Path.GetDirectoryName(script)); dir != null; dir = dir.Parent)
                    if (Directory.Exists(Path.Combine(dir.FullName, "Schematics")) && Directory.Exists(Path.Combine(dir.FullName, "Features")))
                        return dir.FullName;
            }
            return null;
        }

        // XML doc comments into plain text: <see cref="A.B"/> → B, <c>x</c> → x, paragraphs kept.
        private static string Clean(string xml)
        {
            string text = Regex.Replace(xml, @"^\s*///\s?", "", RegexOptions.Multiline);
            text = Regex.Replace(text, @"<see\s+cref=""(?:[\w.]*\.)?([\w`]+)[^""]*""\s*/>", m => Nicify(m.Groups[1].Value));
            text = Regex.Replace(text, @"<paramref\s+name=""(\w+)""\s*/>", "$1");
            text = Regex.Replace(text, @"</?para>", "\n");
            text = Regex.Replace(text, @"<code>.*?</code>", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"<[^>]+>", "");
            text = text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");
            text = Regex.Replace(text, @"[ \t]*\n[ \t]*", "\n");
            text = Regex.Replace(text, @"(?<!\n)\n(?!\n)", " ");
            return Regex.Replace(text, @"[ \t]{2,}", " ").Trim();
        }

        public static string Nicify(string name) => string.IsNullOrEmpty(name) ? "" : ObjectNames.NicifyVariableName(name);

        private static string FirstSentence(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            int end = text.IndexOfAny(new[] { '.', '\n' });
            return end > 0 && end < 160 ? text.Substring(0, end + 1).Trim() : text.Length > 160 ? text.Substring(0, 157) + "…" : text;
        }

        #endregion
    }
}
