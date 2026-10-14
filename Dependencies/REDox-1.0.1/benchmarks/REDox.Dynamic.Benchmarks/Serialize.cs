using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Benchmark;

[MemoryDiagnoser]
public class Serialize
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxSerialize()
    {
        var profile = new
        {
            Name = "Foo",
            Age = 30,
            Address = new { Country = "Japan", City = "Tokyo" },
            Like = new[] { "Microsoft", "Xbox" }
        };
        var profileJson = JsonSerializer.Serialize(profile, SerializerSettings.Default);
        var recordsJson = JsonSerializer.Serialize(CreateRecords(), SerializerSettings.Default);
        var mapJson = JsonSerializer.Serialize(CreateMap(), SerializerSettings.Default);

        var built = new DObject().AsDynamic();
        built.str = "aaa";
        built.obj = new { foo = "bar" };
        var builtJson = built.ToString();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(DynaJson))]
    public void DynaJsonSerialize()
    {
        var profile = new
        {
            Name = "Foo",
            Age = 30,
            Address = new { Country = "Japan", City = "Tokyo" },
            Like = new[] { "Microsoft", "Xbox" }
        };
        var profileJson = DynamicJson.Serialize(profile);
        var recordsJson = DynamicJson.Serialize(CreateRecords());
        var mapJson = JsonObject.Serialize(CreateMap());

        dynamic built = new JsonObject();
        built.str = "aaa";
        built.obj = new { foo = "bar" };
        var builtJson = built.ToString();
    }

    private static FooBar[] CreateRecords()
    {
        return
        [
            new FooBar { foo = "fooooo!", bar = 1000 },
            new FooBar { foo = "orz", bar = 10 }
        ];
    }

    private static Dictionary<string, int> CreateMap()
    {
        return new Dictionary<string, int>
        {
            { "aaa", 1 },
            { "bbb", 2 }
        };
    }
}