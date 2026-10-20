using System.Text;
using REDox.Json;

namespace REDox.Tests;

public sealed class Json5DocumentRegressionTests
{
    // 1. EOF must not turn an unfinished container into its last child value.
    // The trailing space is intentional: it reaches the whitespace EOF path.
    [Theory]
    [InlineData("[1 ")]
    [InlineData("{a:1 ")]
    public void TryParse_UnclosedContainer_ShouldReturnFalse(string json5)
    {
        AssertInvalid(json5);
    }

    // 1. A completed root must not be replaced by a subsequent root value.
    [Theory]
    [InlineData("1 2")]
    [InlineData("{} {}")]
    public void TryParse_MultipleRootValues_ShouldReturnFalse(string json5)
    {
        AssertInvalid(json5);
    }

    // 2. Malformed comments should be reported as parse failures, not exceptions.
    [Theory]
    [InlineData("1/*")]
    [InlineData("1/**")]
    public void TryParse_UnterminatedBlockComment_ShouldReturnFalseWithoutThrowing(string json5)
    {
        AssertInvalid(json5);
    }

    // 2. An empty line comment is valid at EOF, with or without a preceding space.
    [Theory]
    [InlineData("1//")]
    [InlineData("1 //")]
    public void TryParse_EmptyTrailingLineComment_ShouldSucceed(string json5)
    {
        var success = Json5Document.TryParse(Encoding.UTF8.GetBytes(json5), out var doc);
        using (doc)
        {
            Assert.True(success);
            Assert.NotNull(doc);
            Assert.Equal("1", Json5Document.EncodeToString(doc!.RootElement,
                new Json5WriteOptions { PreserveTrivia = false, WriteIndented = false }));
        }
    }

    // 2. Consuming the final multibyte whitespace must not read beyond EOF.
    [Theory]
    [InlineData("1\u00A0")]
    [InlineData("1\u3000")]
    public void TryParse_TrailingUnicodeWhitespace_ShouldSucceed(string json5)
    {
        var success = Json5Document.TryParse(Encoding.UTF8.GetBytes(json5), out var doc);
        using (doc)
        {
            Assert.True(success);
            Assert.NotNull(doc);
            Assert.Equal("1", Json5Document.EncodeToString(doc!.RootElement,
                new Json5WriteOptions { PreserveTrivia = false, WriteIndented = false }));
        }
    }

#if true
    // 3. Changing the delimiter requires re-escaping the decoded string value.
    [Theory]
    [InlineData("{s:'a\"b'}", Json5QuoteStyle.AlwaysDouble, "a\"b")]
    [InlineData("{s:\"a'b\"}", Json5QuoteStyle.AlwaysSingle, "a'b")]
    public void Encode_ChangingStringQuoteStyle_ShouldPreserveValue(
        string input, Json5QuoteStyle style, string expected)
    {
        using var doc = Json5Document.Parse(input);
        var encoded = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions
            {
                PreserveTrivia = false,
                WriteIndented = false,
                StringStyle = style
            });

        using var reparsed = Json5Document.Parse(encoded,
            options: new Json5DocumentOptions { EnableValueValidation = true });
        Assert.Equal(expected, reparsed.RootElement.GetProperty("s").GetString());
    }
#endif

#if true
    // 3. Property names have the same raw-data fast-path problem.
    [Theory]
    [InlineData("{'a\"b':'value'}", Json5QuoteStyle.AlwaysDouble, "a\"b")]
    [InlineData("{\"a'b\":'value'}", Json5QuoteStyle.AlwaysSingle, "a'b")]
    public void Encode_ChangingPropertyQuoteStyle_ShouldPreserveKey(
        string input, Json5QuoteStyle style, string expectedKey)
    {
        using var doc = Json5Document.Parse(input);
        var encoded = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions
            {
                PreserveTrivia = false,
                WriteIndented = false,
                PropertyNameStyle = style,
                StringStyle = Json5QuoteStyle.PreserveOrDouble
            });

        using var reparsed = Json5Document.Parse(encoded,
            options: new Json5DocumentOptions { EnableValueValidation = true });
        Assert.Equal("value", reparsed.RootElement.GetProperty(expectedKey).GetString());
    }
#endif

    // 4. No original separator trivia is available: closing tokens must be synthesized.
    // Compact output and an explicit key style make these exact assertions unambiguous.
    [Theory]
    [InlineData("[{a:1}]", "[{\"a\":1}]")]
    [InlineData("{a:[{b:1}]}", "{\"a\":[{\"b\":1}]}")]
    public void Encode_PreserveTriviaWithoutSourceTrivia_ShouldCloseContainersInOrder(
        string input, string expected)
    {
        using var doc = Json5Document.Parse(input,
            options: new Json5DocumentOptions { PreserveTrivia = false });
        var encoded = Json5Document.EncodeToString(doc.RootElement,
            new Json5WriteOptions
            {
                PreserveTrivia = true,
                WriteIndented = false,
                PropertyNameStyle = Json5QuoteStyle.AlwaysDouble
            });

        Assert.Equal(expected, encoded);
        using var reparsed = Json5Document.Parse(encoded,
            options: new Json5DocumentOptions { EnableValueValidation = true });
    }

    // 5. The C# escape below leaves a literal backslash-u sequence in the JSON5 input.
    [Fact]
    public void Parse_UnquotedKeyStartingWithUnicodeEscape_ShouldDecodeKey()
    {
        using var doc = Json5Document.Parse("{\\u0061:'value'}",
            options: new Json5DocumentOptions { EnableValueValidation = true });

        Assert.Equal("value", doc.RootElement.GetProperty("a").GetString());
    }

    private static void AssertInvalid(string json5)
    {
        // An unexpected exception also fails this test. Dispose even if parsing
        // incorrectly succeeds, so regression failures do not leak the document.
        var success = Json5Document.TryParse(Encoding.UTF8.GetBytes(json5), out var doc);
        using (doc)
        {
            Assert.False(success);
            Assert.Null(doc);
        }
    }
}