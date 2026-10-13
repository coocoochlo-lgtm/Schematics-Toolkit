using Remedy.Schematics;
using Remedy.Schematics.Assistant;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// What the Inspector shows when a Variable or Signal row is clicked (in the IO Dock, the Graph tab or the Inspector's
/// own rows): its description, type and starting value, its Network Sync flag, and for graph items, editing.
/// </summary>
public class ScopeItemInspector : ScriptableObject
{
    public DockItem.ItemKind Kind;
    public SchematicGraph Graph;
    public string VariableId;
    public SignalData Signal;
    public string BehaviorType;
    public string Path;
    public string Name;

    private static ScopeItemInspector _shared;

    public Type Behavior => string.IsNullOrEmpty(BehaviorType) ? null : Type.GetType(BehaviorType);

    /// <summary>Shows a dock item in the Inspector (Variables and Signals; other kinds are ignored).</summary>
    public static void Show(DockItem item)
    {
        if (item == null || (item.Kind != DockItem.ItemKind.Variable && item.Kind != DockItem.ItemKind.GraphVariable && item.Kind != DockItem.ItemKind.Signal))
            return;
        if (_shared == null)
        {
            _shared = CreateInstance<ScopeItemInspector>();
            _shared.hideFlags = HideFlags.DontSave;
        }
        _shared.Kind = item.Kind;
        _shared.Graph = item.Graph;
        _shared.VariableId = item.VariableId;
        _shared.Signal = item.Signal;
        _shared.BehaviorType = item.Behavior?.AssemblyQualifiedName;
        _shared.Path = item.Path;
        _shared.Name = item.Name;
        _shared.name = item.Name;
        if (Selection.activeObject == _shared)
            Selection.activeObject = null; // re-select so the Inspector rebuilds for the new item
        Selection.activeObject = _shared;
    }

    /// <summary>The graph Signal this shows, if it's one of a graph's (rather than a Behavior's).</summary>
    public GraphSignal GraphSignal
    {
        get
        {
            if (Signal == null) return null;
            var owner = Graph != null ? Graph : AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(Signal)) as SchematicGraph;
            return owner != null ? owner.FindSignal(Signal) : null;
        }
    }

    public SchematicGraph SignalGraph => Graph != null ? Graph : Signal != null ? AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(Signal)) as SchematicGraph : null;
}

[CustomEditor(typeof(ScopeItemInspector))]
public class ScopeItemInspectorEditor : Editor
{
    private static GUIStyle _description;

    protected override void OnHeaderGUI()
    {
        var target = (ScopeItemInspector)this.target;
        GUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Space(8);
            var icon = target.Kind == DockItem.ItemKind.Signal ? DockRows.InputIcon
                     : target.Kind == DockItem.ItemKind.GraphVariable ? DockRows.GraphVariableIcon : DockRows.VariableIcon;
            GUILayout.Label(icon, GUILayout.Width(20), GUILayout.Height(20));
            EditorGUILayout.LabelField(ObjectNames.NicifyVariableName(target.Name ?? "?"), EditorStyles.largeLabel, GUILayout.Height(22));
        }
        string scope = target.Kind switch
        {
            DockItem.ItemKind.GraphVariable => $"Graph Variable · {(target.Graph != null ? target.Graph.name : "?")}",
            DockItem.ItemKind.Variable => $"Variable · {target.Behavior?.Name}{(string.IsNullOrEmpty(target.Path) ? "" : " on " + target.Path)}",
            _ => target.GraphSignal != null ? $"Graph Signal · {target.SignalGraph?.name}" : $"Signal · {target.Behavior?.Name ?? "asset"}",
        };
        EditorGUILayout.LabelField("   " + scope, EditorStyles.miniLabel);
        GUILayout.Space(4);
        EditorGUILayout.LabelField(GUIContent.none, GUI.skin.horizontalSlider);
    }

    public override void OnInspectorGUI()
    {
        _description ??= new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
        var target = (ScopeItemInspector)this.target;
        switch (target.Kind)
        {
            case DockItem.ItemKind.GraphVariable: DrawGraphVariable(target); break;
            case DockItem.ItemKind.Variable: DrawBehaviorVariable(target); break;
            case DockItem.ItemKind.Signal: DrawSignal(target); break;
        }
    }

    #region Graph Variables

    private void DrawGraphVariable(ScopeItemInspector target)
    {
        var graph = target.Graph;
        var variable = graph != null ? graph.FindVariable(target.VariableId) : null;
        if (variable == null)
        {
            EditorGUILayout.HelpBox("This Variable was deleted.", MessageType.Info);
            return;
        }

        EditorGUI.BeginChangeCheck();
        string name = EditorGUILayout.DelayedTextField("Name", variable.Name);
        if (EditorGUI.EndChangeCheck())
        {
            GraphScopeEditing.RenameVariable(graph, variable, name);
            target.Name = variable.Name;
        }

        var types = GraphScopeEditing.VariableTypes;
        int index = Array.FindIndex(types, t => t.type == variable.ValueType);
        var labels = types.Select(t => t.path).ToList();
        if (index < 0) { labels.Add(DockRows.TypeName(variable.ValueType)); index = labels.Count - 1; }
        EditorGUI.BeginChangeCheck();
        int picked = EditorGUILayout.Popup("Type", index, labels.ToArray());
        if (EditorGUI.EndChangeCheck() && picked < types.Length)
            GraphScopeEditing.SetVariableType(graph, variable, types[picked].type);

        var type = variable.ValueType;
        if (type != null && SchematicValueField.Draw(new GUIContent("Starting Value", "Every object running the graph starts with this (unless it overrides it)."), type, variable.Default?.Get(type), out var value))
            GraphScopeEditing.SetVariableDefault(graph, variable, value);

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Description");
        string description = EditorGUILayout.TextArea(variable.Description ?? "", GUILayout.MinHeight(40));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graph, "Describe Graph Variable");
            variable.Description = description;
            GraphScopeEditing.Commit(graph, structural: false);
        }

        EditorGUILayout.Space(6);
        DrawNetworkToggle(variable.Networked, graph.Networked, on =>
        {
            Undo.RecordObject(graph, "Network Sync");
            variable.Networked = on;
            GraphScopeEditing.Commit(graph, structural: false);
        });

        EditorGUILayout.Space(6);
        int uses = graph.Nodes.OfType<GraphVariableNodeBase>().Count(n => n.VariableId == variable.Id && n.DeclaringGraph == graph);
        EditorGUILayout.LabelField($"Used by {uses} node(s) in {graph.name}.", EditorStyles.miniLabel);
        DrawLiveValues(graph, variable);
    }

    // Play Mode: every spawned object's current value.
    private static void DrawLiveValues(SchematicGraph graph, GraphVariable variable)
    {
        if (!Application.isPlaying)
            return;
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Live Values", EditorStyles.boldLabel);
        bool any = false;
        foreach (var owner in UnityEngine.Object.FindObjectsByType<SchematicObject>(FindObjectsSortMode.None))
            foreach (var store in owner.GraphStores)
            {
                if (store.Graph != graph) continue;
                int index = store.IndexOf(variable.Id);
                if (index < 0) continue;
                any = true;
                if (SchematicValueField.Draw(new GUIContent(owner.name), variable.ValueType, store.Get(index), out var value))
                    store.Set(index, value);
            }
        if (!any)
            EditorGUILayout.LabelField("No spawned object has used it yet.", EditorStyles.miniLabel);
    }

    #endregion

    #region Behavior Variables

    private void DrawBehaviorVariable(ScopeItemInspector target)
    {
        var behavior = target.Behavior;
        var metadata = behavior != null ? BehaviorMetadata.Get(behavior) : null;
        var definition = metadata != null ? metadata.FindVariable(target.Name) : null;
        if (definition == null)
        {
            EditorGUILayout.HelpBox("This Variable's Behavior Metadata wasn't found. Try Tools/Remedy/Schematics/Refresh Behavior Metadata.", MessageType.Info);
            return;
        }

        var field = FindField(behavior, target.Name);
        var doc = field != null ? SchematicDocHeader.Of(field) : null;
        string about = doc?.description ?? field?.GetCustomAttribute<TooltipAttribute>()?.tooltip;
        if (!string.IsNullOrEmpty(about))
            EditorGUILayout.LabelField(about, _description);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.TextField("Type", DockRows.TypeName(definition.Type.Type));
            var type = definition.Type.Type;
            if (type != null && !SchematicValue.IsBoxable(type))
                SchematicValueField.Draw(new GUIContent("Default", "Set per object in the IO Dock or the object's Inspector."), type, definition.Default?.Get(type), out _);
        }

        EditorGUILayout.Space(6);
        DrawNetworkToggle(definition.Networked, null, on =>
        {
            Undo.RecordObject(metadata, "Network Sync");
            definition.Networked = on;
            definition.NetworkedSetByUser = true; // from now on the [NetworkSync] default no longer applies
            EditorUtility.SetDirty(metadata);
            AssetDatabase.SaveAssetIfDirty(metadata);
        });
        EditorGUILayout.LabelField($"Applies to {behavior.Name} on every object that has it.", EditorStyles.miniLabel);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name ?? "", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    #endregion

    #region Signals

    private void DrawSignal(ScopeItemInspector target)
    {
        var graphSignal = target.GraphSignal;
        var graph = target.SignalGraph;
        if (graphSignal != null && graph != null)
        {
            DrawGraphSignal(target, graph, graphSignal);
            return;
        }

        // A Behavior's Signal (or a plain Signal asset): what it is, read-only.
        var behavior = target.Behavior;
        var field = behavior != null ? FindField(behavior, target.Name) : null;
        var doc = field != null ? SchematicDocHeader.Of(field) : null;
        if (!string.IsNullOrEmpty(doc?.description))
            EditorGUILayout.LabelField(doc.description, _description);
        var direction = SchematicGraphEditorWindow.DirectionOf(target.Signal);
        EditorGUILayout.LabelField("Direction", direction == SignalDirection.Input ? "Input: the Behavior listens; graphs send it"
                                              : direction == SignalDirection.Output ? "Output: the Behavior sends it; graphs react" : "Either way");
        EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
        var parameters = SchematicGraphEditorWindow.SignalParameters(target.Signal);
        if (parameters.Count == 0)
            EditorGUILayout.LabelField("None", EditorStyles.miniLabel);
        foreach (var (name, type) in parameters)
            EditorGUILayout.LabelField(name, DockRows.TypeName(type));
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Asset", target.Signal, typeof(SignalData), false);

        // Network Sync lives on the Signal asset (what travels at runtime). For a Behavior's Signal, the Metadata also
        // remembers it was set by hand, so the Behavior's [NetworkSync] default stops applying.
        if (target.Signal != null)
        {
            EditorGUILayout.Space(6);
            var metadata = behavior != null ? BehaviorMetadata.Get(behavior) : null;
            var definition = metadata != null ? metadata.FindSignal(target.Name) : null;
            DrawSignalNetwork(target.Signal, () =>
            {
                if (definition == null) return;
                Undo.RecordObject(metadata, "Network Sync");
                definition.NetworkedSetByUser = true;
                EditorUtility.SetDirty(metadata);
            }, () =>
            {
                if (metadata != null) AssetDatabase.SaveAssetIfDirty(metadata);
                else AssetDatabase.SaveAssetIfDirty(target.Signal);
            });
            if (behavior != null)
                EditorGUILayout.LabelField($"Applies to {behavior.Name} on every object that has it.", EditorStyles.miniLabel);
        }
    }

    private void DrawGraphSignal(ScopeItemInspector target, SchematicGraph graph, GraphSignal signal)
    {
        EditorGUI.BeginChangeCheck();
        string name = EditorGUILayout.DelayedTextField("Name", signal.Name);
        if (EditorGUI.EndChangeCheck())
        {
            GraphScopeEditing.RenameSignal(graph, signal, name);
            target.Name = signal.Name;
        }

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.LabelField("Description");
        string description = EditorGUILayout.TextArea(signal.Description ?? "", GUILayout.MinHeight(40));
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(graph, "Describe Graph Signal");
            signal.Description = description;
            GraphScopeEditing.Commit(graph, structural: false);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
        var edited = signal.Parameters.Select(p => new GraphSignalParameter { Name = p.Name, Type = p.Type.Type ?? typeof(float) }).ToList();
        bool changed = false;
        var types = GraphScopeEditing.VariableTypes;
        for (int i = 0; i < edited.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                string parameterName = EditorGUILayout.DelayedTextField(edited[i].Name);
                int index = Array.FindIndex(types, t => t.type == edited[i].Type.Type);
                int picked = EditorGUILayout.Popup(Mathf.Max(0, index), types.Select(t => t.path).ToArray(), GUILayout.Width(110));
                if (EditorGUI.EndChangeCheck())
                {
                    edited[i].Name = string.IsNullOrWhiteSpace(parameterName) ? edited[i].Name : parameterName.Trim();
                    edited[i].Type = types[picked].type;
                    changed = true;
                }
                if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(22)))
                {
                    edited.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        if (GUILayout.Button("+ Add Parameter", EditorStyles.miniButton))
        {
            string parameterName = "Value";
            for (int n = 2; edited.Any(p => p.Name == parameterName); n++) parameterName = "Value " + n;
            edited.Add(new GraphSignalParameter { Name = parameterName, Type = typeof(float) });
            changed = true;
        }
        if (changed)
            GraphScopeEditing.SetSignalParameters(graph, signal, edited);

        EditorGUILayout.Space(6);
        if (signal.Asset != null)
            DrawSignalNetwork(signal.Asset, () => Undo.RecordObject(graph, "Network Sync"), () => GraphScopeEditing.Commit(graph, structural: false));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Send it to an object with Send (wire the object into To); react to it with On Received in any graph that object runs.", EditorStyles.wordWrappedMiniLabel);
    }

    private static readonly GUIContent[] _senderLabels =
    {
        new("Authority", "Only the object's authority (the server, or its owner in distributed authority) sends it. Sends from anyone else are dropped, so a Behavior that runs on every peer still fires it once everywhere. For results: died, picked up, opened."),
        new("Owner → Authority", "The object's owner (the player controlling it) sends it; the authority checks it came from the owner and fires it on everyone. The owner hears its own straight away; anyone else's sends are dropped. For inputs: fire, reload, throttle."),
        new("Anyone", "Whoever sends it, it fires on everyone. For chat, emotes, pings."),
    };

    // The Network Sync toggle and Sender dropdown of a Signal (mirrors the Variables' toggle).
    private static void DrawSignalNetwork(SignalData asset, Action before, Action after)
    {
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent("Network Sync",
            "Fire this Signal for every player when it's sent (on objects with Network Sync turned on; Signals sent to everyone go to every player)."),
            asset.Networked, EditorStyles.boldLabel);
        int sender = (int)asset.Sender;
        using (new EditorGUI.DisabledScope(!on))
            sender = EditorGUILayout.Popup(new GUIContent("Sender", "Who may send it."), sender, _senderLabels);
        if (EditorGUI.EndChangeCheck())
        {
            before?.Invoke();
            Undo.RecordObject(asset, "Network Sync");
            asset.Networked = on;
            asset.Sender = (SignalNetworkSender)sender;
            EditorUtility.SetDirty(asset);
            after?.Invoke();
        }
        if (on)
            EditorGUILayout.HelpBox(asset.Sender switch
            {
                SignalNetworkSender.OwnerToAuthority => "The owner sends it to the authority, which fires it on everyone. Other peers' sends are dropped.",
                SignalNetworkSender.Anyone => "Whoever sends it, it fires on every player.",
                _ => "Only the authority's sends count; it fires on every player. Other peers' sends are dropped.",
            } + " Signals sent while handling a networked Signal stay local (each player fires their own), and its parameters must be values or networked objects.", MessageType.None);
    }

    #endregion

    private static void DrawNetworkToggle(bool current, bool? graphNetworked, Action<bool> set)
    {
        EditorGUI.BeginChangeCheck();
        bool on = EditorGUILayout.ToggleLeft(new GUIContent("Network Sync", "The object's authority sends this Variable's value to everyone else whenever it changes."), current, EditorStyles.boldLabel);
        if (EditorGUI.EndChangeCheck())
            set(on);
        if (on)
            EditorGUILayout.HelpBox("Synced on objects with Network Sync turned on (the NetworkObject toggle in their header). The object's owner/server is the one whose changes count; changes made elsewhere are overwritten."
                + (graphNetworked == false ? "\nThis graph isn't marked Networked: the Variable still syncs, but the graph's events run on every peer." : ""), MessageType.None);
    }
}
