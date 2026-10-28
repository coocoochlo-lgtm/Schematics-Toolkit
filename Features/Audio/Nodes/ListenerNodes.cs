using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Set Listener Chain"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Runs everything the player hears through a Sound Cue (one that takes the mix in through a Listener Input node), on the Audio Listener. None: the mix is left as it is. Mix is how much of it is the cue's (0 untouched, 1 only the cue's). Chain is the running chain: set its cue's Parameters with Set Graph Parameters (keep it in a Graph Variable)."", ""inputs"": [{""name"": ""Cue"", ""type"": ""SoundCueGraph""}, {""name"": ""Mix"", ""type"": ""float""}], ""outputs"": [{""name"": ""Chain"", ""type"": ""SoundListenerChain""}], ""use_for"": [""underwater, helmet and low-health effects on the whole mix"", ""ringing ears after an explosion""], ""notes"": ""Runs when flow reaches it. Fade effects in and out with Set Graph Parameters on Chain.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Set Listener Chain", Path = "Audio"), Tags("Default")]
    public class SetListenerChainNode : SchematicActionNode, IGraphSource
    {
        [Tooltip("The Sound Cue to run everything the player hears through (it takes the mix in with a Listener Input node). None: no chain.")]
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;
        [Input("Mix", Editable = true)] public float Mix = 1f;
        [Tooltip("The running chain: wire it into Set Graph Parameters to change the cue's Parameters live.")]
        [Output("Chain")] public SoundListenerChain Chain;

        /// <summary>Chain runs its cue: Set Graph Parameters wired from it gets the cue's Parameters.</summary>
        public SchematicGraph GraphBehind(string outputPort) => outputPort == "Chain" ? Cue : null;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var chain = SoundListenerChain.Ensure();
            if (chain == null)
            {
                Debug.LogWarning($"[{Name}] There's no Audio Listener in the scene to run the chain on.", instance);
                return;
            }
            chain.Cue = GetInputValue("Cue", Cue);
            chain.Mix = Mathf.Clamp01(GetInputValue("Mix", Mix));
            SetOutputValue("Chain", chain);
        }
    }
}
