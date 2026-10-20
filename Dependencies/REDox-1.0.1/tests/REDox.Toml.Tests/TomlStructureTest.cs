using System.Linq;

namespace REDox.Toml.Tests;

public sealed class TomlStructureTest
{
    private static DElement Parse(string toml)
    {
        return TomlDocument.Parse(toml).RootElement;
    }

    [Fact]
    public void BareKeys_ShouldParse()
    {
        var root = Parse("key = \"value\"\nbare_key = 1\nbare-key = 2\n1234 = 3");

        Assert.Equal("value", root.GetProperty("key").GetString());
        Assert.Equal(1, root.GetProperty("bare_key").GetInt64());
        Assert.Equal(2, root.GetProperty("bare-key").GetInt64());
        Assert.Equal(3, root.GetProperty("1234").GetInt64());
    }

    [Fact]
    public void QuotedKeys_ShouldParse()
    {
        var root = Parse("\"127.0.0.1\" = 1\n\"character encoding\" = 2\n'quoted \"value\"' = 3");

        Assert.Equal(1, root.GetProperty("127.0.0.1").GetInt64());
        Assert.Equal(2, root.GetProperty("character encoding").GetInt64());
        Assert.Equal(3, root.GetProperty("quoted \"value\"").GetInt64());
    }

    [Fact]
    public void DottedKeys_ShouldBuildNestedTables()
    {
        var root = Parse("physical.color = \"orange\"\nphysical.shape = \"round\"");

        var physical = root.GetProperty("physical");
        Assert.Equal("orange", physical.GetProperty("color").GetString());
        Assert.Equal("round", physical.GetProperty("shape").GetString());
    }

    [Fact]
    public void Table_ShouldParse()
    {
        const string toml = """
                            [table]
                            key1 = "some string"
                            key2 = 123
                            """;

        var table = Parse(toml).GetProperty("table");
        Assert.Equal("some string", table.GetProperty("key1").GetString());
        Assert.Equal(123, table.GetProperty("key2").GetInt64());
    }

    [Fact]
    public void NestedTables_ShouldParse()
    {
        const string toml = """
                            [a.b.c]
                            answer = 42
                            """;

        var c = Parse(toml).GetProperty("a").GetProperty("b").GetProperty("c");
        Assert.Equal(42, c.GetProperty("answer").GetInt64());
    }

    [Fact]
    public void InlineTable_ShouldParse()
    {
        const string toml = "point = { x = 1, y = 2 }";

        var point = Parse(toml).GetProperty("point");
        Assert.Equal(1, point.GetProperty("x").GetInt64());
        Assert.Equal(2, point.GetProperty("y").GetInt64());
    }

    [Fact]
    public void MultilineInlineTable_ShouldParse()
    {
        const string toml = """
                            a = {
                              b = 1
                            }
                            """;

        Assert.Equal(1, Parse(toml).GetProperty("a").GetProperty("b").GetInt64());
    }

    [Fact]
    public void MultilineInlineTableInArray_ShouldParse()
    {
        const string toml = """
                            a = [{
                              b = 1
                            }]
                            c = [
                              { d = 2 },
                              {
                                e = 3
                              },
                            ]
                            """;

        var root = Parse(toml);

        Assert.Equal(1, root.GetProperty("a").EnumerateArray().ToArray()[0].GetProperty("b").GetInt64());

        var c = root.GetProperty("c").EnumerateArray().ToArray();
        Assert.Equal(2, c.Length);
        Assert.Equal(2, c[0].GetProperty("d").GetInt64());
        Assert.Equal(3, c[1].GetProperty("e").GetInt64());
    }

    [Fact]
    public void Array_ShouldParse()
    {
        var array = Parse("v = [1, 2, 3]").GetProperty("v");

        var values = array.EnumerateArray().Select(e => e.GetInt64()).ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void MixedTypeArray_ShouldParse()
    {
        var array = Parse("v = [\"a\", 1, true]").GetProperty("v");

        var items = array.EnumerateArray().ToArray();
        Assert.Equal(3, items.Length);
        Assert.Equal("a", items[0].GetString());
        Assert.Equal(1, items[1].GetInt64());
        Assert.True(items[2].GetBoolean());
    }

    [Fact]
    public void MultilineArray_WithTrailingComma_ShouldParse()
    {
        const string toml = """
                            v = [
                              1,
                              2,
                              3,
                            ]
                            """;

        var values = Parse(toml).GetProperty("v").EnumerateArray().Select(e => e.GetInt64()).ToArray();
        Assert.Equal(new long[] { 1, 2, 3 }, values);
    }

    [Fact]
    public void NestedArray_ShouldParse()
    {
        var array = Parse("v = [[1, 2], [3, 4]]").GetProperty("v");

        var outer = array.EnumerateArray().ToArray();
        Assert.Equal(2, outer.Length);
        Assert.Equal(new long[] { 1, 2 }, outer[0].EnumerateArray().Select(e => e.GetInt64()).ToArray());
        Assert.Equal(new long[] { 3, 4 }, outer[1].EnumerateArray().Select(e => e.GetInt64()).ToArray());
    }

    [Fact]
    public void ArrayOfTables_ShouldParse()
    {
        const string toml = """
                            [[products]]
                            name = "Hammer"
                            sku = 738594937

                            [[products]]
                            name = "Nail"
                            sku = 284758393
                            """;

        var products = Parse(toml).GetProperty("products").EnumerateArray().ToArray();
        Assert.Equal(2, products.Length);
        Assert.Equal("Hammer", products[0].GetProperty("name").GetString());
        Assert.Equal(738594937, products[0].GetProperty("sku").GetInt64());
        Assert.Equal("Nail", products[1].GetProperty("name").GetString());
        Assert.Equal(284758393, products[1].GetProperty("sku").GetInt64());
    }

    [Fact]
    public void Comments_ShouldBeIgnored()
    {
        const string toml = """
                            # This is a full-line comment
                            key = "value"  # inline comment
                            """;

        Assert.Equal("value", Parse(toml).GetProperty("key").GetString());
    }
}