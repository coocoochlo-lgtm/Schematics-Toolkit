using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using PeterO.Cbor;
using REDox.Benchmarks;
using REDox.Benchmarks.Data;

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
public class CborSerialize<T, U>
{
    private readonly ArrayBufferWriter<byte> _bufferWriter = new();
    private U? _root;

    [GlobalSetup]
    public void Setup()
    {
        var filePath = DataSourceAttribute.GetFullPath<T>();

        var utf8Json = File.ReadAllBytes(filePath);

        _root = JsonSerializer.Deserialize<U>(utf8Json);

        var cbor1 = REDoxCborSerialize();
        var cbor2 = DahomeyCborSerialize();

        var json1 = CBORObject.DecodeFromBytes(cbor1).ToJSONString();
        var json2 = CBORObject.DecodeFromBytes(cbor2).ToJSONString();

        if (json1 != json2)
        {
            throw new InvalidOperationException();
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxCborSerialize()
    {
        return CborSerializer.Serialize<U>(_root!, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Dahomey))]
    public byte[] DahomeyCborSerialize()
    {
        _bufferWriter.ResetWrittenCount();
        Dahomey.Cbor.Cbor.Serialize<U>(_root!, _bufferWriter);
        return _bufferWriter.WrittenSpan.ToArray();
    }
}