using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class BooleanValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public bool Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class IntegerValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public int Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class FloatValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public float Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class StringValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public string Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class GameObjectValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public GameObject Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class Vector2Value : SchematicGraphNode
    {
        [Output("Value"), Editable] public Vector2 Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class Vector3Value : SchematicGraphNode
    {
        [Output("Value"), Editable] public Vector3 Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class Vector4Value : SchematicGraphNode
    {
        [Output("Value"), Editable] public Vector4 Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class ColorValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public Color Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class Color32Value : SchematicGraphNode
    {
        [Output("Value"), Editable] public Color32 Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class QuaternionValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public Quaternion Value;
        public override object OnRequestValue(Port port) => Value;
    }

    [Serializable]
    [Node(Path = "Values"), Tags("Default")]
    public class LayerMaskValue : SchematicGraphNode
    {
        [Output("Value"), Editable] public LayerMask Value;
        public override object OnRequestValue(Port port) => Value;
    }
}
