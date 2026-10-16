using System;
using System.Globalization;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class ReviewFindingsTest
{
    private static T WithCulture<T>(string name, Func<T> action)
    {
        var current = CultureInfo.CurrentCulture;
        var currentUi = CultureInfo.CurrentUICulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
            CultureInfo.CurrentUICulture = currentUi;
        }
    }

    [Fact]
    public void TriviaAddFirstInsertsAtHead()
    {
        using var doc = JsonDocument.Parse("[1]");

        var trivia = new DTriviaCollection(doc.RootElement);

        trivia.AddLast("/*last*/", TriviaKind.BlockComment);
        trivia.AddFirst("/*first*/", TriviaKind.BlockComment);

        var items = trivia.GetEnumerator().Select(t => t.GetString()).ToArray();

        Assert.Equal(new[] { "/*first*/", "/*last*/" }, items);
    }

    [Fact]
    public void SerializeStringEndingWithLoneHighSurrogateDoesNotThrow()
    {
        var exception = Record.Exception(() => JsonSerializer.Serialize("A\uD800"));

        Assert.Null(exception);
    }

    [Fact]
    public void SerializeLoneLowSurrogateDoesNotConsumeNextChar()
    {
        var json = JsonSerializer.Serialize("\uDC00A");

        Assert.EndsWith("A\"", json);
    }

    [Fact]
    public void SerializeInt128IsCultureInvariant()
    {
        var json = WithCulture("sv-SE", () => JsonSerializer.Serialize(Int128.NegativeOne));

        Assert.Equal("-1", json);
    }

    [Fact]
    public void SerializeUInt128IsCultureInvariant()
    {
        var expected = JsonSerializer.Serialize(UInt128.MaxValue);
        var json = WithCulture("ar-SA", () => JsonSerializer.Serialize(UInt128.MaxValue));

        Assert.Equal(expected, json);
    }

    [Fact]
    public void SerializeTimeOnlyIsCultureInvariant()
    {
        var value = new TimeOnly(13, 2, 3);

        var invariant = WithCulture("", () => JsonSerializer.Serialize(value));
        var enUs = WithCulture("en-US", () => JsonSerializer.Serialize(value));

        Assert.Equal(invariant, enUs);
    }

    [Fact]
    public void TimeOnlyRoundTripsAcrossCultures()
    {
        var value = new TimeOnly(13, 2, 3);

        var json = WithCulture("en-US", () => JsonSerializer.Serialize(value));
        var result = WithCulture("de-DE", () => JsonSerializer.Deserialize<TimeOnly>(json));

        Assert.Equal(value, result);
    }

    [Fact]
    public void DeserializeDateOnlyIsCultureInvariant()
    {
        var result = WithCulture("ar-SA", () => JsonSerializer.Deserialize<DateOnly>("\"2024-01-02\""));

        Assert.Equal(new DateOnly(2024, 1, 2), result);
    }
}