using System;
using System.Buffers;
using System.IO;
using BenchmarkDotNet.Attributes;

namespace REDox.MessagePack.Benchmarks;

[MemoryDiagnoser]
public class MessagePackWrite
{
    private readonly ArrayBufferWriter<byte> _bufferWriter;
    private readonly double[] _doubleTbl;
    private readonly int[] _intTbl;
    private readonly MessagePackWriter _messagePackWriter;

    public MessagePackWrite()
    {
        _bufferWriter = new ArrayBufferWriter<byte>();
        _messagePackWriter = new MessagePackWriter(SerializerSettings.Default);

        _doubleTbl = new double[10000];
        _intTbl = new int[10000];

        var random = new Random(12345);
        for (var i = 0; i < _doubleTbl.Length; i++)
        {
            _doubleTbl[i] = (random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-6, 7));
        }

        for (var i = 0; i < _intTbl.Length; i++)
        {
            // mix fixint / int8 / int16 / int32 encodings
            _intTbl[i] = (i % 4) switch
            {
                0 => random.Next(-32, 128),
                1 => random.Next(sbyte.MinValue, byte.MaxValue + 1),
                2 => random.Next(short.MinValue, ushort.MaxValue + 1),
                _ => random.Next(int.MinValue, int.MaxValue)
            };
        }
    }

    [GlobalSetup]
    public void Setup()
    {
        var expected = MessagePackCSharpMessagePackWrite();
        var actual1 = REDoxMessagePackWrite();
        var actual2 = REDoxMessagePackWriteToBuffer();

        if (!expected.AsSpan().SequenceEqual(actual1) || !expected.AsSpan().SequenceEqual(actual2))
        {
            throw new InvalidDataException("MessagePack write output mismatch.");
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxMessagePackWrite()
    {
        var writer = _messagePackWriter;
        writer.Reset(SerializerSettings.Default);

        writer.WriteStartMap(2);
        writer.WriteString("DoubleArray"u8);
        writer.WriteStartArray(_doubleTbl.Length);
        foreach (var d in _doubleTbl)
        {
            writer.WriteDouble(d);
        }

        writer.WriteEndArray();
        writer.WriteString("IntArray"u8);
        writer.WriteStartArray(_intTbl.Length);
        foreach (var d in _intTbl)
        {
            writer.WriteInt32(d);
        }

        writer.WriteEndArray();
        writer.WriteEndMap();

        return writer.Encode();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxMessagePackWriteToBuffer()
    {
        _bufferWriter.Clear();

        var writer = _messagePackWriter;
        writer.Reset(_bufferWriter, SerializerSettings.Default);

        writer.WriteStartMap(2);
        writer.WriteString("DoubleArray"u8);
        writer.WriteStartArray(_doubleTbl.Length);
        foreach (var d in _doubleTbl)
        {
            writer.WriteDouble(d);
        }

        writer.WriteEndArray();
        writer.WriteString("IntArray"u8);
        writer.WriteStartArray(_intTbl.Length);
        foreach (var d in _intTbl)
        {
            writer.WriteInt32(d);
        }

        writer.WriteEndArray();
        writer.WriteEndMap();
        writer.Dispose();

        return _bufferWriter.WrittenSpan.ToArray();
    }

    [Benchmark]
    [BenchmarkCategory("MessagePack")]
    public byte[] MessagePackCSharpMessagePackWrite()
    {
        _bufferWriter.Clear();

        var writer = new global::MessagePack.MessagePackWriter(_bufferWriter);

        writer.WriteMapHeader(2);
        writer.WriteString("DoubleArray"u8);
        writer.WriteArrayHeader(_doubleTbl.Length);
        foreach (var d in _doubleTbl)
        {
            writer.Write(d);
        }

        writer.WriteString("IntArray"u8);
        writer.WriteArrayHeader(_intTbl.Length);
        foreach (var d in _intTbl)
        {
            writer.Write(d);
        }

        writer.Flush();

        return _bufferWriter.WrittenSpan.ToArray();
    }
}