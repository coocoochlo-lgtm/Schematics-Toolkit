using System.Text;
using BenchmarkDotNet.Attributes;

namespace REDox.Json.Benchmarks;

[MemoryDiagnoser]
[GenericTypeArguments(typeof(bool))]
[GenericTypeArguments(typeof(int))]
[GenericTypeArguments(typeof(int[]))]
[GenericTypeArguments(typeof(double))]
[GenericTypeArguments(typeof(decimal))]
[GenericTypeArguments(typeof(string))]
public class SimpleSerialize<T>
{
    private readonly string _json;
    private readonly T? _root;
    private readonly byte[] _utf8Json;

    public SimpleSerialize()
    {
        if (typeof(T) == typeof(string))
        {
            _root = (T)(object)"ABC";
        }

        if (typeof(T) == typeof(int))
        {
            _root = (T)(object)123;
        }

        if (typeof(T) == typeof(int[]))
        {
            _root = (T)(object)new[] { 1, 2, 3 };
        }

        if (typeof(T) == typeof(double))
        {
            _root = (T)(object)123.456;
        }

        if (typeof(T) == typeof(decimal))
        {
            _root = (T)(object)1.00000m;
        }

        _utf8Json = JsonSerializer.SerializeToUtf8Bytes<T>(_root!);
        _json = Encoding.UTF8.GetString(_utf8Json);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public T? REDoxJsonDeserialize()
    {
        return JsonSerializer.Deserialize<T>(_utf8Json, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public T? REDoxJsonDeserializeFromString()
    {
        return JsonSerializer.Deserialize<T>(_json, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public T? Utf8JsonDeserialize()
    {
        return Utf8Json.JsonSerializer.Deserialize<T>(_utf8Json);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public T? Utf8JsonDeserializeFromString()
    {
        return Utf8Json.JsonSerializer.Deserialize<T>(_json);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public T? STJsonDeserialize()
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(_utf8Json);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public T? STJsonDeserializeFromString()
    {
        return System.Text.Json.JsonSerializer.Deserialize<T>(_json);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public byte[] REDoxJsonSerialize()
    {
        return JsonSerializer.SerializeToUtf8Bytes(_root, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public string REDoxJsonSerializeToString()
    {
        return JsonSerializer.Serialize(_root, SerializerSettings.Default);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public byte[] Utf8JsonSerialize()
    {
        return Utf8Json.JsonSerializer.Serialize(_root);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public string Utf8JsonSerializeToString()
    {
        return Encoding.UTF8.GetString(Utf8Json.JsonSerializer.Serialize(_root));
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public byte[] STJsonSerialize()
    {
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root);
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public string STJsonSerializeToString()
    {
        return System.Text.Json.JsonSerializer.Serialize(_root);
    }
}