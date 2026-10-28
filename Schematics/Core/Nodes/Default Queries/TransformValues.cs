using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform"), Tags("Object")]
    public class TransformValues : SchematicGraphNode
    {
        [Input("Target")]
        public GameObject Target;

        [Output("Position")]
        public Vector3 Position;

        [Tooltip("Its rotation, in degrees around X, Y and Z.")]
        [Output("Euler Angles")]
        public Vector3 EulerAngles;

        [Output("Scale")]
        public Vector3 Scale;

        [Tooltip("The direction it faces (its blue Z axis).")]
        [Output("Forward")]
        public Vector3 Forward;

        public override object OnRequestValue(Port port)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null)
                throw new InvalidOperationException("Transform Values requires a target GameObject.");

            var transform = target.transform;
            switch (port.Name)
            {
                case nameof(Position): return Position = transform.position;
                case nameof(EulerAngles): return EulerAngles = transform.eulerAngles;
                case nameof(Scale): return Scale = transform.localScale;
                case nameof(Forward): return Forward = transform.forward;
                default: return null;
            }
        }
    }
}
