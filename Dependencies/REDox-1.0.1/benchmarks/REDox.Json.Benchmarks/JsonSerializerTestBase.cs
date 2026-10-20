using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;

namespace REDox.Json.Benchmarks;

public abstract class JsonSerializerTestBase<T, U>
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    protected byte[] Utf8JsonSource { get; private set; } = [];

    protected U? RootSource { get; private set; }

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();
        Utf8JsonSource = File.ReadAllBytes(filePath);

        var typeInfo = DataSerializerContext.Default.GetTypeInfo(typeof(U))
                       ?? throw new InvalidOperationException(
                           $"No JsonTypeInfo for {typeof(U)}");

        if (typeInfo.Type != typeof(U))
        {
            throw new InvalidOperationException(
                $"Unexpected JsonTypeInfo. Expected={typeof(U)}, Actual={typeInfo.Type}");
        }

        var stjRoot =
            System.Text.Json.JsonSerializer.Deserialize<U>(Utf8JsonSource);

        var redoxRoot =
            JsonSerializer.Deserialize<U>(
                Utf8JsonSource,
                SerializerSettings.Default);

        //
        // Deserialization equivalence:
        // serialize both object graphs with the SAME serializer.
        //
        var stjJson =
            System.Text.Json.JsonSerializer.Serialize(stjRoot, s_options);

        var redoxObjectAsStjJson =
            System.Text.Json.JsonSerializer.Serialize(redoxRoot, s_options);

        if (stjJson != redoxObjectAsStjJson)
        {
            throw new InvalidOperationException(
                "STJ and REDox deserialization results differ.");
        }

        //
        // Serialization equivalence:
        //
        var redoxJson =
            JsonSerializer.Serialize(
                stjRoot,
                SerializerSettings.Default);

        var stjNode = JsonNode.Parse(stjJson);
        var redoxNode = JsonNode.Parse(redoxJson);

        if (!JsonNode.DeepEquals(stjNode, redoxNode))
        {
            throw new InvalidOperationException(
                "STJ and REDox serialization results are not structurally equal.");
        }

        RootSource = stjRoot;
    }
}