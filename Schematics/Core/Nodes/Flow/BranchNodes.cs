// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Per-object memory for flow nodes (which pin a Random picked last, whether a Flip Flop is on A...). The graph asset
    /// is shared by every object running it, so anything a node remembers is kept per object; flows with no object
    /// share one entry.
    /// </summary>
    internal sealed class PerObject<T>
    {
        private readonly Dictionary<GameObject, T> _byObject = new();
        private T _withoutObject;
        private bool _hasWithoutObject;

        public bool TryGet(GameObject instance, out T value)
        {
            if (instance == null)
            {
                value = _withoutObject;
                return _hasWithoutObject;
            }
            return _byObject.TryGetValue(instance, out value);
        }

        public T Get(GameObject instance, T fallback = default) => TryGet(instance, out var value) ? value : fallback;

        public void Set(GameObject instance, T value)
        {
            if (instance == null)
            {
                _withoutObject = value;
                _hasWithoutObject = true;
                return;
            }
            // Destroyed objects are dropped now and then, so the table doesn't grow forever.
            if (_byObject.Count > 64 && !_byObject.ContainsKey(instance))
            {
                var dead = new List<GameObject>();
                foreach (var key in _byObject.Keys)
                    if (key == null)
                        dead.Add(key);
                foreach (var key in dead)
                    _byObject.Remove(key);
            }
            _byObject[instance] = value;
        }
    }

    [SchematicDoc(@"{""name"": ""Random"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Continues from one of its numbered outputs, picked at random each time flow reaches it. Add or remove outputs with + and − on the node. No Repeat never picks the same output twice in a row."", ""use_for"": [""picking a random song, bark or attack"", ""random events"", ""variety in idle behaviour""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from each numbered output to a different choice.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Random", Path = "Flow"), Tags("Default")]
    public class RandomBranch : DynamicFlowNode
    {
        [Editable]
        public bool NoRepeat;

        [NonSerialized] private PerObject<int> _last;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            int count = PinCount;
            if (count == 0)
                return;

            int pick = UnityEngine.Random.Range(0, count);
            _last ??= new PerObject<int>();
            if (NoRepeat && count > 1 && _last.TryGet(instance, out int last) && pick == last)
                pick = (pick + UnityEngine.Random.Range(1, count)) % count;
            _last.Set(instance, pick);

            ContinueFrom(instance, PinName(pick));
        }
    }

    [SchematicDoc(@"{""name"": ""Split"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Continues from every one of its numbered outputs, in order: everything after 1 runs, then everything after 2, and so on (an output that waits, like Wait, lets the next one start). Add or remove outputs with + and − on the node."", ""use_for"": [""doing several things in a set order"", ""tidying a node with lots of arrows out of it""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from each numbered output to what runs in that order.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Split", Path = "Flow"), Tags("Default")]
    public class Split : DynamicFlowNode
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            int count = PinCount;
            for (int i = 0; i < count; i++)
                ContinueFrom(instance, PinName(i));
        }
    }

    [SchematicDoc(@"{""name"": ""Switch"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Continues from the output numbered Value (0, 1, 2...), or from Default when there's no output with that number. Add or remove outputs with + and − on the node."", ""inputs"": [{""name"": ""Value"", ""type"": ""int""}], ""use_for"": [""acting on a state number"", ""picking a branch from a Rand Int"", ""menus and dialogue choices""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, a whole number into Value, and from each output to its branch.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Switch", Path = "Flow"), Tags("Default")]
    [Output(DefaultPort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class Switch : DynamicFlowNode
    {
        public const string DefaultPort = "Default";

        [Input("Value", Editable = true)]
        public int Value;

        public override string PinName(int index) => index.ToString();

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            int value = GetInputValue("Value", Value);
            ContinueFrom(instance, value >= 0 && value < PinCount ? PinName(value) : DefaultPort);
        }
    }

    [SchematicDoc(@"{""name"": ""Flip Flop"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Continues from A, then B the next time, then A again, and so on (for each object on its own). Is A says which one it just took."", ""outputs"": [{""name"": ""Is A"", ""type"": ""bool""}], ""use_for"": [""toggles (lights, doors)"", ""alternating left and right footsteps or attacks""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from A and B to what alternates.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Flip Flop", Path = "Flow"), Tags("Default")]
    [Output(APort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    [Output(BPort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class FlipFlop : SchematicActionNodeBase
    {
        public const string APort = "A";
        public const string BPort = "B";

        [Output("Is A")]
        public bool IsA;

        [NonSerialized] private PerObject<bool> _onB;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            _onB ??= new PerObject<bool>();
            bool isA = !_onB.Get(instance);
            _onB.Set(instance, isA);
            SetOutputValue("Is A", isA);
            ContinueFrom(instance, isA ? APort : BPort);
        }
    }

    [SchematicDoc(@"{""name"": ""Cooldown"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Lets the flow through at most once every Seconds (game time, for each object on its own). While it's cooling down, flow goes to Blocked instead."", ""inputs"": [{""name"": ""Seconds"", ""type"": ""float""}], ""outputs"": [{""name"": ""Remaining"", ""type"": ""float""}], ""use_for"": [""attack and ability cooldowns"", ""stopping a sound or effect spamming"", ""rate limiting""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, from ▶ to what's rate limited, and from Blocked to what happens instead (if anything).""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Cooldown", Path = "Flow"), Tags("Default")]
    [Output(BlockedPort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class Cooldown : SchematicActionNode
    {
        public const string BlockedPort = "Blocked";

        [Input("Seconds", Editable = true)]
        public float Seconds = 1f;

        [Output("Remaining")]
        public float Remaining;

        [NonSerialized] private PerObject<float> _readyAt;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _readyAt ??= new PerObject<float>();
            float seconds = Mathf.Max(0f, GetInputValue("Seconds", Seconds));
            float now = Time.time;

            // Still cooling down. (A ready time further off than Seconds is from an earlier play session: ignored.)
            if (_readyAt.TryGet(instance, out float readyAt) && now < readyAt && readyAt - now <= seconds)
            {
                SetOutputValue("Remaining", readyAt - now);
                _processChildren = false;
                ContinueFrom(instance, BlockedPort);
                return;
            }

            _readyAt.Set(instance, now + seconds);
            SetOutputValue("Remaining", 0f);
        }
    }

    [SchematicDoc(@"{""name"": ""For Loop"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Loops"", ""description"": ""Runs everything after Body Count times, with Index going 0, 1, 2... Then continues from Done."", ""inputs"": [{""name"": ""Count"", ""type"": ""int""}], ""outputs"": [{""name"": ""Index"", ""type"": ""int""}], ""use_for"": [""spawning several things"", ""repeating an action a set number of times""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, from Body to what repeats, and from Done to what runs after.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "For Loop", Path = "Loops"), Tags("Default")]
    [Output(BodyPort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    [Output(DonePort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class ForLoop : SchematicActionNodeBase
    {
        public const string BodyPort = "Body";
        public const string DonePort = "Done";

        [Input("Count", Editable = true)]
        public int Count = 3;

        [Output("Index")]
        public int Index;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            int count = GetInputValue("Count", Count);
            for (int i = 0; i < count; i++)
            {
                SetOutputValue("Index", i);
                ContinueFrom(instance, BodyPort);
            }
            ContinueFrom(instance, DonePort);
        }
    }
}
