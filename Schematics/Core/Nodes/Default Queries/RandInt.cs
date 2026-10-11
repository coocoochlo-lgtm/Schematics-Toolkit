// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Rand Int"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Random"", ""description"": ""A random whole number from Lower Bound up to (not including) Upper Bound."", ""inputs"": [{""name"": ""LowerBound"", ""type"": ""int""}, {""name"": ""UpperBound"", ""type"": ""int""}], ""outputs"": [{""name"": ""Value"", ""type"": ""int""}], ""use_for"": [""random choices"", ""loot rolls"", ""random spawn points""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Random"), Tags("Default")]
    public class RandInt : SchematicGraphNode
    {
        [Input("LowerBound", Editable = true)]
        public int LowerBound;
        [Input("UpperBound", Editable = true)]
        public int UpperBound;

        [Output("Value")]
        public int Value;

        public override object OnRequestValue(Port port)
        {
            try
            {
                LowerBound = GetOutputValue<int>("LowerBound");
            }
            catch
            {
                LowerBound = 0;
            }
            try
            {
                UpperBound = GetOutputValue<int>("UpperBound");
            }
            catch 
            {
                UpperBound = 1;
            }

            return UnityEngine.Random.Range(LowerBound, UpperBound);
        }
    }
}