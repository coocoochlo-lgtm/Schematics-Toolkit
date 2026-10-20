using Remedy.Framework;
using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [Output("▶", typeof(ActionPort), enableCasting: false, Multiple = true)]
    [NodeColor(NodeColors.Event)]
    public class SchematicEventNode : SchematicGraphNode
    {
        /// <summary>Set during OnTrigger to stop the nodes after this event from running this time.</summary>
        protected bool _blocked = false;

        public override object OnRequestValue(Port port)
        {
            return null;
        }

        /// <summary>Fires this event: runs it and everything it leads to, as a new flow.</summary>
        public void Trigger(GameObject instance, params Union[] arguments)
        {
            Flow.Start(this, instance, arguments);
        }

        internal override bool ExecuteInFlow(Flow flow, FlowScope scope, Union[] arguments)
        {
            // The event's arguments fill its output pins in order.
            if (arguments != null)
            {
                var outputs = DataOutputNames;
                int count = Math.Min(outputs.Count, arguments.Length);
                for (int i = 0; i < count; i++)
                    SetOutputValue(outputs[i], arguments[i]);
            }

            _blocked = false;
            OnTrigger(flow.Instance, arguments ?? Array.Empty<Union>());
            return !_blocked;
        }

        protected virtual void OnTrigger(GameObject instance, params Union[] arguments)
        { }

        /// <summary>Runs the nodes after this event now (queued right after it inside a flow, or as a new flow).</summary>
        public void ProcessChildren(GameObject instance, bool awaiting = false, bool parallel = true)
        {
            var flow = Flow.Current;
            if (flow != null && flow.IsExecuting(this))
                flow.Schedule(this, flow.ExecutingScope, fork: true);
            else
                Flow.StartChildren(this, instance);
        }
    }
}
