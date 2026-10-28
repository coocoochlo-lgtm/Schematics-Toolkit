// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Rand Int"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Random"", ""description"": ""A random whole number from Lower Bound up to (not including) Upper Bound; tick Include Upper to allow Upper Bound too."", ""inputs"": [{""name"": ""LowerBound"", ""type"": ""int""}, {""name"": ""UpperBound"", ""type"": ""int""}], ""outputs"": [{""name"": ""Value"", ""type"": ""int""}], ""use_for"": [""random choices"", ""loot rolls"", ""random spawn points""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Random"), Tags("Default")]
    public class RandInt : SchematicGraphNode
    {
        [Tooltip("The lowest number it can pick.")]
        [Input("LowerBound", Editable = true)]
        public int LowerBound;
        [Tooltip("The number it picks below (unless Include Upper is on).")]
        [Input("UpperBound", Editable = true)]
        public int UpperBound;

        /// <summary>Upper Bound can come up too (0 and 1 both, for Lower 0 and Upper 1).</summary>
        [Tooltip("Allow Upper Bound itself to be picked too.")]
        [Editable]
        public bool IncludeUpper;

        [Output("Value")]
        public int Value;

        public override object OnRequestValue(Port port)
        {
            // A new number every flow step (several inputs reading it in the same step see the same roll).
            int lower = GetInputValue("LowerBound", LowerBound);
            int upper = GetInputValue("UpperBound", UpperBound);
            if (IncludeUpper && upper >= lower && upper < int.MaxValue)
                upper++;
            return UnityEngine.Random.Range(lower, upper);
        }
    }
}