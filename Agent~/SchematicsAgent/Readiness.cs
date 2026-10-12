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
        try
        {
            Loading = "the API reference";
            services.GetRequiredService<ReferenceStore>();
            Loading = "the language model";
            services.GetRequiredService<Llm>();
            Loading = "speech recognition";
            services.GetRequiredService<SpeechToText>();
            Loading = "the voice";
            services.GetRequiredService<TextToSpeech>();
            Loading = null;
            Console.WriteLine("[server] Ready");
        }
        catch (Exception exception)
        {
            Failure = $"Loading {Loading} failed: {exception.Message}";
            Console.Error.WriteLine("[server] " + Failure);
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
