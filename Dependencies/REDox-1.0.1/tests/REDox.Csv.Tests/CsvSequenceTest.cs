using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using REDox.Json;

namespace REDox.Csv.Tests;

public sealed class CsvSequenceTest
{
    private static readonly SerializerSettings s_settings = SerializerSettings.Default;

    [Fact]
    public async Task ParseAsync_WithoutHeader()
    {
        using var stream = Stream("a,b\nc,d\n");

        var rows = new List<string>();

        await foreach (var element in CsvSequence.ParseAsync(
                           stream,
                           s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            rows.Add(element.ToJsonString());
        }

        Assert.Equal(
            [
                """["a","b"]""",
                """["c","d"]"""
            ],
            rows);
    }

    [Fact]
    public async Task ParseAsync_WithHeader()
    {
        using var stream = Stream("Name,Age\nMike,43\nFami,66\n");

        var rows = new List<string>();

        await foreach (var element in CsvSequence.ParseAsync(
                           stream,
                           s_settings,
                           new CsvDocumentOptions { HasHeaderRecord = true },
                           TestContext.Current.CancellationToken))
        {
            rows.Add(element.ToJsonString());
        }

        Assert.Equal(
            [
                """{"Name":"Mike","Age":"43"}""",
                """{"Name":"Fami","Age":"66"}"""
            ],
            rows);
    }

    [Fact]
    public async Task DeserializeAsync_WithHeader()
    {
        using var stream = Stream("Name,Age\nMike,43\nFami,66\n");

        var rows = new List<Person>();

        await foreach (var person in CsvSequence.DeserializeAsync<Person>(
                           stream,
                           s_settings,
                           new CsvDocumentOptions { HasHeaderRecord = true },
                           TestContext.Current.CancellationToken))
        {
            rows.Add(person!);
        }

        Assert.Equal([new Person("Mike", 43), new Person("Fami", 66)], rows);
    }

    [Fact]
    public async Task ParseAsync_QuotedNewline()
    {
        using var stream = Stream("Name,Note\nMike,\"hello\nworld\"\nFami,tail\n");

        var rows = new List<string>();

        await foreach (var element in CsvSequence.ParseAsync(
                           stream,
                           s_settings,
                           new CsvDocumentOptions { HasHeaderRecord = true },
                           TestContext.Current.CancellationToken))
        {
            rows.Add(element.ToJsonString());
        }

        Assert.Equal(
            [
                """{"Name":"Mike","Note":"hello\nworld"}""",
                """{"Name":"Fami","Note":"tail"}"""
            ],
            rows);
    }

    [Fact]
    public async Task ParseAsync_UnterminatedQuotedFieldThrows()
    {
        using var stream = Stream("Name\n\"unterminated");

        var ex = await Assert.ThrowsAnyAsync<DocumentParseException>(async () =>
        {
            await foreach (var _ in CsvSequence.ParseAsync(
                               stream,
                               s_settings,
                               new CsvDocumentOptions { HasHeaderRecord = true },
                               TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal(5, ex.BytePosition);
    }

    private static MemoryStream Stream(string csv)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(csv));
    }

    private sealed record Person(string Name, int Age);
}