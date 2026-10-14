// project armada

#pragma warning disable 0414

using BlueGraph;
using System.Collections;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""List Count"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""How many items a list has."", ""inputs"": [{""name"": ""List"", ""type"": ""List<object>""}], ""outputs"": [{""name"": ""Count"", ""type"": ""int""}], ""use_for"": [""checking if any targets remain"", ""counting enemies""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Collections"), Tags("Default")]
    public class ListCount : SchematicGraphNode
    {
        [Input("List")]
        public List<object> List;

        [Output("Count")]
        public int Count;

        public override object OnRequestValue(Port port)
        {
            // (This used to read its input with GetOutputValue, which never found it.)
            var list = GetInputValue<IList>("List", List);
            return list != null ? list.Count : 0;
        }
    }
}