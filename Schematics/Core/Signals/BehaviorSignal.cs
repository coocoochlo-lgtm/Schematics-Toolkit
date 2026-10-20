using System;
using UnityEngine;

/// <summary>
/// A Signal declared by a <see cref="SchematicBehavior"/>. Shared by every copy of its <c>SignalHandle</c>, the same way
/// a Variable's handles share one column.
/// </summary>
public sealed class BehaviorSignal
{
    public int Index { get; }
    public string Name { get; internal set; }
    public Type[] ParameterTypes { get; }

    internal readonly BehaviorCursor Cursor;
    private SignalData _data;

    /// <summary>
    /// The Signal asset carrying this Signal: the one generated into the Behavior's Metadata (which graphs reference),
    /// or a runtime-only one if the Behavior has no Metadata yet.
    /// </summary>
    public SignalData Data
    {
        get
        {
            if (_data == null)
            {
                _data = ScriptableObject.CreateInstance<SignalData>();
                _data.name = Name;
                _data.hideFlags = HideFlags.DontSave;
            }
            return _data;
        }
        internal set => _data = value;
    }

    internal BehaviorSignal(int index, Type[] parameterTypes, BehaviorCursor cursor)
    {
        Index = index;
        ParameterTypes = parameterTypes;
        Cursor = cursor;
    }

    /// <summary>The UID of the Behavior's selected instance, which <c>Invoke()</c> and <c>Subscribe()</c> target by default.</summary>
    internal int SelectedUID
    {
        get
        {
            var instance = Cursor.Instance;
            if (instance == null || !instance.IsSpawned)
                throw new InvalidOperationException(
                    $"Signal '{Name}' has no instance selected. Send it inside a lifecycle method, after GetSchematicBehavior, " +
                    "or pass the target instance explicitly.");
            return instance.UID;
        }
    }

    internal static int UIDOf(BehaviorInstance target)
    {
        if (target == null || !target.IsSpawned)
            throw new InvalidOperationException("The target Behavior Instance is not spawned.");
        return target.UID;
    }
}

internal interface ISignalHandle
{
    BehaviorSignal Signal { get; }
}
