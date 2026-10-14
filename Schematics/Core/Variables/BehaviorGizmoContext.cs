using Remedy.Schematics;
using UnityEngine;

/// <summary>
/// What a Behavior's <see cref="SchematicBehavior.OnDrawGizmos"/> receives. Works in Edit Mode too, where no instance
/// exists yet: <see cref="Get{T}"/> then returns the value configured for this object in the Inspector.
/// </summary>
public readonly struct BehaviorGizmoContext
{
    public readonly GameObject GameObject;
    public readonly SchematicObject Owner;
    /// <summary>The spawned instance, or null in Edit Mode / while despawned.</summary>
    public readonly BehaviorInstance Instance;
    private readonly BehaviorAttachment _attachment;
    private readonly SchematicBehavior _behavior;

    public Transform Transform => GameObject.transform;

    internal BehaviorGizmoContext(GameObject gameObject, SchematicObject owner, BehaviorInstance instance, BehaviorAttachment attachment, SchematicBehavior behavior)
    {
        GameObject = gameObject;
        Owner = owner;
        Instance = instance;
        _attachment = attachment;
        _behavior = behavior;
    }

    /// <summary>
    /// The live value when spawned; otherwise the value this object would spawn with (its override, then the
    /// Behavior Metadata default, then the default in code).
    /// </summary>
    public T Get<T>(VariableHandle<T> variable)
    {
        if (Instance != null && Instance.IsSpawned)
            return variable[Instance];

        return BehaviorDefaults.Resolve(variable, _attachment, _behavior);
    }

    /// <summary>A component on this GameObject.</summary>
    public TComponent GetComponent<TComponent>() where TComponent : Component
        => Instance != null ? Instance.GetComponent<TComponent>() : GameObject.GetComponent<TComponent>();
}

/// <summary>
/// The value an object spawns with: its per-object override, then the Behavior Metadata default, then the code default.
/// </summary>
internal static class BehaviorDefaults
{
    public static T Resolve<T>(VariableHandle<T> variable, BehaviorAttachment attachment, SchematicBehavior behavior)
    {
        int index = variable.Index;
        if (index < 0 || behavior == null || index >= behavior.Variables.Count)
            return default;

        var definition = behavior.Variables[index];
        var entry = attachment?.FindOverride(definition.Name);
        if (entry?.Value != null)
            return entry.Value.Get<T>();

        var metadataDefault = attachment?.Metadata != null ? attachment.Metadata.FindVariable(definition.Name) : null;
        if (metadataDefault != null && metadataDefault.OverrideDefault && metadataDefault.Default != null)
            return metadataDefault.Default.Get<T>();

        return definition is BehaviorVariable<T> typed ? SchematicCloner.Copy(typed.Default) : default;
    }
}
