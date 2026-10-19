// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Remedy.Schematics
{
    /// <summary>The objects of a list that have an Int Tag (it used to compare Unity's string tags).</summary>
    [SchematicDoc(@"{""name"": ""Pick From List With Tag"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""The objects of a list that have an Int Tag (components count as their GameObject)."", ""inputs"": [{""name"": ""List"", ""type"": ""List<object>""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""outputs"": [{""name"": ""Objects With Tag"", ""type"": ""List<object>""}], ""use_for"": [""filtering targets by tag""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Collections"), Tags("Object")]
    public class PickFromListWithTag : SchematicGraphNode
    {
        [Input("List")]
        public List<object> List;
        [Input("Tag", Editable = true)]
        public IntTag ObjectTag = IntTags.Untagged;

        [Output("Objects With Tag")]
        public List<object> WithTag;

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

            var tag = GetInputValue("Tag", ObjectTag);
            var list = GetInputValue<IList>("List", List);
            var result = new List<object>();
            if (list == null)
                return result;

            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i] is Union union ? SignalArgs.ToObject(union) : list[i];
                GameObject go = item as GameObject;
                if (go == null && item is Component component && component != null)
                    go = component.gameObject;
                if (go != null && go.HasIntTag(tag))
                    result.Add(item);
            }
            return result;
        }
    }
}
