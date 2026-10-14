using System.Collections.Generic;
using System.IO;
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
public class JsonSerializeToStream<T, U> : JsonSerializerTestBase<T, U>
{
    private static readonly JsonSerializerOptions s_stjOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly MemoryStream _ms = new();


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxJsonSerializeToStream()
    {
        _ms.Position = 0;
        JsonSerializer.Serialize(_ms, RootSource, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public void STJJsonSerializeToStream()
    {
        _ms.Position = 0;
        System.Text.Json.JsonSerializer.Serialize<U?>(_ms, RootSource, s_stjOptions);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public void Utf8JsonSerializeToStream()
    {
        _ms.Position = 0;
        Utf8Json.JsonSerializer.Serialize(_ms, RootSource);
    }
}