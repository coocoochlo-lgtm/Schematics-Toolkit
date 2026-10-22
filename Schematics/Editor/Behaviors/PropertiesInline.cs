using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// A Properties asset (a Behavior's <see cref="SchematicPropertiesAttribute"/> ScriptableObject) drawn in place under
/// its field, as a foldout with the asset's own Inspector inside, so it can be edited without leaving the object.
/// The field itself still works as usual (double-click it to select the asset), and Open shows it in a window of its
/// own. Each asset's Inspector is made once and kept, so its foldouts stay open.
/// </summary>
public static class PropertiesInline
{
    private static readonly Dictionary<string, Editor> _editors = new();

    static PropertiesInline()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Clear;
        EditorApplication.playModeStateChanged += _ => Clear();
    }

    /// <summary>Draws the foldout (and, open, the asset's Inspector). <paramref name="key"/> names where it's shown, for its open state.</summary>
    public static void Draw(Object asset, string key, string label = "Properties")
    {
        if (asset == null)
            return;
        string stateKey = "PropertiesInline." + key;
        bool open = SessionState.GetBool(stateKey, false);
        using (new EditorGUILayout.HorizontalScope())
        {
            bool now = EditorGUILayout.Foldout(open, new GUIContent(label, $"{asset.name}: edit it here (it's an asset, shared by whatever uses it)."), true);
            if (now != open)
                SessionState.SetBool(stateKey, open = now);
            if (GUILayout.Button(new GUIContent("Open", "Show it in an Inspector window of its own."), EditorStyles.miniButton, GUILayout.Width(44)))
                EditorUtility.OpenPropertyEditor(asset);
        }
        if (!open)
            return;

        _editors.TryGetValue(key, out var editor);
        if (editor == null || editor.target != asset)
        {
            Editor.CreateCachedEditor(asset, null, ref editor);
            _editors[key] = editor;
        }
        if (editor == null)
            return;
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;
            editor.OnInspectorGUI();
            EditorGUI.indentLevel = indent;
        }
    }

    private static void Clear()
    {
        foreach (var editor in _editors.Values)
            if (editor != null)
                Object.DestroyImmediate(editor);
        _editors.Clear();
    }
}
