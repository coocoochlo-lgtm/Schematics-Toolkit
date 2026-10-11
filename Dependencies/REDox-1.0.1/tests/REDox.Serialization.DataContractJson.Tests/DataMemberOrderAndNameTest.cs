using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using REDox.Json;

namespace REDox.Serialization.DataContractJson.Tests;

/// <summary>
///     <see cref="DataMemberAttribute" /> の Order / Name の解釈が
///     DataContractJsonSerializer と一致することを検証する。
/// </summary>
public sealed class DataMemberOrderAndNameTest
{
    private static IEnumerable<object> Cases()
    {
        yield return new OrderMix();
        yield return new SameOrder();
        yield return new NameOverride();
        yield return new NameAndOrder();
        yield return new DerivedOrder();
        yield return new SpecialNames();
        yield return new EmitDefault();
        yield return new PropsAndFields();
    }

    public static IEnumerable<object[]> GetData()
    {
        foreach (var settings in DataContractJsonCompatibility.Settings)
        {
            foreach (var inst in Cases())
            {
                yield return new[] { inst, settings };
            }
        }
    }

    [Theory]
    [MemberData(nameof(GetData))]
    public void SerializeMatchesDataContractJson(object inst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var type = inst.GetType();

        Assert.Equal(SerializeByDataContract(type, inst, settings),
            JsonSerializer.Serialize(inst, type, new DataContractJsonSerializerSettings(settings)));
    }

    [Theory]
    [MemberData(nameof(GetData))]
    public void DeserializeMatchesDataContractJson(object inst,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var type = inst.GetType();

        var json = SerializeByDataContract(type, inst, settings);

        var value = JsonSerializer.Deserialize(json, type, new DataContractJsonSerializerSettings(settings));

        Assert.Equal(json, SerializeByDataContract(type, value, settings));
    }

    private static string SerializeByDataContract(Type type, object? value,
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        var sz = new DataContractJsonSerializer(type, settings);

        using var ms = new MemoryStream();
        sz.WriteObject(ms, value);

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    [DataContract]
    public class OrderMix
    {
        [DataMember(Order = 1)] public int A = 1;
        [DataMember(Order = 2)] public int B = 2;
        [DataMember] public int Y = 25;
        [DataMember] public int Z = 26;
    }

    [DataContract]
    public class SameOrder
    {
        [DataMember(Order = 1)] public int Aaa = 2;
        [DataMember(Order = 1)] public int Zzz = 1;
    }

    [DataContract]
    public class NameOverride
    {
        [DataMember(Name = "zzz")] public int A = 1;
        [DataMember(Name = "aaa")] public int B = 2;
    }

    [DataContract]
    public class NameAndOrder
    {
        [DataMember(Name = "zzz", Order = 1)] public int A = 1;
        [DataMember(Name = "aaa", Order = 2)] public int B = 2;
    }

    [DataContract]
    public class BaseOrder
    {
        [DataMember(Order = 10)] public int BaseHigh = 1;
        [DataMember(Order = 1)] public int BaseLow = 2;
    }

    [DataContract]
    public class DerivedOrder : BaseOrder
    {
        [DataMember(Order = 2)] public int DerivedMid = 3;
        [DataMember(Order = 0)] public int DerivedZero = 4;
    }

    /// <summary>
    ///     DataContractJsonSerializer は XML ローカル名にエンコードした名前
    ///     （"a b" → "a_x0020_b"）を序数比較して並べる。
    /// </summary>
    [DataContract]
    public class SpecialNames
    {
        [DataMember(Name = "a0b")] public int Digit = 5;
        [DataMember(Name = "a.b")] public int Dot = 2;
        [DataMember(Name = "a-b")] public int Hyphen = 3;
        [DataMember(Name = "\u3042")] public int Kana = 8;
        [DataMember(Name = "aab")] public int Lower = 7;
        [DataMember(Name = "a b")] public int Space = 1;
        [DataMember(Name = "a_b")] public int Underscore = 4;
        [DataMember(Name = "aAb")] public int Upper = 6;
    }

    [DataContract]
    public class EmitDefault
    {
        [DataMember(EmitDefaultValue = true)] public int Kept;
        [DataMember(EmitDefaultValue = false)] public string? Null;
        [DataMember(EmitDefaultValue = false)] public int Zero;
    }

    [DataContract]
    public class PropsAndFields
    {
        [DataMember(Order = 1)] public int Field = 1;
        [DataMember(Order = 1)] public int Prop { get; set; } = 2;
    }
}