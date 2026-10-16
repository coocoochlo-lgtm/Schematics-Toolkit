using REDox;
using REDox.Json;
using System.Globalization;
using System.Text;

namespace SchematicsAgent;

/// <summary>
/// JSON for the server: RE:Dox parses everything that comes in (requests from Unity, the model's output); this small
/// writer builds what goes out.
/// </summary>
public static class Json
{
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json);

    public static string Object(Action<JsonBuilder> fill)
    {
        var builder = new JsonBuilder();
        builder.BeginObject();
        fill(builder);
        builder.EndObject();
        return builder.ToString();
    }

    // Reading helpers over RE:Dox elements.
    public static string Str(this DElement element, string name, string fallback = "")
    {
        if (!element.IsValid || element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number => value.GetDouble().ToString(CultureInfo.InvariantCulture),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback,
        };
    }

    public static double Num(this DElement element, string name, double fallback = 0)
    {
        if (!element.IsValid || element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value))
            return fallback;
        if (value.ValueKind == JsonValueKind.Number)
            return value.GetDouble();
        return value.ValueKind == JsonValueKind.String && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed)
            ? parsed : fallback;
    }

    public static DElement Child(this DElement element, string name)
        => element.IsValid && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    public static IEnumerable<DElement> Items(this DElement element)
    {
        if (!element.IsValid || element.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in element.EnumerateArray())
            yield return item;
    }

    public static string Escape(string text)
    {
        var sb = new StringBuilder(text.Length + 8);
        foreach (char c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}

/// <summary>A minimal JSON writer with a fluent API.</summary>
public sealed class JsonBuilder
{
    private readonly StringBuilder _sb = new();
    // Per open container: whether it has an item yet (so the next one needs a comma).
    private readonly Stack<bool> _hasItems = new();

    public JsonBuilder BeginObject() { Comma(); _sb.Append('{'); _hasItems.Push(false); return this; }
    public JsonBuilder BeginObject(string name) { Comma(); Key(name); _sb.Append('{'); _hasItems.Push(false); return this; }
    public JsonBuilder EndObject() { _sb.Append('}'); _hasItems.Pop(); return this; }
    public JsonBuilder BeginArray(string? name = null) { Comma(); if (name != null) Key(name); _sb.Append('['); _hasItems.Push(false); return this; }
    public JsonBuilder EndArray() { _sb.Append(']'); _hasItems.Pop(); return this; }

    public JsonBuilder String(string name, string? value) { Comma(); Key(name); Value(value); return this; }

    public JsonBuilder Number(string name, double value)
    {
        Comma(); Key(name);
        _sb.Append(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "0");
        return this;
    }

    public JsonBuilder Bool(string name, bool value) { Comma(); Key(name); _sb.Append(value ? "true" : "false"); return this; }

    public JsonBuilder Raw(string name, string json) { Comma(); Key(name); _sb.Append(json); return this; }

    public JsonBuilder StringArray(string name, IEnumerable<string> values)
    {
        BeginArray(name);
        foreach (var value in values) { Comma(); Value(value); }
        return EndArray();
    }

    private void Value(string? value)
    {
        if (value == null) _sb.Append("null");
        else _sb.Append('"').Append(Json.Escape(value)).Append('"');
    }

    private void Key(string name) => _sb.Append('"').Append(Json.Escape(name)).Append("\":");

    private void Comma()
    {
        if (_hasItems.Count == 0) return;
        if (_hasItems.Peek()) _sb.Append(',');
        else { _hasItems.Pop(); _hasItems.Push(true); }
    }

    public override string ToString() => _sb.ToString();
}

public static class SpeakRequest
{
    public static (string text, int voice, float speed) Parse(string body)
    {
        using var doc = Json.Parse(body);
        var root = doc.RootElement;
        return (root.Str("text"), (int)root.Num("voice", 0), (float)root.Num("speed", 1.0));
    }
}
