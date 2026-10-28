// project armada

#pragma warning disable 0414

using Remedy.Framework;
using BlueGraph;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;
using System.Collections;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""For Each"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Loops"", ""description"": ""Runs the nodes after it once per item of a list, with the current item and its index as outputs."", ""inputs"": [{""name"": ""Items"", ""type"": ""List<Union>""}], ""outputs"": [{""name"": ""Index"", ""type"": ""int""}, {""name"": ""This"", ""type"": ""object""}], ""use_for"": [""acting on every enemy, pickup or target in a list""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Path = "Loops"), Tags("Default")] 
    public class ForEach : SchematicActionNode
    {
        [Tooltip("The list to go through.")]
        [Input("Items")]
        public List<Union> Items;
        [Output("Index")]
        public int Index;
        [Tooltip("The current item, while the nodes after it run.")]
        [Output("This")]
        public object CurrentItem;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            // The nodes after this one run once per item (below), not once more at the end.
            _processChildren = false;

            // Any list works: Find With Tag's List<Union>, Create List's List<object>, an array...
            // (This used to read a port named "Pool", which doesn't exist, and skipped the last item.)
            var items = GetInputValue<IList>("Items", Items);
            if (items == null)
                return;

            var unions = items as List<Union>;
            for (int i = 0; i < items.Count; i++)
            {
                SetOutputValue("This", unions != null ? unions[i] : PortValues.ToUnion(items[i]));
                SetOutputValue("Index", i);
                ProcessChildren(instance);
            }
        }
    }
}