using Remedy.Schematics;
using UnityEditor;
using UnityEngine;

namespace Remedy.Vehicles.Editor
{
    /// <summary>
    /// Scene view handles for a selected vehicle (in a scene or Prefab Mode): click a seat's or wheel's dot to get a
    /// move handle for it. Seats show their anchor and their exit (an Exit Point transform, or the Exit Offset), wheels
    /// their anchor and tyre. Everything is undoable.
    /// </summary>
    [InitializeOnLoad]
    internal static class VehicleSceneHandles
    {
        private const string PrefKey = "Remedy.Vehicles.SceneHandles";

        // What the move handle is on: a Transform, or a seat's Exit Offset (seat index).
        private static Object _activeTransform;
        private static int _activeExitOffset = -1;
        private static Object _activeOwner;

        static VehicleSceneHandles() => SceneView.duringSceneGui += OnSceneGUI;

        [MenuItem("Tools/Remedy/Vehicles/Scene Handles")]
        private static void Toggle() => EditorPrefs.SetBool(PrefKey, !Enabled);

        [MenuItem("Tools/Remedy/Vehicles/Scene Handles", true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked("Tools/Remedy/Vehicles/Scene Handles", Enabled);
            return true;
        }

        private static bool Enabled => EditorPrefs.GetBool(PrefKey, true);

        private static void OnSceneGUI(SceneView view)
        {
            if (!Enabled || Application.isPlaying)
                return;
            var selected = Selection.activeGameObject;
            if (selected == null || EditorUtility.IsPersistent(selected))
                return;
            var schematic = selected.GetComponentInParent<SchematicObject>(true);
            if (schematic == null || schematic.Behaviors == null)
                return;

            if (_activeOwner != schematic)
            {
                _activeOwner = schematic;
                _activeTransform = null;
                _activeExitOffset = -1;
            }

            foreach (var entry in schematic.Behaviors)
            {
                if (entry.Key == null || entry.Value?.Items == null)
                    continue;
                foreach (var attachment in entry.Value.Items)
                {
                    var type = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                    if (type == typeof(VehicleSeats))
                        DrawSeats(schematic, entry.Key, attachment);
                    else if (type == typeof(ArcadeCar))
                        DrawWheels(attachment);
                }
            }
        }

        private static void DrawSeats(SchematicObject schematic, GameObject target, BehaviorAttachment attachment)
        {
            var seats = VehicleSeatEditing.ReadSeats(attachment);
            if (seats == null)
                return;

            for (int i = 0; i < seats.Length; i++)
            {
                var seat = seats[i];
                if (seat?.Anchor == null) continue;
                var anchor = seat.Anchor;
                Handles.color = VehicleSeats.SeatColor(i);

                // The anchor.
                MovableTransform(anchor, 0.12f, "Move Seat");

                // The exit: its own transform, or the offset from the anchor.
                if (seat.ExitPoint != null)
                    MovableTransform(seat.ExitPoint, 0.09f, "Move Seat Exit");
                else
                {
                    Vector3 exit = VehicleSeats.ExitTarget(anchor, seat.ExitOffset);
                    float size = HandleUtility.GetHandleSize(exit) * 0.09f;
                    if (Handles.Button(exit, Quaternion.identity, size, size * 1.2f, Handles.CubeHandleCap))
                    {
                        _activeTransform = null;
                        _activeExitOffset = i;
                    }
                    if (_activeExitOffset == i)
                    {
                        EditorGUI.BeginChangeCheck();
                        Vector3 moved = Handles.PositionHandle(exit, anchor.rotation);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Vector3 offset = Quaternion.Inverse(anchor.rotation) * (moved - anchor.position);
                            int index = i;
                            string path = AnimationUtility.CalculateTransformPath(target.transform, schematic.transform);
                            VehicleSeatEditing.Edit(schematic.gameObject, path, "Move Seat Exit", scope =>
                            {
                                if (index >= scope.Seats.Length || scope.Seats[index] == null) return false;
                                scope.Seats[index].ExitOffset = offset;
                                return true;
                            });
                        }
                    }
                }
            }
        }

        private static void DrawWheels(BehaviorAttachment attachment)
        {
            var wheels = attachment.FindOverride("Wheels")?.Value?.Get<Wheel[]>();
            if (wheels == null)
                return;
            Handles.color = new Color(1f, 0.6f, 0.1f);
            foreach (var wheel in wheels)
            {
                if (wheel?.Anchor == null) continue;
                MovableTransform(wheel.Anchor, 0.1f, "Move Wheel");
                Handles.DrawWireDisc(wheel.Anchor.position, wheel.Anchor.right, Mathf.Max(0.05f, wheel.Radius));
            }
        }

        // A dot that, once clicked, gets a move handle.
        private static void MovableTransform(Transform transform, float size, string undoName)
        {
            Vector3 position = transform.position;
            float handleSize = HandleUtility.GetHandleSize(position) * size;
            if (Handles.Button(position, Quaternion.identity, handleSize, handleSize * 1.2f, Handles.SphereHandleCap))
            {
                _activeTransform = transform;
                _activeExitOffset = -1;
            }
            if (_activeTransform != transform || Selection.activeTransform == transform)
                return;

            EditorGUI.BeginChangeCheck();
            var rotation = Tools.pivotRotation == PivotRotation.Local ? transform.rotation : Quaternion.identity;
            Vector3 moved = Handles.PositionHandle(position, rotation);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(transform, undoName);
                transform.position = moved;
            }
        }
    }
}
