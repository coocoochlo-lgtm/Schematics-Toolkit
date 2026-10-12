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

    /// <summary>Told how far loading the model is (0-1). Set by <see cref="Readiness"/> before the model loads.</summary>
    public static IProgress<float>? LoadProgress;

    /// <summary>What it's doing for a request right now ("reading 1200 tokens", ...), or null when idle.</summary>
    public static volatile string? Activity;
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
        // With progress (0-1) for /health: the model is the biggest thing the server loads.
        _weights = LLamaWeights.LoadFromFileAsync(_parameters, CancellationToken.None, LoadProgress).GetAwaiter().GetResult();
        Console.WriteLine($"[llm] Loaded {ModelName}");
    }

    /// <summary>Runs one chat turn (ChatML, as Qwen expects) and returns the reply.</summary>
    public async Task<string> CompleteAsync(string task, string system, string user, string? grammar, int maxTokens, CancellationToken cancellation)
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

            // Timed, so the log shows how fast this computer runs the model.
            var clock = System.Diagnostics.Stopwatch.StartNew();
            int promptTokens = _weights.Tokenize(prompt, false, true, Encoding.UTF8).Length;
            Activity = $"{task}: reading ({promptTokens} tokens)";
            Console.WriteLine($"[llm] {task}: reading {promptTokens} tokens");
            double firstToken = 0;
            int tokens = 0;
            var reply = new StringBuilder();
            try
            {
                await foreach (var piece in executor.InferAsync(prompt, inference, cancellation))
                {
                    if (tokens++ == 0)
                        firstToken = clock.Elapsed.TotalSeconds;
                    if (tokens % 8 == 0)
                        Activity = $"{task}: writing ({tokens} tokens, {tokens / Math.Max(0.1, clock.Elapsed.TotalSeconds - firstToken):0.0}/s)";
                    reply.Append(piece);
                }
            }
            finally
            {
                Activity = null;
            }
            double total = clock.Elapsed.TotalSeconds, generating = Math.Max(0.001, total - firstToken);
            Console.WriteLine($"[llm] {task}: read {promptTokens} tokens in {firstToken:0.0} s ({promptTokens / Math.Max(0.001, firstToken):0.0}/s), "
                + $"wrote {tokens} in {generating:0.0} s ({tokens / generating:0.0}/s); {total:0.0} s in all");
            return reply.ToString().Replace("<|im_end|>", "").Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _weights?.Dispose();
}
