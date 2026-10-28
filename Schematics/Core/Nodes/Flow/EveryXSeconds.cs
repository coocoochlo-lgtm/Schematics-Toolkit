// project armada

#pragma warning disable 0414

using BlueGraph;
using Cysharp.Threading.Tasks;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Every X Seconds"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Flow"", ""description"": ""Starts a repeating timer: continues from Tick every Seconds (game time), Times times (0 = forever), then from Done. Count is how many ticks so far. Reaching the node again restarts its timer (for that object); it stops when the object is destroyed."", ""inputs"": [{""name"": ""Seconds"", ""type"": ""float""}, {""name"": ""Times"", ""type"": ""int""}], ""outputs"": [{""name"": ""Count"", ""type"": ""int""}], ""use_for"": [""spawning waves"", ""damage over time"", ""periodic checks"", ""blinking lights""], ""notes"": ""Runs when flow reaches it (On Start, usually): connect an arrow into it, from Tick to what repeats, and from Done to what runs once it's finished.""}")]
    [Serializable]
    [NodeColor(NodeColors.Flow)]
    [Node(Name = "Every X Seconds", Path = "Flow"), Tags("Default")]
    [Output(TickPort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    [Output(DonePort, typeof(ActionPort), enableCasting: false, Multiple = true)]
    public class EveryXSeconds : SchematicActionNodeBase
    {
        public const string TickPort = "Tick";
        public const string DonePort = "Done";

        [Tooltip("How long between ticks (seconds of game time).")]
        [Input("Seconds", Editable = true)]
        public float Seconds = 1f;
        [Tooltip("How many ticks before it's done (0: forever).")]
        [Input("Times", Editable = true)]
        public int Times;
        /// <summary>Tick as soon as the timer starts, rather than after the first wait.</summary>
        [Tooltip("Tick as soon as the timer starts, rather than after the first wait.")]
        [Editable]
        public bool TickRightAway;

        [Output("Count")]
        public int Count;

        // Which run of the timer is current, per object: starting it again bumps it, and older runs see that and stop.
        [NonSerialized] private PerObject<int> _runs;

        // Bumped each play session, so a timer left over from the last one (with domain reload off) stops.
        private static int _session;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void NewSession() => _session++;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            _processChildren = false;
            _runs ??= new PerObject<int>();
            int run = _runs.Get(instance) + 1;
            _runs.Set(instance, run);

            float seconds = Mathf.Max(0.01f, GetInputValue("Seconds", Seconds));
            int times = GetInputValue("Times", Times);
            Repeat(instance, run, seconds, times, Flow.Current?.Capture()).Forget();
        }

        private async UniTaskVoid Repeat(GameObject instance, int run, float seconds, int times, FlowScope scope)
        {
            bool hadInstance = instance != null;
            int session = _session;
            bool Stopped() => session != _session || (hadInstance && instance == null) || _runs.Get(instance) != run;

            for (int count = 1; times <= 0 || count <= times; count++)
            {
                if (count > 1 || !TickRightAway)
                    await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: false);
                if (Stopped())
                    return;

                var tick = new FlowScope { Parent = scope };
                tick.Set(this, "Count", count);
                ContinueFrom(instance, TickPort, tick);
            }

            if (!Stopped())
                ContinueFrom(instance, DonePort, scope);
        }
    }
}
