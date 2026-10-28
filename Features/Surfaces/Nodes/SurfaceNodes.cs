// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Schematics;
using System;
using UnityEngine;

namespace Remedy.Surfaces
{
    public enum SurfaceDirectionSpace { World, Local }

    [SchematicDoc(@"{""name"": ""Get Surface"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Surfaces"", ""description"": ""What the surface is made of in a direction from an object (with nothing connected, the object running the graph; it's never hit itself): its Surface Material and that material's Wetness, Absorption, Hardness and Friction, where it was hit, and how far. Painted surfaces, tagged objects, and the project's default material for any other collider. Layers: All, Painted only, or Colliders only."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Direction"", ""type"": ""Vector3""}, {""name"": ""Distance"", ""type"": ""float""}], ""outputs"": [{""name"": ""Material"", ""type"": ""SurfaceMaterial""}, {""name"": ""Has Hit"", ""type"": ""bool""}, {""name"": ""Wetness"", ""type"": ""float""}, {""name"": ""Absorption"", ""type"": ""float""}, {""name"": ""Hardness"", ""type"": ""float""}, {""name"": ""Friction"", ""type"": ""float""}, {""name"": ""Point"", ""type"": ""Vector3""}, {""name"": ""Normal"", ""type"": ""Vector3""}, {""name"": ""Hit Distance"", ""type"": ""float""}, {""name"": ""Hit Object"", ""type"": ""GameObject""}], ""use_for"": [""footstep sounds by floor"", ""slipping on wet or icy ground"", ""impact effects by material""], ""notes"": ""A value node. Down (0, -1, 0) is the ground under the object.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Get Surface", Path = "Surfaces"), Tags("Default")]
    public class GetSurfaceNode : SchematicGraphNode
    {
        [Input("Object")] public GameObject Object;
        [Input("Direction", Editable = true)] public Vector3 Direction = Vector3.down;
        [Input("Distance", Editable = true)] public float Distance = 2f;
        [Tooltip("World: Direction is in world axes (Down is always down). Local: in the object's own axes (Forward is where it faces).")]
        [Editable] public SurfaceDirectionSpace Space = SurfaceDirectionSpace.World;
        [Tooltip("Which surfaces count: painted surfaces, tagged objects, or both.")]
        [Editable] public SurfaceLayers Layers = SurfaceLayers.All;

        [Tooltip("The Surface Material it's made of (connect to Is Surface or Get Surface Property).")]
        [Output("Material")] public SurfaceMaterial Material;
        [Tooltip("True if a surface was found within Distance.")]
        [Output("Has Hit")] public bool HasHit;
        [Tooltip("How wet the surface is, 0 to 1.")]
        [Output("Wetness")] public float Wetness;
        [Tooltip("How much the surface soaks up sound, 0 to 1.")]
        [Output("Absorption")] public float Absorption;
        [Tooltip("How hard the surface is, 0 to 1.")]
        [Output("Hardness")] public float Hardness;
        [Tooltip("How grippy the surface is, 0 to 1.")]
        [Output("Friction")] public float Friction;
        [Output("Point")] public Vector3 Point;
        [Output("Normal")] public Vector3 Normal;
        [Tooltip("How far away the surface is (meters).")]
        [Output("Hit Distance")] public float HitDistance;
        [Tooltip("The object that was hit.")]
        [Output("Hit Object")] public GameObject HitObject;

        [NonSerialized] private int _cachedFrame = -1;
        [NonSerialized] private GameObject _cachedTarget;
        [NonSerialized] private Vector3 _cachedOrigin, _cachedDirection;
        [NonSerialized] private float _cachedDistance;
        [NonSerialized] private bool _cachedHasHit;
        [NonSerialized] private SurfaceHit _cachedHit;

        public override object OnRequestValue(Port port)
        {
            var target = GetInputValue("Object", Object);
            if (target == null)
                target = RunningInstance;
            bool hasHit = false;
            SurfaceHit hit = default;
            if (target != null)
            {
                var direction = GetInputValue("Direction", Direction);
                if (Space == SurfaceDirectionSpace.Local)
                    direction = target.transform.TransformDirection(direction);
                float distance = GetInputValue("Distance", Distance);
                var origin = target.transform.position;
                // Each output is asked for separately: cast once a frame for the same question.
                if (_cachedFrame == Time.frameCount && _cachedTarget == target && _cachedOrigin == origin
                    && _cachedDirection == direction && _cachedDistance == distance)
                {
                    hasHit = _cachedHasHit;
                    hit = _cachedHit;
                }
                else
                {
                    hasHit = SurfaceQuery.Cast(origin, direction, distance, out hit, Layers, target);
                    _cachedFrame = Time.frameCount;
                    _cachedTarget = target;
                    _cachedOrigin = origin;
                    _cachedDirection = direction;
                    _cachedDistance = distance;
                    _cachedHasHit = hasHit;
                    _cachedHit = hit;
                }
            }
            var material = hasHit ? hit.Material : null;
            switch (port.Name)
            {
                case "Material": return material;
                case "Has Hit": return hasHit;
                case "Wetness": return material != null ? material.Wetness : 0f;
                case "Absorption": return material != null ? material.Absorption : 0f;
                case "Hardness": return material != null ? material.Hardness : 0f;
                case "Friction": return material != null ? material.Friction : 0f;
                case "Point": return hasHit ? hit.Point : Vector3.zero;
                case "Normal": return hasHit ? hit.Normal : Vector3.zero;
                case "Hit Distance": return hasHit ? hit.Distance : 0f;
                case "Hit Object": return hasHit ? hit.Object : null;
            }
            return base.OnRequestValue(port);
        }
    }

    [SchematicDoc(@"{""name"": ""Get Surface Property"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Surfaces"", ""description"": ""One property of a Surface Material, by name: Wetness, Absorption, Hardness, Friction, or one of the material's own. Has Property is false (and Value 0) when it has none by that name."", ""inputs"": [{""name"": ""Material"", ""type"": ""SurfaceMaterial""}, {""name"": ""Property"", ""type"": ""string""}], ""outputs"": [{""name"": ""Value"", ""type"": ""float""}, {""name"": ""Has Property"", ""type"": ""bool""}], ""notes"": ""A value node.""}")]
    [Serializable]
    [NodeColor(NodeColors.Physics)]
    [Node(Name = "Get Surface Property", Path = "Surfaces"), Tags("Default")]
    public class GetSurfacePropertyNode : SchematicGraphNode
    {
        [Tooltip("The Surface Material to read (from Get Surface).")]
        [Input("Material", Editable = true)] public SurfaceMaterial Material;
        [Tooltip("The property's name: Wetness, Absorption, Hardness, Friction, or one the material defines itself.")]
        [Input("Property", Editable = true)] public string Property = "Wetness";
        [Output("Value")] public float Value;
        [Tooltip("True if the material has a property by that name.")]
        [Output("Has Property")] public bool HasProperty;

        public override object OnRequestValue(Port port)
        {
            var material = GetInputValue("Material", Material);
            float value = 0f;
            bool has = material != null && material.TryGet(GetInputValue("Property", Property), out value);
            return port.Name == "Has Property" ? has : (object)value;
        }
    }

    [SchematicDoc(@"{""name"": ""Is Surface"", ""kind"": ""node"", ""flow"": ""condition"", ""category"": ""Surfaces"", ""description"": ""True when Material is the Surface Material picked on the node (connect Get Surface's Material)."", ""inputs"": [{""name"": ""Material"", ""type"": ""SurfaceMaterial""}], ""notes"": ""A true/false value: connect it to an If node's Condition.""}")]
    [Serializable]
    [NodeColor(NodeColors.Condition)]
    [Node(Name = "Is Surface", Path = "Surfaces"), Tags("Default")]
    public class IsSurfaceNode : SchematicConditionalNode
    {
        [Tooltip("The Surface Material to check (from Get Surface).")]
        [Input("Material")] public SurfaceMaterial Material;
        [Tooltip("The Surface Material to compare with.")]
        [Editable] public SurfaceMaterial Is;

        public override bool Condition() => Is != null && GetInputValue<SurfaceMaterial>("Material", null) == Is;
    }
}
