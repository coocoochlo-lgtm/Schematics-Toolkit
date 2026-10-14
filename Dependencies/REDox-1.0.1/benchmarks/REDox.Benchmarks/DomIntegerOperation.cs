using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomIntegerOperation
{
    private readonly DArray _doxNode;
    private readonly int[] _integers;
    private readonly byte[] _jsonBytes;
    private readonly JsonElement _stjElement;
    private readonly JsonArray _stjNode;


    public DomIntegerOperation()
    {
        var json = """
                   [
                   1,
                   12,
                   123,
                   1234,
                   12345,
                   123456,
                   1234567,
                   12345678,
                   123456789,
                   -1,
                   -12,
                   -123,
                   -1234,
                   -12345,
                   -123456,
                   -1234567,
                   -12345678,
                   -123456789
                   ]
                   """;

        _stjNode = JsonNode.Parse(json)!.AsArray();
        _stjElement = JsonDocument.Parse(json).RootElement;
        _doxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        _jsonBytes = Encoding.UTF8.GetBytes(json);

        _integers = JsonSerializer.Deserialize<int[]>(_jsonBytes)!;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxElementInt32Parse()
    {
        long sum = 0;
        foreach (var item in _doxNode.AsElement().EnumerateArray())
        {
            var id = item.GetInt32();
            sum += id;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJElementInt32Parse()
    {
        long sum = 0;
        foreach (var item in _stjElement.EnumerateArray())
        {
            var dt = item.GetInt32();
            sum += dt;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxInt32Serialize()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes<int[]>(_integers, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJInt32Serialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes<int[]>(_integers);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxInt32Dserialize()
    {
        return Json.JsonSerializer.Deserialize<int[]>(_jsonBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJInt32Deserialize()
    {
        return JsonSerializer.Deserialize<int[]>(_jsonBytes)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxNodeInt32Parse()
    {
        long sum = 0;
        foreach (var item in _doxNode)
        {
            var dt = (int)item;
            sum += dt;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJNodeInt32Parse()
    {
        long sum = 0;
        foreach (var item in _stjNode)
        {
            var dt = (int)item!;
            sum += dt;
        }

        return sum;
    }
}