using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Who controls which Schematic Object, and where input goes.
    /// <para><b>Default mode</b> (until a game sets up its players): one default controller, and every object that
    /// isn't possessed by another controller (an AI, say) answers to it. Input reaches all of them, so a single-player
    /// game needs no setup at all.</para>
    /// <para><b>Separated mode</b> (a Singleton Graph uses On Device Connected to set up Player Controllers): the default
    /// controller is turned off, each player's devices give input only to the objects that player possesses, and an
    /// object nobody possesses gets no input until it's possessed.</para>
    /// Singleton objects hear every player (they're the game itself: a pause menu, a join screen). The controller whose
    /// input is being delivered right now is <see cref="Current"/>, so they can tell who pressed what.
    /// </summary>
    public static class Possession
    {
        private static readonly Dictionary<int, (SchematicObject schematic, Controller controller)> _byUID = new();
        // Objects possessed by a controller other than the default: default-mode input skips them.
        private static readonly HashSet<int> _claimed = new();
        private static readonly HashSet<SchematicObject> _hearAll = new();
        private static readonly List<Controller> _controllers = new();

        /// <summary>The controller everything answers to in default mode (set up by the input system). Null if none.</summary>
        public static Controller Default { get; set; }

        /// <summary>True once the game sets up its own players: input then reaches only possessed objects.</summary>
        public static bool Separated { get; private set; }

        /// <summary>The controller whose input is being delivered right now (null outside an input).</summary>
        public static Controller Current { get; private set; }

        /// <summary>Every controller there is, players and AIs.</summary>
        public static IReadOnlyList<Controller> Controllers => _controllers;

        /// <summary>Raised when the game separates its players (the default controller turns off).</summary>
        public static event Action SeparationStarted;

        /// <summary>Raised when an object is possessed (by the controller given) or let go.</summary>
        public static event Action<SchematicObject, Controller> Possessed, Unpossessed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _byUID.Clear();
            _claimed.Clear();
            _hearAll.Clear();
            _controllers.Clear();
            Default = null;
            Separated = false;
            Current = null;
        }

        /// <summary>Lists a new controller (called by the controllers themselves).</summary>
        public static void Register(Controller controller)
        {
            if (controller != null && !_controllers.Contains(controller))
                _controllers.Add(controller);
        }

        internal static void Forget(Controller controller)
        {
            _controllers.Remove(controller);
            if (Default == controller)
                Default = null;
        }

        /// <summary>
        /// Switches to separated players: the default controller lets go of everything and stops giving input, and from
        /// now on objects only get input from the controller that possesses them.
        /// </summary>
        public static void Separate()
        {
            if (Separated)
                return;
            Separated = true;
            Default?.UnpossessAll();
            SeparationStarted?.Invoke();
        }

        /// <summary>Makes an object hear every player's input, possessed or not (Singletons do).</summary>
        public static void HearEveryController(SchematicObject schematic)
        {
            if (schematic != null)
                _hearAll.Add(schematic);
        }

        /// <summary>The Schematic Object a GameObject (or one of its children) belongs to, if it's spawned.</summary>
        public static SchematicObject SchematicOf(GameObject target)
        {
            if (target == null)
                return null;
            var schematic = target.GetComponentInParent<SchematicObject>();
            return schematic != null && schematic.UID != 0 ? schematic : null;
        }

        /// <summary>The controller that possesses an object, or null (default mode: see <see cref="ControllerOf"/>).</summary>
        public static Controller PossessorOf(SchematicObject schematic)
            => schematic != null && schematic.UID != 0 && _byUID.TryGetValue(schematic.UID, out var entry) && entry.schematic == schematic
                ? entry.controller : null;

        /// <summary>
        /// The controller an object answers to: whoever possesses it, or, in default mode, the default controller. Null
        /// if none (separated, and nobody possesses it).
        /// </summary>
        public static Controller ControllerOf(GameObject target)
        {
            var schematic = SchematicOf(target);
            if (schematic == null)
                return null;
            var possessor = PossessorOf(schematic);
            if (possessor != null)
                return possessor;
            return Separated ? null : Default;
        }

        /// <summary>
        /// Gives an object to a controller, taking it from whoever had it. False if the target isn't a spawned
        /// Schematic Object (or part of one).
        /// </summary>
        public static bool Possess(Controller controller, GameObject target)
        {
            var schematic = SchematicOf(target);
            if (controller == null || schematic == null)
                return false;
            var previous = PossessorOf(schematic);
            if (previous == controller)
                return true;
            if (previous != null)
                Unpossess(schematic);

            int uid = schematic.UID;
            _byUID[uid] = (schematic, controller);
            if (controller != Default)
                _claimed.Add(uid);
            controller.Add(schematic);
            Possessed?.Invoke(schematic, controller);
            schematic.SchematicGraph?.TriggerEvent<OnPossessed>(schematic.gameObject, new Union((Component)controller));
            return true;
        }

        /// <summary>Lets an object go: nobody possesses it (in default mode it answers to the default controller again).</summary>
        public static void Unpossess(SchematicObject schematic)
        {
            if (schematic == null || schematic.UID == 0 || !_byUID.TryGetValue(schematic.UID, out var entry) || entry.schematic != schematic)
                return;
            Release(schematic.UID, entry);
            schematic.SchematicGraph?.TriggerEvent<OnUnpossessed>(schematic.gameObject, new Union((Component)entry.controller));
        }

        /// <summary>An object despawned: whoever possessed it lets go (its graph has stopped, so it isn't told).</summary>
        internal static void ForgetObject(int uid)
        {
            if (_byUID.TryGetValue(uid, out var entry))
                Release(uid, entry);
            _claimed.Remove(uid);
        }

        private static void Release(int uid, (SchematicObject schematic, Controller controller) entry)
        {
            _byUID.Remove(uid);
            _claimed.Remove(uid);
            if (entry.controller != null)
                entry.controller.Remove(entry.schematic);
            Unpossessed?.Invoke(entry.schematic, entry.controller);
        }

        /// <summary>
        /// Delivers a controller's input: to everything not possessed by someone else (the default controller, in
        /// default mode), or else to what it possesses (and, for players, to the objects that hear every player).
        /// </summary>
        public static void Deliver(Controller controller, SignalData signal, Union[] args)
        {
            if (controller == null || signal == null)
                return;
            // The default controller is off once players are separated.
            if (Separated && controller == Default)
                return;

            var previous = Current;
            Current = controller;
            try
            {
                if (!Separated && controller == Default)
                {
                    if (_claimed.Count == 0)
                        signal.InvokeFromGraph(0, args);
                    else
                        signal.InvokeExcept(_claimed, args);
                }
                else
                {
                    // Its own list each time: delivering can possess or despawn things, or send more input.
                    var targets = new List<int>(controller.Possessed.Count + (controller.IsPlayer ? _hearAll.Count : 0));
                    foreach (var possessed in controller.Possessed)
                        if (possessed != null && possessed.UID != 0)
                            targets.Add(possessed.UID);
                    if (controller.IsPlayer)
                        foreach (var listener in _hearAll)
                            if (listener != null && listener.UID != 0 && !targets.Contains(listener.UID))
                                targets.Add(listener.UID);
                    if (targets.Count > 0)
                        signal.InvokeTo(targets, args);
                }
                controller.ReportInput(signal, args);
            }
            finally
            {
                Current = previous;
            }
        }
    }
}
