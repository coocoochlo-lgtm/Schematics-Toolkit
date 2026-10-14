using System;
using System.Collections.Generic;
using REDox.Json;

namespace REDox.MessagePack.Tests;

public sealed class MessagePackMapTest
{
    [Fact]
    public void MapCreate()
    {
        var a = new DMap();
        a.Add(12, 34);

        var b = a.DeepClone();


        var map = new DMap();
        var arr = map.AddArray(12);
        arr.Add("A");
        arr.Add("B");
        var obj = map.AddObject(true);
        obj.Add("C", 1);
        map.Add(64, "T");
        map.Add(DValue.Null, DValue.Null);

        var jsonA = map.ToJsonString();

        Assert.Equal(@"{""12"":[""A"",""B""],""true"":{""C"":1},""64"":""T"",""null"":null}", jsonA);

        var mpkA = MessagePackDocument.Encode(map);

        Assert.Equal(jsonA, MessagePackDocument.Parse(mpkA, SerializerSettings.Default).RootElement.ToJsonString());
        Assert.Equal(jsonA,
            global::MessagePack.MessagePackSerializer.ConvertToJson(mpkA,
                cancellationToken: TestContext.Current.CancellationToken));

        var map2 = map.DeepClone();

        map2.AddMap("false").Add(0.1, 0.2);

        Assert.Equal(@"{""12"":[""A"",""B""],""true"":{""C"":1},""64"":""T"",""null"":null}", map.ToJsonString());
        Assert.Equal(@"{""12"":[""A"",""B""],""true"":{""C"":1},""64"":""T"",""null"":null,""false"":{""0.1"":0.2}}",
            map2.ToJsonString());

        var map4 = new DMap
        {
            { "A", "B" },
            { 1, 2 },
            { true, false }
        };

        var jsonB = map4.ToJsonString();

        Assert.Equal(@"{""A"":""B"",""1"":2,""true"":false}", jsonB);

        var mpkB = MessagePackDocument.Encode(map4);

        Assert.Equal(jsonB, MessagePackDocument.Parse(mpkB, SerializerSettings.Default).RootElement.ToJsonString());
        Assert.Equal(jsonB,
            global::MessagePack.MessagePackSerializer.ConvertToJson(mpkB,
                cancellationToken: TestContext.Current.CancellationToken));

        var map5 = MessagePackDocument.Parse(mpkB, SerializerSettings.Default).RootElement.AsMap();

        foreach (var kv in map5)
        {
            if (kv.Key.GetValueKind() == JsonValueKind.Number)
            {
                kv.Key.ReplaceWith(123);
                kv.Value.ReplaceWith(456);
            }
        }

        var jsonC = map5.ToJsonString();

        Assert.Equal(@"{""A"":""B"",""123"":456,""true"":false}", jsonC);

        using var doc = MessagePackDocument.Parse(mpkB, SerializerSettings.Default);

        var mpkC = MessagePackDocument.Encode(doc.RootElement);

        Assert.True(mpkB.SequenceEqual(mpkC));
    }

    [Fact]
    public void MapModify()
    {
        var map = new DMap();
        map.Add(123, true);
        map.Add(false, "abc");

        Assert.Equal(@"{""123"":true,""false"":""abc""}", map.ToJsonString());

        map[0].Value.ReplaceWith("efg");
        map[1].Key.ReplaceWith(77);

        Assert.Equal(@"{""123"":""efg"",""77"":""abc""}", map.ToJsonString());

        map[1] = new KeyValuePair<DValue, DValue>(false, "xyz");

        Assert.Equal(@"{""123"":""efg"",""false"":""xyz""}", map.ToJsonString());

        map.Add(new KeyValuePair<DValue, DValue>(123, 456));

        Assert.Equal(@"{""123"":""efg"",""false"":""xyz"",""123"":456}", map.ToJsonString());

        map.RemoveAt(1);

        Assert.Equal(@"{""123"":""efg"",""123"":456}", map.ToJsonString());

        map.Clear();

        Assert.Equal(@"{}", map.ToJsonString());
    }

    [Fact]
    public void MapToObject()
    {
        var map = new DMap();
        map.Add("name", "myname");
        map.Add("age", 44);
        map.Add("flag", true);
        map.Add("arr", new[] { 1, 2, 3 });

        var obj = map.AsObject();

        Assert.Equal("myname", (string?)obj["name"]);
        Assert.Equal(44, (int)obj["age"]);
        Assert.True((bool)obj["flag"]);
        Assert.Equal(1, obj["arr"].To<int[]>()![0]);
        Assert.Equal(2, obj["arr"].To<int[]>()![1]);
        Assert.Equal(3, obj["arr"].To<int[]>()![2]);
    }

    [Fact]
    public void ObjectToMap()
    {
        var obj = new DObject();
        obj.Add("name", "myname");
        obj.Add("age", 44);
        obj.Add("flag", true);
        obj.Add("arr", new[] { 1, 2, 3 });

        var map = obj.AsMap();

        Assert.Equal("name", (string?)map[0].Key);
        Assert.Equal("myname", (string?)map[0].Value);
        Assert.Equal("age", (string?)map[1].Key);
        Assert.Equal(44, (int)map[1].Value);
        Assert.Equal("flag", (string?)map[2].Key);
        Assert.True((bool)map[2].Value);
        Assert.Equal("arr", (string?)map[3].Key);
        Assert.Equal(1, map[3].Value.To<int[]>()![0]);
    }
}