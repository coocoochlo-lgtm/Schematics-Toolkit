using System.Text.Json;
using System.Text.Json.Nodes;

namespace REDox.Serialization.SystemTextJson.Tests;

public class JsonTypeTest
{
    private static readonly SerializerSettings s_settings = new SystemTextJsonSerializerSettings();

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("123")]
    [InlineData("1234567898765432123456789")]
    [InlineData("12345678987654321234567891234567898765432123456789")]
    [InlineData("123456789876543212345678.91234567898765432123456789")]
    [InlineData("1e3")]
    [InlineData("123.45")]
    [InlineData("1.0000")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData(@"{""a"":123,""b"":[5,false,true]}")]
    [InlineData(@"""text""")]
    public void JsonElementTest(string json)
    {
        var a = JsonSerializer.Deserialize<JsonElement>(json);
        var b = Json.JsonSerializer.Deserialize<JsonElement>(json, s_settings);

        Assert.Equal(b.ToString(), a.ToString());

        var json1 = JsonSerializer.Serialize(a);

        Assert.Equal(json1, json);

        var json2 = Json.JsonSerializer.Serialize(a, s_settings);

        Assert.Equal(json2, json1);
    }

    [Theory]
    [InlineData("[1,2,3]")]
    [InlineData("123")]
    [InlineData("1234567898765432123456789")]
    [InlineData("12345678987654321234567891234567898765432123456789")]
    [InlineData("123456789876543212345678.91234567898765432123456789")]
    [InlineData("1e3")]
    [InlineData("123.45")]
    [InlineData("1.0000")]
    [InlineData("true")]
    [InlineData("false")]
    [InlineData("null")]
    [InlineData(@"{""a"":123,""b"":[5,false,true]}")]
    [InlineData(@"""text""")]
    public void JsonNodeTest(string json)
    {
        var a = JsonSerializer.Deserialize<JsonNode?>(json);
        var b = Json.JsonSerializer.Deserialize<JsonNode?>(json, s_settings);

        Assert.Equal(b?.ToString(), a?.ToString());

        var json1 = JsonSerializer.Serialize(a);

        Assert.Equal(json1, json);

        var json2 = Json.JsonSerializer.Serialize(a, s_settings);

        Assert.Equal(json2, json1);

        if (a is JsonArray arr)
        {
            var c = Json.JsonSerializer.Deserialize<JsonArray?>(json, s_settings);

            Assert.Equal(c?.ToString(), a?.ToString());

            var json3 = Json.JsonSerializer.Serialize<JsonArray>(arr, s_settings);

            Assert.Equal(json3, json1);
        }

        if (a is JsonObject obj)
        {
            var c = Json.JsonSerializer.Deserialize<JsonObject?>(json, s_settings);

            Assert.Equal(c?.ToString(), a?.ToString());

            var json3 = Json.JsonSerializer.Serialize(obj, s_settings);

            Assert.Equal(json3, json1);
        }

        if (a is JsonValue val)
        {
            var c = Json.JsonSerializer.Deserialize<JsonValue?>(json, s_settings);

            Assert.Equal(c?.ToString(), a?.ToString());

            var json3 = Json.JsonSerializer.Serialize(val, s_settings);

            Assert.Equal(json3, json1);
        }
    }
}