using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>How a graph's Parameter is shown where the graph is called as a function (on the calling node).</summary>
    public enum ParameterControl
    {
        /// <summary>A number box.</summary>
        Number,
        /// <summary>A slider between Min and Max.</summary>
        Slider,
        /// <summary>A slider of whole numbers between Min and Max.</summary>
        WholeNumber,
        /// <summary>On or off (1 or 0).</summary>
        Toggle,
        /// <summary>One of Options (its number: 0 for the first).</summary>
        Dropdown,
    }

    /// <summary>
    /// The face a graph's Parameter shows to whoever calls the graph: its control (a slider, a toggle...), range,
    /// group and description. Set on the Parameter node; the calling node draws it.
    /// </summary>
    [Serializable]
    public class ParameterUI
    {
        public ParameterControl Control = ParameterControl.Number;
        [Tooltip("Slider and Whole Number: the lowest value.")]
        public float Min;
        [Tooltip("Slider and Whole Number: the highest value.")]
        public float Max = 1f;
        [Tooltip("Dropdown: the choices, separated by commas (the value is the choice's number, from 0).")]
        public string Options = "";
        [Tooltip("Parameters with the same group are shown together under it on the calling node.")]
        public string Group = "";
        [Tooltip("What it does, for whoever (or whatever AI) uses the graph.")]
        [TextArea(1, 3)]
        public string Description = "";

        public string[] OptionList => string.IsNullOrWhiteSpace(Options)
            ? Array.Empty<string>()
            : Options.Split(',').Select(o => o.Trim()).Where(o => o.Length > 0).ToArray();

        /// <summary>A value kept to what the control allows (in range, whole, 0 or 1, a choice's number).</summary>
        public float Clamp(float value)
        {
            switch (Control)
            {
                case ParameterControl.Slider:
                    return Mathf.Clamp(value, Mathf.Min(Min, Max), Mathf.Max(Min, Max));
                case ParameterControl.WholeNumber:
                    return Mathf.Round(Mathf.Clamp(value, Mathf.Min(Min, Max), Mathf.Max(Min, Max)));
                case ParameterControl.Toggle:
                    return value >= 0.5f ? 1f : 0f;
                case ParameterControl.Dropdown:
                    return Mathf.Clamp(Mathf.Round(value), 0, Mathf.Max(0, OptionList.Length - 1));
                default:
                    return value;
            }
        }

        public ParameterUI Copy() => (ParameterUI)MemberwiseClone();
    }

    /// <summary>A pin of a graph called as a function, besides its Parameters: a Mesh Input it takes, the model it gives back.</summary>
    public readonly struct GraphPinInfo
    {
        public readonly string Name;
        public readonly Type Type;
        public readonly string Description;
        /// <summary>For a number: how it's shown on the calling node (null: a plain number box).</summary>
        public readonly ParameterUI UI;

        public GraphPinInfo(string name, Type type, string description = "", ParameterUI ui = null)
        {
            Name = name;
            Type = type;
            Description = description ?? "";
            UI = ui;
        }
    }

    /// <summary>
    /// One input or output of a Function Graph: its name, its type, and (for numbers) the control it shows on the
    /// nodes calling the graph when nothing's wired in.
    /// </summary>
    [Serializable]
    public class FunctionPin
    {
        [Tooltip("Stays the same when the pin is renamed.")]
        public string Id = Guid.NewGuid().ToString("N");
        public string Name = "Value";
        public SerializableType Type = typeof(float);
        [Tooltip("For a number, bool or whole number: the control on calling nodes, its range and description.")]
        public ParameterUI UI = new();
        [Tooltip("Its value when a calling node (or Return) doesn't set one.")]
        public SchematicValue Default = new();

        public Type ValueType => Type.Type;

        /// <summary>Whether it's a number (a float, whole number or bool), drawn with its control.</summary>
        public bool IsNumber => ValueType == typeof(float) || ValueType == typeof(int) || ValueType == typeof(bool);

        /// <summary>Whether it can be set right on the calling node: numbers, and anything a value field can show (vectors, colours, strings, objects...).</summary>
        public bool HasInlineValue => IsNumber || SchematicValue.IsDirect(ValueType);

        public GraphPinInfo Info => new(Name, ValueType, UI?.Description, IsNumber ? UI : null);

        /// <summary>Its default as an object of its type.</summary>
        public object DefaultValue
        {
            get
            {
                var type = ValueType;
                if (type == null)
                    return null;
                var value = Default?.Get(type);
                return value ?? (type.IsValueType ? Activator.CreateInstance(type) : null);
            }
        }

        /// <summary>As a Parameter (for the number control drawn on calling nodes).</summary>
        public GraphParameter AsParameter() => new(Name, typeof(float), ToFloat(DefaultValue), UI);

        private static float ToFloat(object value) => value switch
        {
            float f => f,
            int i => i,
            bool b => b ? 1f : 0f,
            _ => 0f,
        };
    }

    /// <summary>
    /// A graph offered as a function: listed in the node search of graphs of its kind (under Functions/<see cref="Path"/>),
    /// and documented like a built-in node (its description, what it's for, its Parameters and outputs), so people and
    /// the assistant can build with it.
    /// </summary>
    [Serializable]
    public class GraphFunctionInfo
    {
        [Tooltip("Offer this graph in the node search of graphs of its kind, as a function.")]
        public bool InLibrary;
        [Tooltip("Where it's listed under Functions in the node search, folders separated by / (Characters/Face).")]
        public string Path = "";
        [Tooltip("What it's good for, one use per line (for people and the assistant).")]
        [TextArea(1, 4)]
        public string UseFor = "";
        [Tooltip("Anything else worth knowing when using it.")]
        [TextArea(1, 3)]
        public string Notes = "";

        /// <summary>A Function Graph's inputs: On Called's pins, and the calling node's.</summary>
        public List<FunctionPin> Inputs = new();
        /// <summary>A Function Graph's outputs: Return's pins, and what the calling node gives back.</summary>
        public List<FunctionPin> Outputs = new();
    }

    /// <summary>A value set on a node calling a graph as a function, for one of the graph's Parameters (by name).</summary>
    [Serializable]
    public class FunctionArgument
    {
        public string Name;
        /// <summary>A number's value.</summary>
        public float Value;
        /// <summary>Any other type's value (a vector, a colour, a string, an object), when one's been set.</summary>
        public SchematicValue Any;
    }

    /// <summary>The values a calling node keeps for its graph's Parameters (by name, so they survive reordering).</summary>
    public static class FunctionArguments
    {
        /// <summary>The value kept for a Parameter, or its default.</summary>
        public static float Get(List<FunctionArgument> arguments, GraphParameter parameter)
        {
            if (arguments != null)
                foreach (var argument in arguments)
                    if (argument != null && string.Equals(argument.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                        return argument.Value;
            return parameter.Default is float f ? f : parameter.Default is int i ? i : 0f;
        }

        /// <summary>The value kept for a pin of any type, or null when none has been set.</summary>
        public static bool TryGetObject(List<FunctionArgument> arguments, string name, Type type, out object value)
        {
            value = null;
            if (arguments != null && type != null)
                foreach (var argument in arguments)
                    if (argument != null && argument.Any != null && string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        value = argument.Any.Get(type);
                        return true;
                    }
            return false;
        }

        /// <summary>Keeps a value of any type for a pin.</summary>
        public static void SetObject(List<FunctionArgument> arguments, string name, Type type, object value)
        {
            if (arguments == null)
                return;
            foreach (var argument in arguments)
                if (argument != null && string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    (argument.Any ??= new SchematicValue()).Set(value, type);
                    return;
                }
            var added = new FunctionArgument { Name = name, Any = new SchematicValue() };
            added.Any.Set(value, type);
            arguments.Add(added);
        }

        /// <summary>Keeps a value for a Parameter (held to what its control allows).</summary>
        public static void Set(List<FunctionArgument> arguments, GraphParameter parameter, float value)
        {
            if (arguments == null)
                return;
            if (parameter.UI != null)
                value = parameter.UI.Clamp(value);
            foreach (var argument in arguments)
                if (argument != null && string.Equals(argument.Name, parameter.Name, StringComparison.OrdinalIgnoreCase))
                {
                    argument.Value = value;
                    return;
                }
            arguments.Add(new FunctionArgument { Name = parameter.Name, Value = value });
        }
    }

    /// <summary>Graph functions: finding them, and documenting them the way built-in nodes are (SchematicDoc JSON).</summary>
    public static class GraphFunctions
    {
        /// <summary>The node search folder function graphs are listed under.</summary>
        public const string SearchFolder = "Functions";

        /// <summary>
        /// The graph's documentation in the same form as a node's SchematicDoc: name, kind ("function"), category (its
        /// library path), description, inputs (its function inputs, then its Parameters, with their controls and
        /// descriptions), outputs, use_for and notes.
        /// </summary>
        public static string Doc(SchematicGraph graph)
        {
            if (graph == null)
                return "{}";
            var info = graph.Function ?? new GraphFunctionInfo();
            var json = new StringBuilder("{");
            Field(json, "name", graph.name);
            json.Append(", ");
            Field(json, "kind", "function");
            json.Append(", ");
            Field(json, "graph_type", graph.GetType().Name);
            json.Append(", ");
            Field(json, "category", string.IsNullOrWhiteSpace(info.Path) ? SearchFolder : SearchFolder + "/" + info.Path.Trim('/'));
            json.Append(", ");
            Field(json, "description", graph.Description ?? "");

            json.Append(", \"inputs\": [");
            bool first = true;
            foreach (var input in graph.FunctionInputs)
            {
                if (!first) json.Append(", ");
                first = false;
                json.Append('{');
                Field(json, "name", input.Name);
                json.Append(", ");
                Field(json, "type", TypeName(input.Type));
                if (input.UI != null && input.UI.Control != ParameterControl.Number)
                {
                    json.Append(", ");
                    Field(json, "control", input.UI.Control.ToString());
                    if (input.UI.Control == ParameterControl.Slider || input.UI.Control == ParameterControl.WholeNumber)
                        json.Append(", \"min\": ").Append(Number(input.UI.Min)).Append(", \"max\": ").Append(Number(input.UI.Max));
                    if (input.UI.Control == ParameterControl.Dropdown)
                        json.Append(", \"options\": [").Append(string.Join(", ", input.UI.OptionList.Select(Quote))).Append(']');
                }
                if (!string.IsNullOrEmpty(input.Description))
                {
                    json.Append(", ");
                    Field(json, "description", input.Description);
                }
                json.Append('}');
            }
            foreach (var parameter in graph.GraphParameters)
            {
                if (!first) json.Append(", ");
                first = false;
                json.Append('{');
                Field(json, "name", parameter.Name);
                json.Append(", ");
                Field(json, "type", TypeName(parameter.Type));
                var ui = parameter.UI;
                if (parameter.Default != null)
                    json.Append(", \"default\": ").Append(Number(parameter.Default));
                if (ui != null)
                {
                    json.Append(", ");
                    Field(json, "control", ui.Control.ToString());
                    if (ui.Control == ParameterControl.Slider || ui.Control == ParameterControl.WholeNumber)
                        json.Append(", \"min\": ").Append(Number(ui.Min)).Append(", \"max\": ").Append(Number(ui.Max));
                    if (ui.Control == ParameterControl.Dropdown)
                        json.Append(", \"options\": [").Append(string.Join(", ", ui.OptionList.Select(Quote))).Append(']');
                    if (!string.IsNullOrWhiteSpace(ui.Group))
                    {
                        json.Append(", ");
                        Field(json, "group", ui.Group);
                    }
                    if (!string.IsNullOrWhiteSpace(ui.Description))
                    {
                        json.Append(", ");
                        Field(json, "description", ui.Description);
                    }
                }
                json.Append('}');
            }
            json.Append(']');

            json.Append(", \"outputs\": [");
            first = true;
            foreach (var output in graph.FunctionOutputs)
            {
                if (!first) json.Append(", ");
                first = false;
                json.Append('{');
                Field(json, "name", output.Name);
                json.Append(", ");
                Field(json, "type", TypeName(output.Type));
                if (!string.IsNullOrEmpty(output.Description))
                {
                    json.Append(", ");
                    Field(json, "description", output.Description);
                }
                json.Append('}');
            }
            json.Append(']');

            var uses = (info.UseFor ?? "").Split('\n').Select(u => u.Trim()).Where(u => u.Length > 0).ToArray();
            if (uses.Length > 0)
                json.Append(", \"use_for\": [").Append(string.Join(", ", uses.Select(Quote))).Append(']');
            if (!string.IsNullOrWhiteSpace(info.Notes))
            {
                json.Append(", ");
                Field(json, "notes", info.Notes.Trim());
            }
            json.Append('}');
            return json.ToString();
        }

        private static void Field(StringBuilder json, string name, string value) => json.Append(Quote(name)).Append(": ").Append(Quote(value));

        private static string Quote(string text)
        {
            var quoted = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': break;
                    case '\t': quoted.Append("\\t"); break;
                    default: quoted.Append(c); break;
                }
            }
            return quoted.Append('"').ToString();
        }

        private static string Number(object value) => value switch
        {
            float f => f.ToString("0.####", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            double d => d.ToString("0.####", CultureInfo.InvariantCulture),
            _ => Quote(value?.ToString() ?? ""),
        };

        private static string TypeName(Type type)
        {
            if (type == null) return "object";
            if (type == typeof(float)) return "float";
            if (type == typeof(int)) return "int";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return type.Name;
        }
    }
}
