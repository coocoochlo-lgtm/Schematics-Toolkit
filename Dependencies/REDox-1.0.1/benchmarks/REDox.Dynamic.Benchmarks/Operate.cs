using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Benchmark;

[MemoryDiagnoser]
public class Operate
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxEnumerate()
    {
        using var arrayDoc = JsonDocument.Parse(BenchmarkData.NumberArray, SerializerSettings.Default);
        var elements = arrayDoc.RootElement.AsDynamic()!;
        foreach (int _ in elements)
        {
        }

        using var objectDoc = JsonDocument.Parse(BenchmarkData.FlatObject, SerializerSettings.Default);
        var members = objectDoc.RootElement.AsDynamic()!;
        foreach (KeyValuePair<string, dynamic> _ in members)
        {
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(DynaJson))]
    public void DynaJsonEnumerate()
    {
        var elements = DynamicJson.Parse(BenchmarkData.NumberArray);
        foreach (int _ in elements)
        {
        }

        var members = DynamicJson.Parse(BenchmarkData.FlatObject);
        foreach (KeyValuePair<string, dynamic> _ in members)
        {
        }
    }
}