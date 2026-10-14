using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomContainerAccess
{
    private readonly DObject _dObj;
    private readonly DArray _doxArr;
    private readonly JsonArray _stnArr;
    private readonly JsonObject _stnObj;

    public DomContainerAccess()
    {
        _doxArr = new DArray();
        _stnArr = new JsonArray();
        _dObj = new DObject();
        _stnObj = new JsonObject();


        for (var i = 0; i < 1000; i++)
        {
            _doxArr.Add(i);
            _stnArr.Add(i);
        }

        for (var i = 0; i < 20; i++)
        {
            _dObj.Add(i.ToString(), i);
            _stnObj.Add(i.ToString(), i);
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxArrayNodeEnumerate()
    {
        var sum = 0;
        foreach (var value in _doxArr)
        {
            sum += (int)value;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxArrayNodeIndexer()
    {
        var sum = 0;
        for (var i = 0; i < _doxArr.Count; i++)
        {
            sum += (int)_doxArr[i];
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJArrayNodeEnumerate()
    {
        var sum = 0;
        foreach (var value in _stnArr)
        {
            sum += (int)value!;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJArrayNodeIndexer()
    {
        var sum = 0;
        for (var i = 0; i < _stnArr.Count; i++)
        {
            sum += (int)_stnArr[i]!;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxObjectNodeIndexer()
    {
        var sum = 0;
        for (var i = 0; i < _doxArr.Count; i++)
        {
            sum += (int)_dObj["10"];
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxObjectNodeTryGetProperty()
    {
        var sum = 0;
        for (var i = 0; i < _doxArr.Count; i++)
        {
            if (_dObj.TryGetPropertyValue("10", out var value))
            {
                sum += (int)value;
            }
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJObjectNodeIndexer()
    {
        var sum = 0;
        for (var i = 0; i < _stnArr.Count; i++)
        {
            sum += (int)_stnObj["10"]!;
        }

        return sum;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJObjectNodeTryGetProperty()
    {
        var sum = 0;
        for (var i = 0; i < _stnArr.Count; i++)
        {
            if (_stnObj.TryGetPropertyValue("10", out var value))
            {
                sum += (int)value!;
            }
        }

        return sum;
    }
}