//#define ENABLE_SINGLE

using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;

namespace REDox.Json.Benchmarks;

[MemoryDiagnoser]
public class ParallelForSerialize
{
    public class Person
    {
        public string Name { get; set; } = string.Empty;
        public int Age { get; set; }
        public string Address { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    private readonly Person _root;
    private readonly byte[] _utf8Json;
    private const int ItemCount = 1000000;

    private readonly ParallelOptions _parallelOptions = new()
    {
        MaxDegreeOfParallelism = 4
    };

    public ParallelForSerialize()
    {
        _root = new Person
        {
            Name = "John Doe",
            Age = 30,
            Address = "123 Main St",
            IsActive = true
        };

        _utf8Json = JsonSerializer.SerializeToUtf8Bytes(_root);
    }

#if ENABLE_SINGLE
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public Person?[] REDoxJsonSingleDeserialize()
    {
        var persons = new Person?[ItemCount];

        for(var i = 0;i < ItemCount; i++)
        {
            persons[i] = Json.JsonSerializer.Deserialize<Person>(_utf8Json, SerializerSettings.Default);
        }

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxJsonSingleSerialize()
    {
        int totalBytes = 0;

        for (var i = 0; i < ItemCount; i++)
        {
            var utf8Bytes = Json.JsonSerializer.SerializeToUtf8Bytes(_root, SerializerSettings.Default);

            System.Threading.Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        }

        return totalBytes;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public Person?[] STJsonSingleDeserialize()
    {
        var persons = new Person?[ItemCount];

        for (var i = 0; i < ItemCount; i++)
        {
            persons[i] = System.Text.Json.JsonSerializer.Deserialize<Person>(_utf8Json);
        }

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJsonSingleSerialize()
    {
        int totalBytes = 0;

        for (var i = 0; i < ItemCount; i++)
        {
            var utf8Bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root);

            System.Threading.Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        }

        return totalBytes;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public Person?[] Utf8JsonSingleDeserialize()
    {
        var persons = new Person?[ItemCount];

        for (var i = 0; i < ItemCount; i++)
        {
            persons[i] = Utf8Json.JsonSerializer.Deserialize<Person>(_utf8Json);
        }

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public int Utf8JsonSingleSerialize()
    {
        int totalBytes = 0;

        for (var i = 0; i < ItemCount; i++)
        {
            var utf8Bytes = Utf8Json.JsonSerializer.Serialize(_root);

            System.Threading.Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        }

        return totalBytes;
    }
#endif

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public Person?[] REDoxJsonParallelForDeserialize()
    {
        var persons = new Person?[ItemCount];

        Parallel.For(0, ItemCount, _parallelOptions,
            i => { persons[i] = JsonSerializer.Deserialize<Person>(_utf8Json, SerializerSettings.Default); });

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxJsonParallelForSerialize()
    {
        var totalBytes = 0;

        Parallel.For(0, ItemCount, _parallelOptions, i =>
        {
            var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(_root, SerializerSettings.Default);

            Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        });

        return totalBytes;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public Person?[] STJsonParallelForDeserialize()
    {
        var persons = new Person?[ItemCount];

        Parallel.For(0, ItemCount, _parallelOptions,
            i => { persons[i] = System.Text.Json.JsonSerializer.Deserialize<Person>(_utf8Json); });

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJsonParallelForSerialize()
    {
        var totalBytes = 0;

        Parallel.For(0, ItemCount, _parallelOptions, i =>
        {
            var utf8Bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(_root);

            Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        });

        return totalBytes;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public Person?[] Utf8JsonParallelForDeserialize()
    {
        var persons = new Person?[ItemCount];

        Parallel.For(0, ItemCount, _parallelOptions,
            i => { persons[i] = Utf8Json.JsonSerializer.Deserialize<Person>(_utf8Json); });

        return persons;
    }


    [Benchmark]
    [BenchmarkCategory(nameof(Utf8Json))]
    public int Utf8JsonParallelForSerialize()
    {
        var totalBytes = 0;

        Parallel.For(0, ItemCount, _parallelOptions, i =>
        {
            var utf8Bytes = Utf8Json.JsonSerializer.Serialize(_root);

            Interlocked.Add(ref totalBytes, utf8Bytes.Length);
        });

        return totalBytes;
    }
}