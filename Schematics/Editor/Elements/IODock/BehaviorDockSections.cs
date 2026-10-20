using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Lets a Behavior add its own UI to its section of the IO Dock (after its Variables and Signals), e.g. the Brain's
/// editable input and output slots. Register from an editor class:
/// <code>[InitializeOnLoadMethod] static void Register() => BehaviorDockSections.Register(typeof(MyBehavior), context => ...);</code>
/// </summary>
public static class BehaviorDockSections
{
    private static readonly Dictionary<Type, Func<BehaviorDockContext, VisualElement>> _sections = new();

    public static void Register(Type behaviorType, Func<BehaviorDockContext, VisualElement> create)
        => _sections[behaviorType] = create;

    internal static VisualElement Create(BehaviorDockContext context)
    {
        if (context.BehaviorType == null)
            return null;
        for (var type = context.BehaviorType; type != null; type = type.BaseType)
            if (_sections.TryGetValue(type, out var create))
            {
                try { return create(context); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        return null;
    }
}

/// <summary>Where a dock section is, and how it reads and changes its Behavior's Variables on this object.</summary>
public sealed class BehaviorDockContext
{
    public GameObject Prefab;
    public GameObject Target;
    /// <summary>Path of the object from the prefab root ("" for the root).</summary>
    public string Path;
    public Type BehaviorType;
    public BehaviorAttachment Attachment;
    public SchematicGraphEditorWindow Window;

    internal Func<string, Type, object> Read;
    internal Action<string, Type, object> Write;

    /// <summary>This object's value of a Variable (its override, or the Behavior's default).</summary>
    public T GetValue<T>(string variable) => Read?.Invoke(variable, typeof(T)) is T value ? value : default;

    /// <summary>Changes this object's value of a Variable (saved into the prefab shortly after).</summary>
    public void SetValue<T>(string variable, T value) => Write?.Invoke(variable, typeof(T), value);
}
