using BenchmarkDotNet.Attributes;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Benchmark;

[MemoryDiagnoser]
public class ReadAndAccess
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxReadAndAccess()
    {
        using var doc = JsonDocument.Parse(BenchmarkData.NestedObject, SerializerSettings.Default);
        return Accumulate(doc.RootElement.AsDynamic()!);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(DynaJson))]
    public int DynaJsonReadAndAccess()
    {
        return Accumulate(DynamicJson.Parse(BenchmarkData.NestedObject));
    }

    private static int Accumulate(dynamic root)
    {
        var total = 0;
        for (var n = 0; n < BenchmarkData.AccessIterations; n++)
        {
            total += ((string)root.foo).Length;
            total += (int)root.bar;
            total += (bool)root.nest.foobar ? 1 : 0;
            total += (bool)root["nest"]["foobar"] ? 1 : 0;
        }

        return total;
    }
}