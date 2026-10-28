using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics.Macros
{
    /// <summary>
    /// A macro saved from a graph: a group of nodes (and comments), wired, that's pasted in whole from the graph's
    /// right-click menu (Macros). Made with Save Selection as Macro; its Category and Description can be changed here.
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy/Schematics/Macro", fileName = "New Macro")]
    public sealed class SchematicMacro : ScriptableObject
    {
        [Tooltip("Where it's listed under Macros (e.g. Audio/Vehicles).")]
        public string Category = "My Macros";
        [TextArea(2, 6)] public string Description;
        [Tooltip("The kind of graph it's for (its type's full name; empty: any).")]
        public string GraphType;
        [HideInInspector] public string Data;

        /// <summary>Whether it can go into a graph of this kind.</summary>
        public bool Fits(Graph graph)
        {
            if (graph == null || string.IsNullOrEmpty(GraphType))
                return true;
            for (var type = graph.GetType(); type != null; type = type.BaseType)
                if (type.FullName == GraphType)
                    return true;
            return false;
        }
    }

    /// <summary>A macro that comes with a module, built in code (so it can't go out of date with the nodes it uses).</summary>
    public sealed class BuiltInMacro
    {
        /// <summary>Where it's listed under Macros, with its name last (e.g. "Vehicles/Car Engine (realistic)").</summary>
        public string Path;
        public string Description;
        /// <summary>The kind of graph it's for (and graphs derived from it).</summary>
        public Type GraphType;
        /// <summary>Makes its nodes and wires them.</summary>
        public Action<MacroBuilder> Build;
    }

    /// <summary>Makes a built-in macro's nodes, and the wires between them (or to nodes already in the graph).</summary>
    public sealed class MacroBuilder
    {
        /// <summary>The graph it's going into (to wire into nodes already there).</summary>
        public readonly Graph Graph;
        internal readonly List<Node> Nodes = new();
        internal readonly List<(Node, string, Node, string)> Links = new();

        internal MacroBuilder(Graph graph) => Graph = graph;

        /// <summary>A new node at a position (relative to the macro's other nodes), set up as it's made.</summary>
        public T Add<T>(float x, float y, Action<T> setup = null) where T : Node
        {
            var node = NodeReflection.Instantiate<T>();
            node.Position = new Vector2(x, y);
            setup?.Invoke(node);
            Nodes.Add(node);
            return node;
        }

        /// <summary>Runs once the nodes are in the graph (and wired): changes to nodes already there go here.</summary>
        public Action AfterInsert;

        /// <summary>A wire from one node's output to another's input.</summary>
        public void Link(Node from, string output, Node to, string input) => Links.Add((from, output, to, input));

        /// <summary>The first node of a kind already in the graph (null: none).</summary>
        public T Existing<T>() where T : Node => Graph?.Nodes.OfType<T>().FirstOrDefault();
    }

    /// <summary>
    /// Macros: ready-made groups of nodes, picked from a graph's right-click menu (Macros) and pasted where it was
    /// clicked. Modules register built-in ones (a car engine for Sound Cues...); anyone can save a selection as one.
    /// </summary>
    [InitializeOnLoad]
    public static class SchematicMacros
    {
        /// <summary>Where saved macros go.</summary>
        public const string Folder = "Assets/Content/Macros";

        private static readonly List<BuiltInMacro> _builtIn = new();

        static SchematicMacros()
        {
            CanvasView.ExtendContextMenu -= AddToMenu;
            CanvasView.ExtendContextMenu += AddToMenu;
        }

        /// <summary>Adds a built-in macro (call from an [InitializeOnLoad] class's static constructor).</summary>
        public static void Register(BuiltInMacro macro)
        {
            if (macro == null || macro.Build == null || string.IsNullOrEmpty(macro.Path))
                return;
            _builtIn.RemoveAll(m => m.Path == macro.Path && m.GraphType == macro.GraphType);
            _builtIn.Add(macro);
        }

        /// <summary>The built-in macros that fit a graph.</summary>
        public static IEnumerable<BuiltInMacro> BuiltInFor(Graph graph)
            => _builtIn.Where(m => graph == null || m.GraphType == null || m.GraphType.IsInstanceOfType(graph)).OrderBy(m => m.Path);

        /// <summary>The saved macros that fit a graph.</summary>
        public static IEnumerable<SchematicMacro> SavedFor(Graph graph)
            => AssetDatabase.FindAssets("t:" + nameof(SchematicMacro))
                .Select(guid => AssetDatabase.LoadAssetAtPath<SchematicMacro>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(m => m != null && m.Fits(graph))
                .OrderBy(m => m.Category).ThenBy(m => m.name);

        private static void AddToMenu(CanvasView canvas, ContextualMenuPopulateEvent evt)
        {
            var graph = canvas.Graph;
            bool any = false;
            foreach (var macro in BuiltInFor(graph))
            {
                var chosen = macro;
                evt.menu.AppendAction("Macros/" + chosen.Path, _ => Insert(canvas, chosen));
                any = true;
            }
            foreach (var macro in SavedFor(graph))
            {
                var chosen = macro;
                string category = string.IsNullOrWhiteSpace(chosen.Category) ? "" : chosen.Category.Trim('/') + "/";
                evt.menu.AppendAction("Macros/" + category + chosen.name, _ => canvas.PasteSerialized(chosen.Data, "Insert Macro " + chosen.name));
                any = true;
            }
            if (any)
                evt.menu.AppendSeparator("Macros/");
            bool selected = canvas.SelectedElements.Any();
            evt.menu.AppendAction("Macros/Save Selection as Macro...", _ => SaveSelection(canvas),
                selected ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        /// <summary>Puts a built-in macro's nodes into the canvas's graph where the mouse last was.</summary>
        public static void Insert(CanvasView canvas, BuiltInMacro macro)
        {
            var builder = new MacroBuilder(canvas.Graph);
            try
            {
                macro.Build(builder);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return;
            }
            canvas.InsertNodes(builder.Nodes, builder.Links, "Insert Macro " + macro.Path);
            if (builder.AfterInsert != null)
            {
                Undo.RecordObject(canvas.Graph, "Insert Macro " + macro.Path);
                builder.AfterInsert();
                EditorUtility.SetDirty(canvas.Graph);
            }
        }

        /// <summary>
        /// Fills a graph that isn't open in a window (a new asset, say: a macro's function graphs) with a built-in
        /// macro's nodes and wires, straight into the asset (no Undo).
        /// </summary>
        public static void BuildInto(Graph graph, Action<MacroBuilder> build)
        {
            if (graph == null || build == null)
                return;
            var builder = new MacroBuilder(graph);
            build(builder);
            foreach (var node in builder.Nodes)
                graph.AddNode(node);
            foreach (var (from, output, to, input) in builder.Links)
            {
                var outPort = from?.GetPort(output);
                var inPort = to?.GetPort(input);
                if (outPort != null && inPort != null)
                    graph.AddEdge(outPort, inPort);
                else
                    Debug.LogWarning($"Macro wire {from?.Name}.{output} → {to?.Name}.{input}: no such pin.", graph);
            }
            builder.AfterInsert?.Invoke();
            EditorUtility.SetDirty(graph);
        }

        private static void SaveSelection(CanvasView canvas)
        {
            var elements = canvas.SelectedElements.ToList();
            if (elements.Count == 0)
                return;
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                Directory.CreateDirectory(Folder);
                AssetDatabase.Refresh();
            }
            string path = EditorUtility.SaveFilePanelInProject("Save Macro", "New Macro", "asset",
                "A name for the macro: it's listed under Macros in the right-click menu of graphs like this one.", Folder);
            if (string.IsNullOrEmpty(path))
                return;
            var macro = ScriptableObject.CreateInstance<SchematicMacro>();
            macro.Data = CanvasView.SerializeElements(elements);
            macro.GraphType = canvas.Graph != null ? canvas.Graph.GetType().FullName : null;
            AssetDatabase.CreateAsset(macro, path);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(macro);
        }
    }
}
