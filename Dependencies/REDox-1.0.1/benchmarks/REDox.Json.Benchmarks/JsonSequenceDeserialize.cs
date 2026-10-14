using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace REDox.Json.Benchmarks;

public sealed class SequenceRecord
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public double Value { get; set; }

    public bool Enabled { get; set; }
}

[JsonSerializable(typeof(SequenceRecord))]
[JsonSerializable(typeof(SequenceRecord[]))]
public partial class SequenceSerializerContext : JsonSerializerContext
{
}

[MemoryDiagnoser]
public class JsonSequenceDeserialize
{
    private static readonly JsonDocumentOptions s_ndjsonOptions = new()
    {
        UseNewlineDelimitedFormat = true
    };

    private static readonly JsonSerializerOptions s_stjTypeInfoOptions = new()
    {
        TypeInfoResolver = SequenceSerializerContext.Default
    };

    private byte[] _arrayJson = [];

    private byte[] _ndjson = [];

    [Params(1_000, 100_000)] public int Count { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var array = new StringBuilder();
        var ndjson = new StringBuilder();

        array.Append('[');

        for (var i = 0; i < Count; i++)
        {
            var record =
                $"{{\"Id\":{i},\"Name\":\"name-{i}\",\"Value\":{i}.5,\"Enabled\":{(i % 2 == 0 ? "true" : "false")}}}";

            if (i > 0)
            {
                array.Append(',');
                ndjson.Append('\n');
            }

            array.Append(record);
            ndjson.Append(record);
        }

        array.Append(']');

        _arrayJson = Encoding.UTF8.GetBytes(array.ToString());
        _ndjson = Encoding.UTF8.GetBytes(ndjson.ToString());

        Validate();
    }

    private void Validate()
    {
        var redox = REDoxSequenceDeserializeAsync().GetAwaiter().GetResult();
        var redoxNdjson = REDoxSequenceDeserializeNDJsonAsync().GetAwaiter().GetResult();
        var stj = STJDeserializeAsyncEnumerable().GetAwaiter().GetResult();

        if (redox != Count || redoxNdjson != Count || stj != Count)
        {
            throw new InvalidOperationException(
                $"Unexpected element count. REDox={redox}, NDJSON={redoxNdjson}, STJ={stj}, Expected={Count}");
        }
    }

    [Benchmark(Baseline = true)]
    [BenchmarkCategory(nameof(REDox))]
    public async Task<int> REDoxSequenceDeserializeAsync()
    {
        using var stream = new MemoryStream(_arrayJson);

        var count = 0;

        await foreach (var record in JsonSequence.DeserializeAsync<SequenceRecord>(stream))
        {
            if (record is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public async Task<int> REDoxSequenceDeserializeNDJsonAsync()
    {
        using var stream = new MemoryStream(_ndjson);

        var count = 0;

        await foreach (var record in JsonSequence.DeserializeAsync<SequenceRecord>(
                           stream, options: s_ndjsonOptions))
        {
            if (record is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxDeserializeWholeArray()
    {
        var records = JsonSerializer.Deserialize<SequenceRecord[]>(
            _arrayJson, SerializerSettings.Default);

        return records?.Length ?? 0;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public async Task<int> STJDeserializeAsyncEnumerable()
    {
        using var stream = new MemoryStream(_arrayJson);

        var count = 0;

        await foreach (var record in System.Text.Json.JsonSerializer
                           .DeserializeAsyncEnumerable<SequenceRecord>(stream))
        {
            if (record is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public async Task<int> STJDeserializeAsyncEnumerableTypeInfo()
    {
        using var stream = new MemoryStream(_arrayJson);

        var count = 0;

        await foreach (var record in System.Text.Json.JsonSerializer
                           .DeserializeAsyncEnumerable<SequenceRecord>(stream, s_stjTypeInfoOptions))
        {
            if (record is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJDeserializeWholeArray()
    {
        var records = System.Text.Json.JsonSerializer
            .Deserialize<SequenceRecord[]>(_arrayJson, s_stjTypeInfoOptions);

        return records?.Length ?? 0;
    }
}