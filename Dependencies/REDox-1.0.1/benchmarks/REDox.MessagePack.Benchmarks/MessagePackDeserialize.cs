using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using MessagePack;
using MessagePack.Resolvers;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;
using REDox.Serialization;

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
public class MessagePackDeserialize<T, U>
{
    private static readonly SerializerSettings s_parallelSetting = new DoxSerializerSettings
    {
        ParallelOptions = new ParallelDeserializeOptions
        {
            ParallelDeserializeEnabled = true
        }
    };

    private static readonly MessagePackSerializerOptions s_options = MessagePackSerializerOptions.Standard
        .WithResolver(ContractlessStandardResolver.Instance);

    private byte[] _dox = [];

    private byte[] _messagePack = [];

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();

        var utf8Json = File.ReadAllBytes(filePath);

        var root = JsonSerializer.Deserialize<U>(utf8Json);

        _messagePack = global::MessagePack.MessagePackSerializer.Serialize<U>(root!, s_options);

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(_messagePack);

        var inst1 = REDoxMessagePackDeserialize();
        var inst2 = MessagePackCSharpDeserialize();

        var element1 = JsonSerializer.SerializeToElement(inst1);
        var element2 = JsonSerializer.SerializeToElement(inst2);

        if (!JsonElement.DeepEquals(element1, element2))
        {
            throw new InvalidDataException("Deserialized objects are not equal.");
        }

        _dox = DoxSerializer.Serialize(root!);
        var inst3 = DoxSerializer.Deserialize<U>(_dox);

        var element3 = JsonSerializer.SerializeToElement(inst3);

        if (!JsonElement.DeepEquals(element1, element3))
        {
            throw new InvalidDataException("Deserialized objects are not equal.");
        }
    }

    [Benchmark]
    [BenchmarkCategory("DOX")]
    public U? REDoxDoxDeserialize()
    {
        return DoxSerializer.Deserialize<U>(_dox);
    }

    [Benchmark]
    [BenchmarkCategory("DOX")]
    public U? REDoxDoxParallelDeserialize()
    {
        return DoxSerializer.Deserialize<U>(_dox, s_parallelSetting);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxMessagePackDeserialize()
    {
        return MessagePackSerializer.Deserialize<U>(_messagePack, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxMessagePackParallelDeserialize()
    {
        return MessagePackSerializer.Deserialize<U>(_messagePack, s_parallelSetting);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(MessagePack))]
    public U? MessagePackCSharpDeserialize()
    {
        return global::MessagePack.MessagePackSerializer.Deserialize<U>(_messagePack, s_options);
    }
}