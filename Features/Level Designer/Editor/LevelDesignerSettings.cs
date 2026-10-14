using System;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesigner.Editor
{
    /// <summary>
    /// The Level Designer's per-user preferences (kept in EditorPrefs, so they're personal and not committed):
    /// the blockout X-ray overlay, snapping and the Ctrl ruler.
    /// </summary>
    public static class LevelDesignerSettings
    {
        private const string Prefix = "Remedy.LevelDesigner.";

        /// <summary>Raised when any setting changes (the window, overlays and Scene view refresh).</summary>
        public static event Action Changed;

        /// <summary>Shows the blockout as an X-ray while editing one of its art designs.</summary>
        public static bool XRayEnabled
        {
            get => EditorPrefs.GetBool(Prefix + "XRay", true);
            set => Set(() => EditorPrefs.SetBool(Prefix + "XRay", value));
        }

        /// <summary>How strongly the X-ray shows (0..1).</summary>
        public static float XRayOpacity
        {
            get => EditorPrefs.GetFloat(Prefix + "XRayOpacity", 0.35f);
            set => Set(() => EditorPrefs.SetFloat(Prefix + "XRayOpacity", Mathf.Clamp01(value)));
        }

        /// <summary>Tints the X-ray with each shape's category colour instead of one colour.</summary>
        public static bool XRayCategoryColors
        {
            get => EditorPrefs.GetBool(Prefix + "XRayCategoryColors", false);
            set => Set(() => EditorPrefs.SetBool(Prefix + "XRayCategoryColors", value));
        }

        /// <summary>The snapping step for placing and sizing blockout shapes, in meters (0 = off).</summary>
        public static float Snap
        {
            get => EditorPrefs.GetFloat(Prefix + "Snap", 0.5f);
            set => Set(() => EditorPrefs.SetFloat(Prefix + "Snap", Mathf.Max(0f, value)));
        }

        /// <summary>The snapping steps offered in menus.</summary>
        public static readonly float[] SnapSteps = { 0f, 0.125f, 0.25f, 0.5f, 1f, 2f };

        /// <summary>Holding Ctrl and dragging in a blockout's Scene view measures with a temporary ruler.</summary>
        public static bool CtrlRuler
        {
            get => EditorPrefs.GetBool(Prefix + "CtrlRuler", true);
            set => Set(() => EditorPrefs.SetBool(Prefix + "CtrlRuler", value));
        }

        /// <summary>
        /// Keeps every Ctrl + drag measurement as a Distance ruler when the mouse is let go (otherwise only when Alt is
        /// held on release).
        /// </summary>
        public static bool KeepCtrlRulers
        {
            get => EditorPrefs.GetBool(Prefix + "KeepCtrlRulers", false);
            set => Set(() => EditorPrefs.SetBool(Prefix + "KeepCtrlRulers", value));
        }

        /// <summary>Rounds a length to the snapping step (lengths never snap to zero).</summary>
        public static float SnapLength(float value)
        {
            float step = Snap;
            if (step <= 0f)
                return value;
            return Mathf.Max(step, Mathf.Round(value / step) * step);
        }

        /// <summary>Rounds a signed length to the snapping step (keeps its sign; zero stays possible).</summary>
        public static float SnapSigned(float value)
        {
            float step = Snap;
            if (step <= 0f)
                return value;
            return Mathf.Round(value / step) * step;
        }

        /// <summary>Rounds a position to the snapping grid.</summary>
        public static Vector3 SnapPoint(Vector3 point)
        {
            float step = Snap;
            if (step <= 0f)
                return point;
            return new Vector3(Mathf.Round(point.x / step) * step, Mathf.Round(point.y / step) * step, Mathf.Round(point.z / step) * step);
        }

        private static void Set(Action write)
        {
            write();
            Changed?.Invoke();
            SceneView.RepaintAll();
        }
    }
}
