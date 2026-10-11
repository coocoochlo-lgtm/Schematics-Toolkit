using LLama;
using LLama.Common;
using LLama.Sampling;
using System.Text;

namespace SchematicsAgent;

/// <summary>
/// The language model (llama.cpp through LLamaSharp; Qwen2.5-1.5B-Instruct by default). One request at a time;
/// grammars force its output into valid JSON of the expected shape.
/// </summary>
public sealed class Llm : IDisposable
{
    private readonly LLamaWeights? _weights;
    private readonly ModelParams? _parameters;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Available => _weights != null;
    public string ModelName { get; } = "";

    public Llm(AgentSettings settings)
    {
        string path = settings.Resolve(settings.LlmModel);
        ModelName = Path.GetFileName(path);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[llm] Model not found: {path}. Run setup to download it.");
            return;
        }
        _parameters = new ModelParams(path)
        {
            ContextSize = (uint)settings.ContextSize,
            GpuLayerCount = settings.GpuLayers,
            Threads = settings.Threads,
        };
        _weights = LLamaWeights.LoadFromFile(_parameters);
        Console.WriteLine($"[llm] Loaded {ModelName}");
    }

    /// <summary>Runs one chat turn (ChatML, as Qwen expects) and returns the reply.</summary>
    public async Task<string> CompleteAsync(string system, string user, string? grammar, int maxTokens, CancellationToken cancellation)
    {
        if (_weights == null || _parameters == null)
            throw new InvalidOperationException("The language model isn't loaded. Run setup to download it.");

        await _gate.WaitAsync(cancellation);
        try
        {
            var executor = new StatelessExecutor(_weights, _parameters);
            string prompt = $"<|im_start|>system\n{system}<|im_end|>\n<|im_start|>user\n{user}<|im_end|>\n<|im_start|>assistant\n";
            var pipeline = new DefaultSamplingPipeline
            {
                Temperature = 0.2f,
                Grammar = grammar != null ? new Grammar(grammar, "root") : null,
            };
            var inference = new InferenceParams
            {
                MaxTokens = maxTokens,
                AntiPrompts = new[] { "<|im_end|>" },
                SamplingPipeline = pipeline,
            };

            var reply = new StringBuilder();
            await foreach (var piece in executor.InferAsync(prompt, inference, cancellation))
                reply.Append(piece);
            return reply.ToString().Replace("<|im_end|>", "").Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _weights?.Dispose();
}
