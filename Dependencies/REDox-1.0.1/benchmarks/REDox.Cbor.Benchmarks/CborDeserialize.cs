using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;
using REDox.Serialization;

namespace REDox.Cbor.Benchmarks;

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
[GenericTypeArguments(typeof(REDox.Benchmarks.Data.Random), typeof(REDox.Benchmarks.Data.Random.Root))]
[GenericTypeArguments(typeof(Instruments), typeof(Instruments.Root))]
public class CborDeserialize<T, U>
{
    private static readonly SerializerSettings s_parallelSetting = new DoxSerializerSettings
    {
        ParallelOptions = new ParallelDeserializeOptions
        {
            ParallelDeserializeEnabled = true
        }
    };

    private byte[] _cbor = [];

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();

        var utf8Json = File.ReadAllBytes(filePath);

        var root = JsonSerializer.Deserialize<U>(utf8Json);

        var buffer = new ArrayBufferWriter<byte>();
        Dahomey.Cbor.Cbor.Serialize<U>(root!, buffer);
        _cbor = buffer.WrittenSpan.ToArray();

        var root1 = REDoxCborDeserialize();
        var root2 = DahomeyCborDeserialize();

        var json1 = JsonSerializer.Serialize(root1);
        var json2 = JsonSerializer.Serialize(root2);

        if (json1 != json2)
        {
            throw new InvalidOperationException();
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxCborDeserialize()
    {
        return CborSerializer.Deserialize<U>(_cbor, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public U? REDoxCborParallelDeserialize()
    {
        return CborSerializer.Deserialize<U>(_cbor, s_parallelSetting);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Dahomey))]
    public U DahomeyCborDeserialize()
    {
        return Dahomey.Cbor.Cbor.Deserialize<U>(_cbor);
    }
}