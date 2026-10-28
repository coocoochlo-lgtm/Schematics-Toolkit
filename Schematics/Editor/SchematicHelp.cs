using BlueGraph;
using BlueGraph.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Hover help for everything you build with: nodes (on their title), their pins and settings, and the Variables and
    /// Signals in the IO Dock. It gathers what the code already says (a field's [Tooltip], the [SchematicDoc] the
    /// assistant reads, a function's pin descriptions, a Variable's Description) and, where nothing's written, what a
    /// pin of that name usually is. Every pin's help starts with what kind of value it takes.
    /// </summary>
    [InitializeOnLoad]
    public static class SchematicHelp
    {
        static SchematicHelp()
        {
            NodeView.NodeHelp = Node;
            NodeView.PortHelp = Port;
            NodeView.SettingHelp = (node, field) => Member(field);
        }

        // ---- Nodes ----

        /// <summary>A node's help: what it does, what it's good for, and anything worth knowing.</summary>
        public static string Node(Node node)
        {
            if (node == null)
                return null;
            var doc = MemberHelp.MemberDoc.Of(node.GetType());
            var text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(doc?.description))
                text.Append(doc.description.Trim());
            if (doc?.use_for != null && doc.use_for.Length > 0)
                text.Append("\n\nGood for: ").Append(string.Join("; ", doc.use_for)).Append('.');
            if (!string.IsNullOrWhiteSpace(doc?.notes))
                text.Append("\n\n").Append(doc.notes.Trim());
            if (node is SchematicGraphNode && !string.IsNullOrEmpty(node.CustomName) && node.CustomName != node.Name)
                text.Insert(0, $"({NodeReflection.GetNodeType(node.GetType())?.Name ?? node.GetType().Name})\n");
            return text.Length > 0 ? text.ToString() : null;
        }

        // ---- Pins ----

        /// <summary>A pin's help: "Name (kind of value)", then what it's for.</summary>
        public static string Port(Node node, Port port)
        {
            if (node == null || port == null)
                return null;
            string about = PinText(node, port);
            string head = port.Type == typeof(ActionPort)
                ? (port.Direction == PortDirection.Input ? "Flow in" : "Flow out")
                : $"{Pretty(port.Name)} ({Kind(port.Type)})";
            return string.IsNullOrWhiteSpace(about) ? head : head + "\n" + about.Trim();
        }

        private static string PinText(Node node, Port port)
        {
            // Flow pins.
            if (port.Type == typeof(ActionPort))
                return port.Direction == PortDirection.Input
                    ? "Connect an arrow here: this node runs when the flow reaches it."
                    : port.Name == "▶" ? "What runs next, once this node is done." : $"What runs next when it goes this way ({Pretty(port.Name)}).";

            // The field behind it: its [Tooltip] or [SchematicDoc].
            var field = NodeReflection.GetNodeType(node.GetType())?.GetPortByName(port.Name)?.Field;
            string text = field != null ? Member(field) : null;
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            // Pins made from something else: a function's inputs and outputs, a graph's Parameters, a Variable.
            text = DynamicPin(node, port);
            if (!string.IsNullOrWhiteSpace(text))
                return text;

            // The node's docs, if they describe this pin.
            var doc = MemberHelp.MemberDoc.Of(node.GetType());
            var entry = (port.Direction == PortDirection.Input ? doc?.inputs : doc?.outputs)?.FirstOrDefault(p => p != null && string.Equals(p.name, port.Name, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(entry?.description))
                return entry.description;

            // What a pin of this name usually is.
            return Common(port);
        }

        private static string DynamicPin(Node node, Port port)
        {
            switch (node)
            {
                case CallFunctionNode call when call.Calls != null:
                {
                    var pin = (port.Direction == PortDirection.Input ? call.Calls.Function.Inputs : call.Calls.Function.Outputs)
                        .FirstOrDefault(p => p != null && (p.Name == port.Name || call.OutputPin(p) == port.Name));
                    return Describe(pin, port.Direction == PortDirection.Input ? $"An input of {call.Calls.name}." : $"What {call.Calls.name} gives back.");
                }
                case OnCalledNode or ReturnNode when node.Graph is FunctionGraph function:
                {
                    var pins = node is OnCalledNode ? function.Function.Inputs : function.Function.Outputs;
                    var pin = pins.FirstOrDefault(p => p != null && p.Name == port.Name);
                    return Describe(pin, node is OnCalledNode ? "One of this function's inputs, as the call gave it." : "One of this function's outputs: handed back to the Call node.");
                }
                case GraphVariableNodeBase variable:
                {
                    var definition = variable.Definition;
                    if (port.Name == GraphVariableNodeBase.OfPort)
                        return "Whose value: the object running this graph if nothing's wired in, or the Schematic Object wired here.";
                    return string.IsNullOrWhiteSpace(definition?.Description)
                        ? $"The Graph Variable {definition?.Name}."
                        : definition.Description;
                }
            }
            // A pin named after a graph's Parameter (Set Graph Parameters, Play Sound Cue, a Mesh or Sound Cue Function...).
            var graph = node.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => typeof(SchematicGraph).IsAssignableFrom(f.FieldType))
                .Select(f => f.GetValue(node) as SchematicGraph).FirstOrDefault(g => g != null);
            if (graph != null)
            {
                foreach (var parameter in graph.GraphParameters)
                    if (port.Name == parameter.Name || port.Name.StartsWith(parameter.Name + " ", StringComparison.Ordinal))
                        return !string.IsNullOrWhiteSpace(parameter.UI?.Description) ? parameter.UI.Description : $"{graph.name}'s Parameter {parameter.Name}.";
                foreach (var input in graph.FunctionInputs)
                    if (port.Name == input.Name || port.Name == input.Name + " In")
                        return !string.IsNullOrWhiteSpace(input.Description) ? input.Description : $"What {graph.name} is given as {input.Name}.";
            }
            return null;
        }

        private static string Describe(FunctionPin pin, string fallback)
            => !string.IsNullOrWhiteSpace(pin?.UI?.Description) ? pin.UI.Description : fallback;

        // ---- Fields: settings, Variables, Signals ----

        /// <summary>What the code says about a field: its [Tooltip], else its [SchematicDoc] description.</summary>
        public static string Member(MemberInfo member) => MemberHelp.Of(member);

        /// <summary>A Behavior's Variable or Signal (by its name in the dock), from the field declaring it.</summary>
        public static string BehaviorMember(Type behavior, string name) => MemberHelp.OfBehavior(behavior, name);

        /// <summary>Joins what something is with how to use it, for a row's tooltip.</summary>
        public static string Row(string about, string use) => MemberHelp.Row(about, use);

        // ---- Plain words ----

        /// <summary>The kind of value a type is, in plain words.</summary>
        public static string Kind(Type type)
        {
            if (type == null || type == typeof(object))
                return "anything";
            if (type == typeof(float) || type == typeof(double)) return "number";
            if (type == typeof(int) || type == typeof(long)) return "whole number";
            if (type == typeof(bool)) return "true or false";
            if (type == typeof(string)) return "text";
            if (type == typeof(Vector2)) return "2D vector";
            if (type == typeof(Vector3)) return "3D vector: a position, direction or size";
            if (type == typeof(Vector4)) return "4D vector";
            if (type == typeof(Quaternion)) return "rotation";
            if (type == typeof(Color)) return "colour";
            if (type == typeof(GameObject)) return "GameObject";
            if (type == typeof(Transform)) return "Transform";
            // (Feature types, by name: this assembly doesn't see them.)
            if (type.Name == "MaskMap") return "mask: a grey map, 0 to 1 at each spot on the texture";
            if (type.Name == "MaterialMaps") return "material: colour, roughness, metallic and height at each spot";
            if (type.Name == "MeshData") return "mesh";
            if (type.IsEnum) return "one of: " + string.Join(", ", Enum.GetNames(type).Select(ObjectNames.NicifyVariableName));
            if (type.IsArray) return "list of " + Kind(type.GetElementType());
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return "list of " + Kind(type.GetGenericArguments()[0]);
            return ObjectNames.NicifyVariableName(type.Name);
        }

        private static string Pretty(string name) => string.IsNullOrEmpty(name) ? "" : ObjectNames.NicifyVariableName(name);

        // What a pin of a common name usually is, where its node doesn't say.
        private static readonly Dictionary<string, string> CommonPins = new(StringComparer.OrdinalIgnoreCase)
        {
            ["In"] = "The value it works on.",
            ["Out"] = "The result.",
            ["Result"] = "The result.",
            ["Value"] = "The value.",
            ["A"] = "The first value.",
            ["B"] = "The second value.",
            ["Self"] = "The object running this graph.",
            ["Target"] = "The object it acts on (the one running this graph if nothing's wired in).",
            ["Of"] = "Whose value: the object running this graph if nothing's wired in.",
            ["To"] = "Who it's sent to: the object running this graph if nothing's wired in.",
            ["GameObject"] = "The GameObject it acts on.",
            ["Object"] = "The object it acts on.",
            ["Instance"] = "The object it acts on.",
            ["Audio"] = "The sound coming in (wire a source or effect here).",
            ["Time"] = "Seconds, as it reads them: the cue's own playback time if nothing's wired in. Do math on it to play faster, slower or delayed.",
            ["Progress"] = "How far through its length it is, 0 at the start to 1 at the end.",
            ["Volume"] = "How loud: 1 as it is, 0.5 half, 0 silent.",
            ["Cutoff"] = "The frequency (Hz) where the filter starts working: lower is duller (low pass) or thinner (high pass).",
            ["Resonance"] = "A boost right at the cutoff: 0.7 is flat, higher rings or whistles.",
            ["Frequency"] = "How many times a second (Hz).",
            ["Amplitude"] = "How big the swing is.",
            ["Offset"] = "Added on top: shifts it up or down.",
            ["Delay"] = "Seconds before it starts.",
            ["Duration"] = "How long it lasts (seconds).",
            ["Speed"] = "How fast.",
            ["Mix"] = "How much of the effect is heard: 0 none (dry), 1 all (wet).",
            ["Mesh"] = "The model it works on, or the model it makes.",
            ["Selection"] = "Which parts of the model it works on (from a Select node); everything if nothing's wired in.",
            ["Points"] = "Points to place things at (from a Points node).",
            ["Seed"] = "Picks the random result: the same seed always gives the same result; change it for a different one.",
            ["Position"] = "Where (in meters).",
            ["Rotation"] = "Which way it's turned (degrees).",
            ["Scale"] = "How big, per axis (1 is as it is).",
            ["Size"] = "How big (meters).",
            ["Radius"] = "How far out from the middle (meters).",
            ["Distance"] = "How far (meters).",
            ["Direction"] = "Which way it points.",
            ["Origin"] = "Where it starts from.",
            ["Normal"] = "The direction the surface faces.",
            ["Point"] = "A position in the world.",
            ["Count"] = "How many.",
            ["Index"] = "Which one, counting from 0.",
            ["Amount"] = "How much: 0 none, 1 fully.",
            ["Strength"] = "How strong the effect is.",
            ["Min"] = "The lowest it goes.",
            ["Max"] = "The highest it goes.",
            ["Condition"] = "True or false: decides which way it goes.",
            ["Enabled"] = "Whether it's on.",
            ["Tag"] = "Which tag to look for.",
            ["Layer"] = "Which layers count.",
            ["Collider"] = "The collider involved.",
            ["Other"] = "The other object involved.",
            ["Hit"] = "What was hit.",
            ["Name"] = "Its name.",
            ["Text"] = "Words to show or use.",
            ["Color"] = "A colour.",
            ["Signal"] = "Which Signal.",
            ["Parameters"] = "The values passed along with it.",
        };

        private static string Common(Port port) => CommonPins.TryGetValue(port.Name ?? "", out var text) ? text : null;
    }
}
