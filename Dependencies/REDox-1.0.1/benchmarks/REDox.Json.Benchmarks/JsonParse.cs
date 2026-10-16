using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;

namespace REDox.Json.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(Twitter))]
[GenericTypeArguments(typeof(Canada))]
[GenericTypeArguments(typeof(CitmCatalog))]
[GenericTypeArguments(typeof(Numbers))]
[GenericTypeArguments(typeof(ApacheBuilds))]
[GenericTypeArguments(typeof(GitHubEvents))]
[GenericTypeArguments(typeof(Gsoc2018))]
[GenericTypeArguments(typeof(UpdateCenterTest))]
[GenericTypeArguments(typeof(Mesh))]
[GenericTypeArguments(typeof(MarineIk))]
[GenericTypeArguments(typeof(Random))]
[GenericTypeArguments(typeof(Instruments))]
public class JsonParse<T>
{
    private string _json = string.Empty;
    private byte[] _utf8Json = [];

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();

        _utf8Json = File.ReadAllBytes(filePath);
        _json = Encoding.UTF8.GetString(_utf8Json);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxJsonDocumentParse()
    {
        using var doc = JsonDocument.Parse(_utf8Json, SerializerSettings.Default);

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.GetArrayLength();
        }

        return doc.RootElement.GetPropertyCount();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxJsonDocumentStrictParse()
    {
        using var doc = JsonDocument.Parse(_utf8Json, SerializerSettings.Default,
            new JsonDocumentOptions { EnableValueValidation = true });

        if (doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            return doc.RootElement.GetArrayLength();
        }

        return doc.RootElement.GetPropertyCount();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxJsonNodeParse()
    {
        var node = DValue.ParseJson(_utf8Json, SerializerSettings.Default);

        if (node.GetValueKind() == JsonValueKind.Array)
        {
            return node.AsArray().Count;
        }

        return node.AsObject().Count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJJsonDocumentParse()
    {
        using var doc = System.Text.Json.JsonDocument.Parse(_utf8Json);

        if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return doc.RootElement.GetArrayLength();
        }

        return doc.RootElement.GetPropertyCount();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJJsonNodeParse()
    {
        var node = JsonNode.Parse(_utf8Json);

        if (node!.GetValueKind() == System.Text.Json.JsonValueKind.Array)
        {
            return node.AsArray().Count;
        }

        return node?.AsObject().Count ?? 0;
    }
}