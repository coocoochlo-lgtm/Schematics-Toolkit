using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomValueKind
{
    private readonly DElement _redoxElement;
    private readonly DArray _redoxNode;
    private readonly JsonElement _stj;
    private readonly JsonArray _stn;

    public DomValueKind()
    {
        var json = """
                   [123,true,false,15.0,"ABC","DEF",null,[1,2,3],{"name":"myname","age":55},[true,false],555,"test"]
                   """;

        _stj = System.Text.Json.JsonDocument.Parse(json).RootElement;
        _stn = JsonNode.Parse(json)!.AsArray();
        _redoxElement = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement;
        _redoxNode = Json.JsonDocument.Parse(json, SerializerSettings.Default).RootElement.AsArray();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxElementValueKind()
    {
        var sum = 0;
        foreach (var element in _redoxElement.EnumerateArray())
        {
            sum += (int)element.ValueKind;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxNodeValueKind()
    {
        var sum = 0;
        foreach (var element in _redoxNode)
        {
            sum += (int)element.GetValueKind();
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJElementValueKind()
    {
        var sum = 0;
        foreach (var element in _stj.EnumerateArray())
        {
            sum += (int)element.ValueKind;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJNodeValueKind()
    {
        var sum = 0;
        foreach (var element in _stn)
        {
            if (element == null)
            {
                sum += (int)System.Text.Json.JsonValueKind.Null;
            }
            else
            {
                sum += (int)element.GetValueKind();
            }
        }

        return sum;
    }
}