using BlueGraph;
using System;

namespace Remedy.Schematics
{
    [Serializable]
    [Node(Path = "Strings"), Tags("Default")]
    public class StringCombine : SchematicGraphNode
    {
        [Input("A", Editable = true)]
        public string A;

        [Input("B", Editable = true)]
        public string B;

        [Output("Value")]
        public string Value;

        public override object OnRequestValue(Port port)
        {
            var a = GetInputValue<string>(nameof(A), A);
            var b = GetInputValue<string>(nameof(B), B);
            Value = string.Concat(a, b);
            return Value;
        }
    }
}
