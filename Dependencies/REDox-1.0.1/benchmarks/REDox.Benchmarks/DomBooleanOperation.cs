using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomBooleanOperation
{
    private readonly DArray _doxNode;
    private readonly byte[] _jsonBytes;
    private readonly JsonElement _stjElement;
    private readonly JsonArray _stjNode;
    private readonly bool[] _values;


    public DomBooleanOperation()
    {
        var json = """
                   [
                   true,
                   true,
                   true,
                   true,
                   true,
                   true,
                   true,
                   true,
                   true,
                   false,
                   false,
                   false,
                   false,
                   false,
                   false,
                   false,
                   false,
                   false
                   ]
                   """;

        _stjNode = JsonNode.Parse(json)!.AsArray();
        _stjElement = JsonDocument.Parse(json).RootElement;
        _doxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        _jsonBytes = Encoding.UTF8.GetBytes(json);

        _values = JsonSerializer.Deserialize<bool[]>(_jsonBytes)!;
    }

    [Benchmark]
    public long REDoxElementBooleanParse()
    {
        long sum = 0;
        foreach (var item in _doxNode.AsElement().EnumerateArray())
        {
            var id = item.GetBoolean();
            sum += id ? 1 : 0;
        }

        return sum;
    }

    [Benchmark]
    public long STJElementBooleanParse()
    {
        long sum = 0;
        foreach (var item in _stjElement.EnumerateArray())
        {
            var id = item.GetBoolean();
            sum += id ? 1 : 0;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxBooleanSerialize()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes<bool[]>(_values, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJBooleanSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes<bool[]>(_values);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxBooleanDserialize()
    {
        return Json.JsonSerializer.Deserialize<bool[]>(_jsonBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJBooleanDeserialize()
    {
        return JsonSerializer.Deserialize<bool[]>(_jsonBytes)!.Length;
    }

    [Benchmark]
    public long REDoxNodeBooleanParse()
    {
        long sum = 0;
        foreach (var item in _doxNode)
        {
            var dt = (bool)item;
            sum += dt ? 1 : 0;
        }

        return sum;
    }

    [Benchmark]
    public long STJNodeBooleanParse()
    {
        long sum = 0;
        foreach (var item in _stjNode)
        {
            var dt = (bool)item!;
            sum += dt ? 1 : 0;
        }

        return sum;
    }
}