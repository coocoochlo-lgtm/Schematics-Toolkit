using System.Collections.Generic;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// Counts things up to a target: coins collected, enemies defeated, switches flipped. Collectibles add to the
    /// Counter with the same <c>Id</c>; graphs can Add too. Reaching the Target sends On Complete.
    /// </summary>
    /// <remarks>
    /// Counters are found by Id, so a level needs one Counter per Id (the first spawned wins). Counts added while no
    /// Counter with that Id exists are still kept (<see cref="Total"/>), so a HUD can show them anyway.
    /// </remarks>
    [SchematicDoc(@"{""name"": ""Counter"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Counter"", ""description"": ""Counts up to a Target (coins, kills, switches): Collectibles with the same Id add to it; graphs can Add or Reset. Reaching the Target sends On Complete. Count and IsComplete are network synced."", ""variables"": [""Id"", ""Count"", ""Target"", ""IsComplete"", ""ResetOnComplete""], ""signals"": [""Add"", ""ResetCount"", ""OnChanged"", ""OnComplete""], ""use_for"": [""collect-all-the-coins goals"", ""kill counters"", ""puzzles that need several switches""]}")]
    [SchematicBehaviorPath("Level Kit/Counter")]
    public class Counter : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Id"", ""kind"": ""variable"", ""behavior"": ""Counter"", ""type"": ""string"", ""default"": ""Coins"", ""description"": ""What it counts. Collectibles with this Counter Id add to it.""}")]
        [Tooltip("What it counts. Collectibles with this Counter Id add to it.")]
        public readonly VariableHandle<string> Id = "Coins";

        [SchematicDoc(@"{""name"": ""Count"", ""kind"": ""variable"", ""behavior"": ""Counter"", ""type"": ""int"", ""default"": ""0"", ""description"": ""The count so far. Network synced.""}")]
        [Tooltip("The count so far.")]
        [NetworkSync]
        public readonly VariableHandle<int> Count = 0;

        [SchematicDoc(@"{""name"": ""Target"", ""kind"": ""variable"", ""behavior"": ""Counter"", ""type"": ""int"", ""default"": ""10"", ""description"": ""Reaching this sends On Complete. 0: no target.""}")]
        [Tooltip("Reaching this sends On Complete. 0: no target.")]
        public readonly VariableHandle<int> Target = 10;

        [SchematicDoc(@"{""name"": ""IsComplete"", ""kind"": ""variable"", ""behavior"": ""Counter"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""True once the Target was reached. Network synced.""}")]
        [Tooltip("True once the Target was reached.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsComplete = false;

        [SchematicDoc(@"{""name"": ""ResetOnComplete"", ""kind"": ""variable"", ""behavior"": ""Counter"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Start again from 0 after completing (e.g. every 100 coins gives a life).""}")]
        [Tooltip("Start again from 0 after completing (e.g. every 100 coins gives a life).")]
        public readonly VariableHandle<bool> ResetOnComplete = false;

        [SchematicDoc(@"{""name"": ""Add"", ""kind"": ""signal"", ""behavior"": ""Counter"", ""direction"": ""input"", ""params"": [{""name"": ""Amount"", ""type"": ""int""}], ""description"": ""Adds to the count (negative takes away)."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("Amount", typeof(int))]
        public readonly SignalHandle<int> Add;

        [SchematicDoc(@"{""name"": ""ResetCount"", ""kind"": ""signal"", ""behavior"": ""Counter"", ""direction"": ""input"", ""description"": ""Back to 0, not complete."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle ResetCount;

        [SchematicDoc(@"{""name"": ""OnChanged"", ""kind"": ""signal"", ""behavior"": ""Counter"", ""direction"": ""output"", ""params"": [{""name"": ""Count"", ""type"": ""int""}], ""description"": ""Sent with the new count whenever it changes (update a HUD)."", ""how"": ""React to it with On Signal Received.""}")]
        [OutputSignal, NetworkSync]
        [Parameter("Count", typeof(int))]
        public readonly SignalHandle<int> OnChanged;

        [SchematicDoc(@"{""name"": ""OnComplete"", ""kind"": ""signal"", ""behavior"": ""Counter"", ""direction"": ""output"", ""description"": ""Sent when the count reaches the Target."", ""how"": ""React to it with On Signal Received, e.g. open the exit Door.""}")]
        [OutputSignal, NetworkSync]
        public readonly SignalHandle OnComplete;

        private static readonly Dictionary<string, BehaviorInstance> _byId = new();
        private static readonly Dictionary<string, int> _totals = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _byId.Clear();
            _totals.Clear();
        }

        /// <summary>
        /// Adds to the Counter with this id (and its running total). Returns false when no Counter with that id is
        /// spawned (the total still counts). Only the authority's calls count.
        /// </summary>
        public static bool AddTo(string id, int amount)
        {
            if (string.IsNullOrEmpty(id)) return false;
            _totals.TryGetValue(id, out int total);
            if (_byId.TryGetValue(id, out var instance) && instance != null && instance.IsSpawned)
            {
                ((Counter)instance.Behavior).Change(instance, amount);
                return true;
            }
            _totals[id] = total + amount;
            return false;
        }

        /// <summary>The count for an id: its Counter's, or the running total when there's none.</summary>
        public static int Total(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            if (_byId.TryGetValue(id, out var instance) && instance != null && instance.IsSpawned)
                return ((Counter)instance.Behavior).Count[instance];
            return _totals.TryGetValue(id, out int total) ? total : 0;
        }

        protected override void OnStart()
        {
            var instance = Self;
            string id = Id.Value;
            if (!string.IsNullOrEmpty(id) && !_byId.ContainsKey(id))
            {
                _byId[id] = instance;
                // Anything collected before this Counter existed.
                if (_totals.TryGetValue(id, out int early) && early != 0 && LevelKitQuery.HasAuthority(instance))
                {
                    _totals.Remove(id);
                    Change(instance, early);
                }
            }
            Add.Subscribe(amount => Change(instance, amount));
            ResetCount.Subscribe(() =>
            {
                if (!LevelKitQuery.HasAuthority(instance)) return;
                Count.Value = 0;
                IsComplete.Value = false;
                OnChanged.Invoke(0);
            });
        }

        protected override void OnDestroy()
        {
            var instance = Self;
            string id = Id.Value;
            if (!string.IsNullOrEmpty(id) && _byId.TryGetValue(id, out var registered) && registered == instance)
                _byId.Remove(id);
        }

        private void Change(BehaviorInstance instance, int amount)
        {
            if (instance == null || amount == 0 || !LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                Count.Value += amount;
                OnChanged.Invoke(Count.Value);
                int target = Target.Value;
                if (target > 0 && !IsComplete.Value && Count.Value >= target)
                {
                    IsComplete.Value = true;
                    OnComplete.Invoke();
                    if (ResetOnComplete.Value)
                    {
                        Count.Value = 0;
                        IsComplete.Value = false;
                        OnChanged.Invoke(0);
                    }
                }
            }
        }
    }
}
