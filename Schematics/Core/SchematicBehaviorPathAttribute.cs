using System;

/// <summary>
/// Sets where a <see cref="SchematicBehavior"/> appears in the Inspector's "Add Schematic Behavior" menu, e.g. "Movement/Hover".
/// Without it, the Behavior is listed under its namespace.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public class SchematicBehaviorPathAttribute : Attribute
{
    public string Path { get; }
    public bool Hidden { get; }

    public SchematicBehaviorPathAttribute(string path, bool hidden = false)
    {
        Path = path;
        Hidden = hidden;
    }
}
