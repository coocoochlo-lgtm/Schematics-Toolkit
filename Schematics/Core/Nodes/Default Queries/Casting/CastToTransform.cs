// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Cast To Transform"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Casts"", ""description"": ""Treats a value as a Transform (from a GameObject or component) and outputs its position and rotation."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""outputs"": [{""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""EulerAngles"", ""type"": ""Vector3""}, {""name"": ""Casted"", ""type"": ""T""}], ""use_for"": [""getting the position of an object from a list or Signal""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Value)]
    [Node(Path = "Casts"), Tags("Default")]
    public class CastToTransform : Cast<Transform>
    {
        [Output("Position")]
        public Vector3 TransformPosition;
        [Tooltip("Its rotation, in degrees around X, Y and Z.")]
        [Output("EulerAngles")]
        public Vector3 EulerAngles;
        public override object OnRequestValue(Port port)
        {
            var value = GetInputValue<object>("Value", null);
            if (!PortValues.TryFromObject(value, out Transform transform) || transform == null)
                return null;

            switch (port.Name)
            {
                case "Casted": return transform;
                case "Position": return transform.position;
                case "EulerAngles": return transform.eulerAngles;
                default: return null;
            }
        }
    }
}