using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Inventories
{
    /// <summary>
    /// Items lying in the world, picked up by an <see cref="Inventory"/> on the same Inventory Layer.
    /// </summary>
    [SchematicBehaviorPath("Inventory/Collectable")]
    [RequireComponent(typeof(SphereCollider))]
    public class CollectableInventory : SchematicBehavior
    {
        [Tooltip("Sent when this Inventory is Collected, passing the GameObject whose Inventory picked it up.")]
        [OutputSignal, Parameter("Collector", typeof(GameObject))]
        public readonly SignalHandle<GameObject> OnCollect;

        [Tooltip("The items this collectable holds, and how it's displayed and picked up.")]
        public readonly VariableHandle<CollectableInventoryData> CollectionData = (CollectableInventoryData)null;

        /// <summary>This spawn's items: a copy of the Collection Data's contents.</summary>
        public readonly VariableHandle<InventoryItemCollection> Items = (InventoryItemCollection)null;
        private readonly VariableHandle<GameObject> _renderedObject = (GameObject)null;

        public int InventoryLayer => CollectionData.Value != null ? CollectionData.Value.InventoryLayer : -1;

        protected override void OnCreate(BehaviorInstance instance)
        {
            var pickupCollider = instance.GetComponent<SphereCollider>();
            if (pickupCollider != null)
            {
                pickupCollider.isTrigger = true;
                pickupCollider.radius = GlobalInventoryData.Instance.ItemPickupRange;
            }
        }

        protected override void OnEnable(BehaviorInstance instance)
        {
            var data = CollectionData.Value;

            // Copied, so collecting doesn't empty the shared asset.
            var items = new InventoryItemCollection();
            if (data != null && data.Contents != null)
                foreach (var item in data.Contents)
                    items.Add(item);
            Items.Value = items;

            if (data != null && data.RenderAsItem && data.Contents != null && data.Contents.Count > 0 && data.Contents[0].Prefab != null)
                _renderedObject.Value = Object.Instantiate(data.Contents[0].Prefab, instance.Transform);

            InventoryManager.AddCollectableInventory(instance, InventoryLayer);
        }

        protected override void OnDisable(BehaviorInstance instance)
        {
            InventoryManager.RemoveCollectableInventory(instance);
            if (_renderedObject.Value != null)
                Object.Destroy(_renderedObject.Value);
            _renderedObject.Value = null;
        }

        /// <summary>
        /// Called by the Inventory that picked this up.
        /// </summary>
        public void Collect(BehaviorInstance instance, GameObject collector)
        {
            OnCollect.Invoke(instance, collector);

            var data = CollectionData[instance];
            if (data != null && data.DestroyOnPickup)
                SchematicsManager.Destroy(instance.Owner);
        }
    }
}
