using System.Linq;
using BenchmarkDotNet.Attributes;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Benchmark;

[MemoryDiagnoser]
public class ConvertAndDeserialize
{
    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public void REDoxConvertAndDeserialize()
    {
        using var numbersDoc = JsonDocument.Parse(BenchmarkData.NumberArray, SerializerSettings.Default);
        using var recordDoc = JsonDocument.Parse(BenchmarkData.FlatObject, SerializerSettings.Default);

        var numbers = numbersDoc.RootElement.AsDynamic()!;
        var record = recordDoc.RootElement.AsDynamic()!;

        // 配列: メソッド / 明示キャスト / 暗黙変換
        var viaMethod = numbers.Deserialize<int[]>();
        var viaExplicit = (int[])numbers;
        int[] viaImplicit = numbers;

        // オブジェクト: プロパティ名でマッピング
        var recordViaMethod = record.Deserialize<FooBar>();
        var recordViaExplicit = (FooBar)record;
        FooBar recordViaImplicit = record;

        // LINQ との組み合わせ
        var barItems = JsonDocument.Parse(BenchmarkData.BarList, SerializerSettings.Default)
            .RootElement.AsDynamic()!;
        var total = ((FooBar[])barItems).Select(x => x.bar).Sum();
        var projected = ((dynamic[])barItems).Select(x => x.bar);
    }

    [Benchmark]
    [BenchmarkCategory(nameof(DynaJson))]
    public void DynaJsonConvertAndDeserialize()
    {
        var numbers = DynamicJson.Parse(BenchmarkData.NumberArray);
        var record = DynamicJson.Parse(BenchmarkData.FlatObject);

        var viaMethod = numbers.Deserialize<int[]>();
        var viaExplicit = (int[])numbers;
        int[] viaImplicit = numbers;

        var recordViaMethod = record.Deserialize<FooBar>();
        var recordViaExplicit = (FooBar)record;
        FooBar recordViaImplicit = record;

        var barItems = DynamicJson.Parse(BenchmarkData.BarList);
        var total = ((FooBar[])barItems).Select(x => x.bar).Sum();
        var projected = ((dynamic[])barItems).Select(x => x.bar);
    }
}