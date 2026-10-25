using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The IO Dock's rows, shared by the Prefab tab, the Graph tab and the Inspector: a Signal or Variable as a tinted
/// container with its type chip, icon, name and type/parameters. Press and move to drag it into a graph; click it to
/// find its nodes and show its details.
/// </summary>
public static class DockRows
{
    private static Texture2D _inputIcon, _outputIcon, _variableIcon, _graphIcon, _signalIcon;
    public static Texture2D InputIcon => _inputIcon ??= Resources.Load<Texture2D>("Icons/Input");
    public static Texture2D OutputIcon => _outputIcon ??= Resources.Load<Texture2D>("Icons/Output");
    /// <summary>
    /// Behavior Variables: a white ring with a dimmer centre, tinted with the colour of the Variable's type where it's
    /// drawn (<see cref="ColorOf"/>, the wires' colours), in the dock and the minimap alike.
    /// </summary>
    public static Texture2D VariableIcon => _variableIcon != null ? _variableIcon : _variableIcon = MakeDot(solid: false);
    /// <summary>
    /// Signals: a white triangle with a dimmer centre (the Variables' ring, as a triangle: a value goes, a Signal goes
    /// off), tinted with <see cref="SignalColor"/> where it's drawn.
    /// </summary>
    public static Texture2D SignalIcon => _signalIcon != null ? _signalIcon : _signalIcon = MakeTriangle();

    /// <summary>A Signal's tint, by which way it goes: received, blue; sent, red.</summary>
    public static Color SignalColor(bool output) => output ? new Color(1f, 0.38f, 0.35f) : new Color(0.38f, 0.62f, 1f);

    /// <summary>
    /// The start of a Signal's row, like a Variable's: a bar and the triangle, in the Signal's colour.
    /// </summary>
    public static VisualElement SignalMark(bool output)
    {
        var mark = new VisualElement { pickingMode = PickingMode.Ignore };
        mark.AddToClassList("dock-signal-mark");
        mark.style.flexDirection = FlexDirection.Row;
        mark.style.alignItems = Align.Center;
        mark.style.flexShrink = 0;
        var chip = new VisualElement { pickingMode = PickingMode.Ignore };
        chip.AddToClassList("dock-type-chip");
        chip.style.width = 4;
        chip.style.height = 14;
        chip.style.marginRight = 4;
        chip.style.backgroundColor = SignalColor(output);
        mark.Add(chip);
        var icon = new Image { image = SignalIcon, tintColor = SignalColor(output), pickingMode = PickingMode.Ignore };
        icon.style.width = 16;
        icon.style.height = 16;
        icon.style.marginRight = 4;
        mark.Add(icon);
        return mark;
    }

    /// <summary>Graph Variables: a solid dot (so they read apart from Behavior ones), tinted the same way.</summary>
    public static Texture2D GraphVariableIcon => _graphIcon != null ? _graphIcon : _graphIcon = MakeDot(solid: true);

    /// <summary>A row for a Variable or Signal, styled like the IO Dock's (it needs the IODock stylesheet above it).</summary>
    public static VisualElement Build(DockItem item, Texture icon, string label, string detail, string tooltip, Action click)
    {
        var row = new VisualElement { userData = item }; // so Sprocket can tell what a row is
        row.AddToClassList("dock-row");
        row.AddToClassList("dock-row-" + item.Kind.ToString().ToLowerInvariant());
        // The IO Dock stylesheet's Signal container look, shared by Variables (with their own tint).
        row.AddToClassList("event-container");
        bool signal = item.Kind == DockItem.ItemKind.Signal;
        if (signal)
            row.AddToClassList(item.Direction == SignalDirection.Output ? "event-container-output" : "event-container-input");
        else
            row.AddToClassList("event-container-variable");
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;

        // The handle: icon, name and type. Press and move to drag; click to find the item's nodes.
        var handle = new VisualElement { tooltip = tooltip };
        handle.AddToClassList("dock-row-handle");
        handle.style.flexDirection = FlexDirection.Row;
        handle.style.alignItems = Align.Center;
        handle.style.flexShrink = 0;
        row.Add(handle);

        if (signal)
            handle.Add(SignalMark(item.Direction == SignalDirection.Output));
        else
        {
            var chip = new VisualElement();
            chip.AddToClassList("dock-type-chip");
            chip.style.width = 4;
            chip.style.height = 14;
            chip.style.marginRight = 4;
            chip.style.backgroundColor = ColorOf(item.ValueType);
            handle.Add(chip);
        }

        var image = new Image { image = icon };
        // (A Signal's mark is its triangle, above.)
        if (signal)
            image.style.display = DisplayStyle.None;
        // A Variable's dot in its type's colour.
        if (!signal && (icon == VariableIcon || icon == GraphVariableIcon))
            image.tintColor = ColorOf(item.ValueType);
        image.style.width = 16;
        image.style.height = 16;
        image.style.marginRight = 4;
        handle.Add(image);

        var name = new Label(label);
        name.AddToClassList("dock-row-name");
        name.AddToClassList("event-name");
        handle.Add(name);

        // Networked Signals carry the same ⇅ marker as networked Variables.
        if (signal && item.Signal != null && item.Signal.Networked)
            detail = (detail ?? "") + "  ⇅";
        var info = new Label(detail);
        info.AddToClassList("dock-row-detail");
        info.style.color = new Color(0.65f, 0.65f, 0.65f);
        if (signal)
        {
            info.style.flexGrow = 1;
            info.style.unityTextAlign = TextAnchor.MiddleRight;
            row.Add(info);
        }
        else
            handle.Add(info);

        if (item.Signal == null && signal)
        {
            row.SetEnabled(false);
            row.tooltip = "This Signal has no asset yet. Use Tools/Remedy/Schematics/Refresh Behavior Metadata.";
        }

        MakeDraggable(signal ? row : handle, item, click);
        if (signal)
            SignalFlash.Track(row, item.Signal);
        return row;
    }

    // A press that moves starts a drag; one that doesn't is a click.
    public static void MakeDraggable(VisualElement element, DockItem item, Action click)
    {
        element.userData ??= item;
        Vector2 start = default;
        bool pressed = false;

        element.RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.button != 0) return;
            pressed = true;
            start = evt.mousePosition;
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseMoveEvent>(evt =>
        {
            if (!pressed || (evt.mousePosition - start).sqrMagnitude < 16f) return;
            pressed = false;
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DockItem.DragKey, item);
            DragAndDrop.objectReferences = item.Signal != null ? new UnityEngine.Object[] { item.Signal }
                                         : item.GameObject != null ? new UnityEngine.Object[] { item.GameObject }
                                         : Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(item.DisplayName);
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseUpEvent>(evt =>
        {
            if (!pressed) return;
            pressed = false;
            click?.Invoke();
            evt.StopPropagation();
        });
        element.RegisterCallback<MouseLeaveEvent>(_ => pressed = false);
    }

    /// <summary>
    /// Press and move to drag an item into a graph (a GameObject, a Behavior), with pointer events so it works on buttons
    /// and in foldout headers too; a press that doesn't move is <paramref name="click"/>. <paramref name="stopPress"/>:
    /// the press goes no further (a foldout header around it doesn't fold).
    /// </summary>
    public static void MakeDragSource(VisualElement element, DockItem item, Action click = null, bool stopPress = false)
    {
        Vector2 start = default;
        bool pressed = false;
        element.RegisterCallback<PointerDownEvent>(evt =>
        {
            if (evt.button != 0 || evt.clickCount > 1) return;
            pressed = true;
            start = evt.position;
            if (stopPress) evt.StopPropagation();
        });
        element.RegisterCallback<PointerMoveEvent>(evt =>
        {
            if (!pressed || ((Vector2)evt.position - start).sqrMagnitude < 16f) return;
            pressed = false;
            // (A button holds on to the pointer while pressed: let go, so the drag isn't also its click.)
            if (element.HasPointerCapture(evt.pointerId))
                element.ReleasePointer(evt.pointerId);
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DockItem.DragKey, item);
            DragAndDrop.objectReferences = item.GameObject != null ? new UnityEngine.Object[] { item.GameObject } : Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(item.DisplayName);
        });
        element.RegisterCallback<PointerUpEvent>(evt =>
        {
            if (!pressed) return;
            pressed = false;
            click?.Invoke();
            if (stopPress) evt.StopPropagation();
        });
    }

    public static Label SectionLabel(string text)
    {
        var label = new Label(text);
        label.AddToClassList("dock-section-label");
        label.style.unityFontStyleAndWeight = FontStyle.Bold;
        label.style.marginTop = 4;
        label.style.color = new Color(0.75f, 0.75f, 0.75f);
        return label;
    }

    public static Color ColorOf(Type type)
    {
        if (type != null && SchematicGraphEditorWindow.ColorLookupValueType.TryGetValue(type, out var color))
            return color;
        if (type != null && type.IsEnum) return new Color(0.4f, 0.8f, 0.7f);
        if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return new Color(0.95f, 0.85f, 0.4f);
        return Color.gray;
    }

    public static string TypeName(Type type)
    {
        if (type == null) return "?";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "List<" + TypeName(type.GetGenericArguments()[0]) + ">";
        return type.Name switch { "Single" => "float", "Int32" => "int", "Boolean" => "bool", "String" => "string", "SchematicGraphRunner" => "Graph", _ => type.Name };
    }

    /// <summary>"float amount, GameObject target": a Signal's parameters for its row.</summary>
    public static string Parameters(IEnumerable<(string name, Type type)> parameters)
    {
        var parts = new List<string>();
        foreach (var (name, type) in parameters)
            parts.Add($"{TypeName(type)} {name}");
        return string.Join(", ", parts);
    }

    // White, for tinting, at 32 px: a triangle pointing right, outlined, its centre dimmer (as the Variables' ring).
    private static Texture2D MakeTriangle()
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        var corners = new[] { new Vector2(8f, 4.5f), new Vector2(8f, 27.5f), new Vector2(28f, 16f) };
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                // How far inside every edge it is (negative: outside).
                float d = float.MaxValue;
                for (int i = 0; i < 3; i++)
                {
                    var a = corners[i];
                    var b = corners[(i + 1) % 3];
                    var edge = (b - a).normalized;
                    var inward = new Vector2(-edge.y, edge.x);
                    if (Vector2.Dot(corners[(i + 2) % 3] - a, inward) < 0f)
                        inward = -inward;
                    d = Mathf.Min(d, Vector2.Dot(p - a, inward));
                }
                float alpha = Mathf.Clamp01(d + 0.5f);
                float inner = Mathf.Clamp01(d - 3.5f + 0.5f);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Lerp(alpha, 0.45f, inner)));
            }
        texture.Apply();
        return texture;
    }

    // White, for tinting: drawn at 32 px (crisp at 16 and on high-DPI screens).
    private static Texture2D MakeDot(bool solid)
    {
        const int size = 32;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
        float radius = size * 0.5f - 2.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(size * 0.5f, size * 0.5f));
                float a = Mathf.Clamp01(radius - d + 0.5f);
                // A ring with a dimmer centre reads as "a value" next to the Signal arrows; a graph's is solid.
                float inner = Mathf.Clamp01(radius - 5.5f - d + 0.5f);
                float alpha = solid ? a : Mathf.Lerp(a, 0.45f, inner);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        texture.Apply();
        return texture;
    }
}
