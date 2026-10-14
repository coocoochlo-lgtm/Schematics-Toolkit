using System.Collections.Generic;
using Remedy.Framework;
using Remedy.Schematics;
using UnityEngine;

namespace Remedy.LevelKit
{
    /// <summary>
    /// A lock that opens with a key: when someone carrying its Key Id comes close (or sends Try Unlock), it unlocks and
    /// sends On Unlocked. Wire that to a Door's Unlock and Open, a gate, a platform, anything.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Key Lock"", ""kind"": ""behavior"", ""menu"": ""Level Kit/Key Lock"", ""description"": ""Unlocks when someone carrying its Key Id comes within Unlock Radius (or sends Try Unlock); sends On Unlocked, or On Denied without the key. IsUnlocked is network synced."", ""variables"": [""KeyId"", ""ConsumeKey"", ""UnlockRadius"", ""CarrierTag"", ""IsUnlocked""], ""signals"": [""TryUnlock"", ""ForceUnlock"", ""Relock"", ""OnUnlocked"", ""OnDenied""], ""use_for"": [""keyholes and keycard readers"", ""gates that need a key"", ""locks wired to doors or machines""]}")]
    [SchematicBehaviorPath("Level Kit/Key Lock")]
    public class KeyLock : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""KeyId"", ""kind"": ""variable"", ""behavior"": ""Key Lock"", ""type"": ""string"", ""default"": ""Red Key"", ""description"": ""The key that opens it (a Key Item's Key Id).""}")]
        [Tooltip("The key that opens it (a Key Item's Key Id).")]
        public readonly VariableHandle<string> KeyId = "Red Key";

        [SchematicDoc(@"{""name"": ""ConsumeKey"", ""kind"": ""variable"", ""behavior"": ""Key Lock"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Take the key away when it's used.""}")]
        [Tooltip("Take the key away when it's used.")]
        public readonly VariableHandle<bool> ConsumeKey = false;

        [SchematicDoc(@"{""name"": ""UnlockRadius"", ""kind"": ""variable"", ""behavior"": ""Key Lock"", ""type"": ""float"", ""default"": ""1.5"", ""description"": ""Unlocks by itself when a key carrier comes this close (metres). 0: only Try Unlock.""}")]
        [Tooltip("Unlocks by itself when a key carrier comes this close (metres). 0: only Try Unlock.")]
        public readonly VariableHandle<float> UnlockRadius = 1.5f;

        [SchematicDoc(@"{""name"": ""CarrierTag"", ""kind"": ""variable"", ""behavior"": ""Key Lock"", ""type"": ""IntTag"", ""default"": ""Player"", ""description"": ""Who is checked for the key by proximity. Untagged: any physics body.""}")]
        [Tooltip("Who is checked for the key by proximity. Untagged: any physics body.")]
        public readonly VariableHandle<IntTag> CarrierTag = new IntTag(IntTags.Player);

        [SchematicDoc(@"{""name"": ""IsUnlocked"", ""kind"": ""variable"", ""behavior"": ""Key Lock"", ""type"": ""bool"", ""default"": ""false"", ""description"": ""Whether it's been unlocked. Network synced.""}")]
        [Tooltip("Whether it's been unlocked.")]
        [NetworkSync]
        public readonly VariableHandle<bool> IsUnlocked = false;

        [SchematicDoc(@"{""name"": ""TryUnlock"", ""kind"": ""signal"", ""behavior"": ""Key Lock"", ""direction"": ""input"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Unlocks if the user carries the key, otherwise sends On Denied."", ""how"": ""Send it with a Send a Signal node when the player interacts with the lock.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> TryUnlock;

        [SchematicDoc(@"{""name"": ""ForceUnlock"", ""kind"": ""signal"", ""behavior"": ""Key Lock"", ""direction"": ""input"", ""description"": ""Unlocks without a key."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle ForceUnlock;

        [SchematicDoc(@"{""name"": ""Relock"", ""kind"": ""signal"", ""behavior"": ""Key Lock"", ""direction"": ""input"", ""description"": ""Locks it again."", ""how"": ""Send it with a Send a Signal node.""}")]
        [InputSignal, NetworkSync(SignalSender.Anyone)]
        public readonly SignalHandle Relock;

        [SchematicDoc(@"{""name"": ""OnUnlocked"", ""kind"": ""signal"", ""behavior"": ""Key Lock"", ""direction"": ""output"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Sent when it unlocks (User is null for Force Unlock)."", ""how"": ""React to it with On Signal Received, e.g. send a Door's Unlock and Open.""}")]
        [OutputSignal, NetworkSync]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnUnlocked;

        [SchematicDoc(@"{""name"": ""OnDenied"", ""kind"": ""signal"", ""behavior"": ""Key Lock"", ""direction"": ""output"", ""params"": [{""name"": ""User"", ""type"": ""GameObject""}], ""description"": ""Sent when someone without the key tries it (Try Unlock only)."", ""how"": ""React to it with On Signal Received (show 'You need the Red Key').""}")]
        [OutputSignal, NetworkSync]
        [Parameter("User", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnDenied;

        private readonly List<GameObject> _nearby = new();

        protected override void OnStart()
        {
            var instance = Self;
            TryUnlock.Subscribe(user =>
            {
                if (!LevelKitQuery.HasAuthority(instance) || IsUnlocked.Value) return;
                if (!TryWithKey(instance, user)) OnDenied.Invoke(user);
            });
            ForceUnlock.Subscribe(() => SetUnlocked(instance, null));
            Relock.Subscribe(() => { if (LevelKitQuery.HasAuthority(instance)) IsUnlocked.Value = false; });
        }

        protected override void OnFixedUpdate()
        {
            var instance = Self;
            if (IsUnlocked.Value || !LevelKitQuery.HasAuthority(instance))
                return;
            float radius = UnlockRadius.Value;
            if (radius <= 0f)
                return;
            LevelKitQuery.AgentsInSphere(instance.Transform.position, radius, -1, CarrierTag.Value, _nearby, instance.Transform);
            foreach (var agent in _nearby)
                if (TryWithKey(instance, agent))
                    break;
        }

        private bool TryWithKey(BehaviorInstance instance, GameObject user)
        {
            using (Pick(instance))
            {
                string key = KeyId.Value;
                if (user == null || !KeyRing.Has(user, key))
                    return false;
                if (ConsumeKey.Value)
                    KeyRing.Take(user, key);
                SetUnlocked(instance, LevelKitQuery.AgentOf(user));
                return true;
            }
        }

        private void SetUnlocked(BehaviorInstance instance, GameObject user)
        {
            if (!LevelKitQuery.HasAuthority(instance))
                return;
            using (Pick(instance))
            {
                if (IsUnlocked.Value) return;
                IsUnlocked.Value = true;
                OnUnlocked.Invoke(user);
            }
        }

        protected override void OnDrawGizmos(BehaviorGizmoContext context)
        {
            float radius = context.Get(UnlockRadius);
            if (radius <= 0f) return;
            Gizmos.color = new Color(1f, 0.35f, 0.3f, 0.5f);
            Gizmos.DrawWireSphere(context.Transform.position, radius);
        }
    }
}
