using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Boolean Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Boolean value entered on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""bool""}], ""outputs"": [{""name"": ""value"", ""type"": ""bool""}], ""use_for"": [""providing a fixed true or false value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Boolean Value", Path = "Values"), Tags("Default")]
    public class BooleanValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public bool InputValue;

        [Output("value")]
        public bool Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Integer Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the integer value entered on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""int""}], ""outputs"": [{""name"": ""value"", ""type"": ""int""}], ""use_for"": [""providing a fixed whole-number value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Integer Value", Path = "Values"), Tags("Default")]
    public class IntegerValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public int InputValue;

        [Output("value")]
        public int Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Float Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the float value entered on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""float""}], ""outputs"": [{""name"": ""value"", ""type"": ""float""}], ""use_for"": [""providing a fixed decimal number""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Float Value", Path = "Values"), Tags("Default")]
    public class FloatValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public float InputValue;

        [Output("value")]
        public float Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""String Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the string value entered on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""string""}], ""outputs"": [{""name"": ""value"", ""type"": ""string""}], ""use_for"": [""providing fixed text""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "String Value", Path = "Values"), Tags("Default")]
    public class StringValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public string InputValue;

        [Output("value")]
        public string Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Game Object Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the GameObject assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""GameObject""}], ""outputs"": [{""name"": ""value"", ""type"": ""GameObject""}], ""use_for"": [""providing a fixed scene or prefab object reference""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Game Object Value", Path = "Values"), Tags("Default")]
    public class GameObjectValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public GameObject InputValue;

        [Output("value")]
        public GameObject Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Vector 2 Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Vector2 assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""Vector2""}], ""outputs"": [{""name"": ""value"", ""type"": ""Vector2""}], ""use_for"": [""providing a fixed Vector2 value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Vector 2 Value", Path = "Values"), Tags("Default")]
    public class Vector2Value : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public Vector2 InputValue;

        [Output("value")]
        public Vector2 Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Vector 4 Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Vector4 assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""Vector4""}], ""outputs"": [{""name"": ""value"", ""type"": ""Vector4""}], ""use_for"": [""providing a fixed Vector4 value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Vector 4 Value", Path = "Values"), Tags("Default")]
    public class Vector4Value : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public Vector4 InputValue;

        [Output("value")]
        public Vector4 Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Color Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Color assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""Color""}], ""outputs"": [{""name"": ""value"", ""type"": ""Color""}], ""use_for"": [""providing a fixed Color value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Color Value", Path = "Values"), Tags("Default")]
    public class ColorValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public Color InputValue = Color.white;

        [Output("value")]
        public Color Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Color 32 Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Color32 assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""Color32""}], ""outputs"": [{""name"": ""value"", ""type"": ""Color32""}], ""use_for"": [""providing a fixed 8-bit Color value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Color 32 Value", Path = "Values"), Tags("Default")]
    public class Color32Value : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public Color32 InputValue = new Color32(255, 255, 255, 255);

        [Output("value")]
        public Color32 Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Quaternion Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the Quaternion assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""Quaternion""}], ""outputs"": [{""name"": ""value"", ""type"": ""Quaternion""}], ""use_for"": [""providing a fixed rotation value""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Quaternion Value", Path = "Values"), Tags("Default")]
    public class QuaternionValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public Quaternion InputValue = Quaternion.identity;

        [Output("value")]
        public Quaternion Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }

    [SchematicDoc(@"{""name"": ""Layer Mask Value"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Values"", ""description"": ""Outputs the LayerMask assigned on the node or connected to its input."", ""inputs"": [{""name"": ""Input"", ""type"": ""LayerMask""}], ""outputs"": [{""name"": ""value"", ""type"": ""LayerMask""}], ""use_for"": [""providing a fixed layer mask""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Name = "Layer Mask Value", Path = "Values"), Tags("Default")]
    public class LayerMaskValue : SchematicGraphNode
    {
        [Input("Input", Editable = true)]
        public LayerMask InputValue;

        [Output("value")]
        public LayerMask Value;

        public override object OnRequestValue(Port port) => GetInputValue(nameof(InputValue), InputValue);
    }
}
