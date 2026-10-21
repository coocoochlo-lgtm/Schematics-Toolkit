using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Set Listener Chain"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Runs everything the player hears through a Sound Cue (one that takes the mix in through a Listener Input node), on the Audio Listener. None: the mix is left as it is. Mix is how much of it is the cue's (0 untouched, 1 only the cue's)."", ""inputs"": [{""name"": ""Cue"", ""type"": ""SoundCueGraph""}, {""name"": ""Mix"", ""type"": ""float""}], ""use_for"": [""underwater, helmet and low-health effects on the whole mix"", ""ringing ears after an explosion""], ""notes"": ""Runs when flow reaches it. Fade effects in and out with Set Listener Parameter.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Set Listener Chain", Path = "Audio"), Tags("Default")]
    public class SetListenerChainNode : SchematicActionNode
    {
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;
        [Input("Mix", Editable = true)] public float Mix = 1f;

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
        }
    }

    [SchematicDoc(@"{""name"": ""Set Listener Parameter"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Sets a Parameter of the Listener Chain's cue (by name), e.g. Underwater to 1, or Ringing to how close the explosion was. It's kept, so it still holds when the chain's cue changes."", ""inputs"": [{""name"": ""Name"", ""type"": ""string""}, {""name"": ""Value"", ""type"": ""float""}], ""use_for"": [""fading listener effects in and out from gameplay""], ""notes"": ""Runs when flow reaches it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Set Listener Parameter", Path = "Audio"), Tags("Default")]
    public class SetListenerParameterNode : SchematicActionNode
    {
        [Input("Name", Editable = true)] public string ParameterName = "Amount";
        [Input("Value", Editable = true)] public float Value = 1f;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
            => SoundListenerChain.SetParameter(GetInputValue("Name", ParameterName), GetInputValue("Value", Value));
    }
}
