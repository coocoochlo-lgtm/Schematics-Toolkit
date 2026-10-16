// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remedy.Schematics
{
    /// <summary>
    /// Every object with an Int Tag, as a list. Kept under its old name (the action node "Find With Tag" in
    /// Objects/Int Tags is the flow version); it used to search Unity's string tags.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Find Game Objects With Tag"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Objects"", ""description"": ""Every active object with an Int Tag, as a list."", ""inputs"": [{""name"": ""Tag"", ""type"": ""IntTag""}], ""outputs"": [{""name"": ""Objects"", ""type"": ""List<object>""}], ""use_for"": [""finding all enemies, checkpoints or players""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs. A dictionary lookup (Int Tags), so it's cheap; it's computed once per flow step however many nodes read it.""}")]
    [Serializable]
    [Node(Path = "Objects"), Tags("Default")]
    public class FindGameObjectsWithTag : SchematicGraphNode
    {
        [Input("Tag", Editable = true)]
        public IntTag ObjectTag = IntTags.Untagged;

        [Output("Objects")]
        public List<object> Objects;

        // The Unity tag name older versions of this node saved; moved onto ObjectTag by name.
        [SerializeField, HideInInspector, FormerlySerializedAs("Tag")]
        private string _legacyTag;

        public override void OnValidate()
        {
            MigrateLegacyTag(ref _legacyTag, ref ObjectTag);
            EnsureDataPort("Tag", typeof(IntTag), PortDirection.Input);
            base.OnValidate();
        }

        public override object OnRequestValue(Port port)
        {
            if (_legacyTag != null)
                MigrateLegacyTag(ref _legacyTag, ref ObjectTag);

            var found = IntTagRegistry.Find(GetInputValue("Tag", ObjectTag));

            // A new list each time: a flow that continues later (after a Wait) keeps the one it was given.
            var list = new List<object>(found.Count);
            for (int i = 0; i < found.Count; i++)
                if (found[i] != null)
                    list.Add(found[i]);
            return list;
        }
    }
}
