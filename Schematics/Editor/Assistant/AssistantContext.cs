using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics.Assistant
{
    /// <summary>
    /// What the assistant knows: a snapshot of the open Schematic Graph (nodes with short ids, their ports and links),
    /// the prefab's objects with their Behaviors' Variables and Signals, the node types it can add, and the open
    /// scene. Nothing else, and no history: every request starts fresh from what's on screen.
    /// </summary>
    internal sealed class AssistantContext
    {
        public readonly SchematicGraphEditorWindow Window;

        /// <summary>Short ids ("n1", "n2", ...) the model uses for the graph's nodes.</summary>
        public readonly Dictionary<string, Node> Nodes = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Node types by every name the model might use for them.</summary>
        public readonly Dictionary<string, NodeReflectionData> NodeTypes = new(StringComparer.OrdinalIgnoreCase);

        public string Json { get; private set; }

        // Nodes made from IO Dock items: they need a Behavior or Signal, so they have their own commands.
        private static readonly HashSet<string> DockNodeTypes = new()
        {
            nameof(SendSignalNode), nameof(OnSignalReceivedNode), nameof(GetBehaviorVariableNode),
            nameof(SetBehaviorVariableNode), nameof(BehaviorReferenceNode), nameof(PrefabObjectNode),
        };

        private const int MaxSceneObjects = 40;

        public AssistantContext(SchematicGraphEditorWindow window)
        {
            Window = window;
            Build();
        }

        private void Build()
        {
            var json = new AssistantJson();
            json.BeginObject();
            var graph = Window != null ? Window.SchematicGraph : null;
            if (graph != null)
                WriteGraph(json, graph);
            if (Window != null && Window.Prefab != null)
                WriteObjects(json, Window.Prefab);
            WriteNodeTypes(json);
            WriteScene(json);
            json.EndObject();
            Json = json.ToString();
        }

        private void WriteGraph(AssistantJson json, SchematicGraph graph)
        {
            var ids = new Dictionary<Node, string>();
            int next = 1;
            foreach (var node in graph.Nodes)
            {
                if (node == null) continue;
                string id = "n" + next++;
                ids[node] = id;
                Nodes[id] = node;
            }

            json.BeginObject("graph").String("name", graph.name).String("prefab", Window.Prefab != null ? Window.Prefab.name : "");
            json.BeginArray("nodes");
            foreach (var pair in ids)
            {
                var node = pair.Key;
                var type = NodeReflection.GetNodeType(node.GetType());
                json.BeginObject()
                    .String("id", pair.Value)
                    .String("type", type?.Name ?? node.GetType().Name)
                    .String("name", node.Name)
                    .Number("x", node.Position.x).Number("y", node.Position.y);

                json.BeginArray("inputs");
                foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Input && p.Type != typeof(ActionPort)))
                    json.Value($"{port.Name}:{TypeName(port.Type)}");
                json.EndArray();
                json.BeginArray("outputs");
                foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Output && p.Type != typeof(ActionPort)))
                    json.Value($"{port.Name}:{TypeName(port.Type)}");
                json.EndArray();

                json.BeginArray("links");
                foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Output))
                    foreach (var connected in port.ConnectedPorts)
                        if (connected?.Node != null && ids.TryGetValue(connected.Node, out var to))
                            json.BeginObject()
                                .String("from_port", SchematicGraphEditorWindow.PortLabel(port))
                                .String("to", to)
                                .String("to_port", SchematicGraphEditorWindow.PortLabel(connected))
                                .EndObject();
                json.EndArray();
                json.EndObject();
            }
            json.EndArray().EndObject();
        }

        private static void WriteObjects(AssistantJson json, GameObject prefab)
        {
            var root = prefab.GetComponent<SchematicObject>();
            json.BeginArray("objects");
            foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
            {
                string path = transform == prefab.transform ? "" : AnimationUtility.CalculateTransformPath(transform, prefab.transform);
                json.BeginObject().String("path", path).String("name", transform.name);
                json.BeginArray("behaviors");
                if (root != null && root.Behaviors.TryGetValue(transform.gameObject, out var list) && list != null)
                {
                    foreach (var attachment in list.Items)
                    {
                        var metadata = attachment?.Metadata;
                        var behaviorType = metadata != null ? metadata.BehaviorType.Type : null;
                        if (behaviorType == null)
                            continue;
                        json.BeginObject().String("type", behaviorType.Name);
                        json.BeginArray("variables");
                        foreach (var variable in metadata.Variables)
                        {
                            var type = variable.Type.Type;
                            object value = null;
                            try
                            {
                                var entry = attachment.FindOverride(variable.Name);
                                value = entry != null ? entry.Value.Get(type) : variable.Default?.Get(type);
                            }
                            catch { }
                            json.BeginObject().String("name", variable.Name).String("type", TypeName(type)).String("value", ValueText(value)).EndObject();
                        }
                        json.EndArray();
                        json.BeginArray("signals");
                        foreach (var signal in metadata.Signals)
                            json.BeginObject()
                                .String("name", signal.Name)
                                .String("params", string.Join(", ", signal.Parameters.Select(p => $"{TypeName(p.Type.Type)} {p.Name}")))
                                .String("dir", signal.Direction == SignalDirection.Input ? "input (graph sends it)"
                                             : signal.Direction == SignalDirection.Output ? "output (graph receives it)" : "")
                                .EndObject();
                        json.EndArray();
                        json.EndObject();
                    }
                }
                json.EndArray().EndObject();
            }
            json.EndArray();
        }

        private void WriteNodeTypes(AssistantJson json)
        {
            json.BeginArray("node_types");
            foreach (var type in NodeReflection.GetNodeTypes().Values)
            {
                if (type?.Type == null || DockNodeTypes.Contains(type.Type.Name) || type.Type.IsAbstract)
                    continue;
                string name = string.IsNullOrEmpty(type.Name) ? type.Type.Name : type.Name;
                NodeTypes[name] = type;
                NodeTypes.TryAdd(type.Type.Name, type);
                NodeTypes.TryAdd(SchematicGraphEditorWindow.Simplify(name), type);
                if (type.Type.Name.EndsWith("Node"))
                    NodeTypes.TryAdd(type.Type.Name.Substring(0, type.Type.Name.Length - 4), type);
                json.Value(name);
            }
            json.EndArray();
        }

        private static void WriteScene(AssistantJson json)
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
                return;
            json.BeginObject("scene").String("name", scene.name);
            json.BeginArray("selected");
            foreach (var selected in Selection.gameObjects.Take(8))
                json.Value(selected.name);
            json.EndArray();
            json.BeginArray("objects");
            int count = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (count++ >= MaxSceneObjects)
                    break;
                var p = root.transform.position;
                var components = root.GetComponents<Component>()
                    .Where(c => c != null && !(c is Transform))
                    .Select(c => c.GetType().Name).Take(4);
                json.BeginObject()
                    .String("name", root.name)
                    .String("pos", string.Format(CultureInfo.InvariantCulture, "{0:0.#},{1:0.#},{2:0.#}", p.x, p.y, p.z))
                    .String("components", string.Join(" ", components))
                    .EndObject();
            }
            json.EndArray().EndObject();
        }

        internal static string TypeName(Type type)
        {
            if (type == null) return "?";
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return type.Name;
        }

        private static string ValueText(object value)
        {
            switch (value)
            {
                case null: return "";
                case UnityEngine.Object obj: return obj != null ? obj.name : "none";
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case Vector3 v: return string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##},{2:0.##})", v.x, v.y, v.z);
                case Vector2 v: return string.Format(CultureInfo.InvariantCulture, "({0:0.##},{1:0.##})", v.x, v.y);
                case IFormattable formattable: return formattable.ToString(null, CultureInfo.InvariantCulture);
                default:
                    string text = value.ToString();
                    return text.Length > 40 ? text.Substring(0, 40) : text;
            }
        }
    }
}
