using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using REDox.Json;

namespace REDox.Tests;

public sealed class JsonSequenceTest
{
    private static readonly SerializerSettings s_settings = SerializerSettings.Default;

    private static readonly JsonDocumentOptions s_ndjsonOptions = new()
    {
        UseNewlineDelimitedFormat = true
    };

    private static MemoryStream ToStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    [Fact]
    public async Task ParseAsync_RootArray()
    {
        using var stream = ToStream("[1, 2, 3, 4, 5]");

        var values = new List<int>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(element.GetInt32());
        }

        Assert.Equal([1, 2, 3, 4, 5], values);
    }

    [Fact]
    public async Task DeserializeAsync_RootArray_LargeStream()
    {
        var source = Enumerable.Range(0, 20000).ToArray();

        using var stream = ToStream(System.Text.Json.JsonSerializer.Serialize(source));

        var values = new List<int>();

        await foreach (var value in JsonSequence.DeserializeAsync<int>(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(value);
        }

        Assert.Equal(source, values);
    }

    [Fact]
    public async Task ParseAsync_RootArray_Objects()
    {
        var json = "[" + string.Join(",", Enumerable.Range(0, 5000)
            .Select(i => $"{{\"id\":{i},\"name\":\"item, [{i}]\"}}")) + "]";

        using var stream = ToStream(json);

        var ids = new List<int>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            ids.Add(element.GetProperty("id").GetInt32());
        }

        Assert.Equal(Enumerable.Range(0, 5000), ids);
    }

    [Fact]
    public async Task ParseAsync_NDJson()
    {
        var json = string.Join("\n", Enumerable.Range(0, 3000).Select(i => $"{{\"id\":{i}}}"));

        using var stream = ToStream(json);

        var ids = new List<int>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings, s_ndjsonOptions,
                           TestContext.Current.CancellationToken))
        {
            ids.Add(element.GetProperty("id").GetInt32());
        }

        Assert.Equal(Enumerable.Range(0, 3000), ids);
    }

    [Fact]
    public async Task DeserializeAsync_NDJson()
    {
        using var stream = ToStream("1\n2\n3\n");

        var values = new List<int>();

        await foreach (var value in JsonSequence.DeserializeAsync<int>(stream, s_settings, s_ndjsonOptions,
                           TestContext.Current.CancellationToken))
        {
            values.Add(value);
        }

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public async Task ParseAsync_SingleRootValue()
    {
        using var stream = ToStream("  {\"a\":1}  ");

        var results = new List<string>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(element.ToJsonString());
        }

        Assert.Equal(["{\"a\":1}"], results);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream()
    {
        using var stream = ToStream(string.Empty);

        var count = 0;

        await foreach (var _ in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(0, count);
    }

    private static MemoryStream ToStreamWithBom(string json)
    {
        return new MemoryStream([.. Encoding.UTF8.Preamble, .. Encoding.UTF8.GetBytes(json)]);
    }

    [Fact]
    public async Task ParseAsync_Utf8Bom_RootArray()
    {
        using var stream = ToStreamWithBom("[1, 2, 3]");

        var values = new List<int>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(element.GetInt32());
        }

        Assert.Equal([1, 2, 3], values);
    }

    [Fact]
    public async Task DeserializeAsync_Utf8Bom_RootArray_LargeStream()
    {
        var source = Enumerable.Range(0, 20000).ToArray();

        using var stream = ToStreamWithBom(System.Text.Json.JsonSerializer.Serialize(source));

        var values = new List<int>();

        await foreach (var value in JsonSequence.DeserializeAsync<int>(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            values.Add(value);
        }

        Assert.Equal(source, values);
    }

    [Fact]
    public async Task ParseAsync_Utf8Bom_SingleRootValue()
    {
        using var stream = ToStreamWithBom("{\"a\":1}");

        var results = new List<string>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            results.Add(element.ToJsonString());
        }

        Assert.Equal(["{\"a\":1}"], results);
    }

    [Fact]
    public async Task ParseAsync_Utf8Bom_NDJson()
    {
        using var stream = ToStreamWithBom("{\"id\":0}\n{\"id\":1}\n{\"id\":2}\n");

        var ids = new List<int>();

        await foreach (var element in JsonSequence.ParseAsync(stream, s_settings, s_ndjsonOptions,
                           TestContext.Current.CancellationToken))
        {
            ids.Add(element.GetProperty("id").GetInt32());
        }

        Assert.Equal([0, 1, 2], ids);
    }

    [Fact]
    public async Task ParseAsync_EmptyArray()
    {
        using var stream = ToStream("[]");

        var count = 0;

        await foreach (var _ in JsonSequence.ParseAsync(stream, s_settings,
                           cancellationToken: TestContext.Current.CancellationToken))
        {
            count++;
        }

        Assert.Equal(0, count);
    }
}