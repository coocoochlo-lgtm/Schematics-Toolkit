using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Remedy.Schematics
{
    /// <summary>
    /// A node whose pins depend on something it points at (Play Sound Cue's pins are its cue's Parameters): the editor
    /// asks it to bring them up to date when the graph opens or changes.
    /// </summary>
    public interface IDynamicPorts
    {
        /// <summary>Makes the node's pins match what it points at now. True if anything changed.</summary>
        bool SyncPorts();
    }

    /// <summary>Helpers for <see cref="IDynamicPorts"/> nodes.</summary>
    public static class DynamicPorts
    {
        /// <summary>A node changed its own pins (what it points at changed): open editors redraw it.</summary>
        public static event Action<Node> Changed;

        public static void Notify(Node node) => Changed?.Invoke(node);

        /// <summary>
        /// Makes a node's input pins match <paramref name="wanted"/>: missing ones added, ones of the wrong type
        /// remade, and pins <paramref name="owned"/> says are its dynamic ones removed when no longer wanted. True if
        /// anything changed.
        /// </summary>
        public static bool SyncInputs(Node node, IEnumerable<(string name, Type type)> wanted, Func<Port, bool> owned)
        {
            bool changed = false;
            var names = new HashSet<string>();
            foreach (var (name, type) in wanted)
            {
                if (string.IsNullOrEmpty(name) || !names.Add(name))
                    continue;
                var existing = node.GetPort(name);
                if (existing != null)
                {
                    if (existing.Type == type && existing.Direction == PortDirection.Input)
                        continue;
                    node.RemovePort(existing);
                }
                node.AddPort(new Port { Name = name, Type = type, Direction = PortDirection.Input, Capacity = PortCapacity.Single });
                changed = true;
            }
            foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Input && owned(p) && !names.Contains(p.Name)).ToList())
            {
                node.RemovePort(port);
                changed = true;
            }
            return changed;
        }

        /// <summary>The same as <see cref="SyncInputs"/>, for output pins.</summary>
        public static bool SyncOutputs(Node node, IEnumerable<(string name, Type type)> wanted, Func<Port, bool> owned)
        {
            bool changed = false;
            var names = new HashSet<string>();
            foreach (var (name, type) in wanted)
            {
                if (string.IsNullOrEmpty(name) || !names.Add(name))
                    continue;
                var existing = node.GetPort(name);
                if (existing != null)
                {
                    if (existing.Type == type && existing.Direction == PortDirection.Output)
                        continue;
                    node.RemovePort(existing);
                }
                node.AddPort(new Port { Name = name, Type = type, Direction = PortDirection.Output, Capacity = PortCapacity.Multiple });
                changed = true;
            }
            foreach (var port in node.Ports.Values.Where(p => p.Direction == PortDirection.Output && owned(p) && !names.Contains(p.Name)).ToList())
            {
                node.RemovePort(port);
                changed = true;
            }
            return changed;
        }
    }
}
