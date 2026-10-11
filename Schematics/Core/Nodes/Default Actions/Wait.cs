using BlueGraph;
using Cysharp.Threading.Tasks;
using Remedy.Framework;
using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Wait"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Time"", ""description"": ""Pauses the flow for Wait Time seconds, then continues with the nodes after it."", ""inputs"": [{""name"": ""Wait Time"", ""type"": ""float""}], ""use_for"": [""delays"", ""cooldowns"", ""timed sequences"", ""respawn timers""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [Node(Path = "Time"), Tags("Default")]
    public class Wait : SchematicActionNode
    {
        [Input("Wait Time", typeof(float))]
        public float waitTime = 1f;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            // The nodes after this one run once the time is up, seeing the values they'd have seen now.
            ProcessChildrenAfter(instance, GetInputValue("Wait Time", waitTime));
        }
    }
}