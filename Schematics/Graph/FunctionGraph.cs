using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A Function Graph: a routine any logic graph can call like a node (a Call node). Its inputs come out of its On
    /// Called event, it runs on the object calling it (Self is the caller), and its Return hands its outputs back to
    /// the Call node, whose flow carries on from there, at once, or after the function's Waits.
    /// </summary>
    /// <remarks>
    /// Its inputs and outputs (names, types, and for numbers the control shown on calling nodes) are set in the IO Dock's
    /// Graph tab, under Function, with its description: In Library lists it in every logic graph's node search under
    /// Functions, documented like a built-in node, and the assistant learns it too.
    /// </remarks>
    [CreateAssetMenu(fileName = "New Function", menuName = "Remedy Toolkit/Schematics/Function Graph")]
    [SchematicType("Functions", Order = 20, Icon = "SchematicTab:function", NewAssetName = "New Function", DefaultFolder = Folder)]
    [IncludeTags("Default", "Runtime", "Object", "State", "UI", Tag)]
    [RequireNode(typeof(OnCalledNode), "On Called", 0, 0)]
    public class FunctionGraph : SchematicGraph
    {
        /// <summary>The tag of the nodes only Function Graphs offer (Return).</summary>
        public const string Tag = "Function";

        /// <summary>Exclusive tag for On Called, which every Function Graph has one of (not offered in the search).</summary>
        public const string EntryOnlyTag = "Function Graph Required Entry";

        /// <summary>Where new Function Graphs go.</summary>
        public const string Folder = "Assets/Content/Functions";

        public override string Title => "Function: " + name;

        public override bool CallableAsFunction => true;

        public override IReadOnlyList<GraphPinInfo> FunctionInputs => Function.Inputs.Where(p => p != null).Select(p => p.Info).ToList();

        public override IReadOnlyList<GraphPinInfo> FunctionOutputs => Function.Outputs.Where(p => p != null).Select(p => p.Info).ToList();

        /// <summary>Its On Called event, where a call starts.</summary>
        public OnCalledNode Entry
        {
            get
            {
                foreach (var node in Nodes)
                    if (node is OnCalledNode entry)
                        return entry;
                return null;
            }
        }

        /// <summary>Whether it has a Return: without one, a call carries straight on once the function has started.</summary>
        public bool HasReturn
        {
            get
            {
                foreach (var node in Nodes)
                    if (node is ReturnNode)
                        return true;
                return false;
            }
        }

        public override void OnGraphValidate()
        {
            base.OnGraphValidate();
            bool removed = false;
            foreach (var duplicate in Nodes.OfType<OnCalledNode>().Skip(1).ToArray())
            {
                RemoveNode(duplicate);
                removed = true;
            }
            if (removed)
                CacheNodesByType();
        }
    }
}
