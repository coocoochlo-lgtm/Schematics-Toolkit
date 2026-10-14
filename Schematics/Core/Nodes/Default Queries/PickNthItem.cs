// project armada

#pragma warning disable 0414

using BlueGraph;
using System.Collections;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Pick Nth Item"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""The item at an index of a list."", ""inputs"": [{""name"": ""List"", ""type"": ""List<object>""}, {""name"": ""Index"", ""type"": ""int""}], ""outputs"": [{""name"": ""Item"", ""type"": ""object""}], ""use_for"": [""picking a specific or random element""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Collections"), Tags("Default")]
    public class PickNthItem : SchematicGraphNode
    {
        [Input("List")]
        public List<object> List;
        [Input("Index")]
        public int Index;

        [Output("Item")]
        public object Item;

        public override object OnRequestValue(Port port)
        {
            var list = GetInputValue<IList>("List", List);
            int index = GetInputValue("Index", Index);
            if (list == null || index < 0 || index >= list.Count)
                return null;
            var item = list[index];
            return item is Union union ? SignalArgs.ToObject(union) : item;
        }
    }
}