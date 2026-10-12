using LLama.Native;

namespace SchematicsAgent;

/// <summary>
/// Loading the models takes a while (a minute or more on some machines), so the server answers straight away and loads
/// them in the background. /health says what's loading; the other endpoints wait for it (or say it's still loading).
/// </summary>
public sealed class Readiness
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>What's being loaded now ("language model", ...), or null when everything is.</summary>
    public string? Loading { get; private set; } = "starting";
    public string? Failure { get; private set; }

    /// <summary>How far loading is overall, 0-1.</summary>
    public float Progress => Math.Clamp(_done + _weight * _stepProgress, 0f, 1f);

    /// <summary>Which step (1-based) of how many.</summary>
    public int Step { get; private set; }
    public const int Steps = 4;

    /// <summary>Seconds since loading began.</summary>
    public double Elapsed => _clock.Elapsed.TotalSeconds;

    private readonly System.Diagnostics.Stopwatch _clock = new();
    private float _done, _weight, _stepProgress;

    /// <summary>Progress within the current step, from the libraries that report it (llama.cpp does).</summary>
    private sealed class StepProgress : IProgress<float>
    {
        private readonly Readiness _owner;
        public StepProgress(Readiness owner) => _owner = owner;
        public void Report(float value) => _owner._stepProgress = Math.Clamp(value, 0f, 1f);
    }

    // Each step's share of the bar, roughly by how long it takes (the models' sizes).
    private void Begin(int step, string what, float weight)
    {
        _done += _weight;
        _weight = weight;
        _stepProgress = 0f;
        Step = step;
        Loading = what;
        _stepStarted = Elapsed;
        Console.WriteLine($"[server] Loading {what} (step {step} of {Steps})");
    }

    private double _stepStarted;

    private void Load(int step, string what, float weight, Action load)
    {
        Begin(step, what, weight);
        try
        {
            load();
            Finished();
        }
        catch (Exception exception)
        {
            string failure = $"Loading {what} failed: {(exception.InnerException ?? exception).Message}";
            Failure = Failure == null ? failure : Failure + " " + failure;
            Console.Error.WriteLine("[server] " + failure);
        }
    }

    private void Finished() => Console.WriteLine($"[server] Loaded {Loading} in {Elapsed - _stepStarted:0.0} s");
    public bool IsReady => _ready.Task.IsCompleted;
    public Task Ready => _ready.Task;

    /// <summary>
    /// llama.cpp reports every tensor it loads; only its errors are worth showing (Unity shows the server's error
    /// output as warnings). Must run before the native library loads.
    /// </summary>
    public static void QuietNativeLogs()
    {
        NativeLibraryConfig.All.WithLogCallback((level, message) =>
        {
            if (level == LLamaLogLevel.Error && !string.IsNullOrWhiteSpace(message))
                Console.Error.WriteLine("[llama] " + message.TrimEnd());
        });
    }

    /// <summary>Loads everything, one model after another, on a background thread.</summary>
    public void LoadInBackground(IServiceProvider services) => Task.Run(() =>
    {
        _clock.Start();
        try
        {
            // One failing (speech on an unusual CPU, say) doesn't stop the others: Sprocket can still chat without it.
            Load(1, "the API reference", 0.02f, () => services.GetRequiredService<ReferenceStore>());
            Llm.LoadProgress = new StepProgress(this);
            Load(2, "the language model", 0.70f, () => services.GetRequiredService<Llm>());
            Load(3, "speech recognition", 0.10f, () => services.GetRequiredService<SpeechToText>());
            Load(4, "the voice", 0.18f, () => services.GetRequiredService<TextToSpeech>());
            _done = 1f; _weight = 0f;
            Loading = null;
            Console.WriteLine($"[server] Ready ({Elapsed:0.0} s)");
        }
        finally
        {
            _ready.TrySetResult();
        }
    });

    /// <summary>Waits (a little) for the models; false if they're still loading after that.</summary>
    public async Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellation)
    {
        if (IsReady) return true;
        var finished = await Task.WhenAny(Ready, Task.Delay(timeout, cancellation));
        return finished == Ready;
    }
}
