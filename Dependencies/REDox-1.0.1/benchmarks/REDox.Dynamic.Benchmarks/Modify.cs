using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Benchmark;

[MemoryDiagnoser]
public class Modify
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxModify()
    {
        var target = new DObject().AsDynamic();
        target.str = "aaa";
        target.obj = new { foo = "bar" };
        target.arr = new[] { "aaa", "bbb" };
        target.str = "bbb";

        var items = DValue.Create(new[] { "aaa", "bbb" }).AsDynamic()!;
        items[0] = "ccc";
        items[9] = "ddd"; // 範囲外インデックスへの代入で拡張

        using var numbersDoc = JsonDocument.Parse("[1,2,3]"u8);
        var numbers = numbersDoc.RootElement.AsDynamic()!;
        var total = 0;
        foreach (int n in numbers)
        {
            total += n;
        }

        using var recordDoc = JsonDocument.Parse("""{"foo":"json","bar":100}"""u8);
        var record = recordDoc.RootElement.AsDynamic()!;
        var pairs = new List<string>();
        foreach (KeyValuePair<string, dynamic> kv in record)
        {
            pairs.Add(kv.Key + ":" + kv.Value);
        }
    }

    [Benchmark]
    [BenchmarkCategory(nameof(DynaJson))]
    public void DynaJsonModify()
    {
        dynamic target = new JsonObject();
        target.str = "aaa";
        target.obj = new { foo = "bar" };
        target.arr = new[] { "aaa", "bbb" };
        target.str = "bbb";

        dynamic items = new JsonObject(new[] { "aaa", "bbb" });
        items[0] = "ccc";
        items[9] = "ddd";

        var numbers = JsonObject.Parse(BenchmarkData.SmallNumberArray);
        var total = 0;
        foreach (int n in numbers)
        {
            total += n;
        }

        var record = JsonObject.Parse(BenchmarkData.FlatObject);
        var pairs = new List<string>();
        foreach (KeyValuePair<string, dynamic> kv in record)
        {
            pairs.Add(kv.Key + ":" + kv.Value);
        }
    }
}