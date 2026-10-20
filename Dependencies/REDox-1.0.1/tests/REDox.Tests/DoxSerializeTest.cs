using System;
using System.Collections.Generic;
using System.Text.Json;
using REDox.Benchmarks.Data;
using REDox.Json.Benchmarks;

namespace REDox.Tests;

public class DoxSerializeTest
{
    public static IEnumerable<object[]> GetDeserializeData()
    {
        foreach (var inst in TestData.GetInstances())
        {
            yield return new[] { inst };
        }
    }

    public static IEnumerable<object[]> GetSerializeData()
    {
        foreach (var inst in TestData.GetSerializeInstances())
        {
            yield return new[] { inst };
        }
    }

    [Fact]
    public void Benchmark()
    {
        JsonDeserialize<Twitter, Twitter.Root> benchmark = new();

        benchmark.Setup();
        benchmark.REDoxJsonDeserialize();
    }

    [Theory]
    [InlineData("")]
    [InlineData("ABC")]
    [InlineData("あいうえお")]
    [InlineData("KKVKAKAKAKAKAKAKA漢字KAKA")]
    public void StringTest(string value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Theory]
    [InlineData(123456.78)]
    [InlineData(-123456.78)]
    [InlineData(12345678987654.321)]
    [InlineData(-12345678987654.321)]
    [InlineData(double.MaxValue)]
    [InlineData(double.MinValue)]
    public void FloatTest(double value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));

        var fValue = (float)value;

        Assert.Equal(fValue, DoxSerializer.DeepCopy(fValue));

        var hValue = (Half)value;

        Assert.Equal(hValue, DoxSerializer.DeepCopy(hValue));

        if (value >= (double)decimal.MinValue && value <= (double)decimal.MaxValue)
        {
            var dValue = (decimal)value;

            Assert.Equal(dValue, DoxSerializer.DeepCopy(dValue));
        }
    }

    [Theory]
    [InlineData(123456)]
    [InlineData(-123456)]
    [InlineData(12345678987654)]
    [InlineData(-12345678987654)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Int64Test(long value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Theory]
    [InlineData(123456)]
    [InlineData(12345678987654)]
    [InlineData(ulong.MaxValue)]
    [InlineData(ulong.MinValue)]
    public void UInt64Test(ulong value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Theory]
    [InlineData(0x1000)]
    [InlineData(0x2000)]
    public void LongStringTest(int length)
    {
        var ascii = new string('A', length);
        Assert.Equal(ascii, DoxSerializer.DeepCopy(ascii));

        var multiByte = new string('漢', length / 2);
        Assert.Equal(multiByte, DoxSerializer.DeepCopy(multiByte));
    }

    public static IEnumerable<object[]> GetDecimalData()
    {
        yield return [0m];
        yield return [1.5m];
        yield return [-1.5m];
        yield return [0.0000000000000000000000000001m];
        yield return [decimal.MaxValue];
        yield return [decimal.MinValue];
    }

    [Theory]
    [MemberData(nameof(GetDecimalData))]
    public void DecimalTest(decimal value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    public static IEnumerable<object[]> GetDateTimeData()
    {
        yield return [DateTime.MinValue];
        yield return [DateTime.MaxValue];
        yield return [new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Utc)];
        yield return [new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Unspecified)];
        yield return [new DateTime(2026, 1, 2, 3, 4, 5, 678, DateTimeKind.Local)];
    }

    [Theory]
    [MemberData(nameof(GetDateTimeData))]
    public void DateTimeTest(DateTime value)
    {
        var result = DoxSerializer.DeepCopy(value);

        Assert.Equal(value, result);
        Assert.Equal(value.Kind, result.Kind);
    }

    public static IEnumerable<object[]> GetDateTimeOffsetData()
    {
        yield return [DateTimeOffset.MinValue];
        yield return [DateTimeOffset.MaxValue];
        yield return [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)];
        yield return [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(9))];
        yield return [new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5.5))];
    }

    [Theory]
    [MemberData(nameof(GetDateTimeOffsetData))]
    public void DateTimeOffsetTest(DateTimeOffset value)
    {
        var result = DoxSerializer.DeepCopy(value);

        Assert.Equal(value, result);
        Assert.Equal(value.Offset, result.Offset);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BooleanTest(bool value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    [InlineData(0.1)]
    public void SpecialFloatTest(double value)
    {
        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Fact]
    public void ByteArrayTest()
    {
        byte[] value = [0, 1, 2, 0xfe, 0xff];

        Assert.Equal(value, DoxSerializer.DeepCopy(value));
    }

    [Fact]
    public void CollectionTest()
    {
        var list = new List<int> { 1, -2, int.MaxValue, int.MinValue };
        Assert.Equal(list, DoxSerializer.DeepCopy(list));

        var dict = new Dictionary<string, long> { ["a"] = 1, ["漢字"] = long.MaxValue, [""] = -1 };
        Assert.Equal(dict, DoxSerializer.DeepCopy(dict));

        var nested = new List<List<string>> { new() { "x", "" }, new(), new() { "y" } };
        var nestedResult = DoxSerializer.DeepCopy(nested);
        Assert.Equal(nested.Count, nestedResult.Count);
        for (var i = 0; i < nested.Count; i++)
        {
            Assert.Equal(nested[i], nestedResult[i]);
        }
    }

    [Fact]
    public void DeserializeStringTest()
    {
        var inst = new StringTest();

        var dox = DoxSerializer.Serialize(inst);

        var result = DoxSerializer.Deserialize<StringTest?>(dox);

        TestContext.Current.TestOutputHelper?.WriteLine(result?.ToString() ?? "null");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void DeserializeString(string? inst)
    {
        var dox = DoxSerializer.Serialize(inst);

        var result = DoxSerializer.Deserialize<string?>(dox);

        TestContext.Current.TestOutputHelper?.WriteLine(result ?? "null");
    }

    [Theory]
    [MemberData(nameof(GetDeserializeData))]
    public void Deserialize(object inst)
    {
        if (inst is RefLoop)
        {
            return;
        }

        if (inst is CallbackTest || inst is CallbackTest2)
        {
            return;
        }

        if (inst is InheritCollectionTest)
        {
            return;
        }

        if (inst is KnownTest)
        {
            return;
        }

        if (inst is ObjectMember)
        {
            return;
        }

        if (inst is CallbackStructTest)
        {
            return;
        }

        if (inst is SimpleClass)
        {
            return;
        }

        if (inst is TestBase)
        {
            return;
        }

        if (inst is NumericsTest)
        {
            return;
        }

        if (inst is ExtensionDataCompatibilityStress || inst is AttributeCompatibilityStress)
        {
            return;
        }

        var baseJson = string.Empty;
        var inst1 = inst;
        JsonElement element1;

        try
        {
            baseJson = JsonSerializer.Serialize(inst);
            inst1 = JsonSerializer.Deserialize(baseJson, inst.GetType())!;
            element1 = JsonSerializer.SerializeToElement(inst1);
        }
        catch (Exception)
        {
            return;
        }

        var dox = DoxSerializer.Serialize(inst);

        TestContext.Current.TestOutputHelper?.WriteLine(dox.Length.ToString());

        var result = DoxSerializer.Deserialize(dox, inst.GetType());

        var element2 = JsonSerializer.SerializeToElement(result, inst.GetType());

        var json1 = element1.ToString();
        var json2 = element2.ToString();

        Assert.Equal(json1, json2);
        Assert.True(JsonElement.DeepEquals(element1, element2));
    }
}