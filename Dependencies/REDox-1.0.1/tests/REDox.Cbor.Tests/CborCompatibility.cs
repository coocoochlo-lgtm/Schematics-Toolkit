using System;
using System.Collections.Generic;
using System.Formats.Cbor;
using System.Numerics;
using PeterO.Cbor;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Cbor.Tests;

public sealed class CborCompatibility
{
    private readonly ITestOutputHelper _output;

    public CborCompatibility(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MapWrite()
    {
        var id = Guid.NewGuid();
        var time = DateTimeOffset.Now;

        var map = new DMap();
        map.Add(1, 2);
        map.Add(3, 4);
        map.Add(1.2, 3.4);
        map.Add(true, false);
        map.Add(1.000m, "ABC");
        map.Add(id, time);

        var cbor = CborDocument.Encode(map);

        var r = new CborReader(cbor);
        var count = r.ReadStartMap();

        Assert.Equal(map.Count, count);

        Assert.Equal(1, r.ReadInt32());
        Assert.Equal(2, r.ReadInt32());
        Assert.Equal(3, r.ReadInt32());
        Assert.Equal(4, r.ReadInt32());
        Assert.Equal(1.2, r.ReadDouble());
        Assert.Equal(3.4, r.ReadDouble());
        Assert.True(r.ReadBoolean());
        Assert.False(r.ReadBoolean());
        Assert.Equal(1.000m, r.ReadDecimal());
        Assert.Equal("ABC", r.ReadTextString());
        var tag = r.ReadTag();
        _output.WriteLine(tag.ToString());
        Assert.Equal(id.ToByteArray(true), r.ReadByteString());
        Assert.Equal(time, r.ReadDateTimeOffset());

        r.ReadEndMap();

        var dox = DoxDocument.Encode(CborDocument.Parse(cbor).RootElement);

        using var doxDoc = DoxDocument.Parse(dox);

        var cbor2 = CborDocument.Encode(doxDoc.RootElement);

        Assert.Equal(cbor, cbor2);
    }

    [Fact]
    public void NestTest()
    {
        var item = new Item { SubItem = new Item { SubItem = new Item { SubItem = new Item() } } };

        var cbor = CborSerializer.Serialize(item, SerializerSettings.Default);

        for (var i = 0; i < 10; i++)
        {
            var options = new CborWriteOptions
            {
                MaxDepth = i
            };

            if (i > 0 && i < 4)
            {
                Assert.ThrowsAny<Exception>(() =>
                    CborSerializer.Serialize(item, SerializerSettings.Default, options)
                );
            }
            else
            {
                CborSerializer.Serialize(item, SerializerSettings.Default, options);
            }
        }

        for (var i = 0; i < 10; i++)
        {
            var options = new CborDocumentOptions
            {
                MaxDepth = i
            };

            if (i > 0 && i < 4)
            {
                Assert.ThrowsAny<DocumentParseException>(() =>
                    CborSerializer.Deserialize<Item>(cbor, SerializerSettings.Default, options)
                );
            }
            else
            {
                var doc = CborSerializer.Deserialize<Item>(cbor, SerializerSettings.Default, options);
                Assert.NotNull(doc);
            }
        }
    }

    [Fact]
    public void CborWriterTest()
    {
        var sw = new System.Formats.Cbor.CborWriter();
        using var dw = new CborWriter(SerializerSettings.Default);

        sw.WriteInt32(12345);
        dw.WriteValue(12345);

        var sr = sw.Encode();
        var dr = dw.Encode();

        Assert.Equal(sr.Length, dr.Length);
        Assert.True(sr.AsSpan().SequenceEqual(dr.AsSpan()));

        Assert.True(sw.TryEncode(sr.AsSpan(), out var writtenS) && writtenS == sr.Length);
        Assert.True(dw.TryEncode(dr.AsSpan(), out var writtenD) && writtenD == dr.Length);

        Assert.True(sr.AsSpan().SequenceEqual(dr.AsSpan()));
    }

    [Fact]
    public void CborMapSerialize()
    {
        var settings = new DoxSerializerSettings
        {
            DictionaryFormatHandling = DictionaryFormatHandling.Map
        };

        {
            var inst = new Dictionary<int, int> { { 1, 2 }, { 3, 4 } };
            var cbor = CborSerializer.Serialize(inst, settings);

            var r = new CborReader(cbor);
            Assert.Equal(2, r.ReadStartMap());
            Assert.Equal(1, r.ReadInt32());
            Assert.Equal(2, r.ReadInt32());
            Assert.Equal(3, r.ReadInt32());
            Assert.Equal(4, r.ReadInt32());
            r.ReadEndMap();
        }

        {
            var inst = new Dictionary<int[], int[]> { { new[] { 1, 2 }, new[] { 3, 4 } } };
            var cbor = CborSerializer.Serialize(inst, settings);

            var r = new CborReader(cbor);
            Assert.Equal(1, r.ReadStartMap());
            Assert.Equal(2, r.ReadStartArray());
            Assert.Equal(1, r.ReadInt32());
            Assert.Equal(2, r.ReadInt32());
            r.ReadEndArray();
            Assert.Equal(2, r.ReadStartArray());
            Assert.Equal(3, r.ReadInt32());
            Assert.Equal(4, r.ReadInt32());
            r.ReadEndArray();
            r.ReadEndMap();
        }
    }

    [Fact]
    public void CborGuid()
    {
        var guid = Guid.NewGuid();
        var cbor = CborSerializer.Serialize(guid, SerializerSettings.Default);

        var guid2 = CborSerializer.Deserialize<Guid>(cbor, SerializerSettings.Default);

        Assert.Equal(guid, guid2);

        var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.IsValid);
        Assert.Equal(doc.RootElement.ToString(), guid.ToString());

        if (CborDocument.TryParse(cbor, out var doc2, SerializerSettings.Default))
        {
            Assert.True(doc2.IsValid);
        }
        else
        {
            Assert.Fail();
        }
    }

    [Fact]
    public void CborIndefiniteLengthParse()
    {
        var w = new System.Formats.Cbor.CborWriter();
        w.WriteStartArray(null);
        w.WriteStartMap(null);
        w.WriteTextString("A");
        w.WriteTextString("B");
        w.WriteTextString("C");
        w.WriteTextString("D");
        w.WriteEndMap();
        w.WriteStartIndefiniteLengthTextString();
        w.WriteTextString("A");
        w.WriteTextString("B");
        w.WriteTextString("C");
        w.WriteEndIndefiniteLengthTextString();
        w.WriteStartIndefiniteLengthByteString();
        w.WriteByteString(new byte[] { 1, 2 });
        w.WriteByteString(new byte[] { 3, 4 });
        w.WriteEndIndefiniteLengthByteString();
        w.WriteStartArray(null);
        w.WriteInt32(1);
        w.WriteInt32(2);
        w.WriteEndArray();
        w.WriteEndArray();

        var cbor = w.Encode();

        var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.IsValid);

        Assert.Equal(@"[{""A"":""B"",""C"":""D""},""ABC"",""AQIDBA=="",[1,2]]", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void CborEmptyParse()
    {
        var w = new System.Formats.Cbor.CborWriter();
        w.WriteStartMap(2);
        w.WriteStartMap(0);
        w.WriteEndMap();
        w.WriteStartArray(2);
        w.WriteStartMap(0);
        w.WriteEndMap();
        w.WriteStartArray(0);
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteStartArray(2);
        w.WriteStartMap(0);
        w.WriteEndMap();
        w.WriteStartArray(0);
        w.WriteEndArray();
        w.WriteEndArray();
        w.WriteStartMap(0);
        w.WriteEndMap();
        w.WriteEndMap();

        var cbor = w.Encode();

        var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.IsValid);

        Assert.Equal(@"{""{}"":[{},[]],""[{},[]]"":{}}", doc.RootElement.ToJsonString());
    }

    [Fact]
    public void CborMapParse()
    {
        var w = new System.Formats.Cbor.CborWriter();
        w.WriteStartMap(null);

        w.WriteInt32(1);
        w.WriteInt32(2);

        w.WriteTextString("A");
        w.WriteInt32(4);

        w.WriteStartArray(2);
        w.WriteInt32(5);
        w.WriteInt32(6);
        w.WriteEndArray();
        w.WriteStartArray(2);
        w.WriteInt32(7);
        w.WriteInt32(8);
        w.WriteEndArray();

        w.WriteStartMap(1);
        w.WriteTextString("B");
        w.WriteBoolean(true);
        w.WriteEndMap();
        w.WriteStartMap(1);
        w.WriteTextString("C");
        w.WriteBoolean(false);
        w.WriteEndMap();

        w.WriteEndMap();

        var cbor = w.Encode();

        var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.IsValid);

        Assert.Equal("{\"1\":2,\"A\":4,\"[5,6]\":[7,8],\"{\\\"B\\\":true}\":{\"C\":false}}",
            doc.RootElement.ToJsonString());
    }

    [Fact]
    public void CborSerialize()
    {
        var inst = new Test();

        var cbor = CborSerializer.Serialize(inst, new DoxSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore
        }, new CborWriteOptions
        {
            ConvertIndefiniteLengthEncodings = true
        });

        var json = CBORObject.DecodeFromBytes(cbor).ToJSONString();

        var r = new CborReader(cbor);

        var count = r.ReadStartMap();
        Assert.Equal(count, 6);

        for (var i = 0; i < count; i++)
        {
            var name = r.ReadTextString();

            if (r.PeekState() == CborReaderState.StartArray)
            {
                var arrayCount = r.ReadStartArray();

                for (var j = 0; j < arrayCount; j++)
                {
                    r.ReadInt64();
                }

                r.ReadEndArray();
            }

            switch (i)
            {
                case 1:
                    Assert.Equal("B", name);
                    Assert.Equal(inst.B, r.ReadInt32());
                    break;
                case 2:
                    Assert.Equal("C", name);
                    Assert.Equal(inst.C, r.ReadDecimal());
                    break;
                case 3:
                    Assert.Equal("D", name);
                    Assert.Equal(inst.D, r.ReadDouble());
                    break;
                case 4:
                    Assert.Equal("E", name);
                    Assert.Equal(inst.E, r.ReadDateTimeOffset());
                    break;
                case 5:
                    Assert.Equal("F", name);
                    Assert.Equal(inst.F, r.ReadUInt32());
                    break;
            }
        }

        r.ReadEndMap();
    }

    [Fact]
    public void CborConformanceTest()
    {
        var arr = new object[]
        {
            1,
            1.0,
            "test"
        };

        foreach (var mode in Enum.GetValues<CborConformanceMode>())
        {
            var w = new System.Formats.Cbor.CborWriter(mode);

            w.WriteStartArray(arr.Length);
            foreach (var v in arr)
            {
                if (v is double dval)
                {
                    w.WriteDouble(dval);
                }

                if (v is int ival)
                {
                    w.WriteInt32(ival);
                }

                if (v is string str)
                {
                    w.WriteTextString(str);
                }
            }

            w.WriteEndArray();

            var src = w.Encode();
            var tgt = CborSerializer.Serialize(arr, SerializerSettings.Default);

//                Assert.Equal(src.Length, tgt.Length);
        }
    }

    [Fact]
    public void CborSequence()
    {
        var w = new System.Formats.Cbor.CborWriter(allowMultipleRootLevelValues: true);

        w.WriteTextString("abc");
        w.WriteSimpleValue(CborSimpleValue.Undefined);
        w.WriteBoolean(true);
        w.WriteBigInteger(new BigInteger(123456788));
        w.WriteBigInteger(new BigInteger(-123456788));
        w.WriteDecimal(1.0001m);
        w.WriteDecimal(-1.0001m);
        w.WriteDouble(1.0);
        w.WriteHalf((Half)0.1);
        w.WriteSingle(1);
        w.WriteNull();
        w.WriteUInt32((uint)int.MaxValue + 10);
        w.WriteInt32(int.MinValue);
        w.WriteInt64((long)int.MinValue - 10);
        w.WriteUInt64(ulong.MaxValue);
        w.WriteByteString(new byte[] { 1, 2, 3 });
        w.WriteDateTimeOffset(DateTimeOffset.Now);
        w.WriteStartMap(1);
        w.WriteTextString("key");
        w.WriteTextString("value");
        w.WriteEndMap();
        /*
        w.WriteStartMap(1);
        w.WriteInt32(12);
        w.WriteTextString("value");
        w.WriteEndMap();
        */

        var src = w.Encode();
        var i = 0;

        var dst = new List<byte>();

        while (i < src.Length)
        {
            using var doc = CborDocument.Parse(src.AsMemory().Slice(i), SerializerSettings.Default);

            Assert.True(doc.IsValid);

            i += doc.Source.Length;

            dst.AddRange(CborDocument.Encode(doc.RootElement));

            Console.WriteLine(doc.ToString());
        }

        Assert.Equal(src.Length, dst.Count);

        for (i = 0; i < src.Length; i++)
        {
            Assert.Equal(src[i], dst[i]);
        }

        using var doc2 = CborDocument.Parse(src, SerializerSettings.Default,
            new CborDocumentOptions { UseSequenceFormat = true });

        Assert.True(doc2.IsValid);

        var dst2 = CborDocument.Encode(doc2.RootElement,
            new CborWriteOptions { UseSequenceFormat = true });

        Assert.Equal(src.Length, dst2.Length);

        for (i = 0; i < src.Length; i++)
        {
            Assert.Equal(src[i], dst2[i]);
        }
    }

    [Fact]
    public void CborParser()
    {
        var src = new
        {
            A = 123, B = 1.23, C = true, D = "ABC", E = 0.0010m, F = DateTimeOffset.Now, G = (uint)int.MaxValue + 10,
            H = (long)int.MinValue - 10
        };

        var w = new System.Formats.Cbor.CborWriter();

        w.WriteStartMap(8);
        w.WriteTextString(nameof(src.A));
        w.WriteInt32(src.A);
        w.WriteTextString(nameof(src.B));
        w.WriteDouble(src.B);
        w.WriteTextString(nameof(src.C));
        w.WriteBoolean(src.C);
        w.WriteTextString(nameof(src.D));
        w.WriteTextString(src.D);
        w.WriteTextString(nameof(src.E));
        w.WriteDecimal(src.E);
        w.WriteTextString(nameof(src.F));
        w.WriteDateTimeOffset(src.F);
        w.WriteTextString(nameof(src.G));
        w.WriteUInt32(src.G);
        w.WriteTextString(nameof(src.H));
        w.WriteInt64(src.H);
        w.WriteEndMap();

        var cbor = w.Encode();

        var doc = CborDocument.Parse(cbor, SerializerSettings.Default);

        Assert.True(doc.IsValid);

        var tgtjson = doc.RootElement.ToJsonString();
        var srcjson = DValue.From(src).ToJsonString();

        Assert.Equal(tgtjson, srcjson);
    }

    [Fact]
    public void InvalidCborFormat()
    {
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            var cbor = new byte[] { 0xff, 2, 3 };

            CborDocument.Parse(cbor, SerializerSettings.Default);
        });
    }

    [Fact]
    public void CborDateTime()
    {
        var dt = DateTimeOffset.Now;

        {
            var cbor = CborSerializer.Serialize(dt, SerializerSettings.Default);

            var r = new CborReader(cbor);

            var result = r.ReadDateTimeOffset();

            Assert.Equal(dt, result);
        }

        {
            var cbor = CborSerializer.Serialize(dt, SerializerSettings.Default,
                new CborWriteOptions { DateFormatHandling = CborDateFormatHandling.UnixTimeSecondsInt64 });

            var r = new CborReader(cbor);

            var result = r.ReadUnixTimeSeconds();

            Assert.Equal(dt.ToUniversalTime().ToString(), result.ToString());
        }

        {
            var cbor = CborSerializer.Serialize(dt, SerializerSettings.Default,
                new CborWriteOptions { DateFormatHandling = CborDateFormatHandling.UnixTimeSecondsDouble });

            var r = new CborReader(cbor);

            var result = r.ReadUnixTimeSeconds();

            Assert.Equal(dt.ToUniversalTime().ToUnixTimeMilliseconds(), result.ToUnixTimeMilliseconds());
        }

        {
            var w = new System.Formats.Cbor.CborWriter();
            w.WriteDateTimeOffset(dt);
            var cbor = w.Encode();

            var result = CborSerializer.Deserialize<DateTimeOffset>(cbor, SerializerSettings.Default);

            Assert.Equal(dt, result);
        }

        {
            var w = new System.Formats.Cbor.CborWriter();
            w.WriteUnixTimeSeconds(dt.ToUnixTimeSeconds());
            var cbor = w.Encode();

            var result = CborSerializer.Deserialize<DateTimeOffset>(cbor, SerializerSettings.Default);

            Assert.Equal(dt.ToUniversalTime().ToUnixTimeSeconds(), result.ToUnixTimeSeconds());
        }

        {
            var w = new System.Formats.Cbor.CborWriter();
            w.WriteUnixTimeSeconds((double)dt.ToUnixTimeSeconds());
            var cbor = w.Encode();

            var result = CborSerializer.Deserialize<DateTimeOffset>(cbor, SerializerSettings.Default);

            Assert.Equal(dt.ToUniversalTime().ToUnixTimeSeconds(), result.ToUnixTimeSeconds());
        }
    }

    public record Item
    {
        public Item? SubItem { get; set; }
    }

    public class Test
    {
        public long[] Tbl { get; set; } = new long[2];

        public int? A { get; set; } = null;

        public int? B { get; set; } = 123;

        public decimal C { get; set; } = decimal.MaxValue;

        public double D { get; set; } = double.MaxValue;

        public DateTimeOffset E { get; set; } = DateTimeOffset.Now;

        public uint F { get; set; } = (uint)int.MaxValue + 10;
    }

    private enum MajorType
    {
        PlusInteger,
        MinusInteger,
        Binary,
        String,
        Array,
        Map,
        Tag,
        Other
    }
}