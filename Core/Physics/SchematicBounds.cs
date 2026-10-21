using Box3D.Hybrid;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A Schematic Object's bounds: one box in the object's own space (its root's), made of what its parts say they
    /// take up, so other systems can ask where the object really is -- where its bottom is along a direction, say,
    /// for a camera checking for ground under it. Lives on a child called "Bounds" under the object's root, made the
    /// first time something asks for it in Play Mode (or add it there yourself to set a Base box).
    /// </summary>
    /// <remarks>
    /// The box is the <see cref="Base"/> box, the object's Box3D shapes (with <see cref="IncludeShapes"/>), and every
    /// part pushed by a Behavior or component with <see cref="Set"/> / <see cref="SetSphere"/>, each under its own key
    /// so it can be moved every frame and taken out with <see cref="Remove"/>: an Arcade Car pushes its tyres where its
    /// suspension has them, so the bottom is where the tyres meet the road. Read it with <see cref="Local"/>,
    /// <see cref="Center"/> and <see cref="Lowest"/>.
    /// </remarks>
    [AddComponentMenu("Remedy/Schematic Bounds")]
    [DisallowMultipleComponent]
    public class SchematicBounds : MonoBehaviour
    {
        /// <summary>The child the bounds live on.</summary>
        public const string ChildName = "Bounds";

        [Tooltip("Always part of the box, in the object's space, on top of what its parts push. Zero size: nothing.")]
        public Bounds Base = new(Vector3.zero, Vector3.zero);
        [Tooltip("Include the object's Box3D shapes (its physical size).")]
        public bool IncludeShapes = true;

        private readonly Dictionary<object, Bounds> _parts = new();
        private Box3DShape[] _shapes;
        private Bounds _local;
        private bool _hasBox;
        private int _builtFrame = -1;
        private bool _dirty = true;

        private static readonly Dictionary<Transform, SchematicBounds> _byRoot = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _byRoot.Clear();

        /// <summary>The object whose space the box is in: the Schematic Object's root (this component's parent).</summary>
        public Transform Space => transform.parent != null ? transform.parent : transform;

        /// <summary>
        /// The bounds of the Schematic Object <paramref name="target"/> belongs to (its root's "Bounds" child). Made if
        /// there are none yet and <paramref name="create"/> is on, in Play Mode; otherwise null.
        /// </summary>
        public static SchematicBounds Of(GameObject target, bool create = true)
        {
            if (target == null)
                return null;
            var owner = target.GetComponentInParent<SchematicObject>();
            var root = owner != null ? owner.transform : target.transform;
            if (_byRoot.TryGetValue(root, out var found) && found != null)
                return found;

            found = null;
            for (int i = 0; i < root.childCount && found == null; i++)
                root.GetChild(i).TryGetComponent(out found);
            if (found == null && create && Application.isPlaying)
            {
                var child = new GameObject(ChildName);
                child.transform.SetParent(root, worldPositionStays: false);
                found = child.AddComponent<SchematicBounds>();
            }
            if (found != null)
                _byRoot[root] = found;
            return found;
        }

        /// <summary>
        /// Puts in (or moves) a part's box under <paramref name="key"/> (the part itself, usually): a box in
        /// <paramref name="space"/>'s own space (the object's root when null).
        /// </summary>
        public void Set(object key, Bounds box, Transform space = null)
        {
            if (key == null)
                return;
            _parts[key] = space == null || space == Space ? box : ToSpace(box, space.localToWorldMatrix);
            _dirty = true;
        }

        /// <summary>Puts in (or moves) a ball under <paramref name="key"/>: a wheel, a foot, a hover pad.</summary>
        public void SetSphere(object key, Vector3 worldCenter, float radius)
        {
            if (key == null)
                return;
            var space = Space;
            var size = space.InverseTransformVector(Vector3.one * (2f * radius));
            _parts[key] = new Bounds(space.InverseTransformPoint(worldCenter), new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
            _dirty = true;
        }

        /// <summary>Takes a part's box out.</summary>
        public void Remove(object key)
        {
            if (key != null && _parts.Remove(key))
                _dirty = true;
        }

        /// <summary>Whether there's anything in the box (no Base, shapes or parts: false, and it's a point at the root).</summary>
        public bool HasBox
        {
            get { Build(); return _hasBox; }
        }

        /// <summary>The box, in the object's space.</summary>
        public Bounds Local
        {
            get { Build(); return _local; }
        }

        /// <summary>The middle of the box, in the world.</summary>
        public Vector3 Center => Space.TransformPoint(Local.center);

        /// <summary>
        /// The bottom of the box along <paramref name="up"/> (any direction, e.g. the object's own up on a wall): the
        /// point below its middle, level with its lowest corner.
        /// </summary>
        public Vector3 Lowest(Vector3 up)
        {
            var space = Space;
            var local = Local;
            var center = space.TransformPoint(local.center);
            var e = local.extents;
            float reach = Mathf.Abs(Vector3.Dot(space.TransformVector(new Vector3(e.x, 0f, 0f)), up))
                          + Mathf.Abs(Vector3.Dot(space.TransformVector(new Vector3(0f, e.y, 0f)), up))
                          + Mathf.Abs(Vector3.Dot(space.TransformVector(new Vector3(0f, 0f, e.z)), up));
            return center - up.normalized * reach;
        }

        // Shapes can move (animated children), so they're measured again once a frame; parts when they change.
        private void Build()
        {
            if (!_dirty && _builtFrame == Time.frameCount)
                return;
            _dirty = false;
            _builtFrame = Time.frameCount;
            _hasBox = false;
            _local = new Bounds(Vector3.zero, Vector3.zero);

            if (Base.size != Vector3.zero)
                Add(Base);
            if (IncludeShapes)
            {
                if (_shapes == null || System.Array.Exists(_shapes, s => s == null))
                    _shapes = Space.GetComponentsInChildren<Box3DShape>(true);
                var toRoot = Space.worldToLocalMatrix;
                foreach (var shape in _shapes)
                    if (shape.isActiveAndEnabled && shape.TryGetLocalBounds(out var box))
                        Add(ToSpace(box, toRoot * shape.transform.localToWorldMatrix, alreadyInRoot: true));
            }
            foreach (var part in _parts.Values)
                Add(part);
        }

        private void Add(Bounds box)
        {
            if (!_hasBox)
            {
                _local = box;
                _hasBox = true;
            }
            else
                _local.Encapsulate(box);
        }

        // A box in another space, as a box in the root's space (around its 8 corners).
        private Bounds ToSpace(Bounds box, Matrix4x4 matrix, bool alreadyInRoot = false)
        {
            var toRoot = alreadyInRoot ? matrix : Space.worldToLocalMatrix * matrix;
            var e = box.extents;
            var result = new Bounds(toRoot.MultiplyPoint3x4(box.center), Vector3.zero);
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3((corner & 1) == 0 ? -e.x : e.x, (corner & 2) == 0 ? -e.y : e.y, (corner & 4) == 0 ? -e.z : e.z);
                result.Encapsulate(toRoot.MultiplyPoint3x4(box.center + offset));
            }
            return result;
        }

        /// <summary>Forgets the cached Box3D shapes (after adding or removing some at runtime).</summary>
        public void RefreshShapes()
        {
            _shapes = null;
            _dirty = true;
        }

        private void OnDestroy()
        {
            if (_byRoot.TryGetValue(Space, out var registered) && registered == this)
                _byRoot.Remove(Space);
        }

        private void OnDrawGizmosSelected()
        {
            var space = Space;
            Gizmos.matrix = space.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.6f, 0.85f, 0.9f);
            var local = Local;
            Gizmos.DrawWireCube(local.center, local.size);
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.DrawSphere(Lowest(space.up), 0.05f);
        }
    }
}
