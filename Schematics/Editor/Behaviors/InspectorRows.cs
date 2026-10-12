using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The IO Dock's Signal and Variable rows, drawn in the Inspector (IMGUI): a tinted, rounded container with the
/// Variable's type chip, an icon, the name and its type (or the Signal's parameters), then the value. The name part is
/// a handle: click it to show the item's details (and Network Sync flag), or drag it into a Schematic Graph.
/// What clicking and dragging do is filled in by the Schematic Graph editor (<see cref="Click"/>, <see cref="Drag"/>).
/// </summary>
public static class InspectorRows
{
    public enum RowKind { BehaviorVariable, GraphVariable, Signal }

    /// <summary>What a row shows.</summary>
    public struct Row
    {
        public RowKind Kind;
        public Type Behavior;
        public string Path;
        public string Name;
        public Type ValueType;
        public SignalData Signal;
        public SignalDirection Direction;
        public SchematicGraph Graph;
        public string VariableId;

        public string Key => Kind switch
        {
            RowKind.GraphVariable => "g:" + (Graph != null ? Graph.GetInstanceID() : 0) + VariableId,
            RowKind.Signal => "s:" + (Signal != null ? Signal.GetInstanceID() : 0),
            _ => $"v:{Behavior?.FullName}|{Path}|{Name}",
        };
    }

    /// <summary>A row's name was clicked. Set by the Schematic Graph editor (shows it in the Inspector).</summary>
    public static Action<Row> Click;

    /// <summary>A row's name was dragged. Set by the Schematic Graph editor (drags it into graphs).</summary>
    public static Action<Row> Drag;

    /// <summary>The row last clicked, drawn highlighted.</summary>
    public static string Selected;

    private static readonly Dictionary<Color, GUIStyle> _styles = new();
    private static GUIStyle _name, _detail;
    private static Texture2D _inputIcon, _outputIcon, _variableIcon, _graphIcon;
    private static Vector2 _pressedAt;

    public static readonly Color InputTint = new(0f, 0f, 75f / 255f, 0.25f);
    public static readonly Color OutputTint = new(75f / 255f, 0f, 0f, 0.25f);
    public static readonly Color VariableTint = new(0f, 60f / 255f, 45f / 255f, 0.25f);
    public static readonly Color GraphVariableTint = new(70f / 255f, 20f / 255f, 60f / 255f, 0.25f);

    /// <summary>Starts a row; draw its value next (in the horizontal layout), then call <see cref="End"/>.</summary>
    public static void Begin(Row row, string label, string detail, string tooltip = null)
    {
        Init();
        var tint = row.Kind switch
        {
            RowKind.Signal => row.Direction == SignalDirection.Output ? OutputTint : InputTint,
            RowKind.GraphVariable => GraphVariableTint,
            _ => VariableTint,
        };
        bool selected = Selected == row.Key;
        EditorGUILayout.BeginHorizontal(StyleFor(selected ? tint * 1.8f : tint));

        float width = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.42f - EditorGUI.indentLevel * 15f, 110f, 260f);
        var handle = GUILayoutUtility.GetRect(width, 18f, GUILayout.Width(row.Kind == RowKind.Signal ? width * 0.75f : width));
        DrawHandle(handle, row, label, row.Kind == RowKind.Signal ? null : detail, tooltip);
        Interact(handle, row);

        if (row.Kind == RowKind.Signal)
            GUILayout.Label(detail ?? "", _detail, GUILayout.MinWidth(10));
    }

    public static void End() => EditorGUILayout.EndHorizontal();

    private static void DrawHandle(Rect rect, Row row, string label, string detail, string tooltip)
    {
        float x = rect.x + 2f;
        if (row.Kind != RowKind.Signal)
        {
            EditorGUI.DrawRect(new Rect(x, rect.y + 2f, 4f, rect.height - 4f), ColorOf(row.ValueType));
            x += 8f;
        }
        var icon = row.Kind switch
        {
            RowKind.Signal => row.Direction == SignalDirection.Output ? _outputIcon : _inputIcon,
            RowKind.GraphVariable => _graphIcon,
            _ => _variableIcon,
        };
        if (icon != null)
            GUI.DrawTexture(new Rect(x, rect.y + 1f, 16f, 16f), icon, ScaleMode.ScaleToFit);
        x += 20f;

        var nameContent = new GUIContent(label, tooltip ?? "Click for details (and Network Sync); drag into a Schematic Graph to use it.");
        float nameWidth = Mathf.Min(_name.CalcSize(nameContent).x, rect.xMax - x);
        GUI.Label(new Rect(x, rect.y, nameWidth, rect.height), nameContent, _name);
        x += nameWidth + 4f;
        if (!string.IsNullOrEmpty(detail) && x < rect.xMax)
            GUI.Label(new Rect(x, rect.y, rect.xMax - x, rect.height), detail, _detail);
        EditorGUIUtility.AddCursorRect(rect, MouseCursor.Link);
    }

    // Press and move: drag. Press and release: click.
    private static void Interact(Rect rect, Row row)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive, rect);
        var evt = Event.current;
        switch (evt.GetTypeForControl(id))
        {
            case EventType.MouseDown:
                if (evt.button == 0 && rect.Contains(evt.mousePosition))
                {
                    GUIUtility.hotControl = id;
                    _pressedAt = evt.mousePosition;
                    evt.Use();
                }
                break;
            case EventType.MouseDrag:
                if (GUIUtility.hotControl == id && (evt.mousePosition - _pressedAt).sqrMagnitude > 16f)
                {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    if (Drag != null) Drag(row);
                    else if (row.Signal != null)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new UnityEngine.Object[] { row.Signal };
                        DragAndDrop.StartDrag(row.Name);
                    }
                }
                break;
            case EventType.MouseUp:
                if (GUIUtility.hotControl == id)
                {
                    GUIUtility.hotControl = 0;
                    evt.Use();
                    if (rect.Contains(evt.mousePosition))
                    {
                        Selected = row.Key;
                        Click?.Invoke(row);
                    }
                }
                break;
        }
    }

    /// <summary>The IO Dock's colour for a value type (set by the Schematic Graph editor, which owns the palette).</summary>
    public static Func<Type, Color> ColorOfType;

    private static Color ColorOf(Type type)
    {
        if (ColorOfType != null) return ColorOfType(type);
        if (type != null && type.IsEnum) return new Color(0.4f, 0.8f, 0.7f);
        if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return new Color(0.95f, 0.85f, 0.4f);
        return Color.gray;
    }

    public static string TypeName(Type type)
    {
        if (type == null) return "?";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "List<" + TypeName(type.GetGenericArguments()[0]) + ">";
        return type.Name switch { "Single" => "float", "Int32" => "int", "Boolean" => "bool", "String" => "string", _ => type.Name };
    }

    private static void Init()
    {
        _name ??= new GUIStyle(EditorStyles.label) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip, padding = new RectOffset(2, 2, 1, 0) };
        _detail ??= new GUIStyle(EditorStyles.miniLabel) { clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft, normal = { textColor = new Color(0.65f, 0.65f, 0.65f) } };
        _inputIcon ??= Resources.Load<Texture2D>("Icons/Input");
        _outputIcon ??= Resources.Load<Texture2D>("Icons/Output");
        if (_variableIcon == null) _variableIcon = MakeDot(new Color(0.55f, 0.85f, 1f));
        if (_graphIcon == null) _graphIcon = MakeDot(new Color(0.95f, 0.6f, 0.85f));
    }

    // A rounded, tinted background, nine-sliced so it stretches cleanly.
    private static GUIStyle StyleFor(Color tint)
    {
        if (_styles.TryGetValue(tint, out var style) && style.normal.background != null)
            return style;
        const int size = 12, radius = 4;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(0, Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius))), dy = Mathf.Max(0, Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius)));
                float a = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                texture.SetPixel(x, y, new Color(tint.r, tint.g, tint.b, tint.a * a));
            }
        texture.Apply();
        style = new GUIStyle
        {
            normal = { background = texture },
            border = new RectOffset(radius, radius, radius, radius),
            padding = new RectOffset(2, 4, 1, 1),
            margin = new RectOffset(4, 2, 1, 1),
        };
        _styles[tint] = style;
        return style;
    }

    private static Texture2D MakeDot(Color color)
    {
        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
        float radius = size * 0.5f - 1.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                var c = d < radius - 3f ? color * new Color(1, 1, 1, 0.45f) : color;
                c.a *= a;
                texture.SetPixel(x, y, c);
            }
        texture.Apply();
        return texture;
    }
}
