using System;

/// <summary>
/// Marks a Behavior's ScriptableObject Variable as its Properties: the IO Dock creates the asset automatically for each
/// object that uses the Behavior (if it has none) and shows it, editable in place, in the Behavior's Properties foldout.
/// A ScriptableObject Variable named "Properties" counts as one without the attribute.
/// <code>[SchematicProperties] public readonly VariableHandle&lt;HoverControllerProperties&gt; Properties = (HoverControllerProperties)null;</code>
/// </summary>
[AttributeUsage(AttributeTargets.Field)]
public class SchematicPropertiesAttribute : Attribute { }
