// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Create List"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""Makes a list from the values connected to it (lists connected are merged in)."", ""inputs"": [{""name"": ""Items"", ""type"": ""object""}], ""outputs"": [{""name"": ""value"", ""type"": ""List<object>""}], ""use_for"": [""grouping values for For Each or a brain input""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Collections"), Tags("Default")]
    public class CreateList : SchematicGraphNode
    {
        [Tooltip("The values to put in the list (wire as many as you like; lists wired in are merged).")]
        [Input("Items", Multiple = true)]
        public object Items;

        [Tooltip("The list.")]
        [Output(" ")]
        public List<object> NewCollection;

        public override object OnRequestValue(Port port)
        {
            // A new list each time: a flow that continues later (after a Wait) keeps the one it was given.
            var newList = new List<object>();
            foreach (var item in GetInputValues<object>("Items"))
            {
                if (item is IEnumerable items && item is not string)
                {
                    foreach (var inner in items)
                        newList.Add(inner is Union union ? SignalArgs.ToObject(union) : inner);
                }
                else
                {
                    newList.Add(item);
                }
            }
            return newList;
        }
    }
}