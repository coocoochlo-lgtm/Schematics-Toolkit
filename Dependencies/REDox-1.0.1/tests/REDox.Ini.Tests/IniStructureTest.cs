namespace REDox.Ini.Tests;

public sealed class IniStructureTest
{
    private static DElement Parse(string ini, IniDocumentOptions options = default)
    {
        return IniDocument.Parse(ini, options: options).RootElement;
    }

    [Fact]
    public void GlobalKeyValues_ShouldParse()
    {
        var root = Parse("key=value\nname = REDox\n");

        Assert.Equal("value", root.GetProperty("key").GetString());
        Assert.Equal("REDox", root.GetProperty("name").GetString());
    }

    [Fact]
    public void LastLineWithoutNewLine_ShouldParse()
    {
        var root = Parse("key=value");

        Assert.Equal("value", root.GetProperty("key").GetString());
    }

    [Fact]
    public void Section_ShouldParse()
    {
        const string ini = """
                           [owner]
                           name = John Doe
                           organization = Acme

                           [database]
                           server = 192.0.2.62
                           port = 143
                           """;

        var root = Parse(ini);

        var owner = root.GetProperty("owner");
        Assert.Equal("John Doe", owner.GetProperty("name").GetString());
        Assert.Equal("Acme", owner.GetProperty("organization").GetString());

        var database = root.GetProperty("database");
        Assert.Equal("192.0.2.62", database.GetProperty("server").GetString());
        Assert.Equal("143", database.GetProperty("port").GetString());
    }

    [Fact]
    public void GlobalAndSection_ShouldCoexist()
    {
        const string ini = """
                           global = 1
                           [section]
                           local = 2
                           """;

        var root = Parse(ini);

        Assert.Equal("1", root.GetProperty("global").GetString());
        Assert.Equal("2", root.GetProperty("section").GetProperty("local").GetString());
    }

    [Fact]
    public void Comments_ShouldBeIgnored()
    {
        const string ini = """
                           ; comment line
                           # another comment
                           [section]
                           ; section comment
                           key = value
                           """;

        var root = Parse(ini);

        Assert.Equal("value", root.GetProperty("section").GetProperty("key").GetString());
    }

    [Fact]
    public void CrLfLineEndings_ShouldParse()
    {
        var root = Parse("[section]\r\nkey = value\r\nother = 2\r\n");

        var section = root.GetProperty("section");
        Assert.Equal("value", section.GetProperty("key").GetString());
        Assert.Equal("2", section.GetProperty("other").GetString());
    }

    [Fact]
    public void ValueWithSpaces_ShouldBeTrimmed()
    {
        var root = Parse("  key   =   a value with spaces   \n");

        Assert.Equal("a value with spaces", root.GetProperty("key").GetString());
    }

    [Fact]
    public void EmptyValue_ShouldBeEmptyString()
    {
        var root = Parse("key =\n");

        Assert.Equal(string.Empty, root.GetProperty("key").GetString());
    }

    [Fact]
    public void TryParse_ShouldReturnDocument()
    {
        Assert.True(IniDocument.TryParse("key = value"u8, out var document));
        Assert.NotNull(document);

        using (document)
        {
            Assert.Equal("value", document!.RootElement.GetProperty("key").GetString());
        }
    }

    [Fact]
    public void Duplicate_ShouldCreateIndependentSnapshot()
    {
        using var document = IniDocument.Parse("[section]\nkey = value\n");
        using var duplicated = document.Duplicate();

        Assert.Equal("value", duplicated.RootElement.GetProperty("section").GetProperty("key").GetString());
    }

    [Fact]
    public void Encode_ShouldRoundTrip()
    {
        const string ini = """
                           global = 1
                           [section]
                           key = value
                           """;

        using var document = IniDocument.Parse(ini);

        var encoded = IniDocument.EncodeToString(document.RootElement);

        using var reparsed = IniDocument.Parse(encoded);

        var root = reparsed.RootElement;
        Assert.Equal("1", root.GetProperty("global").GetString());
        Assert.Equal("value", root.GetProperty("section").GetProperty("key").GetString());
    }

    [Fact]
    public void PreserveTrivia_ShouldKeepComments()
    {
        const string ini = """
                           ; header comment
                           [section]
                           key = value
                           """;

        using var document = IniDocument.Parse(ini, options: new IniDocumentOptions { PreserveTrivia = true });

        var encoded = IniDocument.EncodeToString(document.RootElement, new IniWriteOptions { PreserveTrivia = true });

        Assert.Contains("; header comment", encoded);
        Assert.Contains("[section]", encoded);
        Assert.Contains("key=value", encoded.Replace(" ", string.Empty));
    }
}