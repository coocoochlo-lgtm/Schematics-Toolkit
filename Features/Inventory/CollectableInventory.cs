using Remedy.Schematics;
using UnityEngine;

namespace Remedy.Inventories
{
    /// <summary>
    /// Items lying in the world, picked up by an <see cref="Inventory"/> on the same Inventory Layer.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Collectable Inventory"", ""kind"": ""behavior"", ""menu"": ""Inventory/Collectable"", ""description"": ""Items lying in the world, picked up by an Inventory on the same Inventory Layer."", ""variables"": [""CollectionData"", ""Items""], ""signals"": [""OnCollect""], ""use_for"": [""pickups lying in the world (coins, keys, ammo)""]}")]
    [SchematicBehaviorPath("Inventory/Collectable")]
    [RequireComponent(typeof(SphereCollider))]
    public class CollectableInventory : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""OnCollect"", ""kind"": ""signal"", ""behavior"": ""Collectable Inventory"", ""direction"": ""output"", ""params"": [{""name"": ""Collector"", ""type"": ""GameObject""}], ""description"": ""Sent when this Inventory is Collected, passing the GameObject whose Inventory picked it up."", ""how"": ""React to it with On Signal Received (drag it from the IO Dock); it fires when the Behavior reports it.""}")]
        [Tooltip("Sent when this Inventory is Collected, passing the GameObject whose Inventory picked it up.")]
        [OutputSignal, Parameter("Collector", typeof(GameObject))]
        [NetworkSync(SignalSender.Authority)]
        public readonly SignalHandle<GameObject> OnCollect;

        [SchematicDoc(@"{""name"": ""CollectionData"", ""kind"": ""variable"", ""behavior"": ""Collectable Inventory"", ""type"": ""CollectableInventoryData"", ""default"": ""null"", ""description"": ""The items this collectable holds, and how it's displayed and picked up.""}")]
        [Tooltip("The items this collectable holds, and how it's displayed and picked up.")]
        public readonly VariableHandle<CollectableInventoryData> CollectionData = (CollectableInventoryData)null;

        /// <summary>This spawn's items: a copy of the Collection Data's contents.</summary>
        [SchematicDoc(@"{""name"": ""Items"", ""kind"": ""variable"", ""behavior"": ""Collectable Inventory"", ""type"": ""InventoryItemCollection"", ""default"": ""null"", ""description"": ""This spawn's items: a copy of the Collection Data's contents.""}")]
        public readonly VariableHandle<InventoryItemCollection> Items = (InventoryItemCollection)null;
        private readonly VariableHandle<GameObject> _renderedObject = (GameObject)null;

        public int InventoryLayer => CollectionData.Value != null ? CollectionData.Value.InventoryLayer : -1;

        protected override void OnCreate()
        {
            var pickupCollider = Self.GetComponent<SphereCollider>();
            if (pickupCollider != null)
            {
                pickupCollider.isTrigger = true;
                pickupCollider.radius = GlobalInventoryData.Instance.ItemPickupRange;
            }
        }

        protected override void OnEnable()
        {
            var data = CollectionData.Value;

            // Copied, so collecting doesn't empty the shared asset.
            var items = new InventoryItemCollection();
            if (data != null && data.Contents != null)
                foreach (var item in data.Contents)
                    items.Add(item);
            Items.Value = items;

            if (data != null && data.RenderAsItem && data.Contents != null && data.Contents.Count > 0 && data.Contents[0].Prefab != null)
                _renderedObject.Value = Object.Instantiate(data.Contents[0].Prefab, Self.Transform);

            InventoryManager.AddCollectableInventory(Self, InventoryLayer);
        }

        protected override void OnDisable()
        {
            InventoryManager.RemoveCollectableInventory(Self);
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
