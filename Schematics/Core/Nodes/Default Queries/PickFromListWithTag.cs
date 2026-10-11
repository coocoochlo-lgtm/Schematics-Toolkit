// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Pick From List With Tag"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Collections"", ""description"": ""The objects of a list that have a tag."", ""inputs"": [{""name"": ""List"", ""type"": ""List<object>""}, {""name"": ""Tag"", ""type"": ""string""}], ""outputs"": [{""name"": ""Objects With Tag"", ""type"": ""List<object>""}], ""use_for"": [""filtering targets by tag""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Collections"), Tags("Object")]
    public class PickFromListWithTag : SchematicGraphNode
    {
        [Input("List")]
        public List<object> List;
        [Input("Tag")]
        public string Tag;

        [Output("Objects With Tag")]
        public List<object> WithTag;

        private List<object> _tempList;

        public override object OnRequestValue(Port port)
        {
            _tempList.Clear();
            List = GetOutputValue<List<object>>("List");

            if(GetPort("Tag").ConnectionCount != 0)
                Tag = GetOutputValue<string>("Tag");

            foreach(var obj in List)
            {
                var inst = ((GameObject)obj);
                if (inst.CompareTag(Tag))
                {
                    _tempList.Add(inst);
                }
            }

            return _tempList;
        }
    }
}