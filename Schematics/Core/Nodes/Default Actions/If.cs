// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System;

namespace Remedy.Schematics
{
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