// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Find Game Objects With Tag"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Objects"", ""description"": ""Every object in the scene with a tag, as a list."", ""outputs"": [{""name"": ""Objects"", ""type"": ""List<object>""}], ""use_for"": [""finding all enemies, checkpoints or players""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs. Searches the whole scene: avoid using it every frame.""}")]
    [Serializable]
    [Node(Path = "Objects"), Tags("Default")]
    public class FindGameObjectsWithTag : SchematicGraphNode
    {
        [Editable]
        public string Tag;

        [Output("Objects")]
        public List<object> Objects;

        public override object OnRequestValue(Port port)
        {
            return GameObject.FindGameObjectsWithTag(Tag).ToList<object>();
        }
    }
}