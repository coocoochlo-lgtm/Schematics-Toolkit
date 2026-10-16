using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SysCborWriter = System.Formats.Cbor.CborWriter;

namespace REDox.Cbor.Tests;

public sealed class CborSequenceTest
{
    private static readonly SerializerSettings s_settings = SerializerSettings.Default;

    private static byte[] Concat(IEnumerable<Action<SysCborWriter>> items)
    {
        using var output = new MemoryStream();

        foreach (var item in items)
        {
            var writer = new SysCborWriter();
            item(writer);
            output.Write(writer.Encode());
        }

        return output.ToArray();
    }

    private static async Task<List<DElement>> ParseAllAsync(Stream stream, Func<DElement, DElement>? map = null)
    {
        var result = new List<DElement>();

        await foreach (var element in CborSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            result.Add(map != null ? map(element) : element.Clone());
        }

        return result;
    }

    [Fact]
    public async Task ParseAsync_Integers()
    {
        var bytes = Concat(Enumerable.Range(0, 5).Select(i => (Action<SysCborWriter>)(w => w.WriteInt32(i))));

        using var stream = new MemoryStream(bytes);

        var values = new List<int>();

        await foreach (var element in CborSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(element.GetInt32());
        }

        Assert.Equal([0, 1, 2, 3, 4], values);
    }

    [Fact]
    public async Task DeserializeAsync_LargeStream()
    {
        var source = Enumerable.Range(-10000, 20000).ToArray();
        var bytes = Concat(source.Select(i => (Action<SysCborWriter>)(w => w.WriteInt32(i))));

        using var stream = new MemoryStream(bytes);

        var values = new List<int>();

        await foreach (var value in CborSequence.DeserializeAsync<int>(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(value);
        }

        Assert.Equal(source, values);
    }

    [Fact]
    public async Task ParseAsync_Maps()
    {
        var bytes = Concat(Enumerable.Range(0, 3000).Select(i => (Action<SysCborWriter>)(w =>
        {
            w.WriteStartMap(2);
            w.WriteTextString("id");
            w.WriteInt32(i);
            w.WriteTextString("name");
            w.WriteTextString($"item{i}");
            w.WriteEndMap();
        })));

        using var stream = new MemoryStream(bytes);

        var ids = new List<int>();
        var names = new List<string?>();

        await foreach (var element in CborSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            ids.Add(element.GetProperty("id").GetInt32());
            names.Add(element.GetProperty("name").GetString());
        }

        Assert.Equal(Enumerable.Range(0, 3000), ids);
        Assert.Equal(Enumerable.Range(0, 3000).Select(i => $"item{i}"), names);
    }

    [Fact]
    public async Task ParseAsync_IndefiniteLengthItems_OneByteReads()
    {
        var bytes = Concat(Enumerable.Range(0, 200).Select(i => (Action<SysCborWriter>)(w =>
        {
            w.WriteStartArray(null);
            w.WriteInt32(i);
            w.WriteStartMap(null);
            w.WriteTextString("v");
            w.WriteStartIndefiniteLengthTextString();
            w.WriteTextString("ab");
            w.WriteTextString("cd");
            w.WriteEndIndefiniteLengthTextString();
            w.WriteEndMap();
            w.WriteTag(System.Formats.Cbor.CborTag.UnsignedBigNum);
            w.WriteByteString([1, 2, 3]);
            w.WriteEndArray();
        })));

        using var stream = new ChunkedStream(bytes, 1);

        var count = 0;

        await foreach (var element in CborSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            var items = new List<DElement>();
            foreach (var item in element.EnumerateArray())
            {
                items.Add(item);
            }

            Assert.Equal(3, items.Count);
            Assert.Equal(count, items[0].GetInt32());
            Assert.Equal("abcd", items[1].GetProperty("v").GetString());
            count++;
        }

        Assert.Equal(200, count);
    }

    [Fact]
    public async Task ParseAsync_LongString_SplitAcrossReads()
    {
        var longText = new string('x', 100_000);
        var bytes = Concat([w => w.WriteTextString(longText), w => w.WriteInt32(42)]);

        using var stream = new ChunkedStream(bytes, 777);

        var elements = await ParseAllAsync(stream);

        Assert.Equal(2, elements.Count);
        Assert.Equal(longText, elements[0].GetString());
        Assert.Equal(42, elements[1].GetInt32());
    }

    [Fact]
    public async Task ParseAsync_EmptyStream()
    {
        using var stream = new MemoryStream();

        var elements = await ParseAllAsync(stream);

        Assert.Empty(elements);
    }

    [Fact]
    public async Task ParseAsync_TruncatedItem_Throws()
    {
        var bytes = Concat([w => w.WriteInt32(1), w => w.WriteTextString("truncated")]);

        using var stream = new MemoryStream(bytes, 0, bytes.Length - 3);

        await Assert.ThrowsAsync<InvalidDataException>(() => ParseAllAsync(stream));
    }

    [Fact]
    public async Task ParseAsync_ClonedElement_OutlivesIteration()
    {
        var bytes = Concat(Enumerable.Range(0, 100).Select(i => (Action<SysCborWriter>)(w =>
        {
            w.WriteStartMap(1);
            w.WriteTextString("id");
            w.WriteInt32(i);
            w.WriteEndMap();
        })));

        using var stream = new ChunkedStream(bytes, 16);

        var elements = await ParseAllAsync(stream, e => e.Clone());

        Assert.Equal(Enumerable.Range(0, 100), elements.Select(e => e.GetProperty("id").GetInt32()));
    }

    private sealed class ChunkedStream(byte[] data, int chunkSize) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            return base.Read(buffer, offset, Math.Min(count, chunkSize));
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return base.ReadAsync(buffer.Slice(0, Math.Min(buffer.Length, chunkSize)), cancellationToken);
        }
    }
}