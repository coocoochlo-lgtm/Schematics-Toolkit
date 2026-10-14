using Remedy.Schematics;
using UnityEngine;

/// <summary>
/// What <see cref="SchematicBehavior.OnSetup"/> receives: the GameObject the Behavior was just attached to, and a way to
/// set that object's Variable values (saved as its per-object overrides).
/// </summary>
public readonly struct BehaviorSetupContext
{
    public readonly GameObject GameObject;
    private readonly BehaviorAttachment _attachment;
    private readonly SchematicBehavior _behavior;

    public Transform Transform => GameObject.transform;

    internal BehaviorSetupContext(GameObject gameObject, BehaviorAttachment attachment, SchematicBehavior behavior)
    {
        GameObject = gameObject;
        _attachment = attachment;
        _behavior = behavior;
    }

    /// <summary>
    /// Sets the value this object spawns with.
    /// </summary>
    public void Set<T>(VariableHandle<T> variable, T value)
    {
        int index = variable.Index;
        if (_attachment == null || index < 0 || index >= _behavior.Variables.Count)
            return;
        _attachment.SetOverride(_behavior.Variables[index].Name, value, typeof(T));
    }
}
