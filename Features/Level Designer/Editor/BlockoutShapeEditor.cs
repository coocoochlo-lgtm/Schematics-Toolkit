using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// Inspector and Scene view handles for <see cref="BlockoutShape"/>: drag a face's handle to resize the shape
    /// (snapped to the Level Designer's grid, the opposite face stays put), with the size shown in meters.
    /// </summary>
    [CustomEditor(typeof(BlockoutShape))]
    [CanEditMultipleObjects]
    public class BlockoutShapeEditor : UnityEditor.Editor
    {
        private static readonly Vector3[] Faces =
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.forward, Vector3.back,
        };

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            bool changed = EditorGUI.EndChangeCheck();
            serializedObject.ApplyModifiedProperties();
            if (changed)
                foreach (var t in targets)
                    ((BlockoutShape)t).Rebuild();

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Snap Size", "Round the size to the Level Designer's snapping step")))
                {
                    foreach (var t in targets)
                    {
                        var shape = (BlockoutShape)t;
                        Undo.RecordObject(shape, "Snap Blockout Size");
                        shape.Size = new Vector3(LevelDesignerSettings.SnapLength(shape.Size.x), LevelDesignerSettings.SnapLength(shape.Size.y), LevelDesignerSettings.SnapLength(shape.Size.z));
                        shape.Rebuild();
                    }
                }
                if (GUILayout.Button(new GUIContent("Apply Scale to Size", "Fold the object's scale into its size, so measurements stay exact")))
                {
                    foreach (var t in targets)
                    {
                        var shape = (BlockoutShape)t;
                        Undo.RecordObjects(new Object[] { shape, shape.transform }, "Apply Scale to Size");
                        var scale = shape.transform.localScale;
                        shape.Size = Vector3.Scale(shape.Size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                        shape.transform.localScale = Vector3.one;
                        shape.Rebuild();
                    }
                }
            }

            if (targets.Length == 1)
            {
                var shape = (BlockoutShape)target;
                var size = shape.WorldSize;
                EditorGUILayout.HelpBox($"{BlockoutShape.FormatMeters(size.x)} m wide, {BlockoutShape.FormatMeters(size.y)} m high, {BlockoutShape.FormatMeters(size.z)} m long.", MessageType.None);
                if (shape.transform.lossyScale != Vector3.one)
                    EditorGUILayout.HelpBox("This shape is scaled. Prefer changing Size, so the grid and labels stay exact.", MessageType.Info);
            }
        }

        private void OnSceneGUI()
        {
            var shape = (BlockoutShape)target;
            if (shape == null || Tools.current == Tool.View)
                return;

            var transform = shape.transform;

            // While the Blockout Place tool is active, clicks belong to placing: the face handles would otherwise sit
            // under the mouse (the shape just placed stays selected) and a click meant to start the next shape
            // would grab one and resize this shape instead. Only the size label is shown then.
            if (BlockoutPlaceTool.IsActive || BlockoutPaintTool.IsActive)
            {
                DrawSizeLabel(shape);
                return;
            }

            var size = shape.Size;
            var matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);

            using (new Handles.DrawingScope(new Color(1f, 0.85f, 0.3f), matrix))
            {
                foreach (var face in Faces)
                {
                    // Face centre in the shape's local space (pivot at the centre of the bottom face).
                    Vector3 center = new Vector3(0f, size.y * 0.5f, 0f) + Vector3.Scale(face, size * 0.5f);
                    float handleSize = HandleUtility.GetHandleSize(matrix.MultiplyPoint3x4(center)) * 0.08f;
                    EditorGUI.BeginChangeCheck();
                    Vector3 moved = Handles.Slider(center, face, handleSize, Handles.CubeHandleCap, 0f);
                    if (!EditorGUI.EndChangeCheck())
                        continue;

                    float axisSize = Mathf.Abs(Vector3.Dot(size, face));
                    float delta = Vector3.Dot(moved - center, face);
                    float newSize = LevelDesignerSettings.SnapLength(Mathf.Max(0.05f, axisSize + delta));
                    float grown = newSize - axisSize;
                    if (Mathf.Abs(grown) < 1e-4f)
                        continue;

                    Undo.RecordObjects(new Object[] { shape, transform }, "Resize Blockout Shape");
                    var newShapeSize = size;
                    if (face.x != 0f) newShapeSize.x = newSize;
                    else if (face.y != 0f) newShapeSize.y = newSize;
                    else newShapeSize.z = newSize;
                    shape.Size = newShapeSize;

                    // Keep the opposite face where it was: sideways faces move the pivot by half the growth; the top
                    // just grows (the pivot is on the bottom face).
                    if (face.y == 0f)
                        transform.position += transform.TransformVector(face * (grown * 0.5f));
                    shape.Rebuild();
                    size = shape.Size;
                }
            }

            DrawSizeLabel(shape);
        }

        // The shape's total size, in a box above it (the faces only label their 1 m cells).
        private static void DrawSizeLabel(BlockoutShape shape)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var transform = shape.transform;
            var world = shape.WorldSize;
            Vector3 top = transform.position + transform.up * world.y;
            LevelDesignerSceneGUI.Label(top + transform.up * 0.3f,
                $"{BlockoutShape.FormatMeters(world.x)} x {BlockoutShape.FormatMeters(world.y)} x {BlockoutShape.FormatMeters(world.z)} m", Color.white);
        }
    }
}
