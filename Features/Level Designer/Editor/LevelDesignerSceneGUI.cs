using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Shared Scene view helpers for the blockout tools: finding the surface under the mouse, measurement labels,
    /// and making rulers.
    /// </summary>
    public static class LevelDesignerSceneGUI
    {
        /// <summary>A point on a surface (or on the ground plane, when nothing was under the mouse).</summary>
        public struct SurfaceHit
        {
            public Vector3 Point;
            public Vector3 Normal;
            public BlockoutShape Shape;
            /// <summary>False when the ray missed everything and the point is on the ground plane (y = 0).</summary>
            public bool OnSurface;
        }

        /// <summary>
        /// The surface under a GUI point: blockout shapes first (exact), then any other mesh in the Scene view, then
        /// the ground plane at y = 0.
        /// </summary>
        public static bool Raycast(Vector2 guiPoint, out SurfaceHit hit)
        {
            hit = default;
            var ray = HandleUtility.GUIPointToWorldRay(guiPoint);

            if (BlockoutShape.RaycastAll(ray, 10000f, out var shapeHit))
            {
                hit = new SurfaceHit { Point = shapeHit.Point, Normal = shapeHit.Normal, Shape = shapeHit.Shape, OnSurface = true };
                // Something else may still be in front of the blockout (a placed Schematic).
                if (HandleUtility.PlaceObject(guiPoint, out var otherPoint, out var otherNormal) &&
                    Vector3.Distance(ray.origin, otherPoint) + 0.01f < shapeHit.Distance)
                    hit = new SurfaceHit { Point = otherPoint, Normal = otherNormal, OnSurface = true };
                return true;
            }

            if (HandleUtility.PlaceObject(guiPoint, out var point, out var normal))
            {
                hit = new SurfaceHit { Point = point, Normal = normal, OnSurface = true };
                return true;
            }

            var ground = new Plane(Vector3.up, Vector3.zero);
            if (ground.Raycast(ray, out float distance))
            {
                hit = new SurfaceHit { Point = ray.GetPoint(distance), Normal = Vector3.up, OnSurface = false };
                return true;
            }
            return false;
        }

        /// <summary>The world axis closest to a direction (signed).</summary>
        public static Vector3 NearestAxis(Vector3 direction)
        {
            var abs = new Vector3(Mathf.Abs(direction.x), Mathf.Abs(direction.y), Mathf.Abs(direction.z));
            if (abs.y >= abs.x && abs.y >= abs.z)
                return new Vector3(0f, Mathf.Sign(direction.y), 0f);
            if (abs.x >= abs.z)
                return new Vector3(Mathf.Sign(direction.x), 0f, 0f);
            return new Vector3(0f, 0f, Mathf.Sign(direction.z));
        }

        /// <summary>The point on a line (through <paramref name="origin"/> along <paramref name="axis"/>) closest to a ray.</summary>
        public static float ClosestOnLine(Ray ray, Vector3 origin, Vector3 axis)
        {
            axis.Normalize();
            Vector3 w = origin - ray.origin;
            float b = Vector3.Dot(axis, ray.direction);
            float d = Vector3.Dot(axis, w);
            float e = Vector3.Dot(ray.direction, w);
            float denominator = 1f - b * b;
            if (Mathf.Abs(denominator) < 1e-6f)
                return 0f;
            return (b * e - d) / denominator;
        }

        // ---------------------------------------------------------------- labels

        private static GUIStyle _labelStyle;
        private static Texture2D _labelBackground;

        /// <summary>A small readable label (dark box, coloured text) at a world position.</summary>
        public static void Label(Vector3 position, string text, Color color)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            if (_labelStyle == null || _labelBackground == null)
            {
                _labelBackground = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                _labelBackground.SetPixel(0, 0, new Color(0.08f, 0.08f, 0.1f, 0.82f));
                _labelBackground.Apply();
                _labelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(4, 4, 1, 1),
                    richText = true,
                };
                _labelStyle.normal.background = _labelBackground;
            }
            _labelStyle.normal.textColor = color;
            Handles.Label(position, text, _labelStyle);
        }

        /// <summary>A length as a label string.</summary>
        public static string Meters(float value) => BlockoutShape.FormatMeters(value) + " m";

        /// <summary>
        /// Draws a measurement between two points: the line, its horizontal and vertical legs, and their lengths.
        /// </summary>
        public static void DrawMeasurement(Vector3 a, Vector3 b, Color color, string title = null)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var corner = new Vector3(b.x, a.y, b.z);
            float total = Vector3.Distance(a, b);
            float horizontal = Vector3.Distance(a, corner);
            float vertical = b.y - a.y;

            // The end discs face along the ruler (their normal is the ruler's direction), like the end caps of a
            // tape measure, so they read as "ends" from any angle instead of lying flat on the ground.
            Vector3 along = b - a;
            Vector3 discNormal = along.sqrMagnitude > 1e-8f ? along.normalized : Vector3.up;
            using (new Handles.DrawingScope(color))
            {
                Handles.DrawLine(a, b, 3f);
                Handles.DrawWireDisc(a, discNormal, 0.1f);
                Handles.DrawWireDisc(b, discNormal, 0.1f);
            }
            using (new Handles.DrawingScope(new Color(color.r, color.g, color.b, 0.45f)))
            {
                if (horizontal > 0.01f && Mathf.Abs(vertical) > 0.01f)
                {
                    Handles.DrawDottedLine(a, corner, 3f);
                    Handles.DrawDottedLine(corner, b, 3f);
                }
            }

            string text = (string.IsNullOrEmpty(title) ? "" : title + "\n") + Meters(total);
            Label((a + b) * 0.5f, text, color);
            if (horizontal > 0.01f && Mathf.Abs(vertical) > 0.01f)
            {
                Label((a + corner) * 0.5f, "↔ " + Meters(horizontal), new Color(0.75f, 0.85f, 1f));
                Label((corner + b) * 0.5f, "↕ " + Meters(vertical), new Color(0.75f, 1f, 0.8f));
            }
        }

        // ---------------------------------------------------------------- rulers

        /// <summary>
        /// Makes a ruler object in the active scene (undoable, EditorOnly so builds leave it out). It takes the
        /// blockout toolbar's current colour when one is picked (see <see cref="LevelDesignerSettings.CurrentColor"/>).
        /// </summary>
        public static BlockoutRuler CreateRuler(BlockoutRulerKind kind, Vector3 position, Quaternion rotation, string name = null)
        {
            var go = new GameObject(name ?? kind + " Ruler");
            SceneManager.MoveGameObjectToScene(go, SceneManager.GetActiveScene());
            go.tag = "EditorOnly";
            go.transform.SetPositionAndRotation(position, rotation);
            var parent = RulerRoot();
            if (parent != null)
                go.transform.SetParent(parent, true);
            var ruler = go.AddComponent<BlockoutRuler>();
            ruler.Kind = kind;
            if (kind == BlockoutRulerKind.RunDistance)
                ruler.Color = new Color(0.45f, 0.75f, 1f);
            if (LevelDesignerSettings.TryGetCurrentColor(out var current))
                ruler.Color = current;
            Undo.RegisterCreatedObjectUndo(go, "Create Ruler");
            BlockoutRulerDrawer.MarkDirty();
            return ruler;
        }

        // Rulers are kept together under one object per scene, so they're easy to hide or clear.
        private static Transform RulerRoot()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
                if (root.name == "[Rulers]")
                    return root.transform;
            var go = new GameObject("[Rulers]");
            SceneManager.MoveGameObjectToScene(go, scene);
            go.tag = "EditorOnly";
            Undo.RegisterCreatedObjectUndo(go, "Create Ruler");
            return go.transform;
        }
    }
}
