using System;
using System.Linq;
using System.Text;
using REDox.Json;

namespace REDox.Toml.Tests;

public sealed class TomlRoundTripTest
{
    private const string SampleToml = """
                                      title = "Example Tournament"

                                      [champion]
                                      name = "Foo"
                                      active = true

                                      [arcade]
                                      cabinet = "Arcade Board"
                                      credits = [1987, 1991, 2023]
                                      high_score = 999999
                                      continue = true

                                      [stages.foo]
                                      fighter = "Foo"
                                      country = "Japan"

                                      [stages.bar]
                                      fighter = "Bar"
                                      country = "Thailand"

                                      [roster]
                                      data = [["MoveA", "MoveB"], [236, 623]]
                                      guests = [
                                        "Baz",
                                        "Qux",
                                      ]
                                      """;

    private static DElement RoundTrip(string toml)
    {
        var first = TomlDocument.Parse(toml);
        var encoded = TomlDocument.EncodeToString(first.RootElement);
        return TomlDocument.Parse(encoded).RootElement;
    }

    [Fact]
    public void SampleToml_ShouldMatchParsedResult()
    {
        using var doc = TomlDocument.Parse(SampleToml, options: new TomlDocumentOptions
        {
            EnableValueValidation = true
        });

        var expected = new DObject
        {
            { "title", "Example Tournament" },
            {
                "champion", new DObject
                {
                    { "name", "Foo" },
                    { "active", true }
                }
            },
            {
                "arcade", new DObject
                {
                    { "cabinet", "Arcade Board" },
                    { "credits", new DArray(1987, 1991, 2023) },
                    { "high_score", 999999 },
                    { "continue", true }
                }
            },
            {
                "stages", new DObject
                {
                    {
                        "foo", new DObject
                        {
                            { "fighter", "Foo" },
                            { "country", "Japan" }
                        }
                    },
                    {
                        "bar", new DObject
                        {
                            { "fighter", "Bar" },
                            { "country", "Thailand" }
                        }
                    }
                }
            },
            {
                "roster", new DObject
                {
                    {
                        "data", new DArray(
                            new DArray("MoveA", "MoveB"),
                            new DArray(236, 623))
                    },
                    { "guests", new DArray("Baz", "Qux") }
                }
            }
        };

        Assert.Equal(expected.ToJsonString(), doc.RootElement.ToJsonString());
    }

    [Fact]
    public void TerminalComment_ShouldMatchParsedResult()
    {
        const string toml = "key = \"value\"\n# terminal comment";

        using var doc = TomlDocument.Parse(toml, options: new TomlDocumentOptions
        {
            EnableValueValidation = true
        });

        var expected = new DObject
        {
            { "key", "value" }
        };

        Assert.Equal(expected.ToJsonString(), doc.RootElement.ToJsonString());
    }

    [Theory]
    [InlineData("v = \"hello\"")]
    [InlineData("v = 'literal \\ string'")]
    [InlineData("v = 42")]
    [InlineData("v = -3.14")]
    [InlineData("v = true")]
    [InlineData("v = false")]
    [InlineData("v = 0xFF")]
    [InlineData("v = 1_000_000")]
    public void Scalar_ShouldRoundTrip(string toml)
    {
        var first = TomlDocument.Parse(toml);
        var encoded = TomlDocument.EncodeToString(first.RootElement);

        // Re-encoding the parsed result must be stable (idempotent).
        var second = TomlDocument.Parse(encoded);
        var reEncoded = TomlDocument.EncodeToString(second.RootElement);

        Assert.Equal(encoded, reEncoded);
    }

    [Fact]
    public void Table_ShouldRoundTrip()
    {
        const string toml = """
                            [server]
                            host = "localhost"
                            port = 8080
                            enabled = true
                            """;

        var result = RoundTrip(toml).GetProperty("server");
        Assert.Equal("localhost", result.GetProperty("host").GetString());
        Assert.Equal(8080, result.GetProperty("port").GetInt64());
        Assert.True(result.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public void NestedTable_ShouldRoundTrip()
    {
        const string toml = """
                            [a.b.c]
                            value = 99
                            """;

        var c = RoundTrip(toml).GetProperty("a").GetProperty("b").GetProperty("c");
        Assert.Equal(99, c.GetProperty("value").GetInt64());
    }

    [Fact]
    public void Array_ShouldRoundTrip()
    {
        const string toml = "numbers = [1, 2, 3, 4, 5]";

        var values = RoundTrip(toml).GetProperty("numbers")
            .EnumerateArray().Select(e => e.GetInt64()).ToArray();
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, values);
    }

    [Fact]
    public void ArrayOfTables_ShouldRoundTrip()
    {
        const string toml = """
                            [[item]]
                            name = "first"

                            [[item]]
                            name = "second"
                            """;

        var items = RoundTrip(toml).GetProperty("item").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        Assert.Equal("first", items[0].GetProperty("name").GetString());
        Assert.Equal("second", items[1].GetProperty("name").GetString());
    }

    [Fact]
    public void InlineTable_ShouldRoundTrip()
    {
        const string toml = "point = { x = 10, y = 20 }";

        var point = RoundTrip(toml).GetProperty("point");
        Assert.Equal(10, point.GetProperty("x").GetInt64());
        Assert.Equal(20, point.GetProperty("y").GetInt64());
    }

    [Fact]
    public void DateTime_ShouldRoundTrip()
    {
        const string toml = "created = 1979-05-27T07:32:00Z";

        var value = RoundTrip(toml).GetProperty("created").GetDateTimeOffset();
        Assert.Equal(new DateTimeOffset(1979, 5, 27, 7, 32, 0, TimeSpan.Zero), value);
    }

    [Fact]
    public void Complex_ShouldRoundTrip()
    {
        const string toml = """
                            title = "Example"

                            [owner]
                            name = "Tom"
                            age = 30

                            [database]
                            ports = [8000, 8001, 8002]
                            enabled = true

                            [[servers]]
                            ip = "10.0.0.1"

                            [[servers]]
                            ip = "10.0.0.2"
                            """;

        var root = RoundTrip(toml);

        Assert.Equal("Example", root.GetProperty("title").GetString());
        Assert.Equal("Tom", root.GetProperty("owner").GetProperty("name").GetString());
        Assert.Equal(30, root.GetProperty("owner").GetProperty("age").GetInt64());

        var ports = root.GetProperty("database").GetProperty("ports")
            .EnumerateArray().Select(e => e.GetInt64()).ToArray();
        Assert.Equal(new long[] { 8000, 8001, 8002 }, ports);
        Assert.True(root.GetProperty("database").GetProperty("enabled").GetBoolean());

        var servers = root.GetProperty("servers").EnumerateArray().ToArray();
        Assert.Equal(2, servers.Length);
        Assert.Equal("10.0.0.1", servers[0].GetProperty("ip").GetString());
        Assert.Equal("10.0.0.2", servers[1].GetProperty("ip").GetString());
    }

    [Fact]
    public void TryParse_ValidDocument_ShouldSucceed()
    {
        var bytes = Encoding.UTF8.GetBytes("key = \"value\"\nnumber = 123");

        var success = TomlDocument.TryParse(bytes, out var doc);

        using (doc)
        {
            Assert.True(success);
            Assert.NotNull(doc);
            Assert.Equal("value", doc!.RootElement.GetProperty("key").GetString());
            Assert.Equal(123, doc.RootElement.GetProperty("number").GetInt64());
        }
    }
}