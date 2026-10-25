using UnityEngine;

/// <summary>
/// A scratch copy of a Variable's value that Unity's own drawers can edit (arrays, lists and classes: a Weapon Holder's
/// Sockets, its Weapon Inputs...), for the IO Dock: made in memory, never saved; what's edited in it is written to the
/// prefab through the dock's usual load-edit-save.
/// </summary>
internal sealed class DockBoxedValue : ScriptableObject
{
    [SerializeReference] public SchematicBox Box;

    /// <summary>The value's property path in this object.</summary>
    public const string ValuePath = nameof(Box) + ".Value";
}
