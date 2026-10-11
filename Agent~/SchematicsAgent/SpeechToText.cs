using System.Text;
using Whisper.net;

namespace SchematicsAgent;

/// <summary>Speech to text with whisper.cpp (through Whisper.net); base.en by default.</summary>
public sealed class SpeechToText : IDisposable
{
    private readonly WhisperFactory? _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool Available => _factory != null;

    public SpeechToText(AgentSettings settings)
    {
        string path = settings.Resolve(settings.WhisperModel);
        if (!File.Exists(path))
        {
            Console.WriteLine($"[stt] Whisper model not found: {path}. Run setup to download it.");
            return;
        }
        _factory = WhisperFactory.FromPath(path);
        Console.WriteLine($"[stt] Loaded {Path.GetFileName(path)}");
    }

    /// <summary>Transcribes a 16 kHz mono WAV.</summary>
    public async Task<string> TranscribeAsync(Stream wav, CancellationToken cancellation)
    {
        if (_factory == null)
            throw new InvalidOperationException("Speech to text isn't loaded. Run setup to download the Whisper model.");

        await _gate.WaitAsync(cancellation);
        try
        {
            using var processor = _factory.CreateBuilder().WithLanguage("en").Build();
            var text = new StringBuilder();
            await foreach (var segment in processor.ProcessAsync(wav, cancellation))
                text.Append(segment.Text);
            return text.ToString().Trim();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _factory?.Dispose();
}
