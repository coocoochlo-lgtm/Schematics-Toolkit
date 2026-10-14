using System;
using System.Linq;
using SysCborTag = System.Formats.Cbor.CborTag;
using SysCborWriter = System.Formats.Cbor.CborWriter;

namespace REDox.Cbor.Tests;

public sealed class CborPreserveTagTest
{
    private static readonly CborDocumentOptions s_preserve = new() { PreserveTag = true };
    private static readonly CborWriteOptions s_writePreserve = new() { PreserveTag = true };

    [Fact]
    public void UnknownTag_IsPreservedAsTrivia()
    {
        var writer = new SysCborWriter();
        writer.WriteTag(SysCborTag.Uri);
        writer.WriteTextString("https://example.com");
        var cbor = writer.Encode();

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        Assert.Equal("https://example.com", doc.RootElement.GetString());
        var trivia = doc.RootElement.EnumerateTrivia().ToList();
        Assert.Single(trivia);
        Assert.Equal(TriviaKind.Tag, trivia[0].Kind);
        Assert.Equal("Uri", trivia[0].GetString());
    }

    [Fact]
    public void WithoutOption_TagIsDiscarded()
    {
        var writer = new SysCborWriter();
        writer.WriteTag(SysCborTag.Uri);
        writer.WriteTextString("https://example.com");
        var cbor = writer.Encode();

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.Empty(doc.RootElement.EnumerateTrivia().ToList());
        Assert.Equal(new SysCborWriterHelper("https://example.com").Bytes, CborDocument.Encode(doc.RootElement));
    }

    [Fact]
    public void KnownTag_IsNotPreserved()
    {
        var writer = new SysCborWriter();
        writer.WriteDateTimeOffset(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var cbor = writer.Encode();

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        Assert.Empty(doc.RootElement.EnumerateTrivia().ToList());
    }

    [Fact]
    public void RoundTrip_NestedAndMultipleTags()
    {
        var writer = new SysCborWriter();
        writer.WriteStartMap(2);
        writer.WriteTextString("a");
        writer.WriteTag((SysCborTag)1000);
        writer.WriteTag((SysCborTag)70000);
        writer.WriteInt32(1);
        writer.WriteTextString("b");
        writer.WriteTag(SysCborTag.Uri);
        writer.WriteStartArray(1);
        writer.WriteTag((SysCborTag)24);
        writer.WriteByteString([1, 2, 3]);
        writer.WriteEndArray();
        writer.WriteEndMap();
        var cbor = writer.Encode();

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        var a = doc.RootElement.GetProperty("a");
        Assert.Equal(1, a.GetInt32());
        Assert.Equal(["1000", "70000"], a.EnumerateTrivia().Select(t => t.GetString()).ToArray());

        Assert.Equal(cbor, CborDocument.Encode(doc.RootElement, s_writePreserve));
    }

    [Fact]
    public void Encode_WithoutWriteOption_OmitsTags()
    {
        var writer = new SysCborWriter();
        writer.WriteTag(SysCborTag.Uri);
        writer.WriteTextString("x");
        var cbor = writer.Encode();

        using var doc = CborDocument.Parse(cbor, SerializerSettings.Default, s_preserve);

        Assert.Equal(new SysCborWriterHelper("x").Bytes, CborDocument.Encode(doc.RootElement));
    }

    private readonly struct SysCborWriterHelper
    {
        public SysCborWriterHelper(string text)
        {
            var writer = new SysCborWriter();
            writer.WriteTextString(text);
            Bytes = writer.Encode();
        }

        public byte[] Bytes { get; }
    }
}