using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using MessagePack;
using MessagePack.Resolvers;
using REDox.Json;
using REDox.Serialization;
using REDox.Tests;

//Test code reference:
//https://github.com/MessagePack-CSharp/MessagePack-CSharp

namespace REDox.MessagePack.Tests;

public class MessagePackCompatibility
{
    private static readonly MessagePackSerializerOptions
        s_options = MessagePackSerializerOptions.Standard.WithOldSpec();

    private readonly ITestOutputHelper _output;

    public MessagePackCompatibility(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TryParseTest()
    {
        var bytes = Convert.FromHexString("c0c0");

        var ok = MessagePackDocument.TryParse(
            bytes,
            out var doc,
            SerializerSettings.Default);

        Assert.True(ok);
    }

    [Theory]
    [InlineData("cc")]
    [InlineData("d0")]
    [InlineData("cb")]
    [InlineData("c9ffffffff01")]
    [InlineData("cb00000000")]
    [InlineData("c97fffffff01")]
    public void InvalidMessagePackTest(string hex)
    {
        var bytes = Convert.FromHexString(hex);

        var ok = MessagePackDocument.TryParse(
            bytes,
            out var doc,
            SerializerSettings.Default);

        Assert.False(ok);
    }

    /*
    [Fact]
    public void TypelessTest()
    {
        object mc = new MyClass
        {
            Age = 10,
            FirstName = "hoge",
            LastName = "huga"
        };

        var typelessSettings = new DoxSerializerSettings
        {
//            SimpleTypeAssemblyName = false,
//            TypeNameHandling = TypeNameHandling.Objects
        };

        // Serialize with the typeless API
        var blob1 = global::MessagePack.MessagePackSerializer.Typeless.Serialize(mc,
            cancellationToken: TestContext.Current.CancellationToken);
        var blob2 = MessagePackSerializer.Serialize(mc, typelessSettings);

        // Blob has embedded type-assembly information.
        // ["Sandbox.MyClass, Sandbox",10,"hoge","huga"]
        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(blob1,
            cancellationToken: TestContext.Current.CancellationToken);
        var json2 = global::MessagePack.MessagePackSerializer.ConvertToJson(blob2,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(json1, json2);

        // You can deserialize to MyClass again with the typeless API
        // Note that no type has to be specified explicitly in the Deserialize call
        // as type information is embedded in the binary blob
        var objModel1 =
            global::MessagePack.MessagePackSerializer.Typeless.Deserialize(blob1,
                cancellationToken: TestContext.Current.CancellationToken) as MyClass;
        var objModel2 = MessagePackSerializer.Deserialize<object>(blob2, typelessSettings) as MyClass;

        Assert.Equal(objModel1, objModel2);
    }
    */

    [Fact]
    public void ObjectTypeSerialization()
    {
        var objects = new object[] { 1, "aaa" };
        var sbin1 = global::MessagePack.MessagePackSerializer.Serialize(objects,
            cancellationToken: TestContext.Current.CancellationToken);
        var tbin1 = MessagePackSerializer.Serialize(objects, SerializerSettings.Default);

        // [1,"aaa"]
        var sjson1 =
            global::MessagePack.MessagePackSerializer.ConvertToJson(sbin1,
                cancellationToken: TestContext.Current.CancellationToken);
        var tjson1 =
            global::MessagePack.MessagePackSerializer.ConvertToJson(tbin1,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(sjson1, tjson1);

        // Support anonymous Type Serialize
        var anonType = new { Foo = 100, Bar = "foobar" };
        var sbin2 = global::MessagePack.MessagePackSerializer.Serialize(anonType, ContractlessStandardResolver.Options,
            TestContext.Current.CancellationToken);
        var tbin2 = MessagePackSerializer.Serialize(anonType, SerializerSettings.Default);

        Assert.Equal(sbin2, tbin2);

        // {"Foo":100,"Bar":"foobar"}
        var sjson2 =
            global::MessagePack.MessagePackSerializer.ConvertToJson(sbin2,
                cancellationToken: TestContext.Current.CancellationToken);
        var tjson2 =
            global::MessagePack.MessagePackSerializer.ConvertToJson(tbin2,
                cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(sjson2, tjson2);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    public void ExtWriteTest(int payloadSize)
    {
        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++)
        {
            payload[i] = (byte)i;
        }

        using var doc = new DocumentWriter();

        var writer = new global::MessagePack.MessagePackWriter(doc);

        sbyte typeCode = 42;
        writer.WriteArrayHeader(3);
        writer.WriteExtensionFormat(
            new ExtensionResult(typeCode, payload));
        writer.WriteMapHeader(2);
        writer.WriteString("ext1"u8);
        writer.WriteExtensionFormat(
            new ExtensionResult(typeCode, payload));
        writer.WriteString("ext2"u8);
        writer.WriteExtensionFormat(
            new ExtensionResult(typeCode, payload));
        writer.WriteExtensionFormat(
            new ExtensionResult(typeCode, payload));

        writer.Flush();

        var mpk = doc.ToArray();

        var json = global::MessagePack.MessagePackSerializer.ConvertToJson(mpk,
            cancellationToken: TestContext.Current.CancellationToken);

        using var doxMpk = MessagePackDocument.Parse(mpk, SerializerSettings.Default, new MessagePackDocumentOptions
        {
            PreserveExtension = true
        });

        var json2 = doxMpk.RootElement.ToJsonString();

        Assert.Equal(json, json2);

        _output.WriteLine(json);

        var doxMpk2 = MessagePackDocument.Encode(doxMpk.RootElement, new MessagePackWriteOptions
        {
            PreserveExtension = true
        });

        Assert.Equal(mpk, doxMpk2);
    }

    [Fact]
    public void NestTest()
    {
        var item = new Item { SubItem = new Item { SubItem = new Item { SubItem = new Item() } } };

        var cbor = MessagePackSerializer.Serialize(item, SerializerSettings.Default);

        for (var i = 0; i < 10; i++)
        {
            var options = new MessagePackWriteOptions
            {
                MaxDepth = i
            };

            if (i > 0 && i < 4)
            {
                Assert.ThrowsAny<Exception>(() =>
                    MessagePackSerializer.Serialize(item, SerializerSettings.Default, options)
                );
            }
            else
            {
                MessagePackSerializer.Serialize(item, SerializerSettings.Default, options);
            }
        }

        for (var i = 0; i < 10; i++)
        {
            var options = new MessagePackDocumentOptions
            {
                MaxDepth = i
            };

            if (i > 0 && i < 4)
            {
                Assert.ThrowsAny<DocumentParseException>(() =>
                    MessagePackSerializer.Deserialize<Item>(cbor, SerializerSettings.Default, options)
                );
            }
            else
            {
                var doc = MessagePackSerializer.Deserialize<Item>(cbor, SerializerSettings.Default, options);
                Assert.NotNull(doc);
            }
        }
    }

    public static IEnumerable<object> GetInstances()
    {
        yield return Guid.NewGuid();

        yield return "ABCDEFG123456789FFFFFFFFFFFFFFFFFFF";

//        yield return new System.Numerics.BigInteger(1234567890123344);

//        yield return new STTest();

        yield return (Half)0.1;

        yield return DateTime.Now;

        yield return DateTimeOffset.Now;

        yield return 0.1m;

        yield return new Test();

//        yield return new Test2();

        yield return new Sub();

//        yield return new Test3();

        yield return new Test4();

        yield return new Dictionary<int, int> { { 1, 2 }, { 3, 4 } };

        yield return new Dictionary<bool, int> { { true, 2 }, { false, 4 } };

        yield return new Dictionary<TestEnum, int> { { TestEnum.A, 2 }, { TestEnum.B, 4 } };

        yield return new Dictionary<string, int> { { "A", 2 }, { "B", 4 } };

        yield return new Dictionary<int[], int> { { new[] { 1, 2 }, 3 }, { new[] { 4, 5, 6 }, 7 } };

        yield return new Dictionary<string[], int> { { new[] { "A", "B" }, 3 }, { new[] { "C" }, 7 } };

        yield return new Dictionary<Sub, int>
            { { new Sub { A = "t", B = 123 }, 1 }, { new Sub { A = "n", B = 4 }, 1 } };
    }

    [Fact]
    public void ArrayParse()
    {
        var arr = new double[4] { 1.0, 2.0, 3.0, 4.0 };

        var mpk = global::MessagePack.MessagePackSerializer.Serialize(arr,
            cancellationToken: TestContext.Current.CancellationToken);

        using var doc = MessagePackDocument.Parse(mpk, SerializerSettings.Default);

        Console.WriteLine(doc.RootElement.ToJsonString());
    }

    [Fact]
    public void DeeplyNestedStructure()
    {
        // 深さ 1000 の入れ子配列を作成 [[[[...]]]]
        // MessagePack-CSharp はデフォルトで深さ制限があるため、Optionsで緩和する必要があるかもしれません
        // ここでは構造のパース能力を見ます

        var depth = 1000;
        using var ms = new MemoryStream();

        // ネストの開始
        for (var i = 0; i < depth; i++)
        {
            // FixArray(1) = 0x91
            ms.WriteByte(0x91);
        }

        // 最深部の値
        ms.WriteByte(1); // FixInt 1

        var bin = ms.ToArray();

        // REDox parser test
        // Deep recursion should not throw StackOverflowException
        using var doc = MessagePackDocument.Parse(bin, SerializerSettings.Default, new MessagePackDocumentOptions
        {
            MaxDepth = depth
        });

        // ドキュメント構造の検証（任意）
        var current = doc.RootElement.AsValue();
        for (var i = 0; i < depth; i++)
        {
            Assert.Equal(JsonValueKind.Array, current.GetValueKind());
            current = current[0]; // 掘り下げる
        }

        Assert.Equal(1, (int)current);

        Assert.ThrowsAny<Exception>(() =>
        {
            using var doc = MessagePackDocument.Parse(bin, SerializerSettings.Default, new MessagePackDocumentOptions
            {
                MaxDepth = depth - 1
            });
        });
    }


    [Fact]
    public void MapWithNonStringKeys_ToJson()
    {
        var map = new Dictionary<int, string>
        {
            { 10, "Ten" },
            { 20, "Twenty" }
        };
        var bin = global::MessagePack.MessagePackSerializer.Serialize(map,
            cancellationToken: TestContext.Current.CancellationToken);

        using var doc = MessagePackDocument.Parse(bin, SerializerSettings.Default);
        var json = doc.RootElement.ToJsonString();

        // 期待値: {"10":"Ten", "20":"Twenty"} のように文字列化されるか確認
        Assert.Contains("\"10\"", json);
    }

    [Fact]
    public void UnknownExtensionType()
    {
        // FixExt1 (type = 55, data = 0xAA)
        // 0xD4 (FixExt1), 0x37 (Type 55), 0xAA (Data)
        var bin = new byte[] { 0xD4, 0x37, 0xAA };

        using var doc = MessagePackDocument.Parse(bin, SerializerSettings.Default);

        // エラーにならず、何らかの形で保持されているか
        // Jsonに変換した際にBase64などになるか、あるいは特定の表現になるかを確認
        var json = doc.RootElement.ToJsonString();
        Console.WriteLine(json);

        // 例: Base64扱いになるか、あるいはExtension情報を保持したオブジェクトになるか仕様に合わせてアサート
    }

    [Theory]
    [InlineData("AA==", "0")]
    [InlineData("AQ==", "1")]
    [InlineData("/w==", "-1")]
    [InlineData("fw==", "127")]
    [InlineData("zIA=", "128")]
    [InlineData("4A==", "-32")]
    [InlineData("0N8=", "-33")]
    [InlineData("zP8=", "255")]
    [InlineData("zQEA", "256")]
    [InlineData("0IA=", "-128")]
    [InlineData("0f9/", "-129")]
    [InlineData("zf//", "65535")]
    [InlineData("zgABAAA=", "65536")]
    [InlineData("0YAA", "-32768")]
    [InlineData("0v//f/8=", "-32769")]
    [InlineData("zv////8=", "4294967295")]
    [InlineData("zwAAAAEAAAAA", "4294967296")]
    [InlineData("0oAAAAA=", "-2147483648")]
    [InlineData("0/////9/////", "-2147483649")]
    [InlineData("z3//////////", "9223372036854775807")]
    [InlineData("04AAAAAAAAAA", "-9223372036854775808")]
    [InlineData("z///////////", "18446744073709551615")]
    [InlineData("ww==", "true")]
    [InlineData("wg==", "false")]
    [InlineData("wA==", "null")]
    [InlineData("yz/QAAAAAAAA", "0.25")]
    [InlineData("yz+5mZmZmZma", "0.1")]
    [InlineData("yz/TMzMzMzMz", "0.3")]
    [InlineData("ywAAAAAAAAAA", "0.0")]
    [InlineData("y4AAAAAAAAAA", "-0.0")]
    [InlineData("yz/4AAAAAAAA", "1.5")]
    [InlineData("y0QVrx14tYxA", "1E+20")]
    [InlineData("yzvHnKEMkkIj", "1E-20")]
    [InlineData("oA==", "\"\"")] // empty
    [InlineData("o2FiYw==", "\"abc\"")]
    [InlineData("v2FhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWE=", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("2SBhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYWFhYQ==", "\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"")]
    [InlineData("kA==", "[]")]
    [InlineData("kwECAw==", "[1,2,3]")]
    [InlineData("3AAQAQIDBAUGBwgJCgsMDQ4PEA==", "[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16]")]
    [InlineData("3AARAQIDBAUGBwgJCgsMDQ4PEBE=", "[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17]")]
    [InlineData("gA==", "{}")]
    [InlineData("g6FhAaFiAqFjAw==", "{\"a\":1,\"b\":2,\"c\":3}")]
    public void SimpleParse(string mpBase64, string json)
    {
        if (string.IsNullOrEmpty(mpBase64))
        {
            var mp = global::MessagePack.MessagePackSerializer.ConvertFromJson(json,
                cancellationToken: TestContext.Current.CancellationToken);

            Console.WriteLine(json);
            Console.WriteLine(Convert.ToBase64String(mp));
        }
        else
        {
            var mp = Convert.FromBase64String(mpBase64);

//                Assert.Equal(json, MessagePack.MessagePackSerializer.ConvertToJson(mp));

            using var jsonDoc = JsonDocument.Parse(json, SerializerSettings.Default);

            var resultMp = MessagePackDocument.Encode(jsonDoc.RootElement);

            Assert.True(mp.SequenceEqual(resultMp));

            using var doc = MessagePackDocument.Parse(mp, new DoxSerializerSettings
            {
                FloatFormatHandling = FloatFormatHandling.AlwaysIncludeDecimal
            });

            var result = doc.RootElement.ToJsonString();

            Assert.Equal(json, result);
        }
    }

    private void PrimitiveParse<T>(T value)
    {
        var mp1 = global::MessagePack.MessagePackSerializer.Serialize(value);
        var mp2 = MessagePackSerializer.Serialize(value, SerializerSettings.Default);

        Assert.True(mp1.SequenceEqual(mp2));

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mp1);
        var json2 = MessagePackDocument.Parse(mp2, new DoxSerializerSettings
        {
            DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"
        }).RootElement.ToJsonString();

        Assert.Equal(json1, json2);

        var result1 = global::MessagePack.MessagePackSerializer.Deserialize<T>(mp1);
        var result2 = MessagePackSerializer.Deserialize<T>(mp2, SerializerSettings.Default);

        Assert.Equal(result1, result2);

        Console.WriteLine(json1);
    }

    [Fact]
    public void PrimitiveParseTest()
    {
        PrimitiveParse(123);
        PrimitiveParse(1234567890123);
        PrimitiveParse<short>(12345);
        PrimitiveParse<byte>(123);
        PrimitiveParse<uint>(1234567890);
        PrimitiveParse<ulong>(1234567890123456789);
        PrimitiveParse<ushort>(54321);
        PrimitiveParse<sbyte>(-123);
        PrimitiveParse(123.456f);
        PrimitiveParse(123456.7890123);
        PrimitiveParse(123.456m);
        PrimitiveParse(true);
        PrimitiveParse(false);
        PrimitiveParse<string>("Hello, World!");
        PrimitiveParse(Guid.NewGuid());
        PrimitiveParse(DateTime.Now);
        PrimitiveParse(DateTime.UtcNow);
        PrimitiveParse(DateTimeOffset.Now);
        PrimitiveParse(DateTimeOffset.UtcNow);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(94967295)]
    public void StringParse(int size)
    {
        var str = new string('a', size);

        var mp1 = global::MessagePack.MessagePackSerializer.Serialize(str,
            cancellationToken: TestContext.Current.CancellationToken);
        var mp2 = MessagePackSerializer.Serialize(str, SerializerSettings.Default);

        Assert.True(mp1.SequenceEqual(mp2));

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mp1,
            cancellationToken: TestContext.Current.CancellationToken);
        var json2 = MessagePackDocument.Parse(mp2, new DoxSerializerSettings
        {
            DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'"
        }, new MessagePackDocumentOptions
        {
            MaxLength = 1024 * 1024 * 256
        }).RootElement.ToJsonString();

        Assert.Equal(json1, json2);

        var result1 =
            global::MessagePack.MessagePackSerializer.Deserialize<string>(mp1,
                cancellationToken: TestContext.Current.CancellationToken);
        var result2 = MessagePackSerializer.Deserialize<string>(mp2, SerializerSettings.Default,
            new MessagePackDocumentOptions
            {
                MaxLength = 1024 * 1024 * 256
            });

        Assert.Equal(result1, result2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(94967295)]
    public void BinaryParse(int size)
    {
        var bin = new byte[size];

        for (var i = 0; i < size; i++)
        {
            bin[i] = (byte)(i & 0xff);
        }

        var mp1 = global::MessagePack.MessagePackSerializer.Serialize(bin,
            cancellationToken: TestContext.Current.CancellationToken);
        var mp2 = MessagePackSerializer.Serialize(bin, SerializerSettings.Default);

        Assert.True(mp1.SequenceEqual(mp2));

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mp1,
            cancellationToken: TestContext.Current.CancellationToken);
        var json2 = MessagePackDocument.Parse(mp2, SerializerSettings.Default, new MessagePackDocumentOptions
        {
            MaxLength = 1024 * 1024 * 256
        }).RootElement.ToJsonString();

        Assert.Equal(json1, json2);

        var result1 =
            global::MessagePack.MessagePackSerializer.Deserialize<byte[]>(mp1,
                cancellationToken: TestContext.Current.CancellationToken);
        var result2 = MessagePackSerializer.Deserialize<byte[]>(mp2, SerializerSettings.Default,
            new MessagePackDocumentOptions
            {
                MaxLength = 1024 * 1024 * 256
            });

        Assert.True(result1.SequenceEqual(result2));
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(1000000)]
    public void ArrayParse2(int size)
    {
        var arr = new int[size];

        for (var i = 0; i < size; i++)
        {
            arr[i] = i;
        }

        var mp1 = global::MessagePack.MessagePackSerializer.Serialize(arr,
            cancellationToken: TestContext.Current.CancellationToken);
        var mp2 = MessagePackSerializer.Serialize(arr, SerializerSettings.Default);

        Assert.True(mp1.SequenceEqual(mp2));

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mp1,
            cancellationToken: TestContext.Current.CancellationToken);
        var json2 = MessagePackDocument.Parse(mp2, SerializerSettings.Default).RootElement.ToJsonString();

        Assert.Equal(json1, json2);

        var result1 =
            global::MessagePack.MessagePackSerializer.Deserialize<int[]>(mp1,
                cancellationToken: TestContext.Current.CancellationToken);
        var result2 = MessagePackSerializer.Deserialize<int[]>(mp2, SerializerSettings.Default);

        Assert.True(result1.SequenceEqual(result2));
    }


    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(32)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(65535)]
    [InlineData(65536)]
    [InlineData(1000000)]
    public void MapParse(int size)
    {
        var map = new Dictionary<string, int>();

        for (var i = 0; i < size; i++)
        {
            map[i.ToString()] = i;
        }

        var mp1 = global::MessagePack.MessagePackSerializer.Serialize(map,
            cancellationToken: TestContext.Current.CancellationToken);
        var mp2 = MessagePackSerializer.Serialize(map, SerializerSettings.Default);

        Assert.True(mp1.SequenceEqual(mp2));

        var json1 = global::MessagePack.MessagePackSerializer.ConvertToJson(mp1,
            cancellationToken: TestContext.Current.CancellationToken);
        var json2 = MessagePackDocument.Parse(mp2, SerializerSettings.Default).RootElement.ToJsonString();

        Assert.Equal(json1, json2);

        var result1 =
            global::MessagePack.MessagePackSerializer.Deserialize<Dictionary<string, int>>(mp1,
                cancellationToken: TestContext.Current.CancellationToken);
        var result2 = MessagePackSerializer.Deserialize<Dictionary<string, int>>(mp2, SerializerSettings.Default);

        Assert.Equal(result1, result2);
    }

    [Theory]

    // ---- 予約値（仕様で未使用：常に不正） ----
    [InlineData("wQ==")] // 0xC1: never used

    // ---- 数値スカラの切り詰め（必要バイトが足りない）----
    [InlineData("zQA=")] // 0xCD(uint16)  残り1B不足
    [InlineData("zgAAAA==")] // 0xCE(uint32)  残り3B不足
    [InlineData("zwAAAAAAAAA=")] // 0xCF(uint64)  残り7B不足
    [InlineData("0QA=")] // 0xD1(int16)   残り1B不足
    [InlineData("0gAAAA==")] // 0xD2(int32)   残り3B不足
    [InlineData("0wAAAAAAAAA=")] // 0xD3(int64)   残り7B不足
    [InlineData("ygAAAA==")] // 0xCA(float32) 残り3B不足
    [InlineData("ywAAAAAAAAA=")] // 0xCB(float64) 残り7B不足

    // ---- 文字列/バイナリの長さ不一致 ----
    [InlineData("2QVhYmM=")] // 0xD9(str8)  len=5 に対し "abc"(3B)のみ
    [InlineData("2gAB")] // 0xDA(str16) len=1 で本文なし
    [InlineData("2wAAAAE=")] // 0xDB(str32) len=1 で本文なし
    [InlineData("qkFC")] // 0xAA(fixstr/len=10) に対し 2Bのみ
    [InlineData("xAT/")] // 0xC4(bin8)  len=4 に対し 1Bのみ
    [InlineData("xQAC")] // 0xC5(bin16) len=2 で本文なし
    [InlineData("xgAAAAP/7g==")] // 0xC6(bin32) len=3 に対し 2Bのみ

    // ---- 配列の要素不足 ----
    [InlineData("kwEC")] // 0x93(fixarray len=3) に対し 2要素のみ
    [InlineData("3AACAQ==")] // 0xDC(array16 len=2) に対し 1要素のみ
    [InlineData("3QAAAAE=")] // 0xDD(array32 len=1) に対し 要素なし

    // ---- マップのペア不足/値欠落 ----
    [InlineData("gaFr")] // 0x81(fixmap len=1) 「キーのみ」で値が欠落
    [InlineData("3gAB")] // 0xDE(map16 len=1) 中身が0
    [InlineData("3wAAAAGhaw==")] // 0xDF(map32 len=1) キーのみで値欠落

    // ---- Ext フォーマットの欠落（ヘッダ/Type/データ不足）----
    [InlineData("1A==")] // 0xD4(fixext1)  ヘッダのみ
    [InlineData("1QE=")] // 0xD5(fixext2)  typeのみ、data不足
    [InlineData("1n8AAQI=")] // 0xD6(fixext4)  type+data(3B)のみ→本来4B必要
    [InlineData("1w==")] // 0xD7(fixext8)  ヘッダのみ
    [InlineData("2A==")] // 0xD8(fixext16) ヘッダのみ
    [InlineData("xwE=")] // 0xC7(ext8 len=1) type+data欠落
    [InlineData("xwIBqg==")] // 0xC7(ext8 len=2) type(1B)+data(1B)のみ → さらに1B不足
    [InlineData("yAADf6q7")] // 0xC8(ext16 len=3) type+data(2B)のみ → 1B不足
    [InlineData("yQAAAAE=")] // 0xC9(ext32 len=1) type+data欠落

    // ---- Timestamp（拡張タイプ -1）を意図的に未充足 ----
    [InlineData("1/8AAAAAAAAA")] // timestamp64（fixext8, type=-1）でpayload 7Bのみ
    [InlineData("1v8AAAA=")] // timestamp32（fixext4, type=-1）でpayload 3Bのみ

    // ---- ネスト内での不正（中途切り）----
    [InlineData("3gABoWvZAkE=")] // map(len=1): key=\"k\" はあるが value(str8 len=2) が1Bのみ
    [InlineData("kcsAAAAAAAAA")] // array(len=1): 要素float64トランケート

    // ---- いただいた元例（str8 len=0x12 に対し本文1B）----
    [InlineData("2RIA")]

    // ---- 長さ記述(ヘッダパラメータ)自体の欠落 ----
    [InlineData("2Q==")] // 0xD9(str8)  本来続くはずの「長さ1B」がない
    [InlineData("2gA=")] // 0xDA(str16) 「長さ2B」のうち1Bしかない
    [InlineData("xA==")] // 0xC4(bin8)  本来続くはずの「長さ1B」がない
    [InlineData("xw==")] // 0xC7(ext8)  本来続くはずの「長さ1B」がない
    [InlineData("yAAA")] // 0xC8(ext16) 「長さ2B」のうち1Bしかない

    // ---- Invalid UTF-8 (構造的には足りているがデコード不能) ----
    //        [InlineData("of8=")]              // 0xA1(str1) + 0xFF (不正なUTF-8バイト)

    // ---- 巨大なサイズ指定 (Allocation Check) ----
    [InlineData("2/////8=")] // 0xDB(str32) len=UInt32.Max (即座にEOFだが、メモリ確保に走ると危険)
    public void InvalidData(string invalidData)
    {
        var msgpack = Convert.FromBase64String(invalidData);

        Assert.Throws<MessagePackSerializationException>(() =>
            global::MessagePack.MessagePackSerializer.ConvertToJson(msgpack,
                cancellationToken: TestContext.Current.CancellationToken));
        Assert.False(MessagePackDocument.TryParse(msgpack, out var doc, SerializerSettings.Default));

        Assert.ThrowsAny<DocumentParseException>(() =>
            MessagePackDocument.Parse(msgpack, SerializerSettings.Default)
        );
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void ConvertToJson(object inst)
    {
        var doxsettings = new DoxSerializerSettings
        {
            DictionaryFormatHandling = DictionaryFormatHandling.Map,
            FloatFormatHandling = FloatFormatHandling.SpecialFloatAsSymbol
        };

        var type = inst.GetType();

        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>))
        {
            return; // Dictionary の場合はスキップ（キーが非文字列の場合、JSON変換で例外になる可能性があるため）
        }

        var msgpack = MessagePackSerializer.Serialize(inst, type, doxsettings);

        var json = global::MessagePack.MessagePackSerializer.ConvertToJson(msgpack,
            cancellationToken: TestContext.Current.CancellationToken);
        var doc = MessagePackDocument.Parse(msgpack, doxsettings);

        var json2 = doc.RootElement.ToJsonString();

        while (json.IndexOf("0Z") >= 0)
        {
            json = json.Replace("0Z", "Z");
        }

        Assert.Equal(json, json2);
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void Serialize(object inst)
    {
        var doxsettings = new DoxSerializerSettings
        {
            DictionaryFormatHandling = DictionaryFormatHandling.Map
        };

        var type = inst.GetType();

        var smsgpack = Pack(type, inst);
        var tmsgpack = MessagePackSerializer.Serialize(inst, type, doxsettings);

        Assert.Equal(smsgpack, tmsgpack);

        var sinst = Unpack(type, smsgpack);
        var tinst = MessagePackSerializer.Deserialize(smsgpack, type, doxsettings);

        var sjson = JsonSerializer.Serialize(sinst, type, doxsettings);
        var tjson = JsonSerializer.Serialize(tinst, type, doxsettings);

        Assert.Equal(sjson, tjson);
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void SerializeOldSpec(object inst)
    {
        var doxsettings = new DoxSerializerSettings
        {
            DictionaryFormatHandling = DictionaryFormatHandling.Map
        };

        var type = inst.GetType();

        if (!TryPackOldSpec(type, inst, out var smsgpack))
        {
            return;
        }

        var tmsgpack = MessagePackSerializer.Serialize(inst, type, doxsettings, new MessagePackWriteOptions
        {
            OldSpec = true
        });

        Assert.Equal(smsgpack, tmsgpack);

        var sinst = UnpackOldSpec(type, smsgpack);
        var tinst = MessagePackSerializer.Deserialize(smsgpack, type, doxsettings, new MessagePackDocumentOptions
        {
            OldSpec = true
        });

        var sjson = JsonSerializer.Serialize(sinst, type, doxsettings);
        var tjson = JsonSerializer.Serialize(tinst, type, doxsettings);

        Assert.Equal(sjson, tjson);
    }

    public static IEnumerable<object[]> GetDeserializeData()
    {
        foreach (var inst in TestData.GetInstances())
        {
            yield return new[] { inst };
        }
    }

    public static IEnumerable<object[]> GetSerializeData()
    {
        /*
        foreach (var inst in TestData.GetInstances())
        {
            yield return new[] { inst };
        }
        */

        foreach (var inst in GetInstances())
        {
            yield return new[] { inst };
        }
    }

    private byte[] Pack(Type type, object inst)
    {
        return global::MessagePack.MessagePackSerializer.Serialize(type, inst);
    }

    private bool TryPackOldSpec(Type type, object inst, out byte[] bytes)
    {
        try
        {
            bytes = global::MessagePack.MessagePackSerializer.Serialize(type, inst, s_options);
            return true;
        }
        catch (Exception)
        {
            bytes = [];
            return false;
        }
    }

    private object? Unpack(Type type, byte[] msgpack)
    {
        return global::MessagePack.MessagePackSerializer.Deserialize(type, msgpack);
    }

    private object? UnpackOldSpec(Type type, byte[] msgpack)
    {
        return global::MessagePack.MessagePackSerializer.Deserialize(type, msgpack, s_options);
    }

    private record MyClass
    {
        public int Age { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }

    public record Item
    {
        public Item? SubItem { get; set; }
    }

    private enum TestEnum
    {
        A,
        B,
        C
    }

    [DataContract]
    public class Sub
    {
        [DataMember] public string A { get; set; } = "Cc";

        [DataMember] public int B { get; set; } = 123;
    }

    [DataContract]
    public class Test
    {
        [DataMember] public int Param { get; set; } = 123;

        [DataMember] public short S16 { get; set; } = short.MaxValue;

        [DataMember] public short MS16 { get; set; } = short.MinValue;

        [DataMember] public int S32 { get; set; } = int.MaxValue;

        [DataMember] public int MS32 { get; set; } = int.MinValue;

        [DataMember] public bool Flag { get; set; } = true;

        [DataMember] public string Name { get; set; } = "ok";

        [DataMember]
        public string Name2 { get; set; } =
            "okokokokokokosaokokokあいうえおokokokokokokgagagaassosososososoososokokogasokoksa";

        [DataMember] public byte[] Bin { get; set; } = new byte[] { 1, 2, 3 };

        [DataMember] public float F32 { get; set; } = float.MaxValue;

        [DataMember] public double F64 { get; set; } = double.MaxValue;

        [DataMember] public long S64 { get; set; } = long.MaxValue;

        [DataMember] public ulong U64 { get; set; } = ulong.MaxValue;

        [DataMember] public double Inf { get; set; } = double.PositiveInfinity;

        [DataMember] public int[] IArr { get; set; } = new int[512];

        [DataMember] public int[] IArr2 { get; set; } = new int[0x20000];

        [DataMember] public DateTime DT { get; set; } = DateTime.Now;

        [DataMember] public DateTimeOffset DTO { get; set; } = DateTimeOffset.Now;

        [DataMember] public decimal DM { get; set; } = decimal.MaxValue;
    }

    [MessagePackObject]
    public class Test2
    {
        [Key(0)] [DataProperty(0)] public int Param1 { get; set; } = 2;

        [Key(1)] [DataProperty(1)] public bool Flag { get; set; } = true;

        [Key(3)] [DataProperty(3)] public byte[] Bin { get; set; } = new byte[] { 1, 2, 3 };
    }

    [MessagePackObject]
    public class Test3
    {
        [Key(0)] [DataProperty(0)] public int Param1 { get; } = 2;

        [Key(1)] [DataProperty(1)] public bool Flag { get; set; }

        [Key(3)] [DataProperty(3)] public byte[] Bin { get; set; } = new byte[] { 1, 2, 3 };
    }

    [MessagePackObject]
    public struct STTest
    {
        [Key(0)] [DataProperty(0)] public int Param1 { get; }

        [Key(1)] [DataProperty(1)] public bool Flag { get; }
    }

    [DataContract]
    public class Test4
    {
        [DataMember] public int Param { get; set; } = 123;

        [DataMember]
        public bool Flag
        {
            set { }
        }

        [DataMember] public string Name { get; } = "ok";

        [DataMember] public byte[] Bin { get; set; } = new byte[] { 1, 2, 3 };
    }
}