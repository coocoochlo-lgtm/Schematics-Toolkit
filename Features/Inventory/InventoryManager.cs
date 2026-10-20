using Remedy.Framework;
//using SaintsField.Playa;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Inventories
{
    //[Searchable]
    public class InventoryManager : Singleton<InventoryManager>
    {
        private Dictionary<GameObject, int> _inventories;
        public static Dictionary<GameObject, int> Inventories => Instance._inventories ??= new();
        private Dictionary<GameObject, int> _collectableInventories;
        public static Dictionary<GameObject, int> CollectableInventories => Instance._collectableInventories ??= new();

        public static void AddInventory(BehaviorInstance inventory, int inventoryLayer)
        {
            Inventories[inventory.GameObject] = inventoryLayer;
        }
        public static void RemoveInventory(BehaviorInstance inventory)
        {
            Inventories.Remove(inventory.GameObject);
        }

        public static void AddCollectableInventory(BehaviorInstance collectableInventory, int inventoryLayer)
        {
            CollectableInventories[collectableInventory.GameObject] = inventoryLayer;
        }
        public static void RemoveCollectableInventory(BehaviorInstance collectableInventory)
        {
            CollectableInventories.Remove(collectableInventory.GameObject);
        }

        /// <summary>
        /// Gets the Inventory Layer that the GameObject instance is on.
        /// </summary>
        /// <param name="instance"></param>
        /// <returns></returns>
        public static int GetInventoryLayer(GameObject instance)
        {
            if (Inventories.TryGetValue(instance, out var layer))
            {
                return layer;
            }
            else if (CollectableInventories.TryGetValue(instance, out layer))
            {
                return layer;
            }

            return -1;
        }
    }
}