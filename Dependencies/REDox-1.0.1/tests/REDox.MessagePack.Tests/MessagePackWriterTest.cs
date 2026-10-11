using System.Buffers;
using System.IO;
using REDox.Json;
using REDox.Serialization;

namespace REDox.MessagePack.Tests;

public sealed class MessagePackWriterTest
{
    [Fact]
    public void WriteEmpty()
    {
        using var writer = new MessagePackWriter(SerializerSettings.Default);

        Assert.True(writer.Encode().Length == 0);

        using var stream = new MemoryStream();

        writer.Reset(stream, SerializerSettings.Default);
        writer.Flush();
    }

    [Fact]
    public void MapWriteUndefinedLenth()
    {
        using var writer = new MessagePackWriter(SerializerSettings.Default);

        writer.WriteStartMap(null);
        writer.WriteBoolean(true);
        writer.WriteBoolean(false);

        for (var i = 0; i < 10000; i++)
        {
            writer.WriteInt32(i);
            writer.WriteStartArray(null);
            writer.WriteInt32(i);
            writer.WriteInt32(i);
            {
                writer.WriteStartMap(null);
                writer.WriteSymbol(new Utf8Symbol("a"));
                writer.WriteBoolean(true);
                writer.WriteString(i.ToString());
                writer.WriteInt32(i);
                writer.WriteEndMap();
            }
            {
                writer.WriteStartArray(null);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndMap();

        var messagePack = writer.Encode();

        using var doc = MessagePackDocument.Parse(messagePack, SerializerSettings.Default);

        TestContext.Current.TestOutputHelper?.WriteLine(doc.RootElement.ToJsonString());
    }

    [Fact]
    public void WriteUndefinedLenth()
    {
        using var writer = new MessagePackWriter(SerializerSettings.Default);

        writer.WriteStartArray(null);
        writer.WriteBoolean(true);

        for (var i = 0; i < 100000; i++)
        {
            writer.WriteStartArray(null);
            writer.WriteInt32(i);
            writer.WriteInt32(i);
            {
                writer.WriteStartMap(null);
                writer.WriteSymbol(new Utf8Symbol("a"));
                writer.WriteBoolean(true);
                writer.WriteString(i.ToString());
                writer.WriteInt32(i);
                writer.WriteEndMap();
            }
            {
                writer.WriteStartArray(null);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndArray();

        var messagePack = writer.Encode();

        using var doc = MessagePackDocument.Parse(messagePack, SerializerSettings.Default);

        TestContext.Current.TestOutputHelper?.WriteLine(doc.RootElement.ToJsonString());
    }


    [Fact]
    public void WriteUndefinedLenthToBuffer()
    {
        ArrayBufferWriter<byte> buffer = new();

        using var writer = new MessagePackWriter(buffer, SerializerSettings.Default);

        writer.WriteStartArray(null);
        writer.WriteBoolean(true);

        for (var i = 0; i < 10000; i++)
        {
            writer.WriteStartArray(null);
            writer.WriteInt32(i);
            {
                writer.WriteStartMap(null);
                writer.WriteSymbol(new Utf8Symbol("a"));
                writer.WriteBoolean(true);
                writer.WriteString(i.ToString());
                writer.WriteInt32(i);
                writer.WriteEndMap();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.Flush();

        var messagePack = buffer.WrittenMemory.ToArray();

        using var doc = MessagePackDocument.Parse(messagePack, SerializerSettings.Default);

        TestContext.Current.TestOutputHelper?.WriteLine(doc.RootElement.ToJsonString());
    }


    [Fact]
    public void WriteUndefinedLenthToStream()
    {
        using var stream = new MemoryStream();

        using var writer = new MessagePackWriter(stream, SerializerSettings.Default);

        writer.WriteStartArray(null);
        writer.WriteBoolean(true);

        for (var i = 0; i < 10000; i++)
        {
            writer.WriteStartArray(null);
            writer.WriteInt32(i);
            {
                writer.WriteStartMap(null);
                writer.WriteSymbol(new Utf8Symbol("a"));
                writer.WriteBoolean(true);
                writer.WriteString(i.ToString());
                writer.WriteInt32(i);
                writer.WriteEndMap();
            }
            writer.WriteEndArray();
        }

        writer.WriteEndArray();
        writer.Flush();

        var messagePack = stream.ToArray();

        using var doc = MessagePackDocument.Parse(messagePack, SerializerSettings.Default);

        TestContext.Current.TestOutputHelper?.WriteLine(doc.RootElement.ToJsonString());
    }
}