using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class MakeVector2 : SchematicGraphNode
    {
        [Input("X", Editable = true)] public float X;
        [Input("Y", Editable = true)] public float Y;
        [Output("Value")] public Vector2 Value;

        public override object OnRequestValue(Port port)
        {
            Value = new Vector2(GetInputValue<float>(nameof(X), X), GetInputValue<float>(nameof(Y), Y));
            return Value;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class SplitVector2 : SchematicGraphNode
    {
        [Input("Vector")] public Vector2 Vector;
        [Output("X")] public float X;
        [Output("Y")] public float Y;

        public override object OnRequestValue(Port port)
        {
            Vector = GetInputValue<Vector2>(nameof(Vector), Vector);
            return port.Name == nameof(X) ? Vector.x : port.Name == nameof(Y) ? Vector.y : null;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class MakeVector3 : SchematicGraphNode
    {
        [Input("X", Editable = true)] public float X;
        [Input("Y", Editable = true)] public float Y;
        [Input("Z", Editable = true)] public float Z;
        [Output("Value")] public Vector3 Value;

        public override object OnRequestValue(Port port)
        {
            Value = new Vector3(
                GetInputValue<float>(nameof(X), X),
                GetInputValue<float>(nameof(Y), Y),
                GetInputValue<float>(nameof(Z), Z));
            return Value;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class SplitVector3 : SchematicGraphNode
    {
        [Input("Vector")] public Vector3 Vector;
        [Output("X")] public float X;
        [Output("Y")] public float Y;
        [Output("Z")] public float Z;

        public override object OnRequestValue(Port port)
        {
            Vector = GetInputValue<Vector3>(nameof(Vector), Vector);
            return port.Name == nameof(X) ? Vector.x
                : port.Name == nameof(Y) ? Vector.y
                : port.Name == nameof(Z) ? Vector.z
                : null;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class MakeVector4 : SchematicGraphNode
    {
        [Input("X", Editable = true)] public float X;
        [Input("Y", Editable = true)] public float Y;
        [Input("Z", Editable = true)] public float Z;
        [Input("W", Editable = true)] public float W;
        [Output("Value")] public Vector4 Value;

        public override object OnRequestValue(Port port)
        {
            Value = new Vector4(
                GetInputValue<float>(nameof(X), X),
                GetInputValue<float>(nameof(Y), Y),
                GetInputValue<float>(nameof(Z), Z),
                GetInputValue<float>(nameof(W), W));
            return Value;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Math)]
    [Node(Path = "Vectors"), Tags("Default")]
    public class SplitVector4 : SchematicGraphNode
    {
        [Input("Vector")] public Vector4 Vector;
        [Output("X")] public float X;
        [Output("Y")] public float Y;
        [Output("Z")] public float Z;
        [Output("W")] public float W;

        public override object OnRequestValue(Port port)
        {
            Vector = GetInputValue<Vector4>(nameof(Vector), Vector);
            return port.Name == nameof(X) ? Vector.x
                : port.Name == nameof(Y) ? Vector.y
                : port.Name == nameof(Z) ? Vector.z
                : port.Name == nameof(W) ? Vector.w
                : null;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Colors"), Tags("Default")]
    public class MakeColor : SchematicGraphNode
    {
        [Input("R", Editable = true)] public float R = 1f;
        [Input("G", Editable = true)] public float G = 1f;
        [Input("B", Editable = true)] public float B = 1f;
        [Input("A", Editable = true)] public float A = 1f;
        [Output("Value")] public Color Value;

        public override object OnRequestValue(Port port)
        {
            Value = new Color(
                GetInputValue<float>(nameof(R), R),
                GetInputValue<float>(nameof(G), G),
                GetInputValue<float>(nameof(B), B),
                GetInputValue<float>(nameof(A), A));
            return Value;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Colors"), Tags("Default")]
    public class SplitColor : SchematicGraphNode
    {
        [Input("Color")] public Color Color;
        [Output("R")] public float R;
        [Output("G")] public float G;
        [Output("B")] public float B;
        [Output("A")] public float A;

        public override object OnRequestValue(Port port)
        {
            Color = GetInputValue<Color>(nameof(Color), Color);
            return port.Name == nameof(R) ? Color.r
                : port.Name == nameof(G) ? Color.g
                : port.Name == nameof(B) ? Color.b
                : port.Name == nameof(A) ? Color.a
                : null;
        }
    }
}
