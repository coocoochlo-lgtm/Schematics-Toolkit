using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using REDox.Json;
using REDox.Serialization;

namespace REDox.Tests;

public sealed class MiscTest
{
    private readonly ITestOutputHelper _output;

    public MiscTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EscapedJson()
    {
        var dw = new ArrayBufferWriter<byte>();
        {
            var utf8Writer = new Utf8JsonWriter(dw, new JsonWriterOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });

            utf8Writer.WriteStringValue("Hello\nWorld!");
            utf8Writer.Flush();

            var text = Encoding.UTF8.GetString(dw.WrittenSpan);

            _output.WriteLine(text);
        }

        {
            var text = Json.JsonSerializer.Serialize("Hello\nWorld!\nあいう\t\0えお", new DoxSerializerSettings());

            _output.WriteLine(text);
        }

        var value = Json.JsonSerializer.Deserialize<string>(@"""Hello\nWorld!\nあいう\tえお\u0050""",
            SerializerSettings.Default);

        _output.WriteLine(value ?? "null");
    }

    private record Person
    {
        public Person(string? name, int age, bool isMale)
        {
            Name = name;
            Age = age;
            IsMale = isMale;
        }

        public string? Name { get; set; }

        public int Age { get; set; }

        public bool IsMale { get; set; }

        public override string ToString()
        {
            return $"Person(Name={Name}, Age={Age}, IsMale={IsMale})";
        }
    }

    private record struct PersonStruct
    {
        public PersonStruct(string? name, int age, bool isMale)
        {
            Name = name;
            Age = age;
            IsMale = isMale;
        }

        public string? Name { get; set; }
        public int Age { get; set; }
        public bool IsMale { get; set; }

        public override string ToString()
        {
            return $"Person(Name={Name}, Age={Age}, IsMale={IsMale})";
        }
    }

    /*
    [Fact]
    public void XmlStream()
    {
        var source = "<test>text</test>";
        using var doc = REDox.Xml.XmlDocument.Parse(source);

        var result = System.Text.Encoding.UTF8.GetString(doc.Encode());

        _output.WriteLine(result);

        Assert.Equal(source, result);
    }
    */

    [Fact]
    public void JsonStream()
    {
        var person = new Person("Taro", 32, true);
        var obj = DValue.From(person);

        var ms = new MemoryStream();

        Json.JsonDocument.EncodeTo(obj, ms, new JsonWriteOptions { WriteBom = true });

        ms.Seek(0, SeekOrigin.Begin);

        var result = Json.JsonSerializer.Deserialize<Person>(ms, SerializerSettings.Default);

        Assert.NotNull(result);
        Assert.Equal(person.Name, result.Name);
        Assert.Equal(person.Age, result.Age);
        Assert.Equal(person.IsMale, result.IsMale);

        ms.Seek(0, SeekOrigin.Begin);

        using var doc2 = Json.JsonDocument.Parse(ms, SerializerSettings.Default);

        var result2 = doc2.RootElement.To<Person>();


        Assert.NotNull(result2);
        Assert.Equal(person.Name, result2.Name);
        Assert.Equal(person.Age, result2.Age);
        Assert.Equal(person.IsMale, result2.IsMale);
    }

    [Fact]
    public void JsonWithBom()
    {
        var person = new Person("Taro", 32, true);
        var obj = DValue.From(person);

        var utf8Json = Json.JsonDocument.Encode(obj, new JsonWriteOptions { WriteBom = true });

        var result = Json.JsonSerializer.Deserialize<Person>(utf8Json);

        Assert.NotNull(result);
        Assert.Equal(person.Name, result.Name);
        Assert.Equal(person.Age, result.Age);
        Assert.Equal(person.IsMale, result.IsMale);
    }

    [Fact]
    public void CreateObjectFrom()
    {
        var obj = DValue.From(new { A = 123, B = "Hello", C = true });

        var json = obj.ToJsonString(new JsonWriteOptions { WriteIndented = true, NewLine = "\n" });

        Assert.Equal("""
                     {
                       "A": 123,
                       "B": "Hello",
                       "C": true
                     }
                     """, json);

        _output.WriteLine(json);
    }

    [Fact]
    public void CreateArrayFrom()
    {
        var obj = DValue.From(new[] { 1, 2, 3 });

        var json = obj.ToJsonString(new JsonWriteOptions { WriteIndented = true, NewLine = "\n" });

        Assert.Equal("""
                     [
                       1,
                       2,
                       3
                     ]
                     """, json);

        _output.WriteLine(json);
    }

    [Fact]
    public void CreateNullValue()
    {
        var arr = new DArray();
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);
        arr.Add(DValue.Null);

        var json = arr.ToJsonString();

        _output.WriteLine(json);

        Assert.Equal("[null,null,null]", json);

        Assert.Equal(Json.JsonValueKind.Null, DValue.Null.GetValueKind());

        Assert.Equal(DTokenKind.Null, DValue.Null.GetToken().Kind);

        DValue undefinedValue = default;

        Assert.Equal(DTokenKind.Control, undefinedValue.GetToken().Kind);
    }


    [Fact]
    public void NodeAPI()
    {
        var value = new Person("MyName", 32, true);

        var ste = System.Text.Json.JsonSerializer.SerializeToElement(value);
        var stj = System.Text.Json.JsonSerializer.SerializeToNode(value);

        foreach (var element in ste.EnumerateObject())
        {
            _output.WriteLine(element.Name);
        }

        foreach (var element in stj!.AsObject())
        {
            _output.WriteLine(element.Value?.GetPropertyName() ?? "null");
        }

        var dox = DValue.Create(value);

        foreach (var element in dox.AsElement().EnumerateObject())
        {
            _output.WriteLine(element.Name);
        }

        foreach (var element in dox.AsObject())
        {
            _output.WriteLine(element.Value.GetPropertyName());
        }

        var arr = new DArray(new DoxSerializerSettings
            { PropertyNamingPolicy = NamingPolicy.KebabCaseLower });

        arr.Add(value);
        arr.Add(dox);
        arr.Add(DValue.Create(value));

        var json = arr.ToJsonString(new JsonWriteOptions { WriteIndented = true });

        _output.WriteLine(json);
    }

    [Fact]
    public void SerializerAPI()
    {
        var settings = new DoxSerializerSettings();

        var value = new Person("MyName", 32, true);
        var json = System.Text.Json.JsonSerializer.Serialize(value);
        var utf8Json = Encoding.UTF8.GetBytes(json);

        Assert.Equal(json, Json.JsonSerializer.Serialize(value, settings));
        Assert.Equal(json, Json.JsonSerializer.Serialize((object?)value, settings));
        Assert.Equal(json, Json.JsonSerializer.Serialize(value, typeof(Person), settings));

        Assert.True(utf8Json.SequenceEqual(Json.JsonSerializer.SerializeToUtf8Bytes(value, settings)));
        Assert.True(utf8Json.SequenceEqual(Json.JsonSerializer.SerializeToUtf8Bytes((object?)value, settings)));
        Assert.True(utf8Json.SequenceEqual(Json.JsonSerializer.SerializeToUtf8Bytes(value, typeof(Person), settings)));

        Assert.Equal(value.ToString(), Json.JsonSerializer.Deserialize<Person>(json, settings)?.ToString());
        Assert.Equal(value.ToString(), Json.JsonSerializer.Deserialize<Person>(json.AsSpan(), settings)?.ToString());
        Assert.Equal(value.ToString(), Json.JsonSerializer.Deserialize<Person>(utf8Json, settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.Deserialize<Person>(utf8Json.AsSpan(), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.Deserialize<Person>(utf8Json.AsMemory(), settings)?.ToString());

        Assert.Equal(value.ToString(), Json.JsonSerializer.Deserialize(json, typeof(Person), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.Deserialize(json.AsSpan(), typeof(Person), settings)?.ToString());
        Assert.Equal(value.ToString(), Json.JsonSerializer.Deserialize(utf8Json, typeof(Person), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.Deserialize(utf8Json.AsSpan(), typeof(Person), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.Deserialize(utf8Json.AsMemory(), typeof(Person), settings)?.ToString());

        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(json, new Person(string.Empty, 0, false), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(json.AsSpan(), new Person(string.Empty, 0, false), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(utf8Json, new Person(string.Empty, 0, false), settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(utf8Json.AsSpan(), new Person(string.Empty, 0, false), settings)
                ?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(utf8Json.AsMemory(), new Person(string.Empty, 0, false), settings)
                ?.ToString());

        var st = new PersonStruct("MyName2", 40, false);

        Assert.Equal(value.ToString(), Json.JsonSerializer.DeserializeTo(json, (object)st, settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(json.AsSpan(), (object)st, settings)?.ToString());
        Assert.Equal(value.ToString(), Json.JsonSerializer.DeserializeTo(utf8Json, (object)st, settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(utf8Json.AsSpan(), (object)st, settings)?.ToString());
        Assert.Equal(value.ToString(),
            Json.JsonSerializer.DeserializeTo(utf8Json.AsMemory(), (object)st, settings)?.ToString());
    }

    [Fact]
    public void InvalidJson()
    {
        var json = @"{""A"":123}";

        Assert.Throws<SerializationException>(() =>
            Json.JsonSerializer.Deserialize<object[]>(json, SerializerSettings.Default));
    }

    [Fact]
    public void BytesConvert()
    {
        var json = @"[1,2,3]";

        var bytes = Json.JsonSerializer.Deserialize<byte[]>(json, SerializerSettings.Default);

        Assert.Equal(1, bytes![0]);
        Assert.Equal(2, bytes![1]);
        Assert.Equal(3, bytes![2]);
    }

#if false
    [Fact]
    public void BomTest()
    {
        {
            var doc = REDox.JsonDocument.Parse("{}");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'{');
            var bin2 = doc.Encode(new JsonWriteOptions(){WriteBom = true});
            Assert.Equal(bin2[0], 0xef);
        }
        {
            var doc = REDox.Json5Document.Parse("{}");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'{');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        {
            var doc = REDox.CsvDocument.Parse("1,2");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'1');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        {
            var doc = REDox.TomlDocument.Parse("'a'='b'");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'\'');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        /*
        {
            var doc = REDox.YamlDocument.Parse("123");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'a');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        */
        {
            var doc = REDox.XmlDocument.Parse("<a/>");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'<');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        {
            var doc = REDox.HtmlDocument.Parse("<br/>");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'<');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
        {
            var doc = REDox.IniDocument.Parse("a=b\r\n");
            var bin = doc.Encode();
            Assert.Equal(bin[0], (byte)'a');
            doc.Options.WriteBom = true;
            var bin2 = doc.Encode();
            Assert.Equal(bin2[0], 0xef);
        }
    }
#endif

    [Fact]
    public void Fact1()
    {
        var obj = new DObject();

        obj["key"] = 123;

        obj["dic"] = DValue.Create(new Dictionary<object, object>
        {
            { "A", 1 }, { "B", 2 }
        });

        _output.WriteLine(obj.ToString() ?? "null");
    }

    [Fact]
    public void BigString()
    {
        /*
        var count = 1;

        for (var i = 0; i < 10; i++)
        {
            if (count > 166666666)
            {
                count = 166666666;
            }

            var ss = new string('A', count);

            _output.WriteLine($"Length: {ss.Length} {i}");

            var jsonA = Json.JsonSerializer.SerializeToUtf8Bytes(ss, SerializerSettings.Default);
            var jsonB = System.Text.Json.JsonSerializer.Serialize(ss);

            _output.WriteLine($"{jsonA.Length} {jsonB.Length}");

            var resultA = Json.JsonSerializer.Deserialize<string>(jsonB, SerializerSettings.Default);
            var resultB = System.Text.Json.JsonSerializer.Deserialize<string>(jsonB);

            Assert.Equal(resultA, resultB);

            count *= 10;
        }

        count = 1;

        for (var i = 0; i < 10; i++)
        {
            if (count > 116666666)
            {
                count = 116666666;
            }

            var ss = new string('あ', count);

            _output.WriteLine($"Length: {ss.Length} {i}");

            var jsonA = Json.JsonSerializer.Serialize(ss, SerializerSettings.Default);
            var jsonB = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(ss);

            _output.WriteLine($"{jsonA.Length} {jsonB.Length}");

            var resultA = Json.JsonSerializer.Deserialize<string>(jsonB, SerializerSettings.Default);
            var resultB = System.Text.Json.JsonSerializer.Deserialize<string>(jsonB);

            Assert.Equal(resultA, resultB);

            count *= 10;
        }


        count = 1;

        for (var i = 0; i < 10; i++)
        {
            if (count > 166666666)
            {
                count = 166666666;
            }

            var ss = new string('\t', count);

            var jsonB = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(ss);
            var jsonA = Json.JsonSerializer.Serialize(ss, SerializerSettings.Default);

            _output.WriteLine($"{jsonA.Length} {jsonB.Length}");

            var resultA = Json.JsonSerializer.Deserialize<string>(jsonB, SerializerSettings.Default);
            var resultB = System.Text.Json.JsonSerializer.Deserialize<string>(jsonB);

            Assert.Equal(resultA, resultB);

            count *= 10;
        }
        */
    }

    [Fact]
    public void JsonEncode()
    {
        using var obj = Json.JsonDocument.Parse(
            @"{""A"":123,""B"":[[1,2],""X"",""Y"",""Z"",[1,2]],""C"":{""D"":true,""E"":null},""D"":{}}",
            SerializerSettings.Default);

        var result = Json.JsonDocument.Encode(obj.RootElement, new JsonWriteOptions { WriteIndented = true });

        _output.WriteLine(Encoding.UTF8.GetString(result));
    }

    [Fact]
    public void BigNumber()
    {
        var obj = new DObject();
        var bigint =
            BigInteger.Parse(
                "12345678987654321234567799876555512228181818181818272626262522222111222444441112223333");

        obj["int128Max"] = DValue.Create(Int128.MaxValue);
        obj["int128Min"] = DValue.Create(Int128.MinValue);
        obj["uint128Max"] = DValue.Create(UInt128.MaxValue);
        obj["uint128Min"] = DValue.Create(UInt128.MinValue);
        obj["halfMax"] = DValue.Create(Half.MaxValue);
        obj["halfMin"] = DValue.Create(Half.MinValue);
//        obj["bigint"] = DoxValue.Create(bigint);

        Assert.Equal(obj["int128Max"].To<Int128>(), Int128.MaxValue);
        Assert.Equal(obj["int128Min"].To<Int128>(), Int128.MinValue);
        Assert.Equal(obj["uint128Max"].To<UInt128>(), UInt128.MaxValue);
        Assert.Equal(obj["uint128Min"].To<UInt128>(), UInt128.MinValue);
        Assert.Equal(obj["halfMax"].To<Half>(), Half.MaxValue);
        Assert.Equal(obj["halfMin"].To<Half>(), Half.MinValue);
//        Assert.Equal(obj["bigint"].To<BigInteger>(), bigint);

        Assert.Equal((string?)obj["int128Max"], Int128.MaxValue.ToString());
        Assert.Equal((double)obj["int128Max"], (double)Int128.MaxValue);
        Assert.Equal((long)obj["int128Max"], (long)Int128.MaxValue);
        Assert.Equal((ulong)obj["uint128Max"], (ulong)UInt128.MaxValue);
//        Assert.Equal((string?)obj["bigint"], bigint.ToString());
        //      Assert.Equal((double)obj["bigint"], double.Parse(bigint.ToString()));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData(@"{""A"":1,""B"":2,""C"":true}")]
    [InlineData("[]")]
    [InlineData("[1,2,3]")]
    [InlineData(@"[""A"",""B"",""C""]")]
    [InlineData("123")]
    [InlineData("1.23")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    public void REDoxJsonParse(string json)
    {
        using var doc = Json.JsonDocument.Parse(json, SerializerSettings.Default);

        Assert.Equal(Json.JsonDocument.EncodeToString(doc.RootElement), json);
    }

    [Theory]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("t")]
    [InlineData("tru")]
    [InlineData("truE")]
    [InlineData("f")]
    [InlineData("fals")]
    [InlineData("fAlse")]
    [InlineData(":")]
    [InlineData(",")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("[")]
    [InlineData("]")]
    [InlineData(@"""")]
    [InlineData("[1 2]")]
    [InlineData("{1 2}")]
    [InlineData(@"{""A""2}")]
    [InlineData(@"{""A""2,}")]
    [InlineData(@"""\""")]
    [InlineData(@"{""s"":""\u""}")]
    [InlineData(@"{""s"":""\u1""}")]
    [InlineData(@"{""s"":""\u12""}")]
    public void REDoxInvalidJsonParse(string json)
    {
        try
        {
            var stj = System.Text.Json.JsonDocument.Parse(json);

            Assert.True(stj != null);
        }
        catch (JsonException e)
        {
            _output.WriteLine(e.Message);
        }

        var result = Json.JsonDocument.TryParse(Encoding.UTF8.GetBytes(json), out var doc, SerializerSettings.Default,
            new Json.JsonDocumentOptions { EnableValueValidation = true });

        Assert.False(result);

        Assert.ThrowsAny<DocumentParseException>(() => Json.JsonDocument.Parse(json, SerializerSettings.Default,
            new Json.JsonDocumentOptions { EnableValueValidation = true }));
    }

    private static T? GetProperty<T>(string name, T? value)
    {
        return value;
    }

    private static T GetProperty<T>(string name, T? value) where T : struct
    {
        return value!.Value;
    }

    [Fact]
    public void CallTest()
    {
        int? v = 123;

        var result1 = GetProperty(nameof(v), v);

        var result2 = GetProperty<int?>(nameof(v), v);


        _output.WriteLine("OK");
    }
}