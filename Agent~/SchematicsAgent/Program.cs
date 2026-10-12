using SchematicsAgent;

// The Schematics assistant's local server. The Unity editor starts it and talks to it on localhost:
//   GET  /health      what's loaded
//   POST /ask         { utterance, context }      -> { say, commands[] }
//   POST /transcribe  WAV audio                   -> { text }
//   POST /speak       { text, voice, speed }      -> WAV audio
//   GET  /voices      Kokoro voice names
//   POST /reference/sync    every [SchematicDoc] in the project (Unity sends it after each recompile)
//   GET  /reference/search  ?q=text: the reference entries a request would see
//   POST /answers/clear     forget the answers remembered in answers.json
//   POST /voice/clear       empty the voice cache (GET /voice/cache: its size)

var settings = AgentSettings.FromArgs(args);
ServerLog.Start(settings.AgentFolder);
Console.WriteLine($"[server] Version {AgentSettings.Version}; {Cpu.Describe()}; {settings.Threads} threads for the model; language model {settings.LlmModel}");
Readiness.QuietNativeLogs();
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<Readiness>();
builder.Services.AddSingleton<Llm>();
builder.Services.AddSingleton<SpeechToText>();
builder.Services.AddSingleton<TextToSpeech>();
builder.Services.AddSingleton<ReferenceStore>();
builder.Services.AddSingleton<AnswerCache>();
builder.Services.AddSingleton<VoiceCache>();
builder.Services.AddSingleton<Planner>();

var app = builder.Build();

// Answers at once, even while the models load: "ready" says when they're done.
app.MapGet("/health", (Readiness readiness, IServiceProvider services) =>
{
    if (!readiness.IsReady)
        return Results.Text(Json.Object(w => w.Bool("ok", true).Number("version", AgentSettings.Version).Bool("slow", Cpu.Slow).String("cpu", Cpu.Describe()).Bool("ready", false).String("loading", readiness.Loading ?? "")
            .Number("progress", readiness.Progress).Number("step", readiness.Step).Number("steps", Readiness.Steps)
            .Number("elapsed", readiness.Elapsed)), "application/json");
    var llm = readiness.Get<Llm>();
    var stt = readiness.Get<SpeechToText>();
    var tts = readiness.Get<TextToSpeech>();
    return Results.Text(Json.Object(w => w
        .Bool("ok", true).Number("version", AgentSettings.Version).Bool("slow", Cpu.Slow).String("cpu", Cpu.Describe())
        .String("activity", Llm.Activity ?? "").Bool("ready", true)
        .String("error", readiness.Failure ?? "")
        .Bool("llm", llm?.Available == true).Bool("stt", stt?.Available == true).Bool("tts", tts?.Available == true)
        .String("model", llm?.ModelName ?? "")), "application/json");
});


static IResult Unavailable(string what, Readiness readiness) =>
    Results.Text(Json.Object(w => w.String("error", $"{what} isn't available: {readiness.Failure ?? "it didn't load"}")), "application/json", statusCode: 503);

// Requests that need the models wait for them for a while, then say they're still loading.
static IResult StillLoading(Readiness readiness) =>
    Results.Text(Json.Object(w => w.String("error", $"Still loading {readiness.Loading}. Try again in a moment.")), "application/json", statusCode: 503);

app.MapPost("/ask", async (HttpRequest request, Readiness readiness, IServiceProvider services) =>
{
    if (!await readiness.WaitAsync(TimeSpan.FromMinutes(3), request.HttpContext.RequestAborted))
        return StillLoading(readiness);
    // The planner needs what loaded; asking the container for a part that failed would try loading it again.
    if (readiness.Get<Llm>() == null || readiness.Get<ReferenceStore>() == null)
        return Unavailable("The language model", readiness);
    var planner = services.GetRequiredService<Planner>();
    string body = await new StreamReader(request.Body).ReadToEndAsync();
    var reply = await planner.HandleAsync(body, request.HttpContext.RequestAborted);
    return Results.Text(reply, "application/json");
});

app.MapPost("/transcribe", async (HttpRequest request, Readiness readiness, IServiceProvider services) =>
{
    if (!await readiness.WaitAsync(TimeSpan.FromMinutes(3), request.HttpContext.RequestAborted))
        return StillLoading(readiness);
    if (readiness.Get<SpeechToText>() is not { } stt)
        return Unavailable("Speech recognition", readiness);
    using var audio = new MemoryStream();
    await request.Body.CopyToAsync(audio);
    audio.Position = 0;
    string text = await stt.TranscribeAsync(audio, request.HttpContext.RequestAborted);
    return Results.Text(Json.Object(w => w.String("text", text)), "application/json");
});

app.MapPost("/speak", async (HttpRequest request, Readiness readiness, IServiceProvider services) =>
{
    if (!await readiness.WaitAsync(TimeSpan.FromMinutes(3), request.HttpContext.RequestAborted))
        return StillLoading(readiness);
    if (readiness.Get<TextToSpeech>() is not { } tts)
        return Unavailable("The voice", readiness);
    string body = await new StreamReader(request.Body).ReadToEndAsync();
    var (text, voice, speed) = SpeakRequest.Parse(body);
    try
    {
        byte[] wav = tts.Speak(text, voice, speed);
        return Results.File(wav, "audio/wav");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"[tts] Couldn't speak: {exception}");
        return Results.Text(Json.Object(w => w.String("error", exception.Message)), "application/json", statusCode: 500);
    }
});

// Empties the voice cache (Preferences > Schematics Assistant > Clear Voice Cache); GET says how big it is.
app.MapPost("/voice/clear", (VoiceCache voice) =>
{
    voice.Clear();
    Console.WriteLine("[tts] Cleared the voice cache");
    return Results.Text(Json.Object(w => w.Bool("ok", true)), "application/json");
});
app.MapGet("/voice/cache", (VoiceCache voice) =>
{
    var (files, bytes) = voice.Size();
    return Results.Text(Json.Object(w => w.Number("files", files).Number("bytes", bytes)), "application/json");
});

// Forgets every remembered answer (Preferences > Schematics Assistant > Forget Answers).
app.MapPost("/answers/clear", (AnswerCache answers) =>
{
    answers.Clear();
    Console.WriteLine("[cache] Forgot the remembered answers");
    return Results.Text(Json.Object(w => w.Bool("ok", true)), "application/json");
});

app.MapPost("/reference/sync", async (HttpRequest request, ReferenceStore reference) =>
{
    string body = await new StreamReader(request.Body).ReadToEndAsync();
    var (added, updated, removed) = reference.Sync(body);
    if (added + updated + removed > 0)
        Console.WriteLine($"[reference] {added} new, {updated} updated, {removed} removed ({reference.Count} total)");
    return Results.Text(Json.Object(w => w.Number("added", added).Number("updated", updated).Number("removed", removed).Number("total", reference.Count)), "application/json");
});

app.MapGet("/reference/search", (string q, ReferenceStore reference) =>
    Results.Text(Json.Object(w => w.StringArray("results", reference.Search(q ?? "", 12))), "application/json"));

app.MapGet("/voices", async (HttpRequest request, Readiness readiness, IServiceProvider services) =>
{
    if (!await readiness.WaitAsync(TimeSpan.FromMinutes(3), request.HttpContext.RequestAborted))
        return StillLoading(readiness);
    if (readiness.Get<TextToSpeech>() is not { } tts)
        return Unavailable("The voice", readiness);
    return Results.Text(Json.Object(w => w.StringArray("voices", tts.Voices)), "application/json");
});

// Unity stops a server it lost track of (after a crash or a restart) by this file, so the port and files are free.
string pidFile = Path.Combine(settings.AgentFolder, "server.pid");
try { File.WriteAllText(pidFile, Environment.ProcessId.ToString()); } catch { }
app.Lifetime.ApplicationStopping.Register(() => { try { File.Delete(pidFile); } catch { } });

Console.WriteLine($"[server] Listening on http://127.0.0.1:{settings.Port} (process {Environment.ProcessId}); loading the models");
// The models (and the reference, taking in anything Unity left while the server was stopped) load in the background.
app.Lifetime.ApplicationStarted.Register(() => app.Services.GetRequiredService<Readiness>().LoadInBackground(app.Services));
app.Run();
