// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Schematic Graph that manages the game rather than one prefab. Every Singleton Graph in the project is registered
/// by the editor (no Resources folder needed) and, when the game starts, gets its own Schematic Object, kept alive
/// across scenes with DontDestroyOnLoad. That object runs the graph like any other: its events, Signals, Variables
/// and, if one is assigned, its own State Machine (the definition lives on this asset, the state on the object).
/// </summary>
[CreateAssetMenu(fileName = "New Singleton Graph", menuName = "Remedy Toolkit/Schematics/Singleton Schematic"), IncludeTags("Default", "Runtime", "Object", "State")]
[SchematicType("Singletons", Order = 10, Icon = "SchematicTab:singleton", NewAssetName = "New Singleton Graph")]
public class SingletonGraph : SchematicGraph
{
    public override string Title => "Singleton: " + name;

    [Tooltip("Create this Singleton's object when the game starts. Off keeps the graph registered but idle.")]
    public bool SpawnOnStart = true;

    [Tooltip("Singletons are created in ascending order of this value.")]
    public int Order;

    [Tooltip("An optional State Machine run by this Singleton's object. Each object gets its own state.")]
    public StateMachineDefinition StateMachine;

    private static readonly Dictionary<SingletonGraph, SchematicObject> _hosts = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _hosts.Clear();

    /// <summary>The Schematic Object running this graph, while the game is running. Null if it wasn't created.</summary>
    public SchematicObject Host => _hosts.TryGetValue(this, out var host) && host != null ? host : null;

    /// <summary>Records the object running a graph. Called by the Singleton Graph spawner.</summary>
    public static void SetHost(SingletonGraph graph, SchematicObject host)
    {
        if (host != null) _hosts[graph] = host;
        else _hosts.Remove(graph);
    }

    /// <summary>The running object of the Singleton Graph with this name, or null.</summary>
    public static SchematicObject Find(string graphName)
    {
        foreach (var entry in _hosts)
            if (entry.Key != null && entry.Key.name == graphName && entry.Value != null)
                return entry.Value;
        return null;
    }

    /// <summary>Every registered Singleton Graph (registered by the editor; see <see cref="SchematicManagerData"/>).</summary>
    public static IReadOnlyList<SingletonGraph> All => SchematicManagerData.SingletonGraphs;
}
