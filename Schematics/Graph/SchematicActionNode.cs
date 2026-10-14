using BlueGraph;
using Remedy.Framework;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [Output("▶", typeof(ActionPort), enableCasting: false, Multiple = true)]
    public abstract class SchematicActionNode : SchematicActionNodeBase
    { }

    [Serializable]
    [Input("▷", typeof(ActionPort), enableCasting: false, Multiple = true)]
    public abstract class SchematicActionNodeBase : SchematicGraphNode
    {
        /// <summary>
        /// Whether the nodes after this one run once it's done. True at the start of every trigger; a node clears it to
        /// stop there (a failed condition) or to run them itself (a loop, a delay).
        /// </summary>
        protected bool _processChildren = true;

        private string _errorMessage;
        public string ErrorMessage => _errorMessage;

        public override object OnRequestValue(Port port)
        {
            return null;
        }

        /// <summary>Runs this node and everything after it, as a new flow.</summary>
        public void Trigger(GameObject instance, bool awaiting = false)
        {
            Flow.Start(this, instance);
        }

        internal override bool ExecuteInFlow(Flow flow, FlowScope scope, Union[] arguments)
        {
            _processChildren = true;
            OnTrigger(flow.Instance, false);
            return _processChildren;
        }

        protected virtual void OnTrigger(GameObject instance, bool awaiting = false)
        {
        }

        /// <summary>
        /// Runs the nodes after this one now, with this node's outputs as they are at this moment (call it once per item
        /// in a loop). Inside a flow they're queued to run right after this node; outside one they start a new flow.
        /// </summary>
        public void ProcessChildren(GameObject instance, bool awaiting = false, bool parallel = false)
        {
            var flow = Flow.Current;
            if (flow != null && flow.IsExecuting(this))
                flow.Schedule(this, flow.ExecutingScope, fork: true);
            else
                Flow.StartChildren(this, instance);
        }

        /// <summary>
        /// Runs the nodes after this one once the given number of seconds (game time) has passed, with the values they'd
        /// have seen now. Skipped if the object is destroyed in the meantime.
        /// </summary>
        protected void ProcessChildrenAfter(GameObject instance, float seconds)
        {
            _processChildren = false;
            var scope = Flow.Current?.Capture();
            RunChildrenLater(instance, seconds, scope).Forget();
        }

        private async Cysharp.Threading.Tasks.UniTaskVoid RunChildrenLater(GameObject instance, float seconds, FlowScope scope)
        {
            bool hadInstance = instance != null;
            if (seconds > 0f)
                await Cysharp.Threading.Tasks.UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: false);
            else
                await Cysharp.Threading.Tasks.UniTask.Yield();

            if (hadInstance && instance == null)
                return;
            Flow.StartChildren(this, instance, scope);
        }
    }
}
