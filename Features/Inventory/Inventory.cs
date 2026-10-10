using UnityEngine;

namespace Remedy.Inventories
{
    /// <summary>
    /// Holds Inventory Items for its object and picks up Collectable Inventories on its Inventory Layer.
    /// Its Data asset is copied per spawn, so instances never write into the shared asset.
    /// </summary>
    [SchematicBehaviorPath("Inventory/Inventory")]
    public class Inventory : SchematicBehavior
    {
        [Tooltip("The ID of an Inventory Item Data, to select it.")]
        [InputSignal, Parameter("Item ID", typeof(int))] public readonly SignalHandle<int> SetItem;
        [Tooltip("The ID of an Inventory Item Data, to add it.")]
        [InputSignal, Parameter("Item ID", typeof(int))] public readonly SignalHandle<int> AddItem;
        [Tooltip("The ID of an Inventory Item Data, to remove it.")]
        [InputSignal, Parameter("Item ID", typeof(int))] public readonly SignalHandle<int> RemoveItem;

        [OutputSignal] public readonly SignalHandle OnInventoryChanged;
        [OutputSignal, Parameter("Index", typeof(int))] public readonly SignalHandle<int> OnSetActiveItem;
        [OutputSignal, Parameter("Item ID", typeof(int))] public readonly SignalHandle<int> OnItemAdded;
        [OutputSignal, Parameter("Item ID", typeof(int))] public readonly SignalHandle<int> OnItemRemoved;

        [Tooltip("The Inventory asset this object starts with. Copied per spawn.")]
        public readonly VariableHandle<InventoryData> StartingData = (InventoryData)null;

        public readonly VariableHandle<NavigationMode[]> NavigationModes = (NavigationMode[])null;

        // This spawn's own copy of StartingData.
        private readonly VariableHandle<InventoryData> _data = (InventoryData)null;
        // The index of the active item in Contents, or -1.
        private readonly VariableHandle<int> _activeItem = -1;

        /// <summary>The selected Inventory's runtime data.</summary>
        public InventoryData Data => _data.Value;
        public InventoryItemCollection Contents => _data.Value != null ? _data.Value.Contents : null;
        public int InventoryLayer => _data.Value != null ? _data.Value.InventoryLayer : -1;

        protected override void OnStart(BehaviorInstance instance)
        {
            var starting = StartingData.Value;
            _data.Value = starting != null ? Object.Instantiate(starting) : ScriptableObject.CreateInstance<InventoryData>();
            InventoryManager.AddInventory(instance, InventoryLayer);

            AddItem.Subscribe(id =>
            {
                if (!InventoryItemData.Lookup.TryGetValue(id, out var item)) return;
                Contents.Add(item);
                OnItemAdded.Invoke(id);
                OnInventoryChanged.Invoke();
                if (Data.SwitchOnPickup)
                    SelectItem(instance, Contents.Count - 1);
            });

            RemoveItem.Subscribe(id =>
            {
                if (!InventoryItemData.Lookup.TryGetValue(id, out var item)) return;
                Contents.Remove(item);
                OnItemRemoved.Invoke(id);
                OnInventoryChanged.Invoke();
            });

            SetItem.Subscribe(id =>
            {
                for (int i = 0; i < Contents.Count; i++)
                {
                    if (Contents[i] != null && Contents[i].ID == id)
                    {
                        SelectItem(instance, i);
                        break;
                    }
                }
            });

            SelectItem(instance, 0);
        }

        protected override void OnDestroy(BehaviorInstance instance)
        {
            InventoryManager.RemoveInventory(instance);
            if (_data.Value != null)
                Object.Destroy(_data.Value);
            _data.Value = null;
        }

        /// <summary>Makes the item at the given index of the selected Inventory active.</summary>
        public void SelectItem(int itemIndex) => SelectItem(Selected, itemIndex);

        public void SelectItem(BehaviorInstance instance, int itemIndex)
        {
            var contents = _data[instance]?.Contents;
            int active = _activeItem[instance];
            if (contents == null || active == itemIndex || itemIndex < 0 || itemIndex >= contents.Count)
                return;

            if (active > -1 && active < contents.Count)
                contents[active]?.OnDeselected.Invoke(instance);

            _activeItem[instance] = itemIndex;
            contents[itemIndex]?.OnSelected.Invoke(instance);
            OnSetActiveItem.Invoke(instance, itemIndex);
        }

        // Pick up
        protected override void OnTriggerEnter(BehaviorInstance instance, Collider other)
        {
            var collectable = other.gameObject.GetSchematicBehavior<CollectableInventory>();
            if (collectable == null || collectable.InventoryLayer != InventoryLayer)
                return;

            var collectableInstance = collectable.Selected;
            int countBefore = Contents.Count;
            Contents.TransferFrom(collectable.Items.Value);
            collectable.Collect(collectableInstance, instance.GameObject);

            if (Contents.Count != countBefore)
            {
                OnInventoryChanged.Invoke();
                if (Data.SwitchOnPickup)
                    SelectItem(instance, Contents.Count - 1);
            }
        }

        public class NavigationMode
        {
            public enum NavigationLayout
            {
                Switch, // Will switch to the next item in the Inventory and wrap around to the beginning
                SwitchDirectional, // Switches like before, but with the option to go backward
                Table // 2D Table Navigation for a kind of "Bag"
            }
            public NavigationLayout Mode;

            public bool IsSwitch => Mode == NavigationLayout.Switch;
            public bool IsSwitchDirection => Mode == NavigationLayout.SwitchDirectional;
            public bool IsTable => Mode == NavigationLayout.Table;

            public SignalData SwitchInput;
            public SignalData DirectionalSwitchInput;
            public SignalData TableInput;
        }
    }
}
