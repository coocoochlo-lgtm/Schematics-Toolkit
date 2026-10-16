using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using REDox.Json;

namespace REDox.Serialization.SystemTextJson.Tests;

public class PolymorphicTest
{
    [Fact]
    public void ShouldSerialize()
    {
        var weatherForecastBase = new WeatherForecastBase
        {
            Date = DateTimeOffset.Now,
            TemperatureCelsius = 15,
            Summary = "Cool"
        };

        var weatherForecastCity = new WeatherForecastWithCity
        {
            Date = DateTimeOffset.Now,
            City = "Milwaukee",
            TemperatureCelsius = 15,
            Summary = "Cool"
        };

        var weatherForecastName = new WeatherForecastWithName
        {
            Date = DateTimeOffset.Now,
            Name = "AnyName",
            TemperatureCelsius = 15,
            Summary = "Cool"
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        var doxSettings = new SystemTextJsonSerializerSettings(options)
        {
            IncludeDerivedProperties = true
        };
        var doxOptions = new JsonWriteOptions { WriteIndented = options.WriteIndented };

        var s1 = System.Text.Json.JsonSerializer.Serialize<WeatherForecastBase>(weatherForecastCity, options);
        var s2 = System.Text.Json.JsonSerializer.Serialize<WeatherForecastBase>(weatherForecastName, options);
        var s3 = System.Text.Json.JsonSerializer.Serialize<WeatherForecastBase>(weatherForecastBase, options);

        var d1 = System.Text.Json.JsonSerializer.Deserialize<WeatherForecastBase>(s1, options);
        var d2 = System.Text.Json.JsonSerializer.Deserialize<WeatherForecastBase>(s2, options);
        var d3 = System.Text.Json.JsonSerializer.Deserialize<WeatherForecastBase>(s3, options);

        Assert.Equal(weatherForecastCity, d1);
        Assert.Equal(weatherForecastName, d2);
        Assert.Equal(weatherForecastBase, d3);

        var ss1 = Json.JsonSerializer.Serialize<WeatherForecastBase>(weatherForecastCity, doxSettings, doxOptions);
        var ss2 = Json.JsonSerializer.Serialize<WeatherForecastBase>(weatherForecastName, doxSettings, doxOptions);
        var ss3 = Json.JsonSerializer.Serialize(weatherForecastBase, doxSettings, doxOptions);

        Assert.Equal(s1, ss1);
        Assert.Equal(s2, ss2);
        Assert.Equal(s3, ss3);

        var dd1 = Json.JsonSerializer.Deserialize<WeatherForecastBase>(ss1, doxSettings);
        var dd2 = Json.JsonSerializer.Deserialize<WeatherForecastBase>(ss2, doxSettings);
        var dd3 = Json.JsonSerializer.Deserialize<WeatherForecastBase>(ss3, doxSettings);

        Assert.Equal(weatherForecastCity, dd1);
        Assert.Equal(weatherForecastName, dd2);
        Assert.Equal(weatherForecastBase, dd3);
    }

    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$tag")]
    [JsonDerivedType(typeof(WeatherForecastBase), "base")]
    [JsonDerivedType(typeof(WeatherForecastWithCity), "city")]
    [JsonDerivedType(typeof(WeatherForecastWithName), "name")]
    public record WeatherForecastBase
    {
        public DateTimeOffset Date { get; set; }
        public int TemperatureCelsius { get; set; }
        public string? Summary { get; set; }
    }

    public record WeatherForecastWithCity : WeatherForecastBase
    {
        public string? City { get; set; }
    }

    public record WeatherForecastWithName : WeatherForecastBase
    {
        public string? Name { get; set; }
    }
}