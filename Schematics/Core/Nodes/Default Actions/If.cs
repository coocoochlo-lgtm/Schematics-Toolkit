// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""If"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Conditions"", ""description"": ""Continues from ▶ when Condition is true (or false, with Not), and from Else otherwise. Trigger Once lets it pass only the first time."", ""inputs"": [{""name"": ""Condition"", ""type"": ""bool""}], ""use_for"": [""branching on a condition (▶ for true, Else for false)"", ""one-time events (with Trigger Once)"", ""only acting when something holds""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Connect a bool (Comparison, Is Null, Get Variable) into Condition.""}")]
    [Serializable]
    [Node(Path = "Conditions"), Tags("Default")] 
    public class If : SchematicActionNode
    {
        /// <summary>The flow output that runs when the condition fails.</summary>
        public const string ElsePort = "Else";

        [Input("Condition")]
        public bool Condition;
        [Editable]
        public bool Not;
        [Editable]
        public bool TriggerOnce;

        // Trigger Once is per object running the graph (the graph asset is shared by all of them).
        [NonSerialized] private HashSet<GameObject> _triggered;
        [NonSerialized] private bool _triggeredWithoutObject;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            // A connected Condition (Comparison, Is Null, ...) is evaluated now; unconnected, the checkbox on the node.
            bool pass = GetInputValue("Condition", Condition) != Not;
            if (!pass)
            {
                _processChildren = false;
                ContinueFrom(instance, ElsePort);
                return;
            }

            // Trigger Once: only the first time the condition passes.
            if (TriggerOnce && pass)
            {
                if (instance == null)
                {
                    pass = !_triggeredWithoutObject;
                    _triggeredWithoutObject = true;
                }
                else
                {
                    _triggered ??= new HashSet<GameObject>();
                    pass = _triggered.Add(instance);
                }
            }

            _processChildren = pass;
        }

        public override void OnValidate()
        {
            // Ifs saved before Else existed don't have it.
            EnsureDataPort(ElsePort, typeof(ActionPort), PortDirection.Output, PortCapacity.Multiple);
            base.OnValidate();
        }
    }
}