using SherpaOnnx;

namespace SchematicsAgent;

/// <summary>Text to speech with Kokoro (through sherpa-onnx), with a choice of voices.</summary>
public sealed class TextToSpeech : IDisposable
{
    private readonly OfflineTts? _tts;
    private readonly object _gate = new();

    public bool Available => _tts != null;

    /// <summary>Kokoro v0.19's English voices, by speaker id.</summary>
    public IReadOnlyList<string> Voices { get; } = new[]
    {
        "af (American female)", "af_bella", "af_nicole", "af_sarah", "af_sky",
        "am_adam", "am_michael", "bf_emma", "bf_isabella", "bm_george", "bm_lewis",
    };

    public TextToSpeech(AgentSettings settings)
    {
        string folder = settings.Resolve(settings.KokoroFolder);
        string model = Path.Combine(folder, "model.onnx");
        if (!File.Exists(model))
        {
            Console.WriteLine($"[tts] Kokoro not found in {folder}. Run setup to download it.");
            return;
        }

        var config = new OfflineTtsConfig();
        config.Model.Kokoro.Model = model;
        config.Model.Kokoro.Voices = Path.Combine(folder, "voices.bin");
        config.Model.Kokoro.Tokens = Path.Combine(folder, "tokens.txt");
        config.Model.Kokoro.DataDir = Path.Combine(folder, "espeak-ng-data");
        config.Model.NumThreads = Math.Max(1, settings.Threads / 2);
        config.MaxNumSentences = 2;
        _tts = new OfflineTts(config);
        Console.WriteLine("[tts] Loaded Kokoro");
    }

    /// <summary>Speaks the text with a voice (speaker id) and returns a 16-bit mono WAV.</summary>
    public byte[] Speak(string text, int voice, float speed)
    {
        if (_tts == null)
            throw new InvalidOperationException("Text to speech isn't loaded. Run setup to download Kokoro.");

        OfflineTtsGeneratedAudio audio;
        lock (_gate)
            audio = _tts.Generate(text, speed <= 0f ? 1f : speed, Math.Clamp(voice, 0, Voices.Count - 1));
        return Wav.Encode(audio.Samples, audio.SampleRate);
    }

    public void Dispose() => _tts?.Dispose();
}

public static class Wav
{
    /// <summary>16-bit PCM mono WAV from float samples.</summary>
    public static byte[] Encode(float[] samples, int sampleRate)
    {
        using var stream = new MemoryStream(44 + samples.Length * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + samples.Length * 2);
        writer.Write("WAVEfmt "u8.ToArray());
        writer.Write(16); writer.Write((short)1); writer.Write((short)1);
        writer.Write(sampleRate); writer.Write(sampleRate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8.ToArray());
        writer.Write(samples.Length * 2);
        foreach (float sample in samples)
            writer.Write((short)Math.Clamp(sample * 32767f, -32768f, 32767f));
        writer.Flush();
        return stream.ToArray();
    }
}
