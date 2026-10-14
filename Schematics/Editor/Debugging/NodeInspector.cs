using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What the Inspector shows while a Schematic Graph node is selected: the node's settings (breakpoint, network mode),
/// its graph's network flag, and the node's own fields. Other tools can add sections with <see cref="ExtraGUI"/>.
/// </summary>
public class NodeInspectorTarget : ScriptableObject
{
    public SchematicGraph Graph;
    public string NodeId;

    public SchematicGraphNode Node => Graph != null ? Graph.GetNodeById(NodeId) as SchematicGraphNode : null;

    private static NodeInspectorTarget _shared;

    /// <summary>Shows the node in the Inspector.</summary>
    public static void Show(SchematicGraph graph, Node node)
    {
        if (graph == null || node == null)
            return;
        if (_shared == null)
        {
            _shared = CreateInstance<NodeInspectorTarget>();
            _shared.hideFlags = HideFlags.DontSave;
        }
        if (Selection.activeObject == _shared && _shared.Graph == graph && _shared.NodeId == node.ID)
            return;
        _shared.Graph = graph;
        _shared.NodeId = node.ID;
        _shared.name = node.Name;
        Selection.activeObject = _shared;
    }

    /// <summary>
    /// Extra Inspector sections for the selected node, drawn after the built-in ones. For future node features (and
    /// your own tools): <c>NodeInspectorTarget.ExtraGUI += (graph, node) =&gt; { ... };</c>
    /// </summary>
    public static event Action<SchematicGraph, SchematicGraphNode> ExtraGUI;

    internal static void DrawExtras(SchematicGraph graph, SchematicGraphNode node) => ExtraGUI?.Invoke(graph, node);
}

[CustomEditor(typeof(NodeInspectorTarget))]
public class NodeInspectorEditor : Editor
{
    private SerializedObject _graphObject;
    private SchematicGraph _graphFor;

    // Fields declared by BlueGraph's Node and the toolkit's base classes are internal state, not settings.
    private static readonly HashSet<Type> _baseTypes = new()
    {
        typeof(Node), typeof(SchematicGraphNode), typeof(SchematicActionNodeBase), typeof(SchematicActionNode),
        typeof(SchematicEventNode), typeof(SchematicConditionalNode),
    };

    protected override void OnHeaderGUI()
    {
        var target = (NodeInspectorTarget)this.target;
        var node = target.Node;
        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8);
            EditorGUILayout.LabelField(node != null ? node.Name : "Missing node", EditorStyles.largeLabel, GUILayout.Height(22));
        }
        if (node != null)
            EditorGUILayout.LabelField($"   {ObjectNames.NicifyVariableName(node.GetType().Name)} · {target.Graph.name}", EditorStyles.miniLabel);
        GUILayout.Space(4);
        EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
    }

    public override void OnInspectorGUI()
    {
        var target = (NodeInspectorTarget)this.target;
        var graph = target.Graph;
        var node = target.Node;
        if (graph == null || node == null)
        {
            EditorGUILayout.HelpBox("The node no longer exists.", MessageType.Info);
            return;
        }

        // Node settings.
        EditorGUILayout.LabelField("Node", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool breakpoint = EditorGUILayout.Toggle(new GUIContent("Breakpoint", "Stop flows before this node runs (Play Mode). F9 in the graph."), node.Breakpoint);
        NodeNetworkMode mode;
        using (new EditorGUI.DisabledScope(!graph.Networked))
            mode = (NodeNetworkMode)EditorGUILayout.EnumPopup(new GUIContent("Network",
                "Local: runs where its flow runs (the object's authority). Everyone: when the authority reaches it, every peer runs it too, with the same inputs, and carries on from it."),
                node.NetworkMode);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graph, "Edit Node");
            node.Breakpoint = breakpoint;
            node.NetworkMode = mode;
            EditorUtility.SetDirty(graph);
            RefreshGraphWindows(graph);
        }
        if (!graph.Networked && node.NetworkMode != NodeNetworkMode.Local)
            EditorGUILayout.HelpBox("The graph isn't networked, so this node runs locally.", MessageType.None);

        // Graph settings.
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Graph", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool networked = EditorGUILayout.Toggle(new GUIContent("Networked",
            "Sync this graph over the network: its events run on the object's authority, and nodes set to Everyone run on every peer. The object needs a NetworkObject and a Schematic Network Graph."),
            graph.Networked);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graph, "Network Graph");
            graph.Networked = networked;
            EditorUtility.SetDirty(graph);
        }

        // The node's own fields.
        DrawNodeFields(graph, node);

        NodeInspectorTarget.DrawExtras(graph, node);
    }

    private void DrawNodeFields(SchematicGraph graph, SchematicGraphNode node)
    {
        if (_graphObject == null || _graphFor != graph)
        {
            _graphObject = new SerializedObject(graph);
            _graphFor = graph;
        }
        _graphObject.Update();

        // The serialized list is in the same order as Graph.Nodes.
        var nodes = _graphObject.FindProperty("nodes");
        int index = -1;
        for (int i = 0; i < graph.Nodes.Count; i++)
            if (graph.Nodes[i] == node)
                index = i;
        if (nodes == null || index < 0 || index >= nodes.arraySize)
            return;
        var nodeProperty = nodes.GetArrayElementAtIndex(index);

        bool header = false;
        var end = nodeProperty.GetEndProperty();
        var child = nodeProperty.Copy();
        bool enterChildren = true;
        while (child.NextVisible(enterChildren) && !SerializedProperty.EqualContents(child, end))
        {
            enterChildren = false;
            if (!IsSetting(node.GetType(), child.name))
                continue;
            if (!header)
            {
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Values", EditorStyles.boldLabel);
                header = true;
            }
            EditorGUILayout.PropertyField(child, true);
        }

        if (_graphObject.ApplyModifiedProperties())
        {
            node.Validate();
            RefreshGraphWindows(graph);
        }
    }

    // A field declared by the node's own class (or a toolkit subclass), not by the base classes.
    private static bool IsSetting(Type nodeType, string fieldName)
    {
        for (var type = nodeType; type != null && !_baseTypes.Contains(type); type = type.BaseType)
        {
            if (type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) != null)
                return true;
        }
        return false;
    }

    private static void RefreshGraphWindows(SchematicGraph graph)
    {
        foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            if (window.SchematicGraph == graph)
                window.RefreshDebugState(frame: false);
    }
}
