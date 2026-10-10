using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class SchematicManagerData : SingletonData<SchematicManagerData>
{
    [SerializeField, HideInInspector]
    private List<SignalData> _runtimeSignalLookup = new();
    [SerializeField, HideInInspector]
    private List<VariableData> _runtimeVariableLookup = new();
    [SerializeField, HideInInspector]
    private int _schemaSignalStride = 0;
    [SerializeField, HideInInspector]
    private int _schemaVariableStride = 0;

#if UNITY_EDITOR
    // Editor Lookups
    [SerializeField, HideInInspector]
    private List<GlobalObjectId> _schematicOwnerLookup = new();
    public static List<GlobalObjectId> SchematicOwnerLookup => Instance._schematicOwnerLookup;

    [SerializeField, HideInInspector]
    private Dictionary<int, List<SignalData>> _editorSignalData = new();
    public static Dictionary<int, List<SignalData>> EditorSignalLookup => Instance._editorSignalData;

    [SerializeField, HideInInspector]
    private Dictionary<int, List<VariableData>> _editorVariableLookup = new();
    public static Dictionary<int, List<VariableData>> EditorVariableDataLookup => Instance._editorVariableLookup;

    /// <summary>
    /// Registers a new schematic owner by GlobalObjectId.
    /// Returns the schema index assigned to it, or the existing index if already registered.
    /// </summary>
    public static int RegisterSchematic(GlobalObjectId objectId)
    {
        var lookup = Instance._schematicOwnerLookup;

        // Check if already registered
        for (int i = 0; i < lookup.Count; i++)
        {
            if (lookup[i].Equals(objectId))
                return i;
        }

        int newIndex = lookup.Count;
        lookup.Add(objectId);

        // Seed empty lists in editor dictionaries so stride math stays consistent
        Instance._editorSignalData[newIndex] = new List<SignalData>();
        Instance._editorVariableLookup[newIndex] = new List<VariableData>();

        RebuildRuntimeLookups();
        MarkDirty();

        return newIndex;
    }

    /// <summary>
    /// Adds a signal to a schema by its schema index.
    /// Returns the signal's local index within that schema.
    /// </summary>
    public static int AddSignal(int schemaIndex, SignalData signal)
    {
        EnsureEditorListExists(Instance._editorSignalData, schemaIndex);

        var list = Instance._editorSignalData[schemaIndex];
        int localIndex = list.Count;
        list.Add(signal);

        RebuildRuntimeSignalLookup();
        MarkDirty();

        return localIndex;
    }

    /// <summary>
    /// Adds a signal to a schema by its GlobalObjectId.
    /// Registers the schematic first if it doesn't exist yet.
    /// </summary>
    public static int AddSignal(GlobalObjectId objectId, SignalData signal)
    {
        int schemaIndex = RegisterSchematic(objectId);
        return AddSignal(schemaIndex, signal);
    }

    /// <summary>
    /// Adds a variable to a schema by its schema index.
    /// Returns the variable's local index within that schema.
    /// </summary>
    public static int AddVariable(int schemaIndex, VariableData variable)
    {
        EnsureEditorListExists(Instance._editorVariableLookup, schemaIndex);

        var list = Instance._editorVariableLookup[schemaIndex];
        int localIndex = list.Count;
        list.Add(variable);

        RebuildRuntimeVariableLookup();
        MarkDirty();

        return localIndex;
    }

    /// <summary>
    /// Adds a variable to a schema by its GlobalObjectId.
    /// Registers the schematic first if it doesn't exist yet.
    /// </summary>
    public static int AddVariable(GlobalObjectId objectId, VariableData variable)
    {
        int schemaIndex = RegisterSchematic(objectId);
        return AddVariable(schemaIndex, variable);
    }

    /// <summary>
    /// Rebuilds both flat runtime lookups and recalculates strides from editor data.
    /// Call this any time the editor data changes shape.
    /// </summary>
    public static void RebuildRuntimeLookups()
    {
        RebuildRuntimeSignalLookup();
        RebuildRuntimeVariableLookup();
    }

    private static void RebuildRuntimeSignalLookup()
    {
        var inst = Instance;
        var editorData = inst._editorSignalData;
        int schemaCount = inst._schematicOwnerLookup.Count;

        // Stride = largest signal list across all schematics (so every slot fits)
        int maxSignals = 0;
        for (int i = 0; i < schemaCount; i++)
        {
            if (editorData.TryGetValue(i, out var list))
                maxSignals = Mathf.Max(maxSignals, list.Count);
        }

        inst._schemaSignalStride = maxSignals;

        var flat = inst._runtimeSignalLookup;
        flat.Clear();
        flat.Capacity = schemaCount * maxSignals;

        for (int schemaIdx = 0; schemaIdx < schemaCount; schemaIdx++)
        {
            editorData.TryGetValue(schemaIdx, out var signals);

            for (int slot = 0; slot < maxSignals; slot++)
            {
                // Pad with default if this schema has fewer signals than the stride
                flat.Add(signals != null && slot < signals.Count ? signals[slot] : default);
            }
        }
    }

    private static void RebuildRuntimeVariableLookup()
    {
        var inst = Instance;
        var editorData = inst._editorVariableLookup;
        int schemaCount = inst._schematicOwnerLookup.Count;

        int maxVars = 0;
        for (int i = 0; i < schemaCount; i++)
        {
            if (editorData.TryGetValue(i, out var list))
                maxVars = Mathf.Max(maxVars, list.Count);
        }

        inst._schemaVariableStride = maxVars;

        var flat = inst._runtimeVariableLookup;
        flat.Clear();
        flat.Capacity = schemaCount * maxVars;

        for (int schemaIdx = 0; schemaIdx < schemaCount; schemaIdx++)
        {
            editorData.TryGetValue(schemaIdx, out var variables);

            for (int slot = 0; slot < maxVars; slot++)
            {
                flat.Add(variables != null && slot < variables.Count ? variables[slot] : default);
            }
        }
    }

    private static void EnsureEditorListExists<T>(Dictionary<int, List<T>> dict, int schemaIndex)
    {
        if (!dict.ContainsKey(schemaIndex))
            dict[schemaIndex] = new List<T>();
    }

    private static void MarkDirty()
    {
        EditorUtility.SetDirty(Instance);
    }

#endif

    public static bool TryGetSignal(int schemaID, int signalID, out SignalData data)
    {
        data = Instance._runtimeSignalLookup[Instance._schemaSignalStride * schemaID + signalID];
        return data != null;
    }

    // Note: fixed return type to VariableData
    public static VariableData GetVariable(int schemaID, int variableID)
    {
        return Instance._runtimeVariableLookup[Instance._schemaVariableStride * schemaID + variableID];
    }
}