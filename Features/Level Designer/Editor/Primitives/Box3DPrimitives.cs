using System.Collections.Generic;
using Box3D.Hybrid;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor.Primitives
{
    /// <summary>
    /// Project setting (Project Settings → Remedy → Box3D Primitives, saved in ProjectSettings/ so the whole team
    /// shares it): whether GameObject → 3D Object → Cube / Sphere / Capsule / Cylinder / Plane / Quad get Box3D
    /// physics instead of Unity's PhysX colliders, and whether those bodies start static.
    /// </summary>
    [FilePath("ProjectSettings/RemedyBox3DPrimitives.asset", FilePathAttribute.Location.ProjectFolder)]
    public class Box3DPrimitiveSettings : ScriptableSingleton<Box3DPrimitiveSettings>
    {
        [SerializeField] private bool _replacePhysXColliders = true;
        [SerializeField] private bool _static = true;

        /// <summary>Swap the PhysX collider of newly created 3D Object primitives for the Box3D equivalent.</summary>
        public bool ReplacePhysXColliders
        {
            get => _replacePhysXColliders;
            set
            {
                _replacePhysXColliders = value;
                Save(true);
            }
        }

        /// <summary>Give the swapped primitives a static body (level geometry); off makes them dynamic.</summary>
        public bool MakeStatic
        {
            get => _static;
            set
            {
                _static = value;
                Save(true);
            }
        }

        [SettingsProvider]
        private static SettingsProvider CreateProvider()
        {
            return new SettingsProvider("Project/Remedy/Box3D Primitives", SettingsScope.Project)
            {
                label = "Box3D Primitives",
                keywords = new HashSet<string> { "Box3D", "Primitive", "Cube", "Sphere", "Collider", "PhysX" },
                guiHandler = _ =>
                {
                    var settings = instance;
                    EditorGUILayout.HelpBox(
                        "GameObject → 3D Object → Cube, Sphere, Capsule, Cylinder, Plane and Quad keep Unity's mesh but get Box3D " +
                        "physics instead of a PhysX collider: Box / Sphere / Capsule shapes, a convex Hull for the cylinder, and a " +
                        "Mesh shape for the plane and quad. Primitives made inside a Box3D body become part of that body.",
                        MessageType.None);
                    EditorGUI.BeginChangeCheck();
                    bool replace = EditorGUILayout.Toggle(new GUIContent("Use Box3D for New Primitives", "Swap the PhysX collider of new 3D Object primitives for Box3D"), settings.ReplacePhysXColliders);
                    bool makeStatic;
                    using (new EditorGUI.DisabledScope(!replace))
                        makeStatic = EditorGUILayout.Toggle(new GUIContent("Static Bodies", "New primitives are static level geometry (off: dynamic)"), settings.MakeStatic);
                    if (EditorGUI.EndChangeCheck())
                    {
                        settings._replacePhysXColliders = replace;
                        settings._static = makeStatic;
                        settings.Save(true);
                    }
                },
            };
        }
    }

    /// <summary>
    /// Makes Unity's GameObject → 3D Object primitives Box3D objects. Why not our own menu items on the same paths:
    /// Unity refuses a second menu item with a path it already has (it logs "a menu item with the same name already
    /// exists" and keeps its own), so the built-in items can't be replaced that way. Instead this watches what the
    /// built-in items create: they make the primitive through ObjectFactory, which reports each component added
    /// (<see cref="ObjectFactory.componentWasAdded"/>); a new object whose MeshFilter and PhysX collider arrive
    /// together, with one of Unity's built-in primitive meshes, is swapped right after: the collider removed, a
    /// <see cref="Box3DBody"/> (static by default, none when a parent already has a body) and the matching Box3D
    /// shape added, all undoable. As a fallback (should a Unity version not report through ObjectFactory), a newly
    /// selected object whose last undo step is "Create …" and that is still exactly an untouched primitive is
    /// swapped too. (Verified against Unity 6's C# reference: GOCreationCommands' 3D Object items, priorities 1-6,
    /// call ObjectFactory.CreatePrimitive, and MenuItem registration rejects duplicate paths.)<br/>
    /// Adding a collider to an existing object, duplicating, prefabs and Play Mode are never touched. Switch it off
    /// in Project Settings → Remedy → Box3D Primitives.
    /// </summary>
    [InitializeOnLoad]
    public static class Box3DPrimitiveSwapper
    {
        private static readonly HashSet<GameObject> _meshAdded = new();
        private static readonly HashSet<GameObject> _pending = new();
        private static bool _flushQueued;

        static Box3DPrimitiveSwapper()
        {
            ObjectFactory.componentWasAdded += OnComponentAdded;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        private static bool Enabled => !EditorApplication.isPlayingOrWillChangePlaymode && Box3DPrimitiveSettings.instance.ReplacePhysXColliders;

        private static void OnComponentAdded(Component component)
        {
            if (component == null || !Enabled)
                return;
            var go = component.gameObject;
            if (component is MeshFilter)
                _meshAdded.Add(go);
            else if (component is Collider && _meshAdded.Contains(go))
                Queue(go);
            QueueFlush();
        }

        private static void OnHierarchyChanged()
        {
            if (!Enabled)
                return;
            var go = Selection.activeGameObject;
            if (go == null || _pending.Contains(go))
                return;
            string undo = Undo.GetCurrentGroupName();
            if (!string.IsNullOrEmpty(undo) && undo.StartsWith("Create ") && TryGetPrimitive(go, out _, out _))
            {
                Queue(go);
                QueueFlush();
            }
        }

        private static void Queue(GameObject go) => _pending.Add(go);

        private static void QueueFlush()
        {
            if (_flushQueued)
                return;
            _flushQueued = true;
            // After the creating menu item has finished (placed, parented, registered with Undo).
            EditorApplication.delayCall += Flush;
        }

        private static void Flush()
        {
            _flushQueued = false;
            var list = new List<GameObject>(_pending);
            _pending.Clear();
            _meshAdded.Clear();
            if (!Enabled)
                return;
            foreach (var go in list)
                if (go != null && TryGetPrimitive(go, out var kind, out var collider))
                    Swap(go, kind, collider);
        }

        private enum Kind { Cube, Sphere, Capsule, Cylinder, Plane, Quad }

        // The meshes ObjectFactory.CreatePrimitive (what the 3D Object menu calls in Unity 6) assigns, by resource name.
        private static readonly (string resource, Kind kind)[] BuiltinMeshes =
        {
            ("Cube.fbx", Kind.Cube), ("New-Sphere.fbx", Kind.Sphere), ("New-Capsule.fbx", Kind.Capsule),
            ("New-Cylinder.fbx", Kind.Cylinder), ("New-Plane.fbx", Kind.Plane), ("Quad.fbx", Kind.Quad),
        };

        private static bool TryGetKind(Mesh mesh, out Kind kind)
        {
            kind = default;
            foreach (var (resource, candidate) in BuiltinMeshes)
            {
                Mesh builtin = null;
                try { builtin = Resources.GetBuiltinResource<Mesh>(resource); }
                catch (System.Exception) { /* not in this Unity version */ }
                if (builtin != null && builtin == mesh)
                {
                    kind = candidate;
                    return true;
                }
            }
            return false;
        }

        // An untouched built-in primitive: Transform, MeshFilter with Unity's own primitive mesh, MeshRenderer and
        // the PhysX collider Unity gives that primitive, and nothing else.
        private static bool TryGetPrimitive(GameObject go, out Kind kind, out Collider collider)
        {
            kind = default;
            collider = null;
            if (go == null || EditorUtility.IsPersistent(go) || PrefabUtility.IsPartOfPrefabInstance(go))
                return false;
            if (!go.TryGetComponent<MeshFilter>(out var filter) || filter.sharedMesh == null || !go.TryGetComponent<MeshRenderer>(out _))
                return false;
            if (!TryGetKind(filter.sharedMesh, out kind))
                return false;

            var components = go.GetComponents<Component>();
            if (components.Length != 4)
                return false;
            collider = go.GetComponent<Collider>();
            switch (kind)
            {
                case Kind.Cube: return collider is BoxCollider;
                case Kind.Sphere: return collider is SphereCollider;
                case Kind.Capsule:
                case Kind.Cylinder: return collider is CapsuleCollider;
                default: return collider is MeshCollider;
            }
        }

        private static void Swap(GameObject go, Kind kind, Collider collider)
        {
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            int group = Undo.GetCurrentGroup();
            Undo.DestroyObjectImmediate(collider);

            // A primitive made inside an existing Box3D body becomes one of its shapes (a nested body would split
            // the compound); otherwise it gets its own body.
            if (go.transform.parent == null || go.transform.parent.GetComponentInParent<Box3DBody>(true) == null)
            {
                var body = Undo.AddComponent<Box3DBody>(go);
                // Plane and quad use a mesh shape, which only works on static bodies.
                bool isStatic = Box3DPrimitiveSettings.instance.MakeStatic || kind == Kind.Plane || kind == Kind.Quad;
                body.BodyType = isStatic ? Box3DBodyType.Static : Box3DBodyType.Dynamic;
                EditorUtility.SetDirty(body);
            }

            switch (kind)
            {
                // The shapes' defaults match Unity's primitives: 1 m box, 0.5 m radius sphere, 0.5 x 2 m capsule.
                case Kind.Cube: Undo.AddComponent<Box3DBoxShape>(go); break;
                case Kind.Sphere: Undo.AddComponent<Box3DSphereShape>(go); break;
                case Kind.Capsule: Undo.AddComponent<Box3DCapsuleShape>(go); break;
                case Kind.Cylinder:
                {
                    // Unity gives the cylinder a capsule; a convex hull of its own mesh has the real flat ends.
                    var hull = Undo.AddComponent<Box3DHullShape>(go);
                    hull.SetMesh(mesh);
                    EditorUtility.SetDirty(hull);
                    break;
                }
                default:
                {
                    // Plane and quad are flat: an exact (concave-capable, static-only) mesh shape.
                    var shape = Undo.AddComponent<Box3DMeshShape>(go);
                    shape.SetMesh(mesh);
                    EditorUtility.SetDirty(shape);
                    break;
                }
            }
            Undo.CollapseUndoOperations(group);
        }
    }
}
