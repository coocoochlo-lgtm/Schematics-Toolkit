using System;
using System.Collections.Generic;
using System.Text.Json;

namespace REDox.Serialization.SystemTextJson.Tests;

public class CustomNamingPolicyTest
{
    [Fact]
    public void CustomPropertyNamingPolicyIsApplied()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new PrefixNamingPolicy()
        };

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new Person();

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
        Assert.Contains("p_firstname", actual);
    }

    [Fact]
    public void CustomPropertyNamingPolicyRoundTrips()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = new UpperNamingPolicy()
        };

        var settings = new SystemTextJsonSerializerSettings(options);

        var json = Json.JsonSerializer.Serialize(new Person { FirstName = "Ada", AgeValue = 36 }, settings);

        Assert.Contains("FIRSTNAME", json);

        var result = Json.JsonSerializer.Deserialize<Person>(json, settings);

        Assert.NotNull(result);
        Assert.Equal("Ada", result!.FirstName);
        Assert.Equal(36, result.AgeValue);
    }

    [Fact]
    public void CustomDictionaryKeyPolicyIsApplied()
    {
        var options = new JsonSerializerOptions
        {
            DictionaryKeyPolicy = new UpperNamingPolicy()
        };

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new KeyHolder();

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("CamelCase")]
    [InlineData("SnakeCaseLower")]
    [InlineData("SnakeCaseUpper")]
    [InlineData("KebabCaseLower")]
    [InlineData("KebabCaseUpper")]
    public void BuiltInNamingPoliciesStillWork(string name)
    {
        var policy = name switch
        {
            "CamelCase" => JsonNamingPolicy.CamelCase,
            "SnakeCaseLower" => JsonNamingPolicy.SnakeCaseLower,
            "SnakeCaseUpper" => JsonNamingPolicy.SnakeCaseUpper,
            "KebabCaseLower" => JsonNamingPolicy.KebabCaseLower,
            "KebabCaseUpper" => JsonNamingPolicy.KebabCaseUpper,
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = policy
        };

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new Person();

        Assert.Equal(JsonSerializer.Serialize(value, options), Json.JsonSerializer.Serialize(value, settings));
    }

    private sealed class PrefixNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return "p_" + name.ToLowerInvariant();
        }
    }

    private sealed class UpperNamingPolicy : JsonNamingPolicy
    {
        public override string ConvertName(string name)
        {
            return name.ToUpperInvariant();
        }
    }

    private sealed class Person
    {
        public string FirstName { get; set; } = "John";

        public int AgeValue { get; set; } = 42;
    }

    private sealed class KeyHolder
    {
        public Dictionary<string, int> Values { get; set; } = new()
        {
            ["FirstKey"] = 1,
            ["SecondKey"] = 2
        };
    }
}