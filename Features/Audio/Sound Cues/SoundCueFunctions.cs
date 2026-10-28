// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Audio
{
    // Sound Cues as functions: a Function node builds another cue's nodes into this one (its Parameters become the
    // node's pins, each with its own control), Sound Inputs take the sound it's given, and its Output is what it gives
    // back. A radio-voice chain, an engine layer, a footstep's surface mix: made once, used in any cue.

    /// <summary>
    /// Sound this cue takes when another cue calls it as a function (a Function node's pin of the same name). Played on
    /// its own, the cue has nothing given, so Preview stands in: wire in something to listen to while making it.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Sound Input"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Functions"", ""description"": ""Sound this cue takes when another Sound Cue calls it as a function: the Function node calling it gets a pin named Name, and what's wired there comes out here, sample by sample (sound, or any control: a curve, a parameter). When the cue plays on its own, what's wired into Preview comes out instead, so there's something to hear while making it."", ""inputs"": [{""name"": ""Preview"", ""type"": ""float""}], ""outputs"": [{""name"": ""Audio"", ""type"": ""float""}], ""use_for"": [""an effect chain used in many cues (a radio voice, a muffled-through-a-wall pass)"", ""a function that layers something over the sound it's given""]}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Sound Input", Path = "Functions"), Tags(SoundNode.Tag)]
    public sealed class SoundInputNode : SoundNode
    {
        [Editable("Name")] public string InputName = "Audio";
        [Tooltip("What it should be given, for whoever (or whatever AI) calls the cue.")]
        [TextArea(1, 3)]
        public string Description = "";
        [Input("Preview")] public float Preview;
        [Output("Audio")] public float Audio;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            // (Its output is the given block itself: filled by the processor that makes it, which runs first.)
            var passed = new StaticProcessor();
            if (builder.CallInput(InputName, out var given))
                passed.Outputs["Audio"] = given;
            else if (builder.InCall)
                passed.Outputs["Audio"] = builder.Constant(0f);
            else
                passed.Outputs["Audio"] = builder.Input(this, "Preview", 0f);
            return passed;
        }
    }

    /// <summary>
    /// Calls another Sound Cue as a function: its nodes play as part of this cue (its Sound Inputs and Parameters are
    /// this node's pins, each Parameter with the control it was given), and what reaches its Output comes out here.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Function"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Functions"", ""description"": ""Calls another Sound Cue (Cue) as a function: its nodes become part of this cue's sound, on this cue's time, and what reaches its Output (at its Volume) comes out of Audio. Its Sound Inputs and Parameters become pins here: wire sound and controls in, or set the Parameters right on the node with the controls they were given (sliders, toggles, dropdowns). Cues marked In Library are in the node search under Functions. Double-click to open the cue it calls."", ""outputs"": [{""name"": ""Audio"", ""type"": ""float""}], ""use_for"": [""reusing an effect chain or a layer across cues"", ""a library of sound building blocks with sliders for their character"", ""splitting a big cue into named parts""], ""notes"": ""Unlike the Sound Cue node, which plays another cue as its own sound (with its own length, delay and looping), a Function is part of this cue: it can change the sound it's given. The called cue's Output settings (length, falloff, rendering) don't apply. A cue that ends up calling itself is silent (with a warning).""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Function", Path = "Functions"), Tags(SoundNode.Tag)]
    public sealed class SoundCueCallNode : SoundNode, IDynamicPorts
    {
        public const string AudioPort = "Audio";
        private const int MaxDepth = 16;

        [Tooltip("The Sound Cue to call (its nodes become part of this one). Double-click the title to open it.")]
        [Editable("Cue")] public SoundCueGraph Calls;
        [SerializeField, HideInInspector] private List<FunctionArgument> _arguments = new();
        [Output(AudioPort)] public float Audio;

        /// <summary>The values set on the node for the cue's Parameters (by name).</summary>
        public List<FunctionArgument> Arguments => _arguments ??= new();

        /// <summary>The pin for one of the cue's Parameters or Sound Inputs (named after it, unless that's Audio).</summary>
        public static string PinFor(string name) => string.Equals(name, AudioPort, StringComparison.OrdinalIgnoreCase) ? name + " In" : name;

        /// <summary>The cue's Parameter behind a pin, if it is one.</summary>
        public bool ParameterFor(string pin, out GraphParameter parameter)
        {
            parameter = default;
            if (Calls == null)
                return false;
            foreach (var p in Calls.GraphParameters)
                if (PinFor(p.Name) == pin)
                {
                    parameter = p;
                    return true;
                }
            return false;
        }

        /// <summary>The value a Parameter has on this node when nothing's wired into its pin.</summary>
        public float ValueOf(GraphParameter parameter) => FunctionArguments.Get(Arguments, parameter);

        public void SetValue(GraphParameter parameter, float value) => FunctionArguments.Set(Arguments, parameter, value);

        /// <summary>A pin per Sound Input of the cue, then one per Parameter, in the cue's order.</summary>
        public bool SyncPorts()
        {
            var wanted = new List<(string, Type)>();
            if (Calls != null)
            {
                foreach (var input in Calls.FunctionInputs)
                    wanted.Add((PinFor(input.Name), typeof(float)));
                foreach (var parameter in Calls.GraphParameters)
                    wanted.Add((PinFor(parameter.Name), typeof(float)));
            }
            return DynamicPorts.SyncInputs(this, wanted, port => port.Direction == PortDirection.Input);
        }

        public override void OnValidate()
        {
            base.OnValidate();
            if (Calls != null && (string.IsNullOrEmpty(Name) || Name == "Function"))
                Name = Calls.name;
            if (SyncPorts())
                DynamicPorts.Notify(this);
            Warning = Calls == null ? "No Sound Cue set: this is silent."
                : Calls == Graph ? "It calls the cue it's in: that never ends, so it's silent."
                : null;
        }

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            var silent = new StaticProcessor();
            silent.Outputs[AudioPort] = builder.Constant(0f);
            if (Calls == null)
            {
                builder.Warnings.Add($"\"{Name}\" has no Sound Cue to call.");
                return silent;
            }
            if (Calls == Graph || builder.IsCalling(Calls) || builder.CallDepth >= MaxDepth)
            {
                builder.Warnings.Add($"\"{Name}\" calls \"{Calls.name}\", which is (or calls) the cue it's in: it would never end, so it's silent.");
                return silent;
            }
            var output = Calls.Output;
            if (output == null)
            {
                builder.Warnings.Add($"\"{Calls.name}\" (called by \"{Name}\") has no Output, so it's silent.");
                return silent;
            }

            // What's given, built in this cue first: wired pins, else (Parameters) the values set on the node.
            var inputs = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in Calls.FunctionInputs)
                inputs[input.Name] = builder.Input(this, PinFor(input.Name), 0f);
            var parameters = new Dictionary<string, float[]>();
            foreach (var parameter in Calls.GraphParameters)
            {
                string pin = PinFor(parameter.Name);
                parameters[parameter.Name] = builder.IsConnected(this, pin) ? builder.Input(this, pin, 0f) : builder.Constant(ValueOf(parameter));
            }

            var (audio, volume) = builder.BuildCall(Calls, inputs, parameters, () =>
                (builder.Input(output, SoundCueOutput.AudioPort, 0f), builder.Input(output, SoundCueOutput.VolumePort, output.Volume)));
            return new Processor { Audio = audio, Volume = volume };
        }

        // The called cue's sound at its Output's Volume.
        private sealed class Processor : SoundProcessor
        {
            public float[] Audio, Volume;

            public override void Process(SoundContext context, int count)
            {
                var output = Output(AudioPort);
                for (int i = 0; i < count; i++)
                    output[i] = Audio[i] * Volume[i];
            }
        }
    }
}
