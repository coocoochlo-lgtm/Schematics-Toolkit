using BlueGraph;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics.Assistant
{
    /// <summary>The server's reply: what to say, and the edits to make.</summary>
    [Serializable]
    internal class AssistantReply
    {
        public string say;
        public AssistantCommand[] commands;
    }

    /// <summary>One edit. Every command has every field; each op reads the ones it needs.</summary>
    [Serializable]
    internal class AssistantCommand
    {
        public string op, id, type, node, from, from_port, to, to_port, behavior, signal, variable, mode, path, field, value, name;
        public float x, y, z;

        public override string ToString() => op switch
        {
            "add_node" => $"add {type}",
            "add_signal_node" => $"{(mode == "send" ? "send" : "on")} {signal}",
            "add_variable_node" => $"{mode} {variable}",
            "connect" => $"connect {from} → {to}",
            _ => op,
        };
    }

    /// <summary>
    /// Carries out the assistant's commands one at a time, flying the assistant to each place it edits, all as one
    /// Undo step. A command that can't be done is reported and skipped; the rest still run.
    /// </summary>
    internal sealed class AssistantExecutor
    {
        private readonly AssistantContext _context;
        private readonly AssistantOverlay _overlay;
        private readonly Dictionary<string, Node> _made = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _problems = new();
        private Vector2? _lastPosition;
        private bool _needsRefresh;

        private SchematicGraphEditorWindow Window => _context.Window;

        public IReadOnlyList<string> Problems => _problems;

        public AssistantExecutor(AssistantContext context, AssistantOverlay overlay)
        {
            _context = context;
            _overlay = overlay;
        }

        public async Task RunAsync(IReadOnlyList<AssistantCommand> commands, CancellationToken cancellation)
        {
            if (commands == null || commands.Count == 0)
                return;

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Assistant");
            try
            {
                foreach (var command in commands)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (Window == null)
                        return;
                    try
                    {
                        string problem = await RunAsync(command);
                        if (problem != null)
                            _problems.Add($"{command}: {problem}");
                    }
                    catch (Exception exception)
                    {
                        _problems.Add($"{command}: {exception.Message}");
                        Debug.LogException(exception);
                    }
                    await Delay(AssistantSettings.StepDelay, cancellation);
                }
            }
            finally
            {
                if (_needsRefresh && Window != null)
                    Window.AssistantRefresh();
                Undo.CollapseUndoOperations(group);
                if (_problems.Count > 0)
                    Debug.LogWarning("[Assistant] Some steps didn't work:\n" + string.Join("\n", _problems));
            }
        }

        private async Task<string> RunAsync(AssistantCommand c)
        {
            switch (c.op)
            {
                case "add_node":
                {
                    if (!TryNodeType(c.type, out var type))
                        return $"there's no node type \"{c.type}\"";
                    var position = await PlaceAsync(c);
                    return Made(c, Window.AssistantAddNode(type, position));
                }
                case "add_signal_node":
                {
                    if (!TryBehavior(c.behavior, c.path, out var behavior, out string path, out var metadata))
                        return $"no Behavior \"{c.behavior}\" on the prefab";
                    var signal = metadata.Signals.FirstOrDefault(s => Same(s.Name, c.signal));
                    if (signal == null || signal.Asset == null)
                        return $"{behavior.Name} has no Signal \"{c.signal}\"";
                    bool send = c.mode == "send" || (c.mode != "receive" && signal.Direction == SignalDirection.Input);
                    var position = await PlaceAsync(c);
                    return Made(c, Window.AssistantAddSignalNode(signal.Asset, send, position));
                }
                case "add_variable_node":
                {
                    if (!TryBehavior(c.behavior, c.path, out var behavior, out string path, out var metadata))
                        return $"no Behavior \"{c.behavior}\" on the prefab";
                    var variable = metadata.Variables.FirstOrDefault(v => Same(v.Name, c.variable));
                    if (variable == null)
                        return $"{behavior.Name} has no Variable \"{c.variable}\"";
                    var position = await PlaceAsync(c);
                    return Made(c, Window.AssistantAddVariableNode(behavior, path, variable.Name, c.mode == "set", position));
                }
                case "add_behavior_node":
                {
                    if (!TryBehavior(c.behavior, c.path, out var behavior, out string path, out _))
                        return $"no Behavior \"{c.behavior}\" on the prefab";
                    var position = await PlaceAsync(c);
                    return Made(c, Window.AssistantAddBehaviorNode(behavior, path, position));
                }
                case "connect":
                {
                    var from = Find(c.from);
                    var to = Find(c.to);
                    if (from == null) return $"no node \"{c.from}\"";
                    if (to == null) return $"no node \"{c.to}\"";
                    await FlyToAsync(to);
                    return Window.AssistantConnect(from, c.from_port, to, c.to_port);
                }
                case "set_value":
                {
                    var node = Find(c.node);
                    if (node == null) return $"no node \"{c.node}\"";
                    await FlyToAsync(node);
                    return SetValue(node, c.field, c.value);
                }
                case "move_node":
                {
                    var node = Find(c.node);
                    if (node == null) return $"no node \"{c.node}\"";
                    await FlyToAsync(node);
                    Window.AssistantMove(node, new Vector2(c.x, c.y));
                    return null;
                }
                case "delete_node":
                {
                    var node = Find(c.node);
                    if (node == null) return $"no node \"{c.node}\"";
                    await FlyToAsync(node);
                    Window.AssistantDelete(node);
                    return null;
                }
                case "focus":
                {
                    var node = Find(c.node);
                    if (node == null) return $"no node \"{c.node}\"";
                    Window.AssistantFocus(node);
                    await FlyToAsync(node);
                    return null;
                }
                case "add_behavior":
                {
                    var type = BehaviorType(c.behavior);
                    if (type == null) return $"no Behavior called \"{c.behavior}\"";
                    return Window.AssistantAddBehavior(c.path, type) ? null : "the prefab's IO Dock isn't open";
                }
                case "create_object":
                {
                    var created = new GameObject(string.IsNullOrWhiteSpace(c.name) ? "New Object" : c.name);
                    created.transform.position = new Vector3(c.x, c.y, c.z);
                    Undo.RegisterCreatedObjectUndo(created, "Create Object");
                    Selection.activeGameObject = created;
                    return null;
                }
                case "select_object":
                {
                    var found = GameObject.Find(c.name);
                    if (found == null) return $"no object \"{c.name}\" in the scene";
                    Selection.activeGameObject = found;
                    EditorGUIUtility.PingObject(found);
                    return null;
                }
                default:
                    return "unknown command";
            }
        }

        #region Placing and flying

        // Where a new node goes: where the model said, else to the right of the last one placed.
        private async Task<Vector2> PlaceAsync(AssistantCommand c)
        {
            Vector2 position;
            if (c.x != 0 || c.y != 0)
                position = new Vector2(c.x, c.y);
            else if (_lastPosition.HasValue)
                position = _lastPosition.Value + new Vector2(300, 0);
            else
            {
                var nodes = Window.SchematicGraph.Nodes.Where(n => n != null).ToList();
                position = nodes.Count == 0 ? Vector2.zero
                    : new Vector2(nodes.Max(n => n.Position.x) + 320, nodes.Average(n => n.Position.y));
            }
            _lastPosition = position;

            Window.AssistantShow(position);
            await Delay(0.25f, CancellationToken.None);
            if (_overlay != null)
                await _overlay.FlyToAsync(Window.RootPointOf(position) + new Vector2(-40, -30));
            return position;
        }

        private async Task FlyToAsync(Node node)
        {
            Window.AssistantShow(node.Position);
            await Delay(0.25f, CancellationToken.None);
            var rect = Window.RootRectOf(node);
            if (_overlay != null && rect.HasValue)
                await _overlay.FlyToAsync(new Vector2(rect.Value.xMax + 6, rect.Value.yMin - 30));
        }

        private string Made(AssistantCommand c, Node node)
        {
            if (node == null)
                return "couldn't make the node";
            if (!string.IsNullOrEmpty(c.id))
                _made[c.id] = node;
            return null;
        }

        private static async Task Delay(float seconds, CancellationToken cancellation)
        {
            if (seconds > 0f)
                await Task.Delay(TimeSpan.FromSeconds(seconds), cancellation);
        }

        #endregion

        #region Finding things

        private Node Find(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (_made.TryGetValue(id, out var node) || _context.Nodes.TryGetValue(id, out node))
                return node;
            // By name, as a fallback for a model that used the title instead of the id.
            return Window.SchematicGraph.Nodes.FirstOrDefault(n => n != null && Same(n.Name, id));
        }

        private bool TryNodeType(string name, out BlueGraph.Editor.NodeReflectionData type)
        {
            type = null;
            if (string.IsNullOrEmpty(name))
                return false;
            return _context.NodeTypes.TryGetValue(name, out type)
                || _context.NodeTypes.TryGetValue(SchematicGraphEditorWindow.Simplify(name), out type);
        }

        /// <summary>
        /// A Behavior on the prefab, at the given path or (when that object doesn't have it) wherever it is attached.
        /// </summary>
        private bool TryBehavior(string name, string path, out Type type, out string foundPath, out BehaviorMetadata metadata)
        {
            type = null;
            foundPath = path ?? "";
            metadata = null;
            var prefab = Window.Prefab;
            var root = prefab != null ? prefab.GetComponent<SchematicObject>() : null;
            if (root == null)
                return false;

            (GameObject target, BehaviorMetadata metadata)? fallback = null;
            foreach (var pair in root.Behaviors)
            {
                if (pair.Key == null || pair.Value == null)
                    continue;
                foreach (var attachment in pair.Value.Items)
                {
                    var candidate = attachment?.Metadata;
                    var candidateType = candidate != null ? candidate.BehaviorType.Type : null;
                    if (candidateType == null || !(Same(candidateType.Name, name) || Same(ObjectNames.NicifyVariableName(candidateType.Name), name)))
                        continue;
                    string candidatePath = pair.Key.transform == prefab.transform ? "" : AnimationUtility.CalculateTransformPath(pair.Key.transform, prefab.transform);
                    if (candidatePath == foundPath)
                    {
                        type = candidateType;
                        metadata = candidate;
                        return true;
                    }
                    fallback ??= (pair.Key, candidate);
                }
            }
            if (fallback == null)
                return false;
            var (fallbackTarget, fallbackMetadata) = fallback.Value;
            type = fallbackMetadata.BehaviorType.Type;
            metadata = fallbackMetadata;
            foundPath = fallbackTarget.transform == prefab.transform ? "" : AnimationUtility.CalculateTransformPath(fallbackTarget.transform, prefab.transform);
            return true;
        }

        private static Type BehaviorType(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            foreach (var type in TypeCache.GetTypesDerivedFrom<SchematicBehavior>())
                if (!type.IsAbstract && (Same(type.Name, name) || Same(ObjectNames.NicifyVariableName(type.Name), name)))
                    return type;
            return null;
        }

        private static bool Same(string a, string b)
            => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b)
            && (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)
                || string.Equals(SchematicGraphEditorWindow.Simplify(a), SchematicGraphEditorWindow.Simplify(b), StringComparison.OrdinalIgnoreCase));

        #endregion

        #region Values

        private string SetValue(Node node, string fieldName, string text)
        {
            FieldInfo field = null;
            for (var type = node.GetType(); type != null && field == null; type = type.BaseType)
                field = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(f => Same(f.Name, fieldName) || Same(ObjectNames.NicifyVariableName(f.Name), fieldName));
            if (field == null)
                return $"{node.Name} has no field \"{fieldName}\"";
            if (!TryParse(text, field.FieldType, out object value))
                return $"\"{text}\" isn't a {AssistantContext.TypeName(field.FieldType)}";

            Undo.RegisterCompleteObjectUndo(Window.SchematicGraph, "Set Value");
            field.SetValue(node, value);
            node.Validate();
            EditorUtility.SetDirty(Window.SchematicGraph);
            _needsRefresh = true;
            return null;
        }

        private static bool TryParse(string text, Type type, out object value)
        {
            value = null;
            text = (text ?? "").Trim();
            var culture = CultureInfo.InvariantCulture;
            if (type == typeof(string)) { value = text; return true; }
            if (type == typeof(float) && float.TryParse(text, NumberStyles.Float, culture, out float f)) { value = f; return true; }
            if (type == typeof(double) && double.TryParse(text, NumberStyles.Float, culture, out double d)) { value = d; return true; }
            if (type == typeof(int) && float.TryParse(text, NumberStyles.Float, culture, out float i)) { value = Mathf.RoundToInt(i); return true; }
            if (type == typeof(bool) && bool.TryParse(text, out bool b)) { value = b; return true; }
            if (type.IsEnum)
            {
                var match = Enum.GetNames(type).FirstOrDefault(n => Same(n, text));
                if (match != null) { value = Enum.Parse(type, match); return true; }
                return false;
            }
            var numbers = text.Trim('(', ')', '[', ']').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => float.TryParse(s, NumberStyles.Float, culture, out float n) ? n : float.NaN).ToArray();
            if (numbers.Any(float.IsNaN))
                return false;
            if (type == typeof(Vector2) && numbers.Length == 2) { value = new Vector2(numbers[0], numbers[1]); return true; }
            if (type == typeof(Vector3) && numbers.Length == 3) { value = new Vector3(numbers[0], numbers[1], numbers[2]); return true; }
            if (type == typeof(Color) && numbers.Length >= 3) { value = new Color(numbers[0], numbers[1], numbers[2], numbers.Length > 3 ? numbers[3] : 1f); return true; }
            return false;
        }

        #endregion
    }
}
