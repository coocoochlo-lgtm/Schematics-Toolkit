using UnityEditor;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Editor
{
    /// <summary>
    /// The config asset's inspector: every setting, plus what they add up to (weights, copies per training window, the
    /// layout signature saved brains must match) and whether the network can run with them.
    /// </summary>
    [CustomEditor(typeof(MnnConfig))]
    public class MnnConfigEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var config = (MnnConfig)target;
            var s = config.Settings;
            if (s == null)
                return;

            EditorGUILayout.Space();
            try
            {
                s.Validate();
                var layout = new WeightLayout(s);
                EditorGUILayout.HelpBox(
                    $"{layout.Count:N0} trainable values ({layout.MlpCount:N0} MLP weights" +
                    (layout.RulesCount > 0 ? $", {layout.RulesCount} rulebook values" : "") +
                    (layout.BetaCount > 0 ? ", memory β" : "") +
                    (layout.ValuesCount > 0 ? $", {layout.ValuesCount:N0} point values" : "") + ").\n" +
                    $"{s.CopiesPerWindow} copies run per instance while training ({s.perturbMode}, {s.perturbPairs} pairs).\n" +
                    $"Heads move at {s.tickRate:0.#} Hz nominal; base timescales {s.timescaleMin:0.###}–{s.timescaleMax:0.###} s." +
                    (s.spatialGrid && !s.UsesSpatialGrid ? "\nThe spatial grid is off: it needs D ≤ 3 and dispersion ≤ 0.22." : "") +
                    $"\nLayout signature: {s.LayoutSignature}",
                    MessageType.Info);
            }
            catch (System.ArgumentException exception)
            {
                EditorGUILayout.HelpBox(exception.Message, MessageType.Error);
            }
        }
    }
}
