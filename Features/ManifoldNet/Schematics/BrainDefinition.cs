using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    public enum BrainSharing
    {
        /// <summary>Every instance of the Schematic Object uses one brain (and they learn together).</summary>
        Shared,
        /// <summary>Each instance gets its own brain, starting from the same weights.</summary>
        PerInstance,
    }

    public enum BrainOutputKind
    {
        /// <summary>A value from -1 to 1 (move, look).</summary>
        Axis,
        /// <summary>Pressed or not, with hysteresis so it doesn't flicker.</summary>
        Button,
        /// <summary>One of several options (weapon slot, emote): the strongest wins.</summary>
        Choice,
    }

    /// <summary>Something the brain senses: one value, or several (a ray fan), scaled from Min-Max to -1..1.</summary>
    [Serializable]
    public class BrainInputSlot
    {
        public string Name = "Input";
        [Min(1)] public int Size = 1;
        public float Min = -1f;
        public float Max = 1f;

        public float Normalize(float value)
        {
            float range = Max - Min;
            float t = Mathf.Abs(range) < 1e-6f ? 0f : (value - Min) / range * 2f - 1f;
            return Mathf.Clamp(t, -1.5f, 1.5f);
        }
    }

    /// <summary>Something the brain decides: an axis, a button, or a choice between options.</summary>
    [Serializable]
    public class BrainOutputSlot
    {
        public string Name = "Output";
        public BrainOutputKind Kind = BrainOutputKind.Axis;
        [Tooltip("The options of a Choice (one network output each).")]
        public string[] Options = { "A", "B" };
        [Tooltip("The color of this output's region in the brain's value spaces.")]
        public Color Color = new(1f, 0.55f, 0.75f);

        /// <summary>How many network outputs it takes.</summary>
        public int Width => Kind == BrainOutputKind.Choice ? Mathf.Max(1, Options?.Length ?? 1) : 1;
    }

    /// <summary>
    /// A Brain's design, shared by every Schematic Object that uses it: what it senses and decides (its slots), whether
    /// instances share one brain, how it learns, and the weights it has learned so far.
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy/Manifold Network/Brain Definition", fileName = "Brain")]
    public sealed class BrainDefinition : ScriptableObject
    {
        public List<BrainInputSlot> Inputs = new() { new BrainInputSlot { Name = "Input" } };
        public List<BrainOutputSlot> Outputs = new() { new BrainOutputSlot { Name = "Output" } };

        public BrainSharing Sharing = BrainSharing.Shared;

        [Tooltip("Thinks per second. Teaching and rewards line up with these ticks.")]
        [Range(1f, 120f)] public float TickRate = 20f;

        [Header("Learning")]
        public bool Learning = true;
        [Tooltip("How much Teach Output answers count.")]
        [Range(0f, 1f)] public float TeachingWeight = 1f;
        [Tooltip("How much rewards count.")]
        [Range(0f, 1f)] public float RewardWeight = 1f;
        [Tooltip("How far back a reward can credit decisions, in seconds.")]
        [Range(0.25f, 60f)] public float HistorySeconds = 5f;
        [Tooltip("How fast credit fades into the past: after this many seconds, a decision gets half the credit.")]
        [Range(0.05f, 30f)] public float CreditHalfLife = 1f;
        [Tooltip("A little randomness in its decisions while learning, so it has new things to be praised for.")]
        [Range(0f, 1f)] public float Exploration = 0.1f;
        [Tooltip("Lessons replayed per second from credited history.")]
        [Range(0f, 20f)] public float PracticePerSecond = 2f;
        [Tooltip("Shared brains: each bot also tries a slightly different version of the brain, scored by its own rewards (learns together as a crowd).")]
        public bool PopulationRewards = false;

        [Header("Network")]
        [Tooltip("Run on the CPU even where compute shaders exist (often faster for one small brain).")]
        public bool PreferCpu;
        public MnnSettings Network = new() { hiddenWidth = 16, headCount = 8 };

        [Header("Learned")]
        [Tooltip("The weights learned so far. Empty: starts fresh from the seed.")]
        public MnnBrain SavedBrain;
        [Tooltip("In the editor, keep what was learned when Play Mode ends.")]
        public bool SaveOnExitPlayMode = true;

        public int InputWidth
        {
            get { int n = 0; foreach (var slot in Inputs) n += Mathf.Max(1, slot.Size); return Mathf.Max(1, n); }
        }

        public int OutputWidth
        {
            get { int n = 0; foreach (var slot in Outputs) n += slot.Width; return Mathf.Max(1, n); }
        }

        /// <summary>The network's settings with its input and output counts taken from the slots.</summary>
        public MnnSettings BuildSettings()
        {
            var settings = Network.Clone();
            settings.inputCount = InputWidth;
            settings.outputCount = Mathf.Min(MnnSettings.MaxOutputs, OutputWidth);
            settings.trainingMode = Sharing == BrainSharing.Shared && PopulationRewards ? TrainingMode.Population : TrainingMode.Supervised;
            // Heads move in real time against the brain's own tick (blueprint: "Fixed brain tick").
            settings.tickRate = TickRate;
            settings.Clamp();
            return settings;
        }

        /// <summary>True if the saved weights still fit the slots and network settings.</summary>
        public bool SavedBrainFits => SavedBrain != null && SavedBrain.Fits(BuildSettings());

        /// <summary>Where each input slot starts in the network's inputs.</summary>
        public int InputOffset(int slot)
        {
            int offset = 0;
            for (int i = 0; i < slot && i < Inputs.Count; i++) offset += Mathf.Max(1, Inputs[i].Size);
            return offset;
        }

        public int OutputOffset(int slot)
        {
            int offset = 0;
            for (int i = 0; i < slot && i < Outputs.Count; i++) offset += Outputs[i].Width;
            return offset;
        }

        public int FindInput(string name) => Inputs.FindIndex(s => s.Name == name);
        public int FindOutput(string name) => Outputs.FindIndex(s => s.Name == name);

        /// <summary>The color of one network output (a Choice's options get shades of its color).</summary>
        public Color OutputColor(int networkOutput)
        {
            int offset = 0;
            foreach (var slot in Outputs)
            {
                if (networkOutput < offset + slot.Width)
                {
                    int option = networkOutput - offset;
                    if (slot.Width == 1) return slot.Color;
                    Color.RGBToHSV(slot.Color, out float h, out float s, out float v);
                    return Color.HSVToRGB((h + option * 0.11f) % 1f, s, Mathf.Lerp(v, 0.6f, option / (float)slot.Width));
                }
                offset += slot.Width;
            }
            return Color.gray;
        }

        /// <summary>Labels for the debug window.</summary>
        public string[] InputLabels()
        {
            var labels = new List<string>();
            foreach (var slot in Inputs)
                for (int i = 0; i < Mathf.Max(1, slot.Size); i++)
                    labels.Add(slot.Size > 1 ? $"{slot.Name}[{i}]" : slot.Name);
            return labels.ToArray();
        }

        public string[] OutputLabels()
        {
            var labels = new List<string>();
            foreach (var slot in Outputs)
                if (slot.Kind == BrainOutputKind.Choice)
                    foreach (var option in slot.Options) labels.Add($"{slot.Name}: {option}");
                else
                    labels.Add(slot.Name);
            return labels.ToArray();
        }

        private void OnValidate()
        {
            Network ??= new MnnSettings();
            Network.Clamp();
            foreach (var slot in Inputs) slot.Size = Mathf.Max(1, slot.Size);
            foreach (var slot in Outputs)
                if (slot.Options == null || slot.Options.Length == 0) slot.Options = new[] { "A", "B" };
        }
    }
}
