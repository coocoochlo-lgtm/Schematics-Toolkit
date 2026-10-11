// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""If"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Conditions"", ""description"": ""Lets the flow continue only when Condition is true (or false, with Not). Trigger Once lets it pass only the first time."", ""inputs"": [{""name"": ""Condition"", ""type"": ""bool""}], ""use_for"": [""branching on a condition"", ""one-time events (with Trigger Once)"", ""only acting when something holds""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Connect a bool (Comparison, Is Null, Get Variable) into Condition.""}")]
    [Serializable]
    [Node(Path = "Conditions"), Tags("Default")] 
    public class If : SchematicActionNode
    {
        [Input("Condition")]
        public bool Condition;
        [Editable]
        public bool Not;
        [Editable]
        public bool TriggerOnce;
        private bool _triggered = false;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            bool pass = GetInputValue<bool>(nameof(Condition)) != Not;

            // Trigger Once: only the first time the condition passes.
            if (TriggerOnce && pass)
            {
                if (_triggered)
                    pass = false;
                else
                    _triggered = true;
            }

            _processChildren = pass;
        }
    }
}