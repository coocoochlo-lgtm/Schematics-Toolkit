using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomStringOperation
{
    private readonly DArray _doxNode;
    private readonly byte[] _jsonBytes;
    private readonly JsonElement _stjElement;
    private readonly JsonArray _stjNode;
    private readonly string[] _values;


    public DomStringOperation()
    {
        var json = """
                   [
                   "A",
                   "ABC",
                   "ABCDEFGHIJKLMNO",
                   "あいうえお",
                   "かきくけこさシスせそ",
                   "😀",
                   "月火水木",
                   "!OKFoAKOFAKOA",
                   "!OKFoAKOFAKOA",
                   "ああああああああああああああ",
                   "あああ¥u0050¥u0050あ¥u0050あああああああ",
                   "ああああああ¥u0050ああ¥u0050ああああ",
                   "ああああ¥t¥tあああああああああ",
                   "ああああああああああああああ",
                   "ああああああ¥n¥nああああああああ",
                   "ああああああああああああああ",
                   "ああああああああああああああ",
                   "ああああああああああああああ"
                   ]
                   """;

        _stjNode = JsonNode.Parse(json)!.AsArray();
        _stjElement = JsonDocument.Parse(json).RootElement;
        _doxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();

        _jsonBytes = Encoding.UTF8.GetBytes(json);

        _values = JsonSerializer.Deserialize<string[]>(_jsonBytes)!;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public float REDoxElementStringParse()
    {
        float sum = 0;
        foreach (var item in _doxNode.AsElement().EnumerateArray())
        {
            var id = item.GetString();
            sum += id!.Length;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public float STJElementStringParse()
    {
        float sum = 0;
        foreach (var item in _stjElement.EnumerateArray())
        {
            var dt = item.GetString();
            sum += dt!.Length;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxStringSerialize()
    {
        return Json.JsonSerializer.SerializeToUtf8Bytes<string[]>(_values, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJStringSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes<string[]>(_values);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public long REDoxStringDserialize()
    {
        return Json.JsonSerializer.Deserialize<string[]>(_jsonBytes, SerializerSettings.Default)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public long STJStringDeserialize()
    {
        return JsonSerializer.Deserialize<string[]>(_jsonBytes)!.Length;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public float REDoxNodeStringParse()
    {
        float sum = 0;
        foreach (var item in _doxNode)
        {
            var dt = (string)item!;
            sum += dt.Length;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public float STJNodeStringParse()
    {
        float sum = 0;
        foreach (var item in _stjNode)
        {
            var dt = (string)item!;
            sum += dt.Length;
        }

        return sum;
    }
}