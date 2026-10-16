using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit.Sdk;

namespace REDox.Tests;

public sealed class JsonParseTests
{
    private static readonly SerializerSettings Settings = SerializerSettings.Default;

    private static readonly string ParsingDir =
        FindTestSuiteDirectory("test_parsing");

    private static readonly string TransformDir =
        FindTestSuiteDirectory("test_transform");

    private readonly ITestOutputHelper _output;

    public JsonParseTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [MemberData(nameof(GetTransformFiles))]
    public void TransformCases_ShouldRoundTrip(string filePath)
    {
        ValidateRoundTrip(filePath);
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "y_")]
    public void Y_Cases_ShouldParseAndRoundTrip(string filePath)
    {
        ValidateRoundTrip(filePath);
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "n_")]
    public void N_Cases_ShouldFail(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        var success = Json.JsonDocument.TryParse(
            bytes,
            out var doc,
            Settings,
            new Json.JsonDocumentOptions
            {
                EnableValueValidation = true
            });

        using (doc)
        {
            Assert.False(
                success,
                $"Expected parse failure but succeeded: {Path.GetFileName(filePath)}");
        }

        var source = Assert.ThrowsAny<Exception>(() =>
        {
            using var doc = JsonDocument.Parse(bytes);
        });

        _output.WriteLine(source.Message);

        var target = Assert.ThrowsAny<DocumentParseException>(() =>
        {
            using var doc = Json.JsonDocument.Parse(
                bytes,
                Settings,
                new Json.JsonDocumentOptions
                {
                    EnableValueValidation = true
                });
        });

        _output.WriteLine(target.Message);
    }

    [Theory]
    [MemberData(nameof(GetParseFiles), "i_")]
    public void I_Cases_ShouldNotCrash(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);

        try
        {
            using var doc = Json.JsonDocument.Parse(
                bytes,
                Settings,
                new Json.JsonDocumentOptions
                {
                    MaxDepth = int.MaxValue
                });
        }
        catch (Exception)
        {
            _output.WriteLine($"Unsupported irregular json {Path.GetFileName(filePath)}");
        }
    }

    [Fact]
    public void ParseFailure_ShouldKeepDiagnosticMessageAfterInternalDispose()
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(() =>
            Json.JsonDocument.Parse(
                "{\n\"a\": }",
                Settings,
                new Json.JsonDocumentOptions
                {
                    EnableValueValidation = true
                }));

        Assert.Contains("JsonDocument Error:", exception.Message);
        Assert.Contains("LineNumber:", exception.Message);
        Assert.True(exception.LineNumber > 0);
        Assert.True(exception.BytePositionInLine >= 0);
    }

    [Fact]
    public void ParseFailure_ShouldReportLineAndBytePositionInLine()
    {
        var exception = Assert.ThrowsAny<DocumentParseException>(() =>
            Json.JsonDocument.Parse(
                "{\n\"a\": 1,\n\"b\": }",
                Settings,
                new Json.JsonDocumentOptions
                {
                    EnableValueValidation = true
                }));

        Assert.Equal(3, exception.LineNumber);
        Assert.Equal(5, exception.BytePositionInLine);
        Assert.Contains("LineNumber: 3", exception.Message);
        Assert.Contains("BytePositionInLine: 5", exception.Message);
    }

    [Fact]
    public void GetValueValidationErrors_ShouldReturnMultipleValueErrors()
    {
        using var doc = Json.JsonDocument.Parse(
            "{\n\"a\": +1,\n\"b\": 01,\n\"c\": 1.\n}",
            Settings);

        var errors = doc.GetValueValidationErrors();

        Assert.Equal(3, errors.Count);
        Assert.Collection(
            errors,
            error =>
            {
                Assert.Equal(2, error.LineNumber);
                Assert.Equal(5, error.BytePositionInLine);
                Assert.Contains("InvalidStartOfNumber", error.Message);
            },
            error =>
            {
                Assert.Equal(3, error.LineNumber);
                Assert.Equal(5, error.BytePositionInLine);
                Assert.Contains("InvalidStartOfNumber", error.Message);
            },
            error =>
            {
                Assert.Equal(4, error.LineNumber);
                Assert.Equal(5, error.BytePositionInLine);
                Assert.Contains("InvalidEndOfNumber", error.Message);
            });
    }

    [Fact]
    public void GetValueValidationErrors_ShouldTruncateLongValueInMessage()
    {
        var value = "1." + new string('0', 256) + "e";
        using var doc = Json.JsonDocument.Parse(
            "{\"value\":" + value + "}",
            Settings);

        var error = Assert.Single(doc.GetValueValidationErrors());

        Assert.Contains("InvalidNumberValue", error.Message);
        Assert.Contains("...", error.Message);
        Assert.DoesNotContain(new string('0', 256), error.Message);
    }

    private static void ValidateRoundTrip(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var fileName = Path.GetFileName(filePath);

        using var doc = Json.JsonDocument.Parse(
            bytes,
            Settings,
            new Json.JsonDocumentOptions
            {
                EnableValueValidation = true
            });

        using var source = JsonDocument.Parse(bytes);

        var encoded = Json.JsonDocument.Encode(doc.RootElement);

        using var target = JsonDocument.Parse(encoded);

        var equals = SafeDeepEquals(
            source.RootElement,
            target.RootElement);

        if (!equals)
        {
            throw new XunitException(
                $"Semantic mismatch after round-trip: {fileName}");
        }

        if (Json.JsonDocument.TryParse(bytes, out var doc2, Settings, new Json.JsonDocumentOptions
            {
                EnableValueValidation = true
            }) && doc2 != null)
        {
            using (doc2)
            {
                Assert.True(doc2.IsValid);
            }
        }
        else
        {
            Assert.Fail("Round-trip failed");
        }
    }

    public static IEnumerable<object[]> GetParseFiles(string prefix)
    {
        return Directory.EnumerateFiles(ParsingDir, "*.json")
            .Where(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.Ordinal))
            .Select(path => new object[] { path });
    }

    public static IEnumerable<object[]> GetTransformFiles()
    {
        return Directory.EnumerateFiles(TransformDir, "*.json")
            .Select(path => new object[] { path });
    }

    private static bool SafeDeepEquals(
        JsonElement x,
        JsonElement y)
    {
        try
        {
            return JsonElement.DeepEquals(x, y);
        }
        catch (Exception)
        {
            return x.GetRawText() == y.GetRawText();
        }
    }

    private static string FindTestSuiteDirectory(string subDirectory)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current is not null)
        {
            var path = Path.Combine(
                current.FullName,
                "external",
                "JSONTestSuite",
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
}