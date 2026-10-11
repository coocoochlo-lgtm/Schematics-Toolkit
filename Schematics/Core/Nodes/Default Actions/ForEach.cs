// project armada

#pragma warning disable 0414

using Remedy.Framework;
using BlueGraph;
using System.Collections.Generic;
using UnityEngine;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""For Each"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Loops"", ""description"": ""Runs the nodes after it once per item of a list, with the current item and its index as outputs."", ""inputs"": [{""name"": ""Items"", ""type"": ""List<Union>""}], ""outputs"": [{""name"": ""Index"", ""type"": ""int""}, {""name"": ""This"", ""type"": ""object""}], ""use_for"": [""acting on every enemy, pickup or target in a list""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Path = "Loops"), Tags("Default")] 
    public class ForEach : SchematicActionNode
    {
        [Input("Items")]
        public List<Union> Items;
        [Output("Index")]
        public int Index;
        [Output("This")]
        public object CurrentItem;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            Items = GetInputValue<List<Union>>("Pool");
            _processChildren = false;
            
            for(int i = 0; i < Items.Count - 1; i++)
            {
                SetOutputValue(nameof(CurrentItem), Items[i]);
                SetOutputValue(nameof(Index), i);
                ProcessChildren(instance);
            }
        }
    }
}