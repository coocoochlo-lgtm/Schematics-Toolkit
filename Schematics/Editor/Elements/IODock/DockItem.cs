using Remedy.Schematics;
using System;
using UnityEngine;

/// <summary>
/// Something in the IO Dock that can be dragged into a Schematic Graph or navigated to: a Behavior, one of its
/// Variables or Signals, a GameObject of the prefab's hierarchy, or a graph's own Variable or Signal.
/// </summary>
public sealed class DockItem
{
    public enum ItemKind { Behavior, Variable, Signal, GameObject, GraphVariable }

    public const string DragKey = "SchematicDockItem";

    public ItemKind Kind;
    /// <summary>Path of the GameObject from the prefab root ("" for the root).</summary>
    public string Path = "";
    public Type Behavior;
    /// <summary>The Variable or Signal name.</summary>
    public string Name;
    public Type ValueType;
    public SignalData Signal;
    public SignalDirection Direction;
    public GameObject GameObject;
    /// <summary>For graph Variables and Signals: the graph that declares them.</summary>
    public SchematicGraph Graph;
    /// <summary>For graph Variables: the Variable's id.</summary>
    public string VariableId;

    /// <summary>Identifies the item, so dock rows and graph nodes can find each other.</summary>
    public string Key => Kind switch
    {
        ItemKind.Signal => SignalKey(Signal),
        ItemKind.Variable => VariableKey(Behavior, Path, Name),
        ItemKind.GraphVariable => GraphVariableKey(Graph, VariableId),
        ItemKind.Behavior => BehaviorKey(Behavior, Path),
        _ => ObjectKey(Path),
    };

    public string DisplayName => Kind switch
    {
        ItemKind.Behavior => Behavior != null ? Behavior.Name : "Behavior",
        ItemKind.GameObject => GameObject != null ? GameObject.name : Path,
        _ => Name,
    };

    public static string SignalKey(SignalData signal) => signal != null ? "signal:" + signal.Uid : "signal:none";
    public static string VariableKey(Type behavior, string path, string name) => $"var:{behavior?.FullName}|{path}|{name}";
    public static string BehaviorKey(Type behavior, string path) => $"behavior:{behavior?.FullName}|{path}";
    public static string ObjectKey(string path) => "object:" + path;
    public static string GraphVariableKey(SchematicGraph graph, string id)
        => $"gvar:{(graph != null ? graph.Uid : "")}|{id}";
}
