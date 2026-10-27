using BlueGraph;
using BlueGraph.Editor;
using Remedy.Schematics;
using System;
using UnityEngine;
using UnityEngine.UIElements;

[CustomNodeView(typeof(SchematicRedirectNode))]
[CustomNodeView(typeof(SchematicFlowRedirectNode))]
internal sealed class RedirectNodeView : NodeView
{
    internal static readonly Vector2 NodeSize = new(24f, 18f);
    internal static readonly Vector2 PositionOffset = new(12f, 9f);

    protected override void OnInitialize()
    {
        base.OnInitialize();

        style.width = NodeSize.x;
        style.minWidth = NodeSize.x;
        style.maxWidth = NodeSize.x;
        style.height = NodeSize.y;
        style.minHeight = NodeSize.y;
        style.maxHeight = NodeSize.y;
        tooltip = Target is SchematicFlowRedirectNode ? "Flow Redirect" : "Redirect";

        titleContainer.style.display = DisplayStyle.None;
        this.Q("contents")?.SetDisplay(false);
        this.Q("extension")?.SetDisplay(false);
        this.Q("node-border")?.SetDisplay(false);

        // Put the ports directly on the glyph ends. BlueGraph normally keeps these in separate flex columns,
        // which leaves a long flat wire segment underneath a small centered redirect icon.
        inputContainer.RemoveFromHierarchy();
        outputContainer.RemoveFromHierarchy();
        Add(inputContainer);
        Add(outputContainer);
        inputContainer.style.position = Position.Absolute;
        inputContainer.style.left = -7;
        inputContainer.style.top = 2;
        inputContainer.style.width = 14;
        inputContainer.style.height = 14;
        inputContainer.style.backgroundColor = Color.clear;
        outputContainer.style.position = Position.Absolute;
        outputContainer.style.right = -7;
        outputContainer.style.top = 2;
        outputContainer.style.width = 14;
        outputContainer.style.height = 14;
        outputContainer.style.backgroundColor = Color.clear;
        foreach (var port in Inputs)
            StylePort(port);
        foreach (var port in Outputs)
            StylePort(port);

        var wireColor = WireColor(Target is SchematicRedirectNode valueNode
            ? valueNode.ValueType
            : typeof(ActionPort));
        var glyph = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = tooltip };
        glyph.style.position = Position.Absolute;
        glyph.style.left = 0;
        glyph.style.top = -2;
        glyph.style.width = 24;
        glyph.style.height = 18;

        // Reroute symbol: a round input end followed by a large arrow tip. The wires provide the two separated stems.
        var input = new VisualElement { pickingMode = PickingMode.Ignore };
        input.style.position = Position.Absolute;
        input.style.left = -5;
        input.style.top = 4;
        input.style.width = 10;
        input.style.height = 10;
        input.style.borderTopLeftRadius = input.style.borderTopRightRadius = 5;
        input.style.borderBottomLeftRadius = input.style.borderBottomRightRadius = 5;
        input.style.backgroundColor = new Color(0.12f, 0.13f, 0.16f, 1f);
        input.style.borderLeftWidth = input.style.borderRightWidth = 1.5f;
        input.style.borderTopWidth = input.style.borderBottomWidth = 1.5f;
        input.style.borderLeftColor = input.style.borderRightColor = wireColor;
        input.style.borderTopColor = input.style.borderBottomColor = wireColor;
        glyph.Add(input);

        var arrowhead = new Label("›") { pickingMode = PickingMode.Ignore };
        arrowhead.style.position = Position.Absolute;
        arrowhead.style.left = -8;
        arrowhead.style.top = -7;
        arrowhead.style.width = 32;
        arrowhead.style.height = 32;
        arrowhead.style.unityTextAlign = TextAnchor.MiddleRight;
        arrowhead.style.fontSize = 32;
        arrowhead.style.unityFontStyleAndWeight = FontStyle.Bold;
        arrowhead.style.color = wireColor;
        glyph.Add(arrowhead);
        Insert(1, glyph);
    }

    private static void StylePort(PortView port)
    {
        port.style.width = 14;
        port.style.height = 14;
        port.style.minHeight = 14;
        port.style.paddingLeft = port.style.paddingRight = 0;
        port.style.paddingTop = port.style.paddingBottom = 0;
        port.style.backgroundColor = Color.clear;
        port.Q<Label>()?.SetDisplay(false);
    }

    private static Color WireColor(Type type)
    {
        // Match the type colors in BlueGraph's Variables.uss so the redirect reads as part of its wire.
        if (type == typeof(ActionPort)) return Color.white;
        if (type == typeof(string)) return Hex("fb02d1");
        if (type == typeof(bool)) return Hex("ff000c");
        if (type == typeof(int) || type == typeof(long)) return Hex("29e0dd");
        if (type == typeof(float) || type == typeof(double)) return Hex("9ffb52");
        if (type != null && type.IsEnum) return Hex("07ff64");
        if (type == typeof(Vector2) || type == typeof(Vector3) || type == typeof(Vector4)) return Hex("ffc727");
        if (type == typeof(Quaternion)) return Hex("afafdd");
        if (type == typeof(Color)) return Hex("bebebe");
        if (type == typeof(Transform)) return Hex("fa711d");
        if (type == typeof(GameObject)) return Hex("bb60ff");
        if (type != null && typeof(UnityEngine.Object).IsAssignableFrom(type)) return Hex("18abff");
        return Hex("4eaeae");
    }

    private static Color Hex(string value) => ColorUtility.TryParseHtmlString("#" + value, out var color) ? color : Color.white;
}

internal static class RedirectNodeViewExtensions
{
    public static void SetDisplay(this VisualElement element, bool visible)
    {
        if (element != null)
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }
}
