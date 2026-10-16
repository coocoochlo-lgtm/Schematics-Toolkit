using LLama;
using LLama.Common;
using LLama.Sampling;
using REDox;
using Remedy.Schematics.Assistant.Macro;
using System.Text;

namespace SchematicsAgent;

/// <summary>
/// Sprocket's editor-token model, when one has been trained (Agent~/Training): a small model that reads the editor
/// state and a request as tokens and writes editor commands as tokens, which are turned back into the commands Unity
/// runs and a sentence for Sprocket to say. It's used for requests to change something; if it isn't there, or what
/// it writes doesn't decode cleanly, the general model handles the request as before.
/// <para>
/// Put the notebook's two files next to the other models: sprocket-macro.gguf and sprocket-macro.vocab.tsv.
/// </para>
/// </summary>
public sealed class MacroModel : IDisposable
{
    public const string ModelFile = "sprocket-macro.gguf", VocabularyFile = "sprocket-macro.vocab.tsv";

    private readonly LLamaWeights? _weights;
    private readonly ModelParams? _parameters;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _grammar = "";

    public MacroLanguage? Language { get; }
    public bool Available => _weights != null && Language != null;

    public MacroModel(AgentSettings settings)
    {
        string model = settings.Resolve(ModelFile), vocabulary = settings.Resolve(VocabularyFile);
        if (!File.Exists(model) || !File.Exists(vocabulary))
        {
            Console.WriteLine("[macro] No editor-token model (train one with Agent~/Training); the general model handles edits");
            return;
        }
        try
        {
            Language = new MacroLanguage(MacroVocabulary.Load(vocabulary));
            _grammar = Language.Grammar();
            _parameters = new ModelParams(model) { ContextSize = 4096, GpuLayerCount = settings.GpuLayers, Threads = settings.Threads };
            _weights = LLamaWeights.LoadFromFile(_parameters);
            Console.WriteLine($"[macro] Loaded the editor-token model ({Language.Vocabulary.Tokens.Count} tokens, vocabulary {Language.Vocabulary.Fingerprint()})");
        }
        catch (Exception exception)
        {
            // Without it, edits go to the general model; nothing else depends on it.
            Console.Error.WriteLine($"[macro] Couldn't load the editor-token model: {exception.Message}");
            _weights = null;
        }
    }

    /// <summary>Commands (and what to say) for a request, or null if the model isn't there or couldn't do it cleanly.</summary>
    public async Task<(string say, List<MacroCommand> commands)?> TryAsync(MacroState state, string request, CancellationToken cancellation)
    {
        if (!Available)
            return null;
        string prompt = Language!.Prompt(state, request);
        await _gate.WaitAsync(cancellation);
        string answer;
        try
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            var executor = new StatelessExecutor(_weights!, _parameters!);
            var inference = new InferenceParams
            {
                MaxTokens = 160,
                AntiPrompts = new[] { "<end>" },
                SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0f, Grammar = new Grammar(_grammar, "root") },
            };
            var text = new StringBuilder();
            Llm.Activity = "Planning (editor tokens)";
            await foreach (var piece in executor.InferAsync(prompt, inference, cancellation))
                text.Append(piece);
            answer = text.ToString();
            Console.WriteLine($"[macro] Wrote {answer.Length} characters in {clock.Elapsed.TotalSeconds:0.0} s");
        }
        finally
        {
            Llm.Activity = null;
            _gate.Release();
        }

        var commands = Language.Decode(state, answer, out var problems);
        if (problems.Count > 0 || commands.Count == 0)
        {
            Console.WriteLine($"[macro] Couldn't use what it wrote ({(problems.Count > 0 ? string.Join("; ", problems) : "no commands")}); asking the general model");
            return null;
        }
        return (Language.Describe(state, commands), commands);
    }

    /// <summary>The editor state from the context Unity sends with a request.</summary>
    public static MacroState StateFrom(DElement context)
    {
        var state = new MacroState();
        foreach (var node in context.Child("graph").Child("nodes").Items())
        {
            var entry = new StateNode
            {
                Id = node.Str("id"), Type = node.Str("type"), Name = node.Str("name"),
                HasFlowIn = node.Str("flow_in") == "true", HasFlowOut = node.Str("flow_out") == "true",
            };
            // Ports come as "Name:Type".
            foreach (var port in node.Child("inputs").Items()) entry.Inputs.Add(PortName(port.GetString()));
            foreach (var port in node.Child("outputs").Items()) entry.Outputs.Add(PortName(port.GetString()));
            foreach (var link in node.Child("links").Items())
                entry.Links.Add((link.Str("from_port"), link.Str("to"), link.Str("to_port")));
            state.Nodes.Add(entry);
        }
        foreach (var obj in context.Child("objects").Items())
        {
            var entry = new StateObject { Path = obj.Str("path"), Name = obj.Str("name") };
            foreach (var behavior in obj.Child("behaviors").Items())
            {
                var b = new StateBehavior { Type = behavior.Str("type") };
                foreach (var variable in behavior.Child("variables").Items()) b.Variables.Add((variable.Str("name"), variable.Str("type")));
                foreach (var signal in behavior.Child("signals").Items()) b.Signals.Add((signal.Str("name"), signal.Str("dir").StartsWith("input", StringComparison.Ordinal)));
                entry.Behaviors.Add(b);
            }
            state.Objects.Add(entry);
        }
        return state;
    }

    private static string PortName(string? port)
    {
        if (string.IsNullOrEmpty(port)) return "";
        int colon = port.LastIndexOf(':');
        return colon > 0 ? port[..colon] : port;
    }

    /// <summary>Commands as the JSON Unity's executor reads (the same shape as the general model's).</summary>
    public static string ToJson(IEnumerable<MacroCommand> commands)
    {
        var builder = new JsonBuilder();
        builder.BeginArray();
        foreach (var c in commands)
            builder.BeginObject()
                .String("op", c.Op).String("id", c.Id).String("type", c.Type).String("node", c.Node)
                .String("from", c.From).String("from_port", c.FromPort).String("to", c.To).String("to_port", c.ToPort)
                .String("behavior", c.Behavior).String("signal", c.Signal).String("variable", c.Variable).String("mode", c.Mode)
                .String("path", c.Path).String("field", c.Field).String("value", c.Value).String("name", "")
                .Number("x", 0).Number("y", 0).Number("z", 0)
                .EndObject();
        builder.EndArray();
        return builder.ToString();
    }

    public void Dispose() => _weights?.Dispose();
}
