using REDox;
using REDox.Json;
using System.Globalization;
using System.Text;

namespace SchematicsAgent;

/// <summary>
/// Turns a request plus a snapshot of the editor into something to say and a list of editor commands, in two passes
/// of the same model: a reader that understands the request and plans, then an actor that writes the commands. The
/// snapshot is condensed to compact text first, so a small model with a small context can follow it, and grammars
/// keep both passes' output valid JSON.
/// </summary>
public sealed class Planner
{
    private readonly Llm _llm;
    private readonly ReferenceStore _reference;

    public Planner(Llm llm, ReferenceStore reference) { _llm = llm; _reference = reference; }

    // The toolkit pieces relevant to some text, as prompt lines.
    private string ReferenceFor(string text, int count)
    {
        var cards = _reference.Search(text, count);
        return cards.Count == 0 ? "" : "\n\nToolkit reference (what these pieces are and what they're for):\n- " + string.Join("\n- ", cards);
    }

    /// <summary>The commands the editor understands, and the fields each uses.</summary>
    public static readonly IReadOnlyDictionary<string, string> Ops = new Dictionary<string, string>
    {
        ["add_node"] = "id (a new name for it), type (a node type), x, y",
        ["add_signal_node"] = "id, behavior, signal, mode (send|receive), path (object path, \"\" for the root), x, y",
        ["add_variable_node"] = "id, behavior, variable, mode (get|set), path, x, y",
        ["add_behavior_node"] = "id, behavior, path, x, y",
        ["connect"] = "from (node id), from_port (\"flow\" for the flow arrow, or an output's name), to (node id), to_port (\"flow\" or an input's name)",
        ["set_value"] = "node, field, value",
        ["move_node"] = "node, x, y",
        ["delete_node"] = "node",
        ["add_behavior"] = "behavior, path",
        ["focus"] = "node",
        ["create_object"] = "name, x, y, z (in the open scene)",
        ["select_object"] = "name",
    };

    private const string Persona =
        "You are Sprocket, a small, cheerful assistant living inside the Schematics visual scripting editor in Unity. " +
        "Schematic Graphs are node graphs: event nodes (On Update, On Signal Received, ...) start flows through action " +
        "nodes connected by flow ports; Behaviors on the prefab's objects have Variables and Signals that become nodes.";

    private const string ReaderRules =
        "Read the editor state and the request. Reply with JSON: {\"say\": what you'll say out loud (one or two short, " +
        "friendly sentences about what you're going to do, or the answer if it's a question), \"plan\": short concrete " +
        "steps using exact names and node ids from the state}. Leave plan empty for questions or anything you can't do. " +
        "Never invent behaviors, signals, variables or node types that aren't in the state.";

    private static string ActorRules() =>
        "Turn the plan into editor commands, as a JSON array. Allowed ops and their fields:\n" +
        string.Join("\n", Ops.Select(op => $"- {op.Key}: {op.Value}")) +
        "\nFlow arrows run from an event or action node's \"flow\" output to the next action's \"flow\" input; value ports pass data. New nodes get an id you choose (like \"a\", \"b\") that later commands can use. Place new nodes to the right " +
        "of the nodes they follow (x + 300). Use only names and ids from the state. Output [] if there's nothing to do.";

    private const string ReaderGrammar = """
root   ::= "{" ws "\"say\"" ws ":" ws string ws "," ws "\"plan\"" ws ":" ws "[" ws ( string ( ws "," ws string )* )? ws "]" ws "}"
string ::= "\"" ( [^"\\\x7F\x00-\x1F] | "\\" ["\\/bfnrt] )* "\""
ws     ::= [ \t\n]*
""";

    private static string ActorGrammar() => $$"""
root    ::= "[" ws ( command ( ws "," ws command )* )? ws "]"
command ::= "{" ws "\"op\"" ws ":" ws op ( ws "," ws pair )* ws "}"
op      ::= {{string.Join(" | ", Ops.Keys.Select(k => $"\"\\\"{k}\\\"\""))}}
pair    ::= key ws ":" ws value
key     ::= "\"" ( "id" | "type" | "node" | "from" | "from_port" | "to" | "to_port" | "behavior" | "signal" | "variable" | "mode" | "path" | "field" | "value" | "name" | "x" | "y" | "z" ) "\""
value   ::= string | number
string  ::= "\"" ( [^"\\\x7F\x00-\x1F] | "\\" ["\\/bfnrt] )* "\""
number  ::= "-"? [0-9]+ ( "." [0-9]+ )?
ws      ::= [ \t\n]*
""";

    public async Task<string> HandleAsync(string requestJson, CancellationToken cancellation)
    {
        string utterance, state;
        using (var request = Json.Parse(requestJson))
        {
            var root = request.RootElement;
            utterance = root.Str("utterance").Trim();
            state = Condense(root.Child("context"));
        }
        if (utterance.Length == 0)
            return Reply("I didn't catch that.", "[]");
        if (!_llm.Available)
            return Reply("My language model isn't installed yet. Run setup in the Agent folder to download it.", "[]");

        // 1. Read and plan.
        string readerOutput = await _llm.CompleteAsync(
            Persona + "\n" + ReaderRules,
            $"Editor state:\n{state}{ReferenceFor(utterance + " " + state, 10)}\n\nRequest: {utterance}",
            ReaderGrammar, 320, cancellation);

        string say = "Okay.";
        var plan = new List<string>();
        using (var reader = Json.Parse(readerOutput))
        {
            var root = reader.RootElement;
            say = root.Str("say", say);
            foreach (var step in root.Child("plan").Items())
                if (step.ValueKind == JsonValueKind.String)
                    plan.Add(step.GetString() ?? "");
        }
        if (plan.Count == 0)
            return Reply(say, "[]");

        // 2. Act: the plan becomes commands.
        string actorOutput = await _llm.CompleteAsync(
            Persona + "\n" + ActorRules(),
            $"Editor state:\n{state}{ReferenceFor(utterance + " " + string.Join(" ", plan), 12)}\n\nRequest: {utterance}\nPlan:\n- {string.Join("\n- ", plan)}",
            ActorGrammar(), 900, cancellation);

        return Reply(say, NormalizeCommands(actorOutput));
    }

    private static string Reply(string say, string commandsJson)
        => Json.Object(w => w.String("say", say).Raw("commands", commandsJson));

    /// <summary>
    /// Re-writes the model's commands in the flat shape the editor reads (every field present; numbers as numbers),
    /// dropping anything with an unknown op.
    /// </summary>
    private static string NormalizeCommands(string json)
    {
        var builder = new JsonBuilder();
        builder.BeginArray();
        using (var doc = Json.Parse(json))
        {
            foreach (var command in doc.RootElement.Items())
            {
                string op = command.Str("op");
                if (!Ops.ContainsKey(op))
                    continue;
                builder.BeginObject()
                    .String("op", op)
                    .String("id", command.Str("id")).String("type", command.Str("type")).String("node", command.Str("node"))
                    .String("from", command.Str("from")).String("from_port", command.Str("from_port"))
                    .String("to", command.Str("to")).String("to_port", command.Str("to_port"))
                    .String("behavior", command.Str("behavior")).String("signal", command.Str("signal"))
                    .String("variable", command.Str("variable")).String("mode", command.Str("mode"))
                    .String("path", command.Str("path")).String("field", command.Str("field"))
                    .String("value", command.Str("value")).String("name", command.Str("name"))
                    .Number("x", command.Num("x")).Number("y", command.Num("y")).Number("z", command.Num("z"))
                    .EndObject();
            }
        }
        builder.EndArray();
        return builder.ToString();
    }

    /// <summary>
    /// The editor snapshot as compact text: the open graph's nodes and links, the prefab's objects with their
    /// Behaviors' Variables and Signals, the node types available, and a summary of the open scene.
    /// </summary>
    public static string Condense(DElement context)
    {
        var text = new StringBuilder();
        if (!context.IsValid)
            return "(nothing open)";

        var graph = context.Child("graph");
        if (graph.IsValid)
        {
            text.Append("Graph \"").Append(graph.Str("name")).Append('"');
            string prefab = graph.Str("prefab");
            if (prefab.Length > 0) text.Append(" on prefab \"").Append(prefab).Append('"');
            text.Append(":\n");
            foreach (var node in graph.Child("nodes").Items())
            {
                text.Append("  ").Append(node.Str("id")).Append(' ').Append(node.Str("type"));
                string name = node.Str("name");
                if (name.Length > 0 && name != node.Str("type")) text.Append(" \"").Append(name).Append('"');
                text.Append(" @").Append(Round(node.Num("x"))).Append(',').Append(Round(node.Num("y")));
                var inputs = node.Child("inputs").Items().Select(i => i.GetString()).ToList();
                var outputs = node.Child("outputs").Items().Select(o => o.GetString()).ToList();
                if (inputs.Count > 0) text.Append(" in[").Append(string.Join(", ", inputs)).Append(']');
                if (outputs.Count > 0) text.Append(" out[").Append(string.Join(", ", outputs)).Append(']');
                foreach (var link in node.Child("links").Items())
                    text.Append(" ").Append(link.Str("from_port")).Append("->").Append(link.Str("to")).Append('.').Append(link.Str("to_port"));
                text.Append('\n');
            }
        }

        bool anyObject = false;
        foreach (var obj in context.Child("objects").Items())
        {
            if (!anyObject) { text.Append("Prefab objects (path: behaviors):\n"); anyObject = true; }
            string path = obj.Str("path");
            text.Append("  ").Append(path.Length == 0 ? "(root)" : path).Append(" \"").Append(obj.Str("name")).Append("\":");
            foreach (var behavior in obj.Child("behaviors").Items())
            {
                text.Append("\n    ").Append(behavior.Str("type"));
                var variables = behavior.Child("variables").Items()
                    .Select(v => $"{v.Str("name")}:{v.Str("type")}={v.Str("value")}").ToList();
                if (variables.Count > 0) text.Append(" vars[").Append(string.Join("; ", variables)).Append(']');
                var signals = behavior.Child("signals").Items()
                    .Select(s => $"{s.Str("name")}({s.Str("params")}){(s.Str("dir").Length > 0 ? " " + s.Str("dir") : "")}").ToList();
                if (signals.Count > 0) text.Append(" signals[").Append(string.Join("; ", signals)).Append(']');
            }
            text.Append('\n');
        }

        var types = context.Child("node_types").Items().Select(t => t.GetString()).ToList();
        if (types.Count > 0)
            text.Append("Node types: ").Append(string.Join(", ", types)).Append('\n');

        var scene = context.Child("scene");
        if (scene.IsValid)
        {
            text.Append("Scene \"").Append(scene.Str("name")).Append("\"");
            var selected = scene.Child("selected").Items().Select(s => s.GetString()).ToList();
            if (selected.Count > 0) text.Append(", selected: ").Append(string.Join(", ", selected));
            text.Append(":\n");
            foreach (var obj in scene.Child("objects").Items())
                text.Append("  ").Append(obj.Str("name")).Append(" @").Append(obj.Str("pos")).Append(' ').Append(obj.Str("components")).Append('\n');
        }
        return text.ToString();
    }

    private static string Round(double value) => Math.Round(value).ToString(CultureInfo.InvariantCulture);
}
