using System.Collections.Generic;
using System.Linq;
using DynaJson;
using REDox.Json;

namespace REDox.Dynamic.Tests;

/// <summary>
///     DynaJson (DynamicJson) と REDox の動的 API が同じ結果を返すことを検証する。
///     各テストでは同一入力を両ライブラリに与え、結果を突き合わせる。
/// </summary>
public sealed class DynamicJsonCompatibility
{
    public enum TestEnum
    {
        A,
        B,
        C
    }

    private const string NestedSource = """{"foo":"json", "bar":100, "nest":{ "foobar":true } }""";
    private const string FlatSource = """{"foo":"json","bar":100}""";
    private const string NumberSource = "[1,10,200,300]";
    private const string BarListSource = """[{"bar":50},{"bar":100}]""";

    private static dynamic? ParseREDox(string source)
    {
        return JsonDocument.Parse(source, SerializerSettings.Default).RootElement.AsDynamic();
    }

    private static FooBar[] CreateRecords()
    {
        return
        [
            new FooBar { foo = "fooooo!", bar = 1000 },
            new FooBar { foo = "orz", bar = 10 }
        ];
    }

    [Fact]
    public void DomSerialize()
    {
        var node = new DObject().AsDynamic();
        node.Param = 123;
        node.Name = "MyObj";
        node.List = new[] { 1, 2, 3 };

        var text = JsonSerializer.Serialize(node, SerializerSettings.Default);

        Assert.Equal("""{"Param":123,"Name":"MyObj","List":[1,2,3]}""", text);

        var restored = ((IDoxNode)JsonSerializer.Deserialize<IDoxNode>(text)).AsDynamic();

        restored?.Extend = true;

        Assert.Equal("""{"Param":123,"Name":"MyObj","List":[1,2,3],"Extend":true}""", restored?.ToString());
    }

    [Fact]
    public void DomJoin()
    {
        var node = new DObject().AsDynamic();
        node.Param = 123;
        node.Name = "MyObj";
        node.List = new[] { 1, 2, 3 };

        TestContext.Current.TestOutputHelper?.WriteLine(node.ToString());

        var container = new DArray();
        container.Add(1);
        container.Add(node);
        container.Add("MyElement");

        TestContext.Current.TestOutputHelper?.WriteLine(container.ToString()!);
    }

    [Fact]
    public void REDoxReadAndAccess()
    {
        using var doc = JsonDocument.Parse(NestedSource, SerializerSettings.Default);

        var root = doc.RootElement.AsDynamic();

        var total = ((string)root!.foo).Length
                    + (int)root.bar
                    + ((bool)root.nest.foobar ? 1 : 0)
                    + ((bool)root["nest"]["foobar"] ? 1 : 0);

        Assert.Equal(106, total);
    }

    [Fact]
    public void ReadAndAccess()
    {
        var expected = DynamicJson.Parse(NestedSource);
        var actual = ParseREDox(NestedSource);

        Assert.Equal(expected.foo, actual?.foo);
        Assert.Equal(expected.bar, actual?.bar);
        Assert.Equal(expected.nest.foobar, actual?.nest.foobar);
        Assert.Equal(expected["nest"]["foobar"], actual?["nest"]["foobar"]);
        Assert.Equal(expected.IsDefined("foo"), actual?.IsDefined("foo"));
        Assert.Equal(expected.IsDefined("foooo"), actual?.IsDefined("foooo"));
    }

    [Fact]
    public void Operate()
    {
        var expected = DynamicJson.Parse(NestedSource);
        var actual = ParseREDox(NestedSource);

        // 存在確認（メソッド呼び出し形式を含む）
        Assert.Equal(expected.IsDefined("foo"), actual?.IsDefined("foo"));
        Assert.Equal(expected.IsDefined("foooo"), actual?.IsDefined("foooo"));
        Assert.Equal(expected.foo(), actual?.foo());
        Assert.Equal(expected.foooo(), actual?.foooo());

        // メンバー追加
        expected.Arr = new[] { "NOR", "XOR" };
        expected.Obj1 = new { };
        expected.Obj2 = new { foo = "abc", bar = 100 };

        actual!.Arr = new[] { "NOR", "XOR" };
        actual.Obj1 = new { };
        actual.Obj2 = new { foo = "abc", bar = 100 };

        Assert.Equal(expected.ToString(), actual.ToString());

        // 削除（Delete と呼び出し形式）
        expected.Delete("foo");
        expected.Arr.Delete(0);
        expected("bar");
        expected.Arr(1);

        actual.Delete("foo");
        actual.Arr.Delete(0);
        actual("bar");
        actual.Arr(1);

        // 置換
        expected.Obj1 = 5000;
        actual.Obj1 = 5000;

        Assert.Equal(expected.ToString(), actual.ToString());

        // 新規作成
        dynamic expectedNew = new JsonObject();
        expectedNew.str = "aaa";
        expectedNew.obj = new { foo = "bar" };

        var actualNew = new DObject().AsDynamic();
        actualNew.str = "aaa";
        actualNew.obj = new { foo = "bar" };

        Assert.Equal(expectedNew.ToString(), actualNew.ToString());
    }

    [Fact]
    public void Enumerate()
    {
        var elements = new Queue<int>();
        foreach (int value in DynamicJson.Parse(NumberSource))
        {
            elements.Enqueue(value);
        }

        foreach (int value in ParseREDox(NumberSource)!)
        {
            Assert.Equal(elements.Dequeue(), value);
        }

        var members = new Queue<KeyValuePair<string, dynamic>>();
        var expectedObject = DynamicJson.Parse(FlatSource);
        var actualObject = ParseREDox(FlatSource);
        foreach (KeyValuePair<string, dynamic> member in expectedObject)
        {
            members.Enqueue(member);
        }

        foreach (KeyValuePair<string, dynamic> member in actualObject!)
        {
            var reference = members.Dequeue();
            Assert.Equal(reference.Key, member.Key);
            Assert.Equal(reference.Value.ToString(), member.Value.ToString());
        }
    }

    [Fact]
    public void ConvertAndDeserialize()
    {
        var expectedArray = DynamicJson.Parse(NumberSource);
        var expectedObject = DynamicJson.Parse(FlatSource);

        var expectedInts1 = expectedArray.Deserialize<int[]>();
        var expectedInts2 = (int[])expectedArray;
        int[] expectedInts3 = expectedArray;

        var expectedRecord1 = expectedObject.Deserialize<FooBar>();
        var expectedRecord2 = (FooBar)expectedObject;
        FooBar expectedRecord3 = expectedObject;

        var expectedList = DynamicJson.Parse(BarListSource);
        var expectedSum = ((FooBar[])expectedList).Select(x => x.bar).Sum();
        var expectedProjection = ((dynamic[])expectedList).Select(x => x.bar);

        var actualArray = ParseREDox(NumberSource);
        var actualObject = ParseREDox(FlatSource);

        var actualInts1 = actualArray?.Deserialize<int[]>();
        var actualInts2 = (int[]?)actualArray;
        int[]? actualInts3 = actualArray;

        var actualRecord1 = actualObject?.Deserialize<FooBar>();
        var actualRecord2 = (FooBar?)actualObject;
        FooBar? actualRecord3 = actualObject;

        var actualList = ParseREDox(BarListSource);
        var actualSum = ((FooBar[])expectedList).Select(x => x.bar).Sum();
        var actualProjection = ((dynamic[])expectedList).Select(x => x.bar);

        Assert.True(DoxSerializer.DeepEquals(expectedInts2, actualInts2, SerializerSettings.Default));
        Assert.True(DoxSerializer.DeepEquals(expectedInts3, actualInts3, SerializerSettings.Default));

        Assert.True(DoxSerializer.DeepEquals(expectedRecord2, actualRecord2, SerializerSettings.Default));
        Assert.True(DoxSerializer.DeepEquals(expectedRecord3, actualRecord3, SerializerSettings.Default));

        Assert.Equal(expectedList.ToString(), actualList?.ToString());
        Assert.True(DoxSerializer.DeepEquals(expectedSum, actualSum, SerializerSettings.Default));
        Assert.True(DoxSerializer.DeepEquals(expectedProjection, actualProjection, SerializerSettings.Default));
    }

    [Fact]
    public void Serialize()
    {
        var profile = new
        {
            Name = "Foo",
            Age = 30,
            Address = new { Country = "Japan", City = "Tokyo" },
            Like = new[] { "Microsoft", "Xbox" }
        };
        var records = CreateRecords();

        var expectedProfile = DynamicJson.Serialize(profile);
        var expectedRecords = DynamicJson.Serialize(records);

        var actualProfile = JsonSerializer.Serialize(profile, SerializerSettings.Default);
        var actualRecords = JsonSerializer.Serialize(records, SerializerSettings.Default);

        Assert.Equal(expectedProfile, actualProfile);
        Assert.Equal(expectedRecords, actualRecords);
    }

    [Fact]
    public void CornerCase()
    {
        const string nestedSource = """{"tes":10,"nest":{"a":0}}""";
        var expectedNested = DynamicJson.Parse(nestedSource);
        var actualNested = ParseREDox(nestedSource);

        // name() は IsDefined、name("x") は Delete の糖衣構文
        Assert.Equal(expectedNested.nest(), actualNested?.nest());
        Assert.Equal(expectedNested.nest("a"), actualNested?.nest("a"));

        // C# の予約語と同名のキーは @ を付けてアクセスする
        const string keywordSource = """{"int":10,"event":null}""";
        var expectedKeyword = DynamicJson.Parse(keywordSource);
        var actualKeyword = ParseREDox(keywordSource);

        Assert.Equal(expectedKeyword.@int, actualKeyword?.@int);
        Assert.Null(expectedKeyword.@event);
        Assert.Null(actualKeyword?.@event);
    }

    [Fact]
    public void DynaJson()
    {
        const string source = """
                              {
                                  "foo": "json",
                                  "bar": [100,200],
                                  "nest": {"foobar": true}
                              }
                              """;
        var expected = JsonObject.Parse(source);
        var actual = ParseREDox(source)!;

        // プロパティアクセス（ドット / インデクサ）
        Assert.Equal(expected.foo, actual.foo);
        Assert.Equal(expected.nest.foobar, actual.nest.foobar);
        Assert.Equal(expected["nest"]["foobar"], actual["nest"]["foobar"]);

        // 存在確認
        Assert.Equal(expected.IsDefined("foo"), actual.IsDefined("foo"));
        Assert.Equal(expected.IsDefined("foooo"), actual.IsDefined("foooo"));
        Assert.Equal(expected.foo(), actual.foo());
        Assert.Equal(expected.foooo(), actual.foooo());

        // 配列要素と境界
        Assert.Equal(expected.bar[0], actual.bar[0]);
        Assert.Equal(expected.bar.IsDefined(1), actual.bar.IsDefined(1));
        Assert.Equal(expected.bar.IsDefined(2), actual.bar.IsDefined(2));

        // 要素数
        Assert.Equal(expected.bar.Length, actual.bar.Length);
        Assert.Equal(expected.bar.Count, actual.bar.Count);
    }

    [Fact]
    public void DynaJsonSerialize()
    {
        var records = CreateRecords();
        var expectedRecords = JsonObject.Serialize(records);
        var actualRecords = JsonSerializer.Serialize(records, SerializerSettings.Default);

        var map = new Dictionary<string, int>
        {
            { "aaa", 1 },
            { "bbb", 2 }
        };
        var expectedMap = JsonObject.Serialize(map);
        var actualMap = JsonSerializer.Serialize(map, SerializerSettings.Default);

        dynamic expectedBuilt = new JsonObject();
        expectedBuilt.str = "aaa";
        expectedBuilt.obj = new { foo = "bar" };
        var expectedBuiltText = expectedBuilt.ToString();

        var actualBuilt = new DObject().AsDynamic();
        actualBuilt.str = "aaa";
        actualBuilt.obj = new { foo = "bar" };
        var actualBuiltText = expectedBuilt.ToString();

        Assert.Equal(expectedRecords, actualRecords);
        Assert.Equal(expectedMap, actualMap);
        Assert.Equal(expectedBuiltText, actualBuiltText);
    }

    [Fact]
    public void DynaJsonModify()
    {
        dynamic expected = new JsonObject();
        expected.str = "aaa";
        expected.obj = new { foo = "bar" };
        expected.arr = new[] { "aaa", "bbb" };
        expected.str = "bbb";

        var actual = new DObject().AsDynamic();
        actual.str = "aaa";
        actual.obj = new { foo = "bar" };
        actual.arr = new[] { "aaa", "bbb" };
        actual.str = "bbb";

        Assert.Equal(expected.ToString(), actual.ToString());

        // 1 回目は成功、2 回目は既に存在しないため失敗
        Assert.Equal(expected.Delete("str"), actual.Delete("str"));
        Assert.Equal(expected.Delete("str"), actual.Delete("str"));
        Assert.Equal(expected("obj"), actual("obj"));

        Assert.Equal(expected.ToString(), actual.ToString());

        dynamic expectedItems = new JsonObject(new[] { "aaa", "bbb" });
        var actualItems = DValue.Create(new[] { "aaa", "bbb" }).AsDynamic()!;

        Assert.Equal(expectedItems.ToString(), actualItems.ToString());

        expectedItems[0] = "ccc";
        actualItems[0] = "ccc";

        Assert.Equal(expectedItems.ToString(), actualItems.ToString());

        Assert.Equal(expectedItems.Delete(0), actualItems.Delete(0));
        Assert.Equal(expectedItems[0], actualItems[0]);
        Assert.Equal(expectedItems(0), actualItems(0));
        Assert.Equal(expectedItems.Length, actualItems.Length);

        Assert.Equal(expectedItems.ToString(), actualItems.ToString());

        // 範囲外インデックスへの代入
        expectedItems[9] = "ddd";
        actualItems[9] = "ddd";

        Assert.Equal(expectedItems.ToString(), actualItems.ToString());

        const string smallNumbers = "[1,2,3]";
        Assert.Equal(SumInts(JsonObject.Parse(smallNumbers)), SumInts(ParseREDox(smallNumbers)!));

        var expectedPairs = FlattenPairs(JsonObject.Parse(FlatSource));
        var actualPairs = FlattenPairs(ParseREDox(FlatSource)!);

        Assert.True(DoxSerializer.DeepEquals(expectedPairs, actualPairs, SerializerSettings.Default));
    }

    private static int SumInts(dynamic source)
    {
        var total = 0;
        foreach (int value in source)
        {
            total += value;
        }

        return total;
    }

    private static List<string> FlattenPairs(dynamic source)
    {
        var pairs = new List<string>();
        foreach (KeyValuePair<string, dynamic> member in source)
        {
            pairs.Add(member.Key + ":" + member.Value);
        }

        return pairs;
    }

    [Fact]
    public void NullTest()
    {
        const string source = "null";

        Assert.Null(DynamicJson.Parse(source));
        Assert.Null(ParseREDox(source));
    }

    [Fact]
    public void TypeCompare()
    {
        dynamic expected = new JsonObject();
        var actual = new DObject().AsDynamic();
        Populate(expected);
        Populate(actual);

        Assert.Equal(expected.ToString(), actual.ToString());

        Assert.Equal(expected.a, actual.a);
        Assert.Equal(expected.b, actual.b);
        Assert.Equal(expected.c, actual.c);
        Assert.Equal(expected.d, actual.d);
        Assert.Equal(expected.e, actual.e);
        Assert.Equal(expected.f, actual.f);
        Assert.Equal(expected.g, actual.g);

        Assert.Equal(expected.a.GetType(), actual.a.GetType());
        Assert.Equal(expected.b.GetType(), actual.b.GetType());
        Assert.Equal(expected.c.GetType(), actual.c.GetType());
        Assert.Equal(expected.d.GetType(), actual.d.GetType());
        Assert.Equal(expected.e.GetType(), actual.e.GetType());
        Assert.Equal(expected.g.GetType(), actual.g.GetType());

        static void Populate(dynamic target)
        {
            target.a = 123;
            target.b = 1.001m;
            target.c = 1.23;
            target.d = 1.45;
            target.e = "abc";
            target.f = TestEnum.B;
            target.g = string.Empty;
        }
    }

    public class FooBar
    {
        public string foo { get; set; } = string.Empty;
        public int bar { get; set; }
    }
}