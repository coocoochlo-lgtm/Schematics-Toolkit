using System.Security.Cryptography;
using NVorbis;
using OggVorbisEncoder;

namespace SchematicsAgent;

/// <summary>
/// Speech Sprocket has already said, kept on disk (voice-cache/ next to the models) so saying it again is instant.
/// Kept as Ogg Vorbis, about a tenth the size of WAV; turned back into WAV (what Unity and Windows play) when used.
/// One file per text, voice, speed and voice model. The least recently used go once the folder passes its limit.
/// </summary>
public sealed class VoiceCache
{
    private const long MaxBytes = 200L * 1024 * 1024;
    // Vorbis quality, -0.1 to 1: 0.4 keeps speech clear at roughly 40 kbit/s.
    private const float Quality = 0.4f;

    private readonly string _folder;
    private readonly string _model;
    private readonly object _gate = new();

    public VoiceCache(AgentSettings settings)
    {
        _folder = Path.Combine(settings.AgentFolder, "voice-cache");
        _model = settings.KokoroFolder;
    }

    private string PathFor(string text, int voice, float speed)
    {
        string key = $"{_model}|{voice}|{speed:0.00}|{text.Trim()}";
        return Path.Combine(_folder, Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key)))[..24] + ".ogg");
    }

    /// <summary>The WAV for something already said this way, or null.</summary>
    public byte[]? Find(string text, int voice, float speed)
    {
        string path = PathFor(text, voice, speed);
        lock (_gate)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);   // recently used: kept longest
                using var reader = new VorbisReader(path);
                var samples = new float[reader.TotalSamples * reader.Channels];
                int read = 0, got;
                while (read < samples.Length && (got = reader.ReadSamples(samples, read, samples.Length - read)) > 0)
                    read += got;
                if (read < samples.Length)
                    Array.Resize(ref samples, read);
                // Speech is mono; keep the first channel if it somehow isn't.
                if (reader.Channels > 1)
                    samples = Enumerable.Range(0, read / reader.Channels).Select(i => samples[i * reader.Channels]).ToArray();
                return Wav.Encode(samples, reader.SampleRate);
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[tts] A cached clip couldn't be read ({exception.Message}); making it again");
                try { File.Delete(path); } catch { }
                return null;
            }
        }
    }

    /// <summary>Keeps speech (mono samples) for next time.</summary>
    public void Remember(string text, int voice, float speed, float[] samples, int sampleRate)
    {
        if (samples.Length == 0)
            return;
        string path = PathFor(text, voice, speed);
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_folder);
                string temporary = path + ".part";
                using (var file = File.Create(temporary))
                    EncodeOgg(samples, sampleRate, file);
                File.Move(temporary, path, overwrite: true);
                Console.WriteLine($"[tts] Cached it ({new FileInfo(path).Length / 1024} KB)");
                Trim();
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"[tts] Couldn't cache the speech: {exception.Message}");
            }
        }
    }

    /// <summary>How much is cached (files, bytes).</summary>
    public (int files, long bytes) Size()
    {
        lock (_gate)
        {
            if (!Directory.Exists(_folder)) return (0, 0);
            var files = new DirectoryInfo(_folder).GetFiles("*.ogg");
            return (files.Length, files.Sum(f => f.Length));
        }
    }

    public void Clear()
    {
        lock (_gate)
            if (Directory.Exists(_folder))
                foreach (var file in Directory.GetFiles(_folder))
                    try { File.Delete(file); } catch { }
    }

    // Oldest-used first, until it's under the limit.
    private void Trim()
    {
        var files = new DirectoryInfo(_folder).GetFiles("*.ogg").OrderBy(f => f.LastWriteTimeUtc).ToList();
        long total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= MaxBytes) break;
            total -= file.Length;
            try { file.Delete(); } catch { }
        }
    }

    private static void EncodeOgg(float[] samples, int sampleRate, Stream output)
    {
        var info = VorbisInfo.InitVariableBitRate(1, sampleRate, Quality);
        var stream = new OggStream(RandomNumberGenerator.GetInt32(int.MaxValue));
        stream.PacketIn(HeaderPacketBuilder.BuildInfoPacket(info));
        stream.PacketIn(HeaderPacketBuilder.BuildCommentsPacket(new Comments()));
        stream.PacketIn(HeaderPacketBuilder.BuildBooksPacket(info));
        Flush(stream, output, force: true);

        var state = ProcessingState.Create(info);
        const int Chunk = 1024;
        var buffer = new float[1][];
        for (int offset = 0; offset < samples.Length; offset += Chunk)
        {
            int length = Math.Min(Chunk, samples.Length - offset);
            buffer[0] = new float[length];
            Array.Copy(samples, offset, buffer[0], 0, length);
            state.WriteData(buffer, length);
            Drain(state, stream, output);
        }
        state.WriteEndOfStream();
        Drain(state, stream, output);
        Flush(stream, output, force: true);
    }

    private static void Drain(ProcessingState state, OggStream stream, Stream output)
    {
        while (!stream.Finished && state.PacketOut(out OggPacket packet))
        {
            stream.PacketIn(packet);
            Flush(stream, output, force: false);
        }
    }

    private static void Flush(OggStream stream, Stream output, bool force)
    {
        while (stream.PageOut(out OggPage page, force))
        {
            output.Write(page.Header, 0, page.Header.Length);
            output.Write(page.Body, 0, page.Body.Length);
        }
    }
}
