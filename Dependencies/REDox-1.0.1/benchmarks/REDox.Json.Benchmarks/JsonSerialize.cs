using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks.Data;

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
public class JsonSerialize<T, U> : JsonSerializerTestBase<T, U>
{
    private static readonly JsonSerializerOptions s_stjOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions s_stjTypeInfoOptions = new()
    {
        TypeInfoResolver = DataSerializerContext.Default,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxJsonSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(RootSource, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJJsonSerialize()
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<U?>(RootSource, s_stjOptions);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJJsonTypeInfoSerialize()
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<U?>(RootSource, s_stjTypeInfoOptions);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public byte[] Utf8JsonSerialize()
    {
        return Utf8Json.JsonSerializer.Serialize(RootSource);
    }
}