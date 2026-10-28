// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Play Sound Cue"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Plays a Sound Cue at an object (with nothing connected, the object running the graph), following it if Follow is on. Player is what's playing it, for Stop Sound Cue and Set Graph Parameters. The cue's Parameters appear as pins: what's wired in is set as it starts (unwired: the cue's default); they are read once, as it starts: change them while it plays with Set Graph Parameters on Player (which a Graph Variable can hold). Object is the playing cue's own object: send a Signal To it to fire the cue's Triggers. Dropping a Sound Cue into a graph makes one."", ""inputs"": [{""name"": ""Cue"", ""type"": ""SoundCueGraph""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Volume"", ""type"": ""float""}, {""name"": ""Spatial Blend"", ""type"": ""float""}], ""outputs"": [{""name"": ""Player"", ""type"": ""SoundCuePlayer""}, {""name"": ""Object"", ""type"": ""GameObject""}], ""use_for"": [""designed sound effects"", ""engine and ambient loops driven by parameters""], ""notes"": ""Runs when flow reaches it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Play Sound Cue", Path = "Audio"), Tags("Default")]
    [AssetNode(typeof(SoundCueGraph), Title = "Play Sound Cue: {0}")]
    public class PlaySoundCueNode : SchematicActionNode, IDynamicPorts, IGraphSource
    {
        [Tooltip("The Sound Cue to play (drop one on the graph to make this node).")]
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;
        [Input("Target")] public GameObject Target;
        [Input("Volume", Editable = true)] public float Volume = 1f;
        [Tooltip("How 3D it sounds: 1 placed in the world (panned and fading with distance), 0 flat and centred like UI sound.")]
        [Input("Spatial Blend", Editable = true)] public float SpatialBlend = 1f;
        /// <summary>Moves with the target (a car's engine) rather than staying where it started (an impact).</summary>
        [Tooltip("Follow the object as it moves. Off: plays where it started.")]
        [Editable] public bool Follow = true;
        [Tooltip("What's playing it: give it to Stop Sound Cue or Set Graph Parameters.")]
        [Output("Player")] public SoundCuePlayer Player;
        [Output("Object")] public GameObject PlayingObject;

        private static readonly string[] FixedInputs = { "Cue", "Target", "Volume", "Spatial Blend" };

        /// <summary>Player (and Object) run its cue: Set Graph Parameters wired from them gets the cue's Parameters.</summary>
        public SchematicGraph GraphBehind(string outputPort) => outputPort == "Player" || outputPort == "Object" ? Cue : null;

        /// <summary>The pin for one of the cue's Parameters (named after it, unless that's one of the node's own).</summary>
        public static string ParameterPort(string parameter, string[] taken)
            => Array.IndexOf(taken, parameter) >= 0 ? parameter + " (Parameter)" : parameter;

        /// <summary>One pin per Parameter of the cue: what's wired in is set as it starts (unwired: the cue's default).</summary>
        public bool SyncPorts()
        {
            var parameters = Cue != null ? Cue.ParameterList : new List<(string name, float value)>();
            return DynamicPorts.SyncInputs(this, parameters.Select(p => (ParameterPort(p.name, FixedInputs), typeof(float))),
                port => port.Type == typeof(float) && Array.IndexOf(FixedInputs, port.Name) < 0);
        }

        public override void OnValidate()
        {
            base.OnValidate();
            if (SyncPorts())
                DynamicPorts.Notify(this);
        }

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
            // The cue's Parameters wired in here, set before it makes a sound.
            List<KeyValuePair<string, float>> parameters = null;
            foreach (var (name, _) in cue.ParameterList)
            {
                string port = ParameterPort(name, FixedInputs);
                if (GetPort(port) != null && HasInputConnections(port))
                    (parameters ??= new()).Add(new KeyValuePair<string, float>(name, GetInputValue(port, 0f)));
            }
            var player = SoundCuePlayer.PlayAt(cue, position, Follow && target != null ? target.transform : null,
                GetInputValue("Volume", Volume), GetInputValue("Spatial Blend", SpatialBlend), parameters);
            SetOutputValue("Player", player);
            SetOutputValue("Object", player != null ? player.gameObject : null);
        }
    }

    [SchematicDoc(@"{""name"": ""Stop Sound Cue"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Stops a playing Sound Cue: the Player a Play Sound Cue gave, or else every player of Cue on Target (and its children)."", ""inputs"": [{""name"": ""Player"", ""type"": ""SoundCuePlayer""}, {""name"": ""Target"", ""type"": ""GameObject""}, {""name"": ""Cue"", ""type"": ""SoundCueGraph""}], ""notes"": ""Runs when flow reaches it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Stop Sound Cue", Path = "Audio"), Tags("Default")]
    public class StopSoundCueNode : SchematicActionNode
    {
        [Tooltip("The player to stop (from Play Sound Cue). Empty: stops every player of Cue on Target.")]
        [Input("Player")] public SoundCuePlayer Player;
        [Input("Target")] public GameObject Target;
        [Tooltip("Stop every player of this cue on Target (when no Player is wired).")]
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
            foreach (var found in SoundCuePlayer.On(target))
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

    [SchematicDoc(@"{""name"": ""Preload Sound Cue"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Audio"", ""description"": ""Gets a Sound Cue ready so its first plays don't hitch: reads its audio files, makes its Generators' sounds and builds Voices players ahead of time. Run it while a level loads (On Start, or behind a loading screen) for cues played often or right away."", ""inputs"": [{""name"": ""Cue"", ""type"": ""SoundCueGraph""}, {""name"": ""Voices"", ""type"": ""int""}], ""notes"": ""Runs when flow reaches it. Sound Cue Players placed in a scene preload their own cue.""}")]
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    [Node(Name = "Preload Sound Cue", Path = "Audio"), Tags("Default")]
    public class PreloadSoundCueNode : SchematicActionNode
    {
        [Tooltip("The Sound Cue to get ready.")]
        [Input("Cue", Editable = true)] public SoundCueGraph Cue;
        /// <summary>How many plays at once to get ready for (a rapid-fire weapon might want 4 or more).</summary>
        [Tooltip("How many copies to have ready to play at once (for sounds played often, like footsteps or gunshots).")]
        [Input("Voices", Editable = true)] public int Voices = 2;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var cue = GetInputValue("Cue", Cue);
            if (cue != null)
                cue.Preload(Mathf.Clamp(GetInputValue("Voices", Voices), 0, SoundCueVoicePool.MaxIdlePerCue));
        }
    }
}
