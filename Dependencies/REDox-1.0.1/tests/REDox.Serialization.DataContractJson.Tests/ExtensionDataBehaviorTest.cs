using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace REDox.Serialization.DataContractJson.Tests;

/// <summary>
///     DataContractJsonSerializer の ExtensionData に関する実挙動を記録する仕様テスト。
///     REDox 側で ExtensionData 互換を実装する際の基準となる。
/// </summary>
public sealed class ExtensionDataBehaviorTest
{
    private static string Write<T>(T value)
    {
        var sz = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream();
        sz.WriteObject(ms, value);
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static T? Read<T>(string json)
    {
        var sz = new DataContractJsonSerializer(typeof(T));
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return (T?)sz.ReadObject(ms);
    }

    [Fact]
    public void ExtensionDataPreservesOriginalMemberOrder()
    {
        // 既知メンバ Mmm を含め、入力 JSON の出現順がそのまま出力される
        var value = Read<OrderProbe>("{\"Zzz\":\"z\",\"Aaa\":\"a\",\"Mmm\":\"m\"}");

        Assert.Equal("{\"Zzz\":\"z\",\"Aaa\":\"a\",\"Mmm\":\"m\"}", Write(value));
    }

    [Fact]
    public void ExtensionDataPreservesOriginalMemberOrderWhenKnownMemberIsFirst()
    {
        var value = Read<OrderProbe>("{\"Mmm\":\"m\",\"Zzz\":\"z\",\"Aaa\":\"a\"}");

        Assert.Equal("{\"Mmm\":\"m\",\"Zzz\":\"z\",\"Aaa\":\"a\"}", Write(value));
    }

    [Fact]
    public void UnknownMembersRoundTripThroughExtensionData()
    {
        var json = Write(new ProbeV2 { Aaa = "a", Id = 1, Zzz = "z" });

        Assert.Equal("{\"Aaa\":\"a\",\"Id\":1,\"Zzz\":\"z\"}", json);
        Assert.Equal(json, Write(Read<ProbeV1>(json)));
    }

    /// <summary>
    ///     DataMember を持たないモック型を使えば、任意の JSON から ExtensionDataObject を生成できる。
    /// </summary>
    [Fact]
    public void MockTypeCanMaterializeExtensionDataObject()
    {
        var carrier = Read<Carrier>("{\"Aaa\":\"a\",\"Zzz\":\"z\"}");

        Assert.NotNull(carrier!.ExtensionData);
        Assert.Equal("{\"Aaa\":\"a\",\"Zzz\":\"z\"}", Write(new Carrier { ExtensionData = carrier.ExtensionData }));
    }

    /// <summary>
    ///     ただしモック経由で移植した ExtensionDataObject は既知メンバとの相対位置を失い、
    ///     拡張データが既知メンバより前に出力される。
    /// </summary>
    [Fact]
    public void TransplantedExtensionDataLosesRelativeOrder()
    {
        var carrier = Read<Carrier>("{\"Aaa\":\"a\",\"Zzz\":\"z\"}");

        var transplanted = new ProbeV1 { Id = 9, ExtensionData = carrier!.ExtensionData };

        Assert.Equal("{\"Aaa\":\"a\",\"Zzz\":\"z\",\"Id\":9}", Write(transplanted));
    }

    [DataContract(Name = "Probe", Namespace = "")]
    public sealed class ProbeV2
    {
        [DataMember] public string? Aaa { get; set; }

        [DataMember] public int Id { get; set; }

        [DataMember] public string? Zzz { get; set; }
    }

    [DataContract(Name = "Probe", Namespace = "")]
    public sealed class ProbeV1 : IExtensibleDataObject
    {
        [DataMember] public int Id { get; set; }

        public ExtensionDataObject? ExtensionData { get; set; }
    }

    [DataContract(Name = "Order", Namespace = "")]
    public sealed class OrderProbe : IExtensibleDataObject
    {
        [DataMember] public string? Mmm { get; set; }

        public ExtensionDataObject? ExtensionData { get; set; }
    }

    /// <summary>DataMember を一切持たない「モック」型。未知メンバのみを受け取る。</summary>
    [DataContract(Name = "Carrier", Namespace = "")]
    public sealed class Carrier : IExtensibleDataObject
    {
        public ExtensionDataObject? ExtensionData { get; set; }
    }
}