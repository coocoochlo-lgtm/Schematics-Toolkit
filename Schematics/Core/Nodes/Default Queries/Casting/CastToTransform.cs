// project armada

#pragma warning disable 0414

using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Cast To Transform"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Casts"", ""description"": ""Treats a value as a Transform (from a GameObject or component) and outputs its position and rotation."", ""inputs"": [{""name"": ""Value"", ""type"": ""object""}], ""outputs"": [{""name"": ""Position"", ""type"": ""Vector3""}, {""name"": ""EulerAngles"", ""type"": ""Vector3""}, {""name"": ""Casted"", ""type"": ""T""}], ""use_for"": [""getting the position of an object from a list or Signal""], ""notes"": ""A value node: no flow arrows; connect its outputs to other nodes' inputs.""}")]
    [Serializable]
    [Node(Path = "Casts"), Tags("Default")]
    public class CastToTransform : Cast<Transform>
    {
        [Output("Position")]
        public Vector3 TransformPosition;
        [Output("EulerAngles")]
        public Vector3 EulerAngles;
        public override object OnRequestValue(Port port)
        {
            var value = GetOutputValue<object>("Value");

            if (value.GetType() == typeof(Transform))
                CastedValue = (Transform)value;
            if (value.GetType() == typeof(GameObject))
                CastedValue = ((GameObject)value).transform;
            if (value.GetType() == typeof(Component))
                CastedValue = ((Component)value).transform;
            if (value.GetType() == typeof(MonoBehaviour))
                CastedValue = ((MonoBehaviour)value).transform;

            if (port.Name == "Casted")
            {
                return CastedValue;
            }
            else if (port.Name == "Position")
            {
                return CastedValue.position;
            }
            else if (port.Name == "EulerAngles")
            {
                return CastedValue.eulerAngles;
            }
            return null;
        }
    }
}