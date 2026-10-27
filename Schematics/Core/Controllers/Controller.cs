using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Something that controls Schematic Objects by giving them input: a player (with their devices) or an AI. A
    /// controller possesses objects, and the input it sends (its Input Signals) reaches only those, so two players
    /// on one screen each drive their own character. Player and AI controllers share this base, so anything a player
    /// can do to an object, an AI can do the same way.
    /// <para>Until a game sets up players itself, there's one default controller and every object answers to it:
    /// input reaches everything, as if there were no possession at all (see <see cref="Possession"/>).</para>
    /// </summary>
    public abstract class Controller : MonoBehaviour
    {
        private readonly List<SchematicObject> _possessed = new();

        /// <summary>Its number among controllers of its kind, from 0 (Player 1 is 0).</summary>
        public int Index { get; internal set; }

        /// <summary>A name to show ("Player 1", "AI 2").</summary>
        public virtual string DisplayName => name;

        /// <summary>True for a player's controller (their devices give the input), false for an AI's.</summary>
        public abstract bool IsPlayer { get; }

        /// <summary>
        /// Who it is on this peer, for telling other players' machines ("p0" for Player 1, "a2" for AI 3, "d" for the
        /// default player). Online, the peer's id goes in front (see <see cref="Possession.KeyOf"/>).
        /// </summary>
        public virtual string NetworkId => (IsPlayer ? "p" : "a") + Index;

        /// <summary>
        /// False for a stand-in for a controller on another player's machine (<see cref="RemoteController"/>): its input
        /// happens there, and reaches everyone through the objects' networked Signals and Variables.
        /// </summary>
        public virtual bool IsLocal => true;

        /// <summary>The objects it possesses right now, in the order it took them (the first is its main one).</summary>
        public IReadOnlyList<SchematicObject> Possessed => _possessed;

        /// <summary>The first object it possesses that's still spawned, or null.</summary>
        public SchematicObject Primary
        {
            get
            {
                foreach (var possessed in _possessed)
                    if (possessed != null && possessed.UID != 0)
                        return possessed;
                return null;
            }
        }

        /// <summary>
        /// Raised for every input it sends: the Signal and its values (released inputs send their defaults). An AI
        /// learning from this player listens here.
        /// </summary>
        public event Action<Controller, SignalData, Union[]> InputSent;

        /// <summary>Raised when it takes or lets go of an object.</summary>
        public event Action<Controller, SchematicObject> PossessionGained, PossessionLost;

        /// <summary>Takes control of an object (its Schematic Object, if given a child). False if it isn't spawned.</summary>
        public bool Possess(GameObject target) => Possession.Possess(this, target);

        /// <summary>Lets go of an object, if it has it.</summary>
        public void Unpossess(GameObject target)
        {
            var schematic = Possession.SchematicOf(target);
            if (schematic != null && Possession.PossessorOf(schematic) == this)
                Possession.Unpossess(schematic);
        }

        /// <summary>Lets go of everything it possesses.</summary>
        public void UnpossessAll()
        {
            for (int i = _possessed.Count - 1; i >= 0; i--)
                if (i < _possessed.Count)
                    Possession.Unpossess(_possessed[i]);
        }

        /// <summary>Whether it possesses an object (or the Schematic Object a child belongs to).</summary>
        public bool Possesses(GameObject target)
        {
            var schematic = Possession.SchematicOf(target);
            return schematic != null && _possessed.Contains(schematic);
        }

        /// <summary>
        /// Gives input: sends an Input Signal to the objects this controller possesses, as a press of one of its
        /// buttons would.
        /// </summary>
        public void SendInput(SignalData signal, params Union[] args)
        {
            if (signal == null)
                return;
            Possession.Deliver(this, signal, args ?? Array.Empty<Union>());
        }

        /// <summary>Lets go of an input: its possessed objects get the Signal's defaults (false, 0, (0, 0)...).</summary>
        public void ReleaseInput(SignalData signal)
        {
            if (signal == null)
                return;
            // A Signal with no value has nothing to say about a release (sent, it'd be another press).
            if (signal.Parameters.Contents.Count == 0)
                return;
            Possession.Deliver(this, signal, signal.DefaultArguments());
        }

        internal void Add(SchematicObject schematic)
        {
            if (!_possessed.Contains(schematic))
                _possessed.Add(schematic);
            OnPossessed(schematic);
            PossessionGained?.Invoke(this, schematic);
        }

        internal void Remove(SchematicObject schematic)
        {
            if (!_possessed.Remove(schematic))
                return;
            OnUnpossessed(schematic);
            PossessionLost?.Invoke(this, schematic);
        }

        internal void ReportInput(SignalData signal, Union[] args) => InputSent?.Invoke(this, signal, args);

        /// <summary>It took control of an object.</summary>
        protected virtual void OnPossessed(SchematicObject schematic) { }

        /// <summary>It let go of an object (or the object despawned).</summary>
        protected virtual void OnUnpossessed(SchematicObject schematic) { }

        protected virtual void OnDestroy()
        {
            UnpossessAll();
            Possession.Forget(this);
        }
    }
}
