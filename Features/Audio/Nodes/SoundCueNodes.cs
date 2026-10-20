// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Play Sound Cue"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Plays a Sound Cue at an object (with nothing connected, the object running the graph), following it if Follow is on. Player is what's playing it, for Stop Sound Cue and Set Sound Cue Parameter. Dropping a Sound Cue into a graph makes one."", ""inputs"": [{""name"": ""Cue"", ""type"": ""SoundCueGraph""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Volume"", ""type"": ""float""}, {""name"": ""Spatial Blend"", ""type"": ""float""}], ""outputs"": [{""name"": ""Player"", ""type"": ""SoundCuePlayer""}], ""use_for"": [""designed sound effects"", ""engine and ambient loops driven by parameters""], ""notes"": ""Runs when flow reaches it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Play Sound Cue", Path = "Audio"), Tags("Default")]
    [AssetNode(typeof(SoundCueGraph), Title = "Play Sound Cue: {0}")]
    public class PlaySoundCueNode : SchematicActionNode
    {
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;
        [Input("Target")] public GameObject Target;
        [Input("Volume", Editable = true)] public float Volume = 1f;
        [Input("Spatial Blend", Editable = true)] public float SpatialBlend = 1f;
        /// <summary>Moves with the target (a car's engine) rather than staying where it started (an impact).</summary>
        [Editable] public bool Follow = true;
        [Output("Player")] public SoundCuePlayer Player;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var cue = GetInputValue("Cue", Cue);
            if (cue == null)
            {
                Debug.LogWarning($"[{Name}] No Sound Cue is assigned or connected.", instance);
                return;
            }
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            var position = target != null ? target.transform.position : Vector3.zero;
            var player = SoundCuePlayer.PlayAt(cue, position, Follow && target != null ? target.transform : null,
                GetInputValue("Volume", Volume), GetInputValue("Spatial Blend", SpatialBlend));
            SetOutputValue("Player", player);
        }
    }

    [SchematicDoc(@"{""name"": ""Stop Sound Cue"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Stops a playing Sound Cue: the Player a Play Sound Cue gave, or else every player of Cue on Target (and its children)."", ""inputs"": [{""name"": ""Player"", ""type"": ""SoundCuePlayer""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Cue"", ""type"": ""SoundCueGraph""}], ""notes"": ""Runs when flow reaches it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Stop Sound Cue", Path = "Audio"), Tags("Default")]
    public class StopSoundCueNode : SchematicActionNode
    {
        [Input("Player")] public SoundCuePlayer Player;
        [Input("Target")] public GameObject Target;
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var player = GetInputValue<SoundCuePlayer>("Player", null);
            if (player != null)
            {
                Finish(player);
                return;
            }
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            if (target == null)
                return;
            var cue = GetInputValue("Cue", Cue);
            foreach (var found in target.GetComponentsInChildren<SoundCuePlayer>())
                if (cue == null || found.Cue == cue)
                    Finish(found);
        }

        private static void Finish(SoundCuePlayer player)
        {
            player.Stop();
            if (player.DestroyWhenDone)
                UnityEngine.Object.Destroy(player.gameObject);
        }
    }

    [SchematicDoc(@"{""name"": ""Set Sound Cue Parameter"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Sets a playing Sound Cue's Parameter (by its name) to Value: the Player a Play Sound Cue gave, or every Sound Cue playing on Target (and its children)."", ""inputs"": [{""name"": ""Player"", ""type"": ""SoundCuePlayer""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Parameter"", ""type"": ""string""}, {""name"": ""Value"", ""type"": ""float""}], ""use_for"": [""engine pitch from speed"", ""music intensity"", ""wind strength""], ""notes"": ""Runs when flow reaches it. Run it every frame (On Update) for values that keep changing.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Set Sound Cue Parameter", Path = "Audio"), Tags("Default")]
    public class SetSoundCueParameterNode : SchematicActionNode
    {
        [Input("Player")] public SoundCuePlayer Player;
        [Input("Target")] public GameObject Target;
        [Input("Parameter", Editable = true)] public string Parameter = "Intensity";
        [Input("Value", Editable = true)] public float Value;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            string name = GetInputValue("Parameter", Parameter);
            float value = GetInputValue("Value", Value);
            var player = GetInputValue<SoundCuePlayer>("Player", null);
            if (player != null)
            {
                player.SetParameter(name, value);
                return;
            }
            var target = GetInputValue("Target", Target);
            if (target == null)
                target = instance;
            if (target == null)
                return;
            foreach (var found in target.GetComponentsInChildren<SoundCuePlayer>())
                found.SetParameter(name, value);
        }
    }
}
