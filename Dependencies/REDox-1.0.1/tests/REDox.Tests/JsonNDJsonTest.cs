using System;
using System.Collections.Generic;
using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class JsonNDJsonTest
{
    private static readonly SerializerSettings s_settings = SerializerSettings.Default;

    private static readonly JsonDocumentOptions s_options = new()
    {
        UseNewlineDelimitedFormat = true
    };

    private static readonly JsonWriteOptions s_writeOptions = new()
    {
        UseNewlineDelimitedFormat = true,
        NewLine = "\n"
    };

    [Theory]
    [InlineData("{\"a\":1}\n{\"a\":2}", "{\"a\":1}\n{\"a\":2}")]
    [InlineData("{\"a\":1}\r\n{\"a\":2}\r\n", "{\"a\":1}\n{\"a\":2}")]
    [InlineData("\n{\"a\":1}\n\n{\"b\":[1,2]}\n", "{\"a\":1}\n{\"b\":[1,2]}")]
    [InlineData("1\n\"text\"\ntrue\nnull\n[1,2]", "1\n\"text\"\ntrue\nnull\n[1,2]")]
    [InlineData("{\"a\":1}", "{\"a\":1}")]
    [InlineData("", "")]
    public void EncodeNDJson(string input, string expected)
    {
        using var doc = JsonDocument.Parse(input, s_settings, s_options);

        Assert.Equal(expected, JsonDocument.EncodeToString(doc.RootElement, s_writeOptions));
    }

    [Fact]
    public void EncodeNDJson_UsesConfiguredNewLine()
    {
        using var doc = JsonDocument.Parse("{\"a\":1}\n{\"a\":2}", s_settings, s_options);

        var json = JsonDocument.EncodeToString(doc.RootElement, new JsonWriteOptions
        {
            UseNewlineDelimitedFormat = true,
            NewLine = "\r\n"
        });

        Assert.Equal("{\"a\":1}\r\n{\"a\":2}", json);
    }

    [Fact]
    public void EncodeNDJson_NonArrayRoot_WritesSingleValue()
    {
        using var doc = JsonDocument.Parse("{\"a\":1}", s_settings);

        Assert.Equal("{\"a\":1}", JsonDocument.EncodeToString(doc.RootElement, s_writeOptions));
    }

    [Fact]
    public void EncodeNDJson_RoundTrip()
    {
        const string input = "{\"a\":1,\"b\":[1,2]}\n\"text\"\n[{\"x\":null}]";

        using var doc = JsonDocument.Parse(input, s_settings, s_options);
        var ndjson = JsonDocument.EncodeToString(doc.RootElement, s_writeOptions);

        using var reparsed = JsonDocument.Parse(ndjson, s_settings, s_options);

        Assert.Equal(doc.RootElement.ToJsonString(), reparsed.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("{\"a\":1}\n{\"a\":2}", "[{\"a\":1},{\"a\":2}]")]
    [InlineData("{\"a\":1}\n{\"a\":2}\n", "[{\"a\":1},{\"a\":2}]")]
    [InlineData("{\"a\":1}\r\n{\"a\":2}\r\n", "[{\"a\":1},{\"a\":2}]")]
    [InlineData("{\"a\":1}", "[{\"a\":1}]")]
    [InlineData("\n\n{\"a\":1}\n\n  \n{\"b\":[1,2]}\n\n", "[{\"a\":1},{\"b\":[1,2]}]")]
    [InlineData("1\n\"text\"\ntrue\nnull\n[1,2]", "[1,\"text\",true,null,[1,2]]")]
    [InlineData("  {\"a\":1}  \n\t{\"a\":2}\t", "[{\"a\":1},{\"a\":2}]")]
    [InlineData("", "[]")]
    [InlineData("\n \n", "[]")]
    public void ParseNDJson(string input, string expected)
    {
        using var doc = JsonDocument.Parse(input, s_settings, s_options);

        Assert.Equal(expected, doc.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("{\"a\":1}\n{\"a\":2}", "[{\"a\":1},{\"a\":2}]")]
    [InlineData("{\"nested\":{\"x\":\"y\"}}\n[1,[2,3]]\n", "[{\"nested\":{\"x\":\"y\"}},[1,[2,3]]]")]
    public void TryParseNDJson(string input, string expected)
    {
        Assert.True(JsonDocument.TryParse(Encoding.UTF8.GetBytes(input), out var doc, s_settings, s_options));

        using (doc)
        {
            Assert.NotNull(doc);
            Assert.Equal(expected, doc!.RootElement.ToJsonString());
        }
    }

    [Fact]
    public void ParseNDJson_PreservesStringContents()
    {
        using var doc = JsonDocument.Parse(
            "{\"a\":\"first\"}\n{\"a\":\"second\"}\n{\"a\":\"third\"}",
            s_settings,
            s_options);

        var root = doc.RootElement;

        Assert.Equal(3, root.GetArrayLength());

        var values = new List<string?>();

        foreach (var item in root.EnumerateArray())
        {
            values.Add(item.GetProperty("a").GetString());
        }

        Assert.Equal(new[] { "first", "second", "third" }, values);
    }

    [Theory]
    [InlineData("{\"a\":1}\n{\"a\":}")]
    [InlineData("{\"a\":1}\n{\"a\":1")]
    [InlineData("{\"a\":1} {\"a\":2}")]
    [InlineData("{\"a\":1,\n\"b\":2}")]
    public void ParseNDJson_InvalidInput_ShouldThrow(string input)
    {
        Assert.ThrowsAny<Exception>(() =>
        {
            using var doc = JsonDocument.Parse(input, s_settings, s_options);
        });
    }

    [Theory]
    [InlineData("{\"a\":1}\n{\"a\":}")]
    [InlineData("{\"a\":1} {\"a\":2}")]
    public void TryParseNDJson_InvalidInput_ShouldFail(string input)
    {
        var success = JsonDocument.TryParse(Encoding.UTF8.GetBytes(input), out var doc, s_settings, s_options);

        using (doc)
        {
            Assert.False(success);
        }
    }
}