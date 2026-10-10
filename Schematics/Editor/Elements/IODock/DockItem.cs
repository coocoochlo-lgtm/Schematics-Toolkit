using System;
using UnityEngine;

/// <summary>
/// Something in the IO Dock that can be dragged into a Schematic Graph or navigated to: a Behavior, one of its
/// Variables or Signals, or a GameObject of the prefab's hierarchy.
/// </summary>
public sealed class DockItem
{
    public enum ItemKind { Behavior, Variable, Signal, GameObject }

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

    /// <summary>Identifies the item, so dock rows and graph nodes can find each other.</summary>
    public string Key => Kind switch
    {
        ItemKind.Signal => SignalKey(Signal),
        ItemKind.Variable => VariableKey(Behavior, Path, Name),
        ItemKind.Behavior => BehaviorKey(Behavior, Path),
        _ => ObjectKey(Path),
    };

    public string DisplayName => Kind switch
    {
        ItemKind.Behavior => Behavior != null ? Behavior.Name : "Behavior",
        ItemKind.GameObject => GameObject != null ? GameObject.name : Path,
        _ => Name,
    };

    public static string SignalKey(SignalData signal) => signal != null ? "signal:" + signal.GetHashCode() : "signal:none";
    public static string VariableKey(Type behavior, string path, string name) => $"var:{behavior?.FullName}|{path}|{name}";
    public static string BehaviorKey(Type behavior, string path) => $"behavior:{behavior?.FullName}|{path}";
    public static string ObjectKey(string path) => "object:" + path;
}
