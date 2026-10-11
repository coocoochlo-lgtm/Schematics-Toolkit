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
        /// What's at a screen point in a window. Scene view picks arrive a moment later, so this calls back (with null
        /// when there's nothing particular there).
        /// </summary>
        public static void At(EditorWindow window, Vector2 screen, Action<InspectTarget> done)
        {
            switch (window)
            {
                case SceneView sceneView:
                    PickInScene(sceneView, screen, picked => done(picked != null ? Placed(ForGameObject(picked), sceneView, screen) : null));
                    return;
                case SchematicGraphEditorWindow graph:
                    done(InGraph(graph, screen));
                    return;
            }
            switch (window.GetType().Name)
            {
                case "SceneHierarchyWindow":
                {
                    var picked = Probe(window, screen) as GameObject;
                    done(picked != null ? Placed(ForGameObject(picked), window, screen) : null);
                    return;
                }
                case "ProjectBrowser":
                {
                    var asset = Probe(window, screen);
                    done(asset != null ? Placed(ForAsset(asset), window, screen) : null);
                    return;
                }
                case "InspectorWindow":
                    done(Selection.activeObject != null ? Placed(Selection.activeObject is GameObject go ? ForGameObject(go) : ForAsset(Selection.activeObject), window, screen) : null);
                    return;
            }
            done(null);
        }

        /// <summary>Just the name of what's under the pointer, for the "What is this?" tag while dragging.</summary>
        public static void Peek(EditorWindow window, Vector2 screen, Action<string> done)
        {
            switch (window)
            {
                case SceneView sceneView:
                    PickInScene(sceneView, screen, picked => done(picked != null ? picked.name : null));
                    return;
                case SchematicGraphEditorWindow graph:
                    done(InGraph(graph, screen)?.Title);
                    return;
            }
            string type = window.GetType().Name;
            if (type == "SceneHierarchyWindow" || type == "ProjectBrowser")
                done(Probe(window, screen)?.name);
            else
                done(null);
        }

        private static InspectTarget Placed(InspectTarget target, EditorWindow window, Vector2 screen)
        {
            target.Window = window;
            target.ScreenRect = new Rect(screen - new Vector2(30, 12), new Vector2(60, 24));
            return target;
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
                    return Target(window, element, ForDockItem(item));
                if (element is NodeView view && view.Target != null)
                    return Target(window, view, ForNode(view.Target));
            }
            return null;
        }

        // Scene view picking needs its GUI, so it happens on its next repaint.
        private static void PickInScene(SceneView view, Vector2 screen, Action<GameObject> done)
        {
            void Pick(SceneView sceneView)
            {
                if (sceneView != view) return;
                SceneView.duringSceneGui -= Pick;
                GameObject picked = null;
                try { picked = HandleUtility.PickGameObject(GUIUtility.ScreenToGUIPoint(screen), false); }
                catch (Exception exception) { Debug.LogWarning("[Assistant] Couldn't pick in the Scene view: " + exception.Message); }
                done(picked);
            }
            SceneView.duringSceneGui += Pick;
            view.Repaint();
        }

        #endregion

        #region Hierarchy and Project: what's under a point

        // The Hierarchy and Project window only say what's under the pointer to a drag. So Sprocket asks with a
        // pretend drag: a single "drag hover" event at the point, answered through Unity's public drop handlers, then
        // an exit so the windows forget it. Nothing is dropped and nothing changes.
        private const string ProbeKey = "Sprocket.Probe";
        private static bool _probing;
        private static UnityEngine.Object _probed;

        [InitializeOnLoadMethod]
        private static void RegisterProbes()
        {
            DragAndDrop.AddDropHandlerV2(OnHierarchyProbe);
            DragAndDrop.AddDropHandlerV2(OnProjectProbe);
        }

        private static DragAndDropVisualMode OnHierarchyProbe(EntityId target, HierarchyDropFlags flags, Transform parent, bool perform)
        {
            if (!_probing || DragAndDrop.GetGenericData(ProbeKey) == null)
                return DragAndDropVisualMode.None;
            _probed = EditorUtility.EntityIdToObject(target);
            return DragAndDropVisualMode.Link;
        }

        private static DragAndDropVisualMode OnProjectProbe(EntityId dragged, string path, bool perform)
        {
            if (!_probing || DragAndDrop.GetGenericData(ProbeKey) == null)
                return DragAndDropVisualMode.None;
            if (!string.IsNullOrEmpty(path))
                _probed = AssetDatabase.LoadMainAssetAtPath(path);
            return DragAndDropVisualMode.Link;
        }

        /// <summary>The object or asset under a screen point in the Hierarchy or Project window (null if none).</summary>
        public static UnityEngine.Object Probe(EditorWindow window, Vector2 screen)
        {
            if (_probing || window == null)
                return null;
            _probing = true;
            _probed = null;
            try
            {
                DragAndDrop.PrepareStartDrag();
                DragAndDrop.SetGenericData(ProbeKey, true);
                var local = screen - window.position.position;
                window.SendEvent(new Event { type = EventType.DragUpdated, mousePosition = local });
                window.SendEvent(new Event { type = EventType.DragExited, mousePosition = local });
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[Assistant] Couldn't see what's there: " + exception.Message);
            }
            finally
            {
                DragAndDrop.SetGenericData(ProbeKey, null);
                _probing = false;
            }
            // A scene row is a Scene, not an object: nothing to say about it here.
            return _probed is GameObject || (_probed != null && EditorUtility.IsPersistent(_probed)) ? _probed : null;
        }

        #endregion

        #region Describing

        private sealed class Description
        {
            public string Title, Text;
        }

        private static InspectTarget Target(EditorWindow window, VisualElement element, Description description)
        {
            var bounds = element != null ? element.worldBound : window.rootVisualElement.worldBound;
            var local = window.rootVisualElement.WorldToLocal(bounds.position);
            return new InspectTarget
            {
                Title = description.Title,
                Text = description.Text,
                Window = window,
                ScreenRect = new Rect(window.position.position + local, bounds.size),
            };
        }

        // Hand-written words in the Sprocket Docs asset win; then the code's own [SchematicDoc].
        private static string Override(string key) => Docs.Find(key) is { } entry && !string.IsNullOrWhiteSpace(entry.Description) ? entry.Description : null;

        private static Description ForNode(Node node)
        {
            switch (node)
            {
                case SendSignalNode send when send.Signal != null:
                    return ForSignalNode(send.Signal, "Signal Node (Send)", "Sends");
                case OnSignalReceivedNode receive when receive.Signal != null:
                    return ForSignalNode(receive.Signal, "Signal Node (On Received)", "Runs when this arrives");
                case GetBehaviorVariableNode get when get.Target?.Type != null:
                    return ForVariableNode(get.Target.Type, get.Variable, "Variable Node (Get)");
                case SetBehaviorVariableNode set when set.Target?.Type != null:
                    return ForVariableNode(set.Target.Type, set.Variable, "Variable Node (Set)");
                case BehaviorReferenceNode behavior when behavior.Target?.Type != null:
                    return ForBehavior(behavior.Target.Type);
            }
            var type = node.GetType();
            var header = SchematicDocHeader.Of(type);
            return new Description
            {
                Title = header?.name ?? node.Name,
                Text = Override("node:" + type.FullName) ?? header?.Text ?? Docs.Find("node:" + type.FullName)?.Text ?? $"A {Nicify(type.Name)} node.",
            };
        }

        // "Signal Node (Send): " and then what the Signal itself is.
        private static Description ForSignalNode(SignalData signal, string label, string verb)
        {
            var (behavior, name) = SignalOwner(signal);
            var field = behavior?.GetField(name ?? "", BindingFlags.Instance | BindingFlags.Public);
            var header = SchematicDocHeader.Of(field);
            string about = header?.description ?? (behavior != null ? $"a Signal of {Nicify(behavior.Name)}." : $"the \"{signal.name}\" Signal.");
            return new Description
            {
                Title = $"{Nicify(name ?? signal.name)}" + (behavior != null ? $" ({Nicify(behavior.Name)})" : ""),
                Text = $"<b>{label}.</b> {verb} {Nicify(name ?? signal.name)}: {about}",
            };
        }

        private static Description ForVariableNode(Type behavior, string variable, string label)
        {
            var header = SchematicDocHeader.Of(behavior.GetField(variable ?? "", BindingFlags.Instance | BindingFlags.Public));
            string about = header?.description ?? $"a Variable of {Nicify(behavior.Name)}.";
            return new Description
            {
                Title = $"{Nicify(variable)} ({Nicify(behavior.Name)})",
                Text = $"<b>{label}.</b> {Nicify(variable)}{(header?.type != null ? $" ({header.type})" : "")}: {about}",
            };
        }

        private static Description ForBehavior(Type type)
        {
            var header = SchematicDocHeader.Of(type);
            return new Description
            {
                Title = Nicify(type.Name),
                Text = Override("behavior:" + type.FullName) ?? header?.Text ?? Docs.Find("behavior:" + type.FullName)?.Text ?? $"The {Nicify(type.Name)} Behavior.",
            };
        }

        private static Description ForDockItem(DockItem item)
        {
            switch (item.Kind)
            {
                case DockItem.ItemKind.Behavior when item.Behavior != null:
                    return ForBehavior(item.Behavior);
                case DockItem.ItemKind.Variable when item.Behavior != null:
                case DockItem.ItemKind.Signal when item.Behavior != null:
                {
                    bool signal = item.Kind == DockItem.ItemKind.Signal;
                    var field = item.Behavior.GetField(item.Name ?? "", BindingFlags.Instance | BindingFlags.Public);
                    var header = SchematicDocHeader.Of(field);
                    string key = $"{(signal ? "signal" : "variable")}:{item.Behavior.FullName}.{item.Name}";
                    string text = Override(key) ?? header?.description ?? Docs.Find(key)?.Text
                        ?? (signal ? $"A Signal of {Nicify(item.Behavior.Name)}." : $"A Variable of {Nicify(item.Behavior.Name)}: a value it keeps for each object.");
                    string how = signal
                        ? (header?.direction == "input" ? "Drag it into the graph to send it." : "Drag it into the graph to react to it.")
                        : "Drag it into the graph to Get or Set it.";
                    return new Description { Title = $"{Nicify(item.Name)} ({Nicify(item.Behavior.Name)})", Text = $"<b>{(signal ? "Signal" : "Variable")}.</b> {text} {how}" };
                }
                case DockItem.ItemKind.GameObject:
                    return new Description
                    {
                        Title = item.GameObject != null ? item.GameObject.name : "Prefab Object",
                        Text = "One of this prefab's objects. Behaviors attach to it; drag it into the graph to refer to it.",
                    };
            }
            return new Description { Title = item.DisplayName, Text = "Part of this prefab." };
        }

        /// <summary>The Behavior that declares a Signal asset, and the Signal's name there.</summary>
        private static (Type behavior, string name) SignalOwner(SignalData signal)
        {
            if (signal != null && AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(signal)) is BehaviorMetadata metadata)
                foreach (var definition in metadata.Signals)
                    if (definition.Asset == signal)
                        return (metadata.BehaviorType.Type, definition.Name);
            return (null, signal != null ? signal.name : null);
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
                        text.Append($"\n• <b>{Nicify(type.Name)}</b>: {FirstSentence(ForBehavior(type).Text) ?? "a Behavior."}");
                    }
            }

            // Its Unity components, described.
            var components = gameObject.GetComponents<Component>().Where(c => c != null && !(c is Transform) && !(c is SchematicObject)).ToList();
            if (schematic == null)
                text.Append(components.Count == 0 ? "An empty object: just a position in the world, maybe a parent for others." : "An object with:");
            else if (components.Count > 0)
                text.Append("\nIts components:");
            foreach (var component in components.Take(6))
            {
                var type = component.GetType();
                var header = SchematicDocHeader.Of(type);
                string about = Override("component:" + type.FullName) ?? header?.description ?? UnityObjectDocs.For(type, UnityObjectDocs.Components);
                text.Append($"\n• <b>{Nicify(type.Name)}</b>{(about != null ? ": " + FirstSentence(about) : "")}");
            }
            if (components.Count > 6) text.Append($"\n…and {components.Count - 6} more.");
            if (PrefabUtility.IsPartOfPrefabInstance(gameObject))
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(gameObject);
                if (source != null) text.Append($"\nIt comes from the prefab \"{source.transform.root.name}\".");
            }
            return new InspectTarget { Title = gameObject.name, Text = text.ToString() };
        }

        private static InspectTarget ForAsset(UnityEngine.Object asset)
        {
            string text = null;
            if (asset is MonoScript script && script.GetClass() is Type scripted)
            {
                var header = SchematicDocHeader.Of(scripted);
                text = header != null ? $"A script: the {header.name} {header.kind}. {header.Text}" : null;
            }
            else if (asset is GameObject prefab && prefab.GetComponent<SchematicObject>() is SchematicObject schematic)
                text = "A Schematic prefab: an object with Schematic Behaviors and a Schematic Graph. " + ForGameObject(prefab).Text;
            else if (asset is GameObject model)
                text = UnityObjectDocs.Assets["GameObject"] + (model.GetComponentsInChildren<Component>().Length > 1 ? " " + ForGameObject(model).Text : "");
            text ??= Override("asset:" + asset.GetType().FullName) ?? UnityObjectDocs.For(asset.GetType(), UnityObjectDocs.Assets) ?? $"A {Nicify(asset.GetType().Name)} asset.";
            return new InspectTarget { Title = asset.name, Text = text };
        }

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
                var text = new StringBuilder(SchematicDocHeader.Of(type.Type)?.description ?? summary ?? type.Help ?? "");
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
                Add("behavior:" + type.FullName, Nicify(type.Name), SchematicDocHeader.Of(type)?.description ?? summary ?? $"The {Nicify(type.Name)} Behavior.");
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    string fieldType = field.FieldType.Name;
                    bool signal = fieldType.StartsWith("SignalHandle");
                    bool variable = fieldType.StartsWith("VariableHandle");
                    if (!signal && !variable) continue;
                    string tooltip = field.GetCustomAttribute<TooltipAttribute>()?.tooltip;
                    summaries.TryGetValue(type.Name + "." + field.Name, out string fieldSummary);
                    Add($"{(signal ? "signal" : "variable")}:{type.FullName}.{field.Name}", $"{Nicify(field.Name)} ({Nicify(type.Name)})",
                        SchematicDocHeader.Of(field)?.description ?? tooltip ?? fieldSummary ?? (signal ? $"A Signal of {Nicify(type.Name)}." : $"A Variable of {Nicify(type.Name)}."));
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
