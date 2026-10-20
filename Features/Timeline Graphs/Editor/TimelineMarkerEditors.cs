using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.TimelineGraphs
{
    /// <summary>
    /// Pickers for the Schematic markers and clips: the Timeline Graph's own Signals and Variables, and those of the
    /// Behaviors on its prefab, so markers are set up by choosing from lists rather than typing names.
    /// </summary>
    internal static class TimelinePickers
    {
        /// <summary>A Signal choice: its label, asset and where it is.</summary>
        public struct SignalChoice { public string Label; public SignalData Signal; public string Path; }

        public static List<SignalChoice> Signals(TimelineGraph graph)
        {
            var choices = new List<SignalChoice>();
            if (graph == null) return choices;
            foreach (var signal in graph.Signals)
                if (signal?.Asset != null)
                    choices.Add(new SignalChoice { Label = $"{graph.name}/{signal.Name}", Signal = signal.Asset, Path = "" });
            foreach (var (path, metadata) in Behaviors(graph))
                foreach (var signal in metadata.Signals)
                    if (signal.Asset != null)
                        choices.Add(new SignalChoice
                        {
                            Label = $"{(string.IsNullOrEmpty(path) ? "" : path + "/")}{metadata.BehaviorType.Type?.Name}/{ObjectNames.NicifyVariableName(signal.Name)}",
                            Signal = signal.Asset, Path = path,
                        });
            return choices;
        }

        /// <summary>The Behaviors on the graph's prefab: (path from the root, metadata).</summary>
        public static IEnumerable<(string path, BehaviorMetadata metadata)> Behaviors(SchematicGraph graph)
        {
            var root = graph != null && graph.Prefab != null ? graph.Prefab.GetComponent<SchematicObject>() : null;
            if (root == null) yield break;
            foreach (var pair in root.Behaviors)
            {
                if (pair.Key == null || pair.Value?.Items == null) continue;
                string path = pair.Key == root.gameObject ? "" : AnimationUtility.CalculateTransformPath(pair.Key.transform, root.transform);
                foreach (var attachment in pair.Value.Items)
                    if (attachment?.Metadata != null && attachment.Metadata.BehaviorType.Type != null)
                        yield return (path, attachment.Metadata);
            }
        }

        /// <summary>Draws a Variable picker. True if it changed.</summary>
        public static bool PickVariable(VariableTarget target, TimelineGraph owner)
        {
            var labels = new List<string>();
            var apply = new List<Action>();
            int current = -1;

            var graph = target.Graph != null ? target.Graph : owner;
            if (graph != null)
                foreach (var variable in graph.Variables.Where(v => v != null))
                {
                    var v = variable;
                    if (target.Kind == VariableTarget.TargetKind.GraphVariable && target.VariableId == v.Id) current = labels.Count;
                    labels.Add($"Graph: {graph.name}/{v.Name}");
                    apply.Add(() =>
                    {
                        target.Kind = VariableTarget.TargetKind.GraphVariable;
                        target.VariableId = v.Id;
                        target.Type = v.ValueType;
                    });
                }
            foreach (var (path, metadata) in Behaviors(owner))
                foreach (var variable in metadata.Variables.Where(v => v != null))
                {
                    var v = variable;
                    var behavior = metadata.BehaviorType.Type;
                    var p = path;
                    if (target.Kind == VariableTarget.TargetKind.BehaviorVariable
                        && target.Behavior.Type == behavior && target.Path == p && target.Variable == v.Name)
                        current = labels.Count;
                    labels.Add($"{(string.IsNullOrEmpty(p) ? "" : p + "/")}{behavior.Name}/{ObjectNames.NicifyVariableName(v.Name)}");
                    apply.Add(() =>
                    {
                        target.Kind = VariableTarget.TargetKind.BehaviorVariable;
                        target.Behavior = behavior;
                        target.Path = p;
                        target.Variable = v.Name;
                        target.Type = v.Type.Type;
                    });
                }

            if (labels.Count == 0)
            {
                EditorGUILayout.HelpBox(owner == null
                    ? "This Timeline isn't any Timeline Graph's yet."
                    : "No Variables to pick: add some in the graph's Graph tab, or give the graph a prefab with Behaviors.", MessageType.Info);
                return false;
            }
            if (current < 0) labels.Insert(0, "(pick a Variable)");
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("Variable", current < 0 ? 0 : current, labels.ToArray());
            if (!EditorGUI.EndChangeCheck()) return false;
            int index = current < 0 ? picked - 1 : picked;
            if (index < 0) return false;
            apply[index]();
            return true;
        }
    }

    [CustomEditor(typeof(SignalMarker))]
    internal class SignalMarkerEditor : Editor
    {
        private static Type TypeOf(SerializableType type) => type.Type;

        public override void OnInspectorGUI()
        {
            var marker = (SignalMarker)target;
            var owner = TimelineGraphEditor.OwnerOf(marker);
            if (owner != null)
                EditorGUILayout.LabelField($"Timeline Graph: {owner.name}", EditorStyles.miniLabel);

            var choices = TimelinePickers.Signals(owner);
            int current = choices.FindIndex(c => c.Signal == marker.Signal);
            var labels = choices.Select(c => c.Label).ToList();
            labels.Insert(0, current < 0 && marker.Signal != null ? $"(other) {marker.Signal.name}" : "(pick a Signal)");
            EditorGUI.BeginChangeCheck();
            int picked = EditorGUILayout.Popup("Signal", current + 1, labels.ToArray());
            var other = EditorGUILayout.ObjectField(" ", marker.Signal, typeof(SignalData), false) as SignalData;
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(marker, "Pick Signal");
                if (picked > 0)
                {
                    marker.Signal = choices[picked - 1].Signal;
                    marker.TargetPath = choices[picked - 1].Path;
                }
                else
                    marker.Signal = other;
                EditorUtility.SetDirty(marker);
            }

            EditorGUI.BeginChangeCheck();
            bool everyone = EditorGUILayout.Toggle(new GUIContent("Everyone", "Send it to every object, not just the one playing the Timeline."), marker.Everyone);
            string path;
            using (new EditorGUI.DisabledScope(everyone))
                path = EditorGUILayout.TextField(new GUIContent("To Child", "Path of a child of the object playing the Timeline. Empty: the object itself."), marker.TargetPath);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(marker, "Signal Marker");
                marker.Everyone = everyone;
                marker.TargetPath = path;
                EditorUtility.SetDirty(marker);
            }

            // One field per parameter, kept in step with the Signal's parameters.
            var parameters = SchematicGraphEditorWindow.SignalParameters(marker.Signal);
            if (parameters.Count > 0)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            }
            bool reshaped = marker.Arguments.Count != parameters.Count
                || parameters.Where((p, i) => marker.Arguments[i].Name != p.name || TypeOf(marker.Arguments[i].Type) != p.type).Any();
            if (reshaped)
            {
                Undo.RecordObject(marker, "Signal Marker");
                var old = marker.Arguments.ToDictionary(a => a.Name ?? "", a => a);
                marker.Arguments = parameters.Select(p => old.TryGetValue(p.name, out var keep) && TypeOf(keep.Type) == p.type
                    ? keep : new MarkerArgument { Name = p.name, Type = p.type, Value = new SchematicValue(p.type.IsValueType ? Activator.CreateInstance(p.type) : null, p.type) }).ToList();
                EditorUtility.SetDirty(marker);
            }
            foreach (var argument in marker.Arguments)
            {
                var type = TypeOf(argument.Type);
                if (type == null) continue;
                if (SchematicValueField.Draw(new GUIContent(ObjectNames.NicifyVariableName(argument.Name)), type, argument.Value.Get(type), out var value))
                {
                    Undo.RecordObject(marker, "Signal Marker");
                    argument.Value = new SchematicValue(value, type);
                    EditorUtility.SetDirty(marker);
                }
            }

            EditorGUILayout.Space(4);
            DrawFlags(marker);
        }

        internal static void DrawFlags(SchematicMarker marker)
        {
            EditorGUI.BeginChangeCheck();
            bool retroactive = EditorGUILayout.Toggle(new GUIContent("Retroactive", "Also fire when the Timeline starts (or jumps) past it."), marker.Retroactive);
            bool once = EditorGUILayout.Toggle(new GUIContent("Only Once", "Fire only the first time (not on loops or replays)."), marker.OnlyOnce);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(marker, "Marker");
                marker.Retroactive = retroactive;
                marker.OnlyOnce = once;
                EditorUtility.SetDirty(marker);
            }
        }
    }

    [CustomEditor(typeof(VariableMarker))]
    internal class VariableMarkerEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var marker = (VariableMarker)target;
            var owner = TimelineGraphEditor.OwnerOf(marker);
            Undo.RecordObject(marker, "Variable Marker");
            if (TimelinePickers.PickVariable(marker.Target, owner))
            {
                var type = marker.Target.ValueType;
                marker.Value = new SchematicValue(type != null && type.IsValueType ? Activator.CreateInstance(type) : null, type);
                EditorUtility.SetDirty(marker);
            }
            var valueType = marker.Target.ValueType;
            if (valueType != null && SchematicValueField.Draw(new GUIContent("Set To"), valueType, marker.Value.Get(valueType), out var value))
            {
                marker.Value = new SchematicValue(value, valueType);
                EditorUtility.SetDirty(marker);
            }
            EditorGUILayout.Space(4);
            SignalMarkerEditor.DrawFlags(marker);
        }
    }

    [CustomEditor(typeof(VariableClip))]
    internal class VariableClipEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var clip = (VariableClip)target;
            var owner = TimelineGraphEditor.OwnerOf(clip);
            Undo.RecordObject(clip, "Variable Clip");
            if (TimelinePickers.PickVariable(clip.Target, owner))
            {
                var type = clip.Target.ValueType;
                object zero = type != null && type.IsValueType ? Activator.CreateInstance(type) : null;
                clip.From = new SchematicValue(zero, type);
                clip.To = new SchematicValue(zero, type);
                EditorUtility.SetDirty(clip);
            }
            var valueType = clip.Target.ValueType;
            if (valueType != null)
            {
                if (SchematicValueField.Draw(new GUIContent("From"), valueType, clip.From.Get(valueType), out var from))
                {
                    clip.From = new SchematicValue(from, valueType);
                    EditorUtility.SetDirty(clip);
                }
                if (SchematicValueField.Draw(new GUIContent("To"), valueType, clip.To.Get(valueType), out var to))
                {
                    clip.To = new SchematicValue(to, valueType);
                    EditorUtility.SetDirty(clip);
                }
            }
            EditorGUI.BeginChangeCheck();
            var curve = EditorGUILayout.CurveField(new GUIContent("Curve", "How it moves from From to To across the clip."), clip.Curve);
            if (EditorGUI.EndChangeCheck())
            {
                clip.Curve = curve;
                EditorUtility.SetDirty(clip);
            }
        }
    }
}
