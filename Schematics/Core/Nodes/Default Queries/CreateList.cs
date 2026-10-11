// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Create List"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""Makes a list from the values connected to it (lists connected are merged in)."", ""inputs"": [{""name"": ""Items"", ""type"": ""object""}], ""outputs"": [{""name"": ""value"", ""type"": ""List<object>""}], ""use_for"": [""grouping values for For Each or a brain input""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Collections"), Tags("Default")]
    public class CreateList : SchematicGraphNode
    {
        [Input("Items", Multiple = true)]
        public object Items;

        [Output(" ")]
        public List<object> NewCollection;

        public override object OnRequestValue(Port port)
        {
            var itemList = GetInputValues<object>("Items").ToList();
            itemList ??= new List<object>();

            var newList = new List<object>();

            foreach(var item in itemList)
            {
                if(item is IEnumerable && !(item is string))
                {
                    newList.AddRange(((IEnumerable<object>)item).ToList());
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