using System;
using UnityEngine;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// What a network has learned: its settings (which fix the value spaces and point types through the seed), its
    /// weights (including the WFC rulebooks and the memory's β), and its episodic memory slots. Load it into any network
    /// with the same layout. Save it as an asset to ship a trained bot.
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy/Manifold Network/Brain", fileName = "Manifold Network Brain")]
    public sealed class MnnBrain : ScriptableObject
    {
        public MnnSettings Settings = new();
        [HideInInspector] public float[] Parameters = Array.Empty<float>();
        [Tooltip("The episodic memory bank's slots (empty with the memory off).")]
        [HideInInspector] public float[] MemorySlots = Array.Empty<float>();
        [HideInInspector] public string LayoutSignature = "";
        [Tooltip("Training updates this brain has had.")]
        public int Updates;

        public bool IsEmpty => Parameters == null || Parameters.Length == 0;

        /// <summary>True if this brain's weights fit a network with these settings.</summary>
        public bool Fits(MnnSettings settings) => !IsEmpty && LayoutSignature == settings.LayoutSignature;

        public static MnnBrain Create(MnnSettings settings, float[] parameters, int updates, float[] memorySlots = null)
        {
            var brain = CreateInstance<MnnBrain>();
            brain.Settings = settings.Clone();
            brain.Parameters = (float[])parameters.Clone();
            brain.MemorySlots = memorySlots != null ? (float[])memorySlots.Clone() : Array.Empty<float>();
            brain.LayoutSignature = settings.LayoutSignature;
            brain.Updates = updates;
            return brain;
        }

        public void CopyFrom(MnnBrain other)
        {
            Settings = other.Settings.Clone();
            Parameters = (float[])other.Parameters.Clone();
            MemorySlots = other.MemorySlots != null ? (float[])other.MemorySlots.Clone() : Array.Empty<float>();
            LayoutSignature = other.LayoutSignature;
            Updates = other.Updates;
        }
    }
}
