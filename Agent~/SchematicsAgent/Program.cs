using SchematicsAgent;

// The Schematics assistant's local server. The Unity editor starts it and talks to it on localhost:
//   GET  /health      what's loaded
//   POST /ask         { utterance, context }      -> { say, commands[] }
//   POST /transcribe  WAV audio                   -> { text }
//   POST /speak       { text, voice, speed }      -> WAV audio
//   GET  /voices      Kokoro voice names
//   POST /reference/sync    every [SchematicDoc] in the project (Unity sends it after each recompile)
//   GET  /reference/search  ?q=text: the reference entries a request would see

var settings = AgentSettings.FromArgs(args);
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls($"http://127.0.0.1:{settings.Port}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(settings);
builder.Services.AddSingleton<Llm>();
builder.Services.AddSingleton<SpeechToText>();
builder.Services.AddSingleton<TextToSpeech>();
builder.Services.AddSingleton<ReferenceStore>();
builder.Services.AddSingleton<Planner>();

var app = builder.Build();

app.MapGet("/health", (Llm llm, SpeechToText stt, TextToSpeech tts) =>
    Results.Text(Json.Object(w => w
        .Bool("ok", true)
        .Bool("llm", llm.Available).Bool("stt", stt.Available).Bool("tts", tts.Available)
        .String("model", llm.ModelName)), "application/json"));

app.MapPost("/ask", async (HttpRequest request, Planner planner) =>
{
    string body = await new StreamReader(request.Body).ReadToEndAsync();
    var reply = await planner.HandleAsync(body, request.HttpContext.RequestAborted);
    return Results.Text(reply, "application/json");
});

app.MapPost("/transcribe", async (HttpRequest request, SpeechToText stt) =>
{
    using var audio = new MemoryStream();
    await request.Body.CopyToAsync(audio);
    audio.Position = 0;
    string text = await stt.TranscribeAsync(audio, request.HttpContext.RequestAborted);
    return Results.Text(Json.Object(w => w.String("text", text)), "application/json");
});

app.MapPost("/speak", async (HttpRequest request, TextToSpeech tts) =>
{
    string body = await new StreamReader(request.Body).ReadToEndAsync();
    var (text, voice, speed) = SpeakRequest.Parse(body);
    byte[] wav = tts.Speak(text, voice, speed);
    return Results.File(wav, "audio/wav");
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

app.MapGet("/voices", (TextToSpeech tts) =>
    Results.Text(Json.Object(w => w.StringArray("voices", tts.Voices)), "application/json"));

Console.WriteLine($"Schematics assistant listening on http://127.0.0.1:{settings.Port}");
// Loads the reference now, taking in anything Unity left while the server was stopped.
app.Services.GetRequiredService<ReferenceStore>();
app.Run();
