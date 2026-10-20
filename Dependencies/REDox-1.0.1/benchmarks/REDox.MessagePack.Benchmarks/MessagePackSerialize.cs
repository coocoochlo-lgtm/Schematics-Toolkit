using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MessagePack;
using MessagePack.Resolvers;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;

namespace REDox.MessagePack.Benchmarks;

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
public class MessagePackSerialize<T, U>
{
    private static readonly MessagePackSerializerOptions s_options = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance);

    private U? _root;

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();

        var utf8Json = File.ReadAllBytes(filePath);

        _root = JsonSerializer.Deserialize<U>(utf8Json);

        var mpk1 = REDoxMessagePackSerialize();
        var mpk2 = MessagePackCSharpSerialize();

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mpk1);
        var json2 = global::MessagePack.MessagePackSerializer.ConvertToJson(mpk2);

        if (json1 != json2)
        {
            throw new InvalidDataException($"MessagePack serialization mismatch for {typeof(T).Name}");
        }
    }

    [Benchmark]
    [BenchmarkCategory("DOX")]
    public byte[] REDoxDoxSerialize()
    {
        return DoxSerializer.Serialize<U>(_root!, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxMessagePackSerialize()
    {
        return MessagePackSerializer.Serialize<U>(_root!, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(MessagePack))]
    public byte[] MessagePackCSharpSerialize()
    {
        return global::MessagePack.MessagePackSerializer.Serialize<U>(_root!, s_options);
    }
}