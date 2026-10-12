namespace SchematicsAgent;

/// <summary>Where the models are and how to run them. Defaults match setup.ps1 / setup.sh; override with --name value.</summary>
public sealed class AgentSettings
{
    /// <summary>Unity checks this against its own, and replaces a server left from an older version.</summary>
    public const int Version = 7;

    public int Port = 5317;
    public string Models = FindModels();

    public string LlmModel = "qwen2.5-1.5b-instruct-q4_k_m.gguf";
    public int ContextSize = 8192;
    public int GpuLayers = 99;          // ignored by the CPU backend
    public string WhisperModel = "ggml-base.en.bin";
    public string KokoroFolder = "kokoro-en-v0_19";
    public int Threads = Math.Max(2, Environment.ProcessorCount / 2);

    // Agent~/models, found from wherever the server was built to (Agent~/bin, or SchematicsAgent/bin/<config>/<tfm>).
    private static string FindModels()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "models");
            if (Directory.Exists(candidate))
                return candidate;
        }
        return Path.Combine(AppContext.BaseDirectory, "models");
    }

    /// <summary>The Agent~ folder (the models' parent), where the API reference lives too.</summary>
    public string AgentFolder => Path.GetFullPath(Path.Combine(Models, ".."));

    /// <summary>Where the API reference is kept (Unity leaves incoming.json there). Default: Agent~/reference.</summary>
    public string? ReferenceFolder;

    public string Reference => ReferenceFolder ?? Path.Combine(AgentFolder, "reference");

    public string Resolve(string file) => Path.GetFullPath(Path.IsPathRooted(file) ? file : Path.Combine(Models, file));

    public static AgentSettings FromArgs(string[] args)
    {
        var settings = new AgentSettings();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            string value = args[i + 1];
            switch (args[i])
            {
                case "--port": settings.Port = int.Parse(value); i++; break;
                case "--models": settings.Models = value; i++; break;
                case "--reference": settings.ReferenceFolder = value; i++; break;
                case "--llm": settings.LlmModel = value; i++; break;
                case "--context": settings.ContextSize = int.Parse(value); i++; break;
                case "--gpu-layers": settings.GpuLayers = int.Parse(value); i++; break;
                case "--whisper": settings.WhisperModel = value; i++; break;
                case "--kokoro": settings.KokoroFolder = value; i++; break;
                case "--threads": settings.Threads = int.Parse(value); i++; break;
            }
        }
        return settings;
    }
}
