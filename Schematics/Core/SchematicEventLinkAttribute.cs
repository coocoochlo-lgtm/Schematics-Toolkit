using System;

/// <summary>
/// Links a Behavior's Signal field to a Signal of another Behavior, so the field is that other Behavior's Signal: the
/// IO Dock shows it on this Behavior too, and sending or listening to it is the same as using the other Behavior's.
/// E.g. a character controller showing the Character Motion Context's MoveInput.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
public class SchematicEventLinkAttribute : Attribute
{
    public Type Type;
    public string FieldName;

    public SchematicEventLinkAttribute(Type type, string fieldName)
    {
        Type = type;
        FieldName = fieldName;
    }
}
