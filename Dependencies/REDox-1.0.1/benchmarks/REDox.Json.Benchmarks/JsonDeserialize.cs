using System.Collections.Generic;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks.Data;
using REDox.Serialization;

namespace REDox.Json.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter), typeof(Twitter.Root))]
[GenericTypeArguments(typeof(Canada), typeof(Canada.Root))]
[GenericTypeArguments(typeof(CitmCatalog), typeof(CitmCatalog.Root))]
[GenericTypeArguments(typeof(Numbers), typeof(double[]))]
[GenericTypeArguments(typeof(ApacheBuilds), typeof(ApacheBuilds.Root))]
[GenericTypeArguments(typeof(GitHubEvents), typeof(GitHubEvents.GitHubEvent[]))]
[GenericTypeArguments(typeof(Gsoc2018), typeof(Dictionary<int, Gsoc2018.GsocProject>))]
[GenericTypeArguments(typeof(UpdateCenterTest), typeof(UpdateCenterTest.Root))]
[GenericTypeArguments(typeof(Mesh), typeof(Mesh.Root))]
[GenericTypeArguments(typeof(MarineIk), typeof(MarineIk.Root))]
[GenericTypeArguments(typeof(Random), typeof(Random.Root))]
[GenericTypeArguments(typeof(Instruments), typeof(Instruments.Root))]
public class JsonDeserialize<T, U> : JsonSerializerTestBase<T, U>
{
    private static readonly SerializerSettings s_parallelSetting = new DoxSerializerSettings
    {
        ParallelOptions = new ParallelDeserializeOptions
        {
            ParallelDeserializeEnabled = true
        }
    };

    private static readonly JsonSerializerOptions s_stjTypeInfoOptions = new()
    {
        TypeInfoResolver = DataSerializerContext.Default
    };

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxJsonDeserialize()
    {
        return JsonSerializer.Deserialize<U>(Utf8JsonSource, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxJsonParallelDeserialize()
    {
        return JsonSerializer.Deserialize<U>(Utf8JsonSource, s_parallelSetting);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public U? STJJsonDeserialize()
    {
        return System.Text.Json.JsonSerializer.Deserialize<U>(Utf8JsonSource);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public U? STJJsonTypeInfoDeserialize()
    {
        return System.Text.Json.JsonSerializer.Deserialize<U>(Utf8JsonSource, s_stjTypeInfoOptions);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public U? Utf8JsonDeserialize()
    {
        return Utf8Json.JsonSerializer.Deserialize<U>(Utf8JsonSource);
    }
}