using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomGuidOperation
{
    private readonly DArray _doxNode;
    private readonly Guid[] _guids;
    private readonly byte[] _jsonBytes;
    private readonly string _jsonRelax;
    private readonly byte[] _jsonRelaxBytes;
    private readonly JsonElement _stjElement;
    private readonly JsonArray _stjNode;


    public DomGuidOperation()
    {
        var json = """
                   [
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000",
                   "550E8400-E29B-41D4-A716-446655440000"                
                   ]
                   """;
        var jsonRelax = """
                        [
                        "   550e8400-e29b-41d4-a716-446655440000",
                        "550E8400-E29B-41D4-A716-446655440000",
                        "{0X550E8400,0Xe29B,0X41D4,{0Xa7,0X16,0X44,0X66,0X55,0X44,0X00,0X00}}",
                        "d3ea415b5136407a9a005339d1b6e4d2",
                        "{550e8400-e29b-41d4-a716-446655440000}",
                        "(550e8400-e29b-41d4-a716-446655440000)",
                        "550E8400-E29B-41D4-A716-446655440000",
                        "550E8400-E29B-41D4-A716-446655440000",
                        "550E8400-E29B-41D4-A716-446655440000",
                        "550E8400-E29B-41D4-A716-446655440000",
                        "550E8400-E29B-41D4-A716-446655440000"
                        ]
                        """;

        _stjNode = JsonNode.Parse(json)!.AsArray();
        _stjElement = JsonDocument.Parse(json).RootElement;
        _doxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        _jsonBytes = Encoding.UTF8.GetBytes(json);
        _jsonRelax = jsonRelax;
        _jsonRelaxBytes = Encoding.UTF8.GetBytes(jsonRelax);

        _guids = JsonSerializer.Deserialize<Guid[]>(_jsonBytes)!;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxElementGuidParse()
    {
        long sum = 0;
        foreach (var item in _doxNode.AsElement().EnumerateArray())
        {
            var id = item.GetGuid();
            sum += id.Variant;
        }

        return sum;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJElementGuidParse()
    {
        long sum = 0;
        foreach (var item in _stjElement.EnumerateArray())
        {
            var dt = item.GetGuid();
            sum += dt.Variant;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxGuidSerialize()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes<Guid[]>(_guids, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJGuidSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes<Guid[]>(_guids);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxGuidDserialize()
    {
        return Json.JsonSerializer.Deserialize<Guid[]>(_jsonBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJGuidDeserialize()
    {
        return JsonSerializer.Deserialize<Guid[]>(_jsonBytes)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxGuidRelaxDserialize()
    {
        return Json.JsonSerializer.Deserialize<Guid[]>(_jsonRelaxBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxNodeGuidParse()
    {
        long sum = 0;
        foreach (var item in _doxNode)
        {
            var dt = (Guid)item;
            sum ^= dt.Variant;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJNodeGuidParse()
    {
        long sum = 0;
        foreach (var item in _stjNode)
        {
            var dt = (Guid)item!;
            sum ^= dt.Variant;
        }

        return sum;
    }
}