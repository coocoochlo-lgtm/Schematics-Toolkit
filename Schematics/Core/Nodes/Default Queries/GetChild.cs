// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Get Child"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Schematic"", ""description"": ""A child Transform of this object."", ""outputs"": [{""name"": ""Child"", ""type"": ""Transform""}], ""use_for"": [""referring to part of the object""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Schematic"), Tags("Default")]
    public class GetChild : SchematicGraphNode
    {
        [Output]
        public Transform Child;

        [NonSerialized] private Transform _pathFor;
        [NonSerialized] private string _path;

        public override object OnRequestValue(Port port)
        {
            // Child is picked from the prefab; the same child of the spawned object running the graph is wanted.
            var instance = RunningInstance;
            if (Child == null || instance == null)
                return Child;

            if (_pathFor != Child)
            {
                _pathFor = Child;
                _path = PathFromRoot(Child);
            }
            if (_path == null)
                return Child;
            var found = _path.Length == 0 ? instance.transform : instance.transform.Find(_path);
            return found != null ? found : Child;
        }

        // The child's path under the prefab root, or null if it isn't part of the prefab.
        private string PathFromRoot(Transform child)
        {
            var prefab = Graph is SchematicGraph schematic ? schematic.Prefab : null;
            if (prefab == null)
                return null;
            var root = prefab.transform;
            string path = "";
            for (var t = child; t != null; t = t.parent)
            {
                if (t == root)
                    return path;
                path = path.Length == 0 ? t.name : t.name + "/" + path;
            }
            return null;
        }
    }
}