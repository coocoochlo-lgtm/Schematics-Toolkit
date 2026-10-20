using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;

/// <summary>
/// One SerializedObject per object drawn by the IO Dock's field renderers, kept for as long as the object lives.
/// Unity's own drawers (like the Input Action's binding tree) hold on to the SerializedObject and its properties
/// between frames and open popups that edit through them, so it must not be swapped out or disposed under them:
/// a fresh SerializedObject per redraw is what made the bindings popup fail with "SerializedProperty ... has
/// disappeared!".
/// </summary>
public static class DockSerializedObjects
{
    private static readonly Dictionary<UnityEngine.Object, SerializedObject> _objects = new();

    /// <summary>The SerializedObject for <paramref name="target"/> (made the first time it's asked for).</summary>
    public static SerializedObject Get(UnityEngine.Object target)
    {
        if (target == null)
            return null;
        if (_objects.TryGetValue(target, out var serializedObject) && IsAlive(serializedObject))
            return serializedObject;

        // Drop the ones whose objects are gone.
        if (_objects.Count > 0)
        {
            var dead = new List<UnityEngine.Object>();
            foreach (var pair in _objects)
                if (pair.Key == null || !IsAlive(pair.Value))
                    dead.Add(pair.Key);
            foreach (var key in dead)
                _objects.Remove(key);
        }

        serializedObject = new SerializedObject(target);
        _objects[target] = serializedObject;
        return serializedObject;
    }

    // When each object's SerializedObject was last made again (see FindProperty), so it never happens every frame.
    private static readonly Dictionary<UnityEngine.Object, double> _renewedAt = new();

    /// <summary>
    /// The property at <paramref name="path"/>, from <paramref name="target"/>'s SerializedObject brought up to date.
    /// If the object holds something at that path the SerializedObject doesn't know about (an item the dock just added
    /// to a list, like a new Remedy Input), the SerializedObject is made again so it does; at most once a second, so
    /// drawers holding on to the old one aren't pulled out from under every frame. Null if it isn't there.
    /// </summary>
    public static SerializedProperty FindProperty(UnityEngine.Object target, string path)
    {
        var serializedObject = Get(target);
        if (serializedObject == null || string.IsNullOrEmpty(path))
            return null;
        Refresh(serializedObject);
        var property = serializedObject.FindProperty(path);
        if (property != null || ManagedPath.Resolve(target, path) == null)
            return property;

        // Paths a fresh SerializedObject didn't have either aren't saved by Unity at all: never worth renewing for.
        if (_unserialized.Contains((target, path)))
            return null;
        double now = EditorApplication.timeSinceStartup;
        if (_renewedAt.TryGetValue(target, out double last) && now - last < 1.0)
            return null;
        _renewedAt[target] = now;

        serializedObject = new SerializedObject(target);
        _objects[target] = serializedObject;
        property = serializedObject.FindProperty(path);
        if (property == null)
            _unserialized.Add((target, path));
        return property;
    }

    private static readonly HashSet<(UnityEngine.Object, string)> _unserialized = new();

    /// <summary>
    /// Brings a shared SerializedObject up to date with its object, keeping changes made through it that aren't saved
    /// yet. Every drawer in the dock shares it, and Unity's own (the Input Action's binding tree) change it from menu
    /// callbacks and leave the saving to the next draw: a plain Update() from another drawer first threw those away,
    /// so a binding just added vanished under its row ("SerializedProperty ... has disappeared!" on double-click).
    /// </summary>
    public static void Refresh(SerializedObject serializedObject)
    {
        if (serializedObject == null)
            return;
        if (serializedObject.hasModifiedProperties)
            serializedObject.ApplyModifiedProperties();
        serializedObject.Update();
    }

    private static bool IsAlive(SerializedObject serializedObject)
    {
        try { return serializedObject != null && serializedObject.targetObject != null; }
        catch { return false; } // disposed
    }
}

/// <summary>
/// Finds the live C# object at a serialized property path ("InputMaps.Array.data[0].Inputs") from the object that
/// owns it. The IO Dock looks things up again this way at the moment it changes them, instead of keeping references
/// from when it was drawn: Unity may replace those objects when the asset is deserialized (after an Undo, or after an
/// Inspector-style field applies its changes), and writing to a stale copy would undo the user's other edits.
/// </summary>
public static class ManagedPath
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>"a" + "b" → "a.b" ("" + "b" → "b").</summary>
    public static string Combine(string path, string member)
        => string.IsNullOrEmpty(path) ? member : path + "." + member;

    /// <summary>The object at <paramref name="path"/> ("" is the root itself), or null if it isn't there.</summary>
    public static object Resolve(object root, string path)
    {
        if (root == null || string.IsNullOrEmpty(path))
            return root;
        object current = root;
        var parts = path.Split('.');
        for (int i = 0; i < parts.Length && current != null; i++)
        {
            if (parts[i] == "Array" && i + 1 < parts.Length && TryIndex(parts[i + 1], out int index))
            {
                current = current is IList list && index >= 0 && index < list.Count ? list[index] : null;
                i++;
                continue;
            }
            var field = FindField(current.GetType(), parts[i]);
            current = field?.GetValue(current);
        }
        return current;
    }

    /// <summary>Sets the member at the end of <paramref name="path"/> (a field of a class, not of a struct).</summary>
    public static bool Assign(object root, string path, object value)
    {
        if (root == null || string.IsNullOrEmpty(path))
            return false;
        int dot = path.LastIndexOf('.');
        var owner = dot < 0 ? root : Resolve(root, path.Substring(0, dot));
        string member = dot < 0 ? path : path.Substring(dot + 1);
        if (owner == null || owner.GetType().IsValueType)
            return false;
        var field = FindField(owner.GetType(), member);
        if (field == null)
            return false;
        field.SetValue(owner, value);
        return true;
    }

    /// <summary>The element type of an array or List field.</summary>
    public static Type ElementType(Type collectionType)
    {
        if (collectionType == null) return null;
        if (collectionType.IsArray) return collectionType.GetElementType();
        if (collectionType.IsGenericType) return collectionType.GetGenericArguments()[0];
        return null;
    }

    private static bool TryIndex(string part, out int index)
    {
        index = -1;
        return part.StartsWith("data[", StringComparison.Ordinal) && part.EndsWith("]", StringComparison.Ordinal)
            && int.TryParse(part.Substring(5, part.Length - 6), out index);
    }

    private static FieldInfo FindField(Type type, string name)
    {
        for (var t = type; t != null; t = t.BaseType)
        {
            var field = t.GetField(name, Members | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
    }
}
