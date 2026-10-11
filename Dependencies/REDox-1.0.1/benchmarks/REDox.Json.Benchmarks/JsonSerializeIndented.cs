using System.Collections.Generic;
using System.Text;
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
public class JsonSerializeIndented<T, U> : JsonSerializerTestBase<T, U>
{
    private static readonly JsonSerializerOptions s_stjIndentedOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxJsonSerializeIndented()
    {
        return JsonSerializer.SerializeToUtf8Bytes(RootSource, SerializerSettings.Default, new JsonWriteOptions
        {
            WriteIndented = true
        });
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJJsonSerializeIndented()
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes<U?>(RootSource, s_stjIndentedOptions);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public byte[] Utf8JsonSerializeIndented()
    {
        var utf8Bytes = Utf8Json.JsonSerializer.Serialize(RootSource);

        return Encoding.UTF8.GetBytes(Utf8Json.JsonSerializer.PrettyPrint(utf8Bytes));
    }
}