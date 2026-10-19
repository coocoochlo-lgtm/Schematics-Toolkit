using BlueGraph;
using System;

namespace Remedy.Schematics
{
    /// <summary>
    /// A flow node with a row of numbered flow outputs ("pins") that can be added and removed on the node in the graph
    /// editor (its + and − buttons): Random, Split, Switch... The pins are ordinary ports, saved with the node; run the
    /// nodes on one with <see cref="SchematicActionNodeBase.ContinueFrom(UnityEngine.GameObject, string)"/> and
    /// <see cref="PinName"/>.
    /// </summary>
    [Serializable]
    public abstract class DynamicFlowNode : SchematicActionNodeBase
    {
        /// <summary>The fewest pins the node can have (it starts with this many).</summary>
        public virtual int MinPins => 2;

        /// <summary>The most pins the node can have.</summary>
        public virtual int MaxPins => 16;

        /// <summary>The name of a pin, by its position (0 is the first).</summary>
        public virtual string PinName(int index) => (index + 1).ToString();

        /// <summary>How many pins the node has now.</summary>
        public int PinCount
        {
            get
            {
                int count = 0;
                while (count < MaxPins && GetPort(PinName(count)) != null)
                    count++;
                return count;
            }
        }

        public bool CanAddPin => PinCount < MaxPins;
        public bool CanRemovePin => PinCount > MinPins;

        public override void OnAddedToGraph()
        {
            base.OnAddedToGraph();
            EnsurePins();
        }

        public override void OnValidate()
        {
            EnsurePins();
            base.OnValidate();
        }

        /// <summary>Adds a pin after the last one.</summary>
        public void AddPin()
        {
            if (!CanAddPin)
                return;
            AddPort(new Port
            {
                Name = PinName(PinCount),
                Type = typeof(ActionPort),
                Direction = PortDirection.Output,
                Capacity = PortCapacity.Multiple,
                EnableCasting = false,
            });
            IsDirty = true;
        }

        /// <summary>Removes the last pin (and its wires).</summary>
        public void RemovePin()
        {
            if (!CanRemovePin)
                return;
            var port = GetPort(PinName(PinCount - 1));
            if (port != null)
                RemovePort(port);
            IsDirty = true;
        }

        private void EnsurePins()
        {
            while (PinCount < MinPins)
                AddPin();
        }
    }
}
