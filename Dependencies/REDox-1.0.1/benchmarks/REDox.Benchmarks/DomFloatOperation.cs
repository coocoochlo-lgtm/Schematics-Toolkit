using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomFloatOperation
{
    private readonly DArray _doxNode;
    private readonly byte[] _jsonBytes;
    private readonly JsonElement _stjElement;
    private readonly JsonArray _stjNode;
    private readonly float[] _values;

    public DomFloatOperation()
    {
        var json = """
                   [
                   1.5,
                   12.5,
                   123.5,
                   1234.5,
                   12345.5,
                   123456.5,
                   1234567.5,
                   12345678.5,
                   123456789.5,
                   -1.5,
                   -12.5,
                   -123.5,
                   -1234.5,
                   -12345.5,
                   -123456.5,
                   -1234567.5,
                   -12345678.5,
                   -123456789.5
                   ]
                   """;

        _stjNode = JsonNode.Parse(json)!.AsArray();
        _stjElement = JsonDocument.Parse(json).RootElement;
        _doxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        _jsonBytes = Encoding.UTF8.GetBytes(json);

        _values = JsonSerializer.Deserialize<float[]>(_jsonBytes)!;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public float REDoxElementFloatParse()
    {
        float sum = 0;
        foreach (var item in _doxNode.AsElement().EnumerateArray())
        {
            var id = item.GetSingle();
            sum += id;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public float STJElementFloatParse()
    {
        float sum = 0;
        foreach (var item in _stjElement.EnumerateArray())
        {
            var dt = item.GetSingle();
            sum += dt;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxFloatSerialize()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes<float[]>(_values, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJFloatSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes<float[]>(_values);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxFloatDserialize()
    {
        return Json.JsonSerializer.Deserialize<float[]>(_jsonBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJFloatDeserialize()
    {
        return JsonSerializer.Deserialize<float[]>(_jsonBytes)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public float REDoxNodeFloatParse()
    {
        float sum = 0;
        foreach (var item in _doxNode)
        {
            var dt = (float)item;
            sum += dt;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public float STJNodeFloatParse()
    {
        float sum = 0;
        foreach (var item in _stjNode)
        {
            var dt = (float)item!;
            sum += dt;
        }

        return sum;
    }
}