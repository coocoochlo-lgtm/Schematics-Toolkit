using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>
    /// What an AI Controller learned by watching a player, kept: for each kind of object it learned to control (a
    /// character, a car), the network's weights and what it considers usual for that object. Give it to Create AI
    /// Controller (Skill) and the AI starts out knowing it; in the editor, what it learns during Play Mode is saved back
    /// into it. One asset per skill level makes easy difficulty settings.
    /// </summary>
    [CreateAssetMenu(fileName = "New Learned Controls", menuName = "Remedy Toolkit/AI/Learned Controls")]
    public sealed class LearnedControls : ControllerSkillData
    {
        [Tooltip("In the editor, keep what the AI learned when Play Mode ends (or when the AI goes away).")]
        public bool SaveOnExitPlayMode = true;

        [Tooltip("Each kind of object it has learned to control.")]
        public List<LearnedKind> Kinds = new();

        /// <summary>What it learned about one kind of object (its Schematic Object's ObjectID), or null.</summary>
        public LearnedKind Find(int objectId) => Kinds.Find(kind => kind != null && kind.ObjectId == objectId);

        /// <summary>Adds or replaces what it learned about a kind of object.</summary>
        public void Put(LearnedKind kind)
        {
            Kinds.RemoveAll(existing => existing == null || existing.ObjectId == kind.ObjectId);
            Kinds.Add(kind);
        }
    }

    /// <summary>One kind of object an AI learned to control: the network and what it saw as usual.</summary>
    [Serializable]
    public sealed class LearnedKind
    {
        [Tooltip("The Schematic Object (prefab) it controls.")]
        public int ObjectId;
        public string ObjectName;
        [Tooltip("The Input Signals its outputs press, in order. Learned controls only fit while these stay the same.")]
        public string InputLayout;
        [Tooltip("How many numbers it reads from the object. They only fit while its Behaviors' Variables stay the same.")]
        public int Observations;
        [HideInInspector] public float[] Mean = Array.Empty<float>();
        [HideInInspector] public float[] Variance = Array.Empty<float>();
        [HideInInspector] public MnnSettings Settings;
        [HideInInspector] public float[] Parameters = Array.Empty<float>();
        [HideInInspector] public float[] Memory = Array.Empty<float>();
        [HideInInspector] public float[] Identities = Array.Empty<float>();
        [Tooltip("Training updates so far.")]
        public int Updates;
    }

    /// <summary>The same, as a file (Save AI Skill with a file name).</summary>
    [Serializable]
    internal sealed class LearnedControlsFile
    {
        public List<LearnedKind> Kinds = new();
    }
}
