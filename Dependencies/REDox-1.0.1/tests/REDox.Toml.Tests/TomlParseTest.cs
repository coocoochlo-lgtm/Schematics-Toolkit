using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace REDox.Toml.Tests;

public class TomlParseTest
{
    private static readonly string ValidParsingDir =
        FindTestSuiteDirectory("valid");

    private static readonly string InvalidParsingDir =
        FindTestSuiteDirectory("invalid");

    private readonly ITestOutputHelper _output;

    public TomlParseTest(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string FindTestSuiteDirectory(string subDirectory)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var path = Path.Combine(
                current.FullName,
                "external",
                "toml-test",
                "tests",
                subDirectory);

            if (Directory.Exists(path))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"JSONTestSuite directory not found: {subDirectory}");
    }

    [Theory]
    [MemberData(nameof(GetValidParseFiles), "*.toml")]
    public void TomlValidParse(string filePath)
    {
        _output.WriteLine(filePath);

        using var file = File.OpenRead(filePath);

        using var doc = TomlDocument.Parse(file, options: new TomlDocumentOptions
        {
            EnableValueValidation = true
        });
    }

    [Theory]
    [MemberData(nameof(GetInvalidParseFiles), "*.toml")]
    public void TomlInvalidParse(string filePath)
    {
        _output.WriteLine(filePath);

        using var file = File.OpenRead(filePath);

        //TODO: Implement parse error handling
        /*
        Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = REDox.Toml.TomlDocument.Parse(file,options : new TomlDocumentOptions()
            {
                EnableValueValidation = true
            });
        });
        */
    }

    [Fact]
    public void ParseOverloadsPreserveSource()
    {
        var bytes = Encoding.UTF8.GetBytes("title = \"TOML\"\n");

        using var spanDoc = TomlDocument.Parse(bytes.AsSpan());
        Assert.True(spanDoc.Source.Span.SequenceEqual(bytes));

        using var stream = new MemoryStream(bytes);
        using var streamDoc = TomlDocument.Parse(stream);
        Assert.True(streamDoc.Source.Span.SequenceEqual(bytes));
    }

    public static IEnumerable<object[]> GetValidParseFiles(string filter)
    {
        return Directory.EnumerateFiles(ValidParsingDir, filter, SearchOption.AllDirectories)
            .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> GetInvalidParseFiles(string filter)
    {
        return Directory.EnumerateFiles(InvalidParsingDir, filter, SearchOption.AllDirectories)
            .Select(path => new object[] { path });
    }
}