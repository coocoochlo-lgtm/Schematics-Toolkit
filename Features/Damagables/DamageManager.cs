using Remedy.Framework;
/*using SaintsField;
using SaintsField.Playa;*/
using System.Collections.Generic;
using UnityEngine;
using Remedy.Common;

namespace Remedy.Damagables
{
    //[Searchable]
    public class DamageManager : Singleton<DamageManager>
    {
        //[ReadOnly, SaintsDictionary]
        private Dictionary<int, List<BehaviorInstance>> _activeDamageables = new();
        /// <summary>Spawned, enabled Damageables by Damage Layer.</summary>
        public static Dictionary<int, List<BehaviorInstance>> ActiveDamageables => Instance._activeDamageables;

        private Dictionary<GameObject, int> _damageables;
        public static Dictionary<GameObject, int> Damageables => Instance._damageables ??= new();

        private void OnEnable()
        {
            for(int i = 0; i < DamageData.Instance.DamageLayers.Length; i++)
            {
                if (!ActiveDamageables.ContainsKey(i))
                    ActiveDamageables.Add(i, new());
            }
        }

        /// <summary>Called by Damageables when they become enabled.</summary>
        public static void AddDamageable(BehaviorInstance damageable, int damageLayer)
        {
            if (!ActiveDamageables.TryGetValue(damageLayer, out var list))
                ActiveDamageables[damageLayer] = list = new();
            if (!list.Contains(damageable))
                list.Add(damageable);
            Damageables[damageable.GameObject] = damageLayer;
        }

        /// <summary>Called by Damageables when they become disabled or despawn.</summary>
        public static void RemoveDamageable(BehaviorInstance damageable)
        {
            if (Damageables.TryGetValue(damageable.GameObject, out var layer) && ActiveDamageables.TryGetValue(layer, out var list))
                list.Remove(damageable);
            Damageables.Remove(damageable.GameObject);
        }

        public static void InstigateDamage(DamageInstigation instigation)
        {
            ForEachInRadius(instigation, (damageable, instance) => damageable.Damage(instance, instigation));
        }

        public static void InstigateHeal(DamageInstigation instigation)
        {
            ForEachInRadius(instigation, (damageable, instance) => damageable.Heal(instance, instigation));
        }

        private static void ForEachInRadius(DamageInstigation instigation, System.Action<Damageable, BehaviorInstance> apply)
        {
            if (!DamageInstigationData.Lookup.TryGetValue(instigation.DataID, out var data) ||
                !ActiveDamageables.TryGetValue(data.DamageLayer, out var damageables))
                return;

            // Copied: damage can kill and despawn things, which removes them from the list.
            foreach (var instance in damageables.ToArray())
            {
                if (instance.IsSpawned && Vector3.Distance(instance.Transform.position, instigation.Position) < data.Radius)
                    apply((Damageable)instance.Behavior, instance);
            }
        }
    }
}
