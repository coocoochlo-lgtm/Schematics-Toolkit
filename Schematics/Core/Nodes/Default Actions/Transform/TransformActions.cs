using BlueGraph;
using System;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform"), Tags("Object")]
    public class SetRotation : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Euler Angles")] public Vector3 EulerAngles;
        [Editable] public bool Local;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            var angles = GetInputValue<Vector3>("Euler Angles", EulerAngles);
            if (target == null) throw new InvalidOperationException("Set Rotation requires a target GameObject.");
            if (Local) target.transform.localEulerAngles = angles;
            else target.transform.eulerAngles = angles;
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform"), Tags("Object")]
    public class SetScale : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Scale")] public Vector3 Scale = Vector3.one;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null) throw new InvalidOperationException("Set Scale requires a target GameObject.");
            target.transform.localScale = GetInputValue<Vector3>(nameof(Scale), Scale);
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform", Name="Translate"), Tags("Object")]
    public class TranslateTransform : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Translation")] public Vector3 Translation;
        [Editable] public Space Space = Space.Self;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null) throw new InvalidOperationException("Translate requires a target GameObject.");
            target.transform.Translate(GetInputValue<Vector3>(nameof(Translation), Translation), Space);
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform", Name="Rotate"), Tags("Object")]
    public class RotateTransform : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Euler Angles")] public Vector3 EulerAngles;
        [Editable] public Space Space = Space.Self;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null) throw new InvalidOperationException("Rotate requires a target GameObject.");
            target.transform.Rotate(GetInputValue<Vector3>("Euler Angles", EulerAngles), Space);
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects/Transform"), Tags("Object")]
    public class SetParent : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Parent")] public GameObject Parent;
        [Editable] public bool PreserveWorldTransform = true;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null) throw new InvalidOperationException("Set Parent requires a target GameObject.");
            var parent = GetInputValue<GameObject>(nameof(Parent), Parent);
            target.transform.SetParent(parent == null ? null : parent.transform, PreserveWorldTransform);
        }
    }

    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Path = "Objects"), Tags("Object")]
    public class SetGameObjectActive : SchematicActionNode
    {
        [Input("Target")] public GameObject Target;
        [Input("Active", Editable = true)] public bool Active = true;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = GetInputValue<GameObject>(nameof(Target), Target);
            if (target == null) throw new InvalidOperationException("Set Active requires a target GameObject.");
            target.SetActive(GetInputValue<bool>(nameof(Active), Active));
        }
    }
}
