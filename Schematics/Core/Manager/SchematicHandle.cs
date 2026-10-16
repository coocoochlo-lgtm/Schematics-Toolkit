using Remedy.Schematics;
using System;
using UnityEngine;

/// <summary>
/// A spawned Schematic Object picked by UID through <see cref="ExecutionContext.Pick"/>.
/// UIDs change every time an object is spawned, so a handle kept after its object was despawned simply stops doing anything.
/// </summary>
public readonly struct SchematicHandle : IEquatable<SchematicHandle>
{
    public readonly int UID;

    public SchematicHandle(int uid)
    {
        UID = uid;
    }

    public bool IsValid => ExecutionContext.Contains(UID);
    public SchematicObject Object => ExecutionContext.GetObject(UID);

    public SchematicHandle Enable() => SetEnabled(true);
    public SchematicHandle Disable() => SetEnabled(false);
    public SchematicHandle Enable<TBehavior>(GameObject target = null) where TBehavior : SchematicBehavior => SetEnabled(true, typeof(TBehavior), target);
    public SchematicHandle Disable<TBehavior>(GameObject target = null) where TBehavior : SchematicBehavior => SetEnabled(false, typeof(TBehavior), target);

    /// <param name="behaviorType">Only this Behavior. Null for all of them.</param>
    /// <param name="target">Only Behaviors attached to this GameObject. Null for the root and all children.</param>
    public SchematicHandle SetEnabled(bool enabled, Type behaviorType = null, GameObject target = null)
    {
        ExecutionContext.SetEnabled(UID, enabled, behaviorType, target);
        return this;
    }

    /// <summary>
    /// Despawns the object (back to its pool if it came from one).
    /// </summary>
    public void Destroy()
    {
        var schematicObject = Object;
        if (schematicObject != null)
            SchematicsManager.Destroy(schematicObject);
    }

    public bool Equals(SchematicHandle other) => UID == other.UID;
    public override bool Equals(object obj) => obj is SchematicHandle other && Equals(other);
    public override int GetHashCode() => UID;
    public override string ToString() => $"Schematic {UID}";
}
