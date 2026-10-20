using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson.Tests;

public class CustomConverterTest
{
    [Fact]
    public void ConverterFromOptionsIsUsedForWrite()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new Shape { Origin = new Point { X = 1, Y = 2 } };

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
        Assert.Contains("\"1,2\"", actual);
    }

    [Fact]
    public void ConverterFromOptionsIsUsedForRead()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        const string json = "{\"Origin\":\"3,4\",\"Name\":\"box\"}";

        var result = Json.JsonSerializer.Deserialize<Shape>(json, settings);

        Assert.NotNull(result);
        Assert.Equal(3, result!.Origin.X);
        Assert.Equal(4, result.Origin.Y);
    }

    [Fact]
    public void ConverterFactoryFromOptionsIsUsed()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverterFactory());

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new Shape { Origin = new Point { X = 5, Y = 6 } };

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConverterFromOptionsAppliesToCollectionElements()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new List<Point>
        {
            new() { X = 1, Y = 2 },
            new() { X = 3, Y = 4 }
        };

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ConverterFromOptionsAppliesToDictionaryKeyWrite()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new Dictionary<Point, int>
        {
            [new Point { X = 1, Y = 2 }] = 3
        };

        var expected = JsonSerializer.Serialize(value, options);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
        Assert.Contains("\"P:1,2\"", actual);
    }

    [Fact]
    public void ConverterFromOptionsAppliesToDictionaryKeyRead()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new PointConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        var result = Json.JsonSerializer.Deserialize<Dictionary<Point, int>>("{\"P:3,4\":5}", settings);

        Assert.NotNull(result);
        Assert.Single(result!);

        foreach (var pair in result)
        {
            Assert.Equal(3, pair.Key.X);
            Assert.Equal(4, pair.Key.Y);
            Assert.Equal(5, pair.Value);
        }
    }

    [Fact]
    public void PropertyConverterAttributeIsUsed()
    {
        var settings = new SystemTextJsonSerializerSettings();

        var value = new AnnotatedHolder { Origin = new Point { X = 7, Y = 8 } };

        var expected = JsonSerializer.Serialize(value);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
        Assert.Contains("\"7,8\"", actual);
    }

    [Fact]
    public void TypeConverterAttributeIsUsed()
    {
        var settings = new SystemTextJsonSerializerSettings();

        var value = new AnnotatedTypeHolder { Origin = new AnnotatedPoint { X = 9, Y = 10 } };

        var expected = JsonSerializer.Serialize(value);
        var actual = Json.JsonSerializer.Serialize(value, settings);

        Assert.Equal(expected, actual);
        Assert.Contains("\"9/10\"", actual);
    }

    [Fact]
    public void TypeConverterAttributeRoundTrips()
    {
        var settings = new SystemTextJsonSerializerSettings();

        var result = Json.JsonSerializer.Deserialize<AnnotatedTypeHolder>("{\"Origin\":\"11/12\"}", settings);

        Assert.NotNull(result);
        Assert.Equal(11, result!.Origin.X);
        Assert.Equal(12, result.Origin.Y);
    }

    [Fact]
    public void StringEnumConverterStillWorks()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());

        var settings = new SystemTextJsonSerializerSettings(options);

        var value = new EnumHolder { Value = DayOfWeek.Friday };

        Assert.Equal(JsonSerializer.Serialize(value, options), Json.JsonSerializer.Serialize(value, settings));
    }

    public struct Point
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    private sealed class PointConverter : JsonConverter<Point>
    {
        public override Point Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? throw new JsonException();

            var parts = text.Split(',');

            return new Point
            {
                X = int.Parse(parts[0]),
                Y = int.Parse(parts[1])
            };
        }

        public override void Write(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"{value.X},{value.Y}");
        }

        public override Point ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? throw new JsonException();

            if (!text.StartsWith("P:", StringComparison.Ordinal))
            {
                throw new JsonException();
            }

            var parts = text.Substring(2).Split(',');

            return new Point
            {
                X = int.Parse(parts[0]),
                Y = int.Parse(parts[1])
            };
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, Point value, JsonSerializerOptions options)
        {
            writer.WritePropertyName($"P:{value.X},{value.Y}");
        }
    }

    private sealed class PointConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert)
        {
            return typeToConvert == typeof(Point);
        }

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
        {
            return new PointConverter();
        }
    }

    private sealed class Shape
    {
        public Point Origin { get; set; }

        public string Name { get; set; } = "box";
    }

    [JsonConverter(typeof(AnnotatedPointConverter))]
    public struct AnnotatedPoint
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    private sealed class AnnotatedPointConverter : JsonConverter<AnnotatedPoint>
    {
        public override AnnotatedPoint Read(ref Utf8JsonReader reader, Type typeToConvert,
            JsonSerializerOptions options)
        {
            var text = reader.GetString() ?? throw new JsonException();

            var parts = text.Split('/');

            return new AnnotatedPoint
            {
                X = int.Parse(parts[0]),
                Y = int.Parse(parts[1])
            };
        }

        public override void Write(Utf8JsonWriter writer, AnnotatedPoint value, JsonSerializerOptions options)
        {
            writer.WriteStringValue($"{value.X}/{value.Y}");
        }
    }

    private sealed class AnnotatedTypeHolder
    {
        public AnnotatedPoint Origin { get; set; }
    }

    private sealed class AnnotatedHolder
    {
        [JsonConverter(typeof(PointConverter))]
        public Point Origin { get; set; }
    }

    private sealed class EnumHolder
    {
        public DayOfWeek Value { get; set; }
    }
}