namespace REDox.Dynamic.Benchmark;

/// <summary>
///     ベンチマーク間で共有する入力データ。
/// </summary>
static class BenchmarkData
{
    public const string NumberArray = "[1,10,200,300]";
    public const string SmallNumberArray = "[1,2,3]";
    public const string FlatObject = """{"foo":"json","bar":100}""";
    public const string NestedObject = """{"foo":"json", "bar":100, "nest":{ "foobar":true } }""";
    public const string BarList = """[{"bar":50},{"bar":100}]""";

    public const int AccessIterations = 100;
}

/// <summary>
///     "foo" / "bar" キーを持つ JSON オブジェクトのマッピング先。
/// </summary>
public class FooBar
{
    public string foo { get; set; } = string.Empty;
    public int bar { get; set; }
}