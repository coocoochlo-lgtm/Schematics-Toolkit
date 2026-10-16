using System;
using System.Collections.Generic;
using System.Linq;
using REDox.Json;

namespace REDox.Tests;

public sealed class DomComplexScenarioTest
{
    [Fact]
    public void BuildDeeplyNestedTree_ThenMutateAcrossLevels()
    {
        var root = new DObject();

        var users = root.AddArray("Users");

        for (var i = 0; i < 3; i++)
        {
            var user = users.AddObject();
            user.Add("Id", i);
            user.Add("Name", $"user{i}");

            var roles = user.AddArray("Roles");
            roles.Add("reader");

            if (i % 2 == 0)
            {
                roles.Add("writer");
            }

            var profile = user.AddObject("Profile");
            profile.Add("Age", 20 + i);
            profile.AddObject("Address").Add("City", $"city{i}");
        }

        users[1].AsObject()["Profile"].AsObject()["Address"].AsObject()["City"] = "osaka";
        users[2].AsObject()["Roles"].AsArray().Insert(0, "admin");
        users.RemoveAt(0);

        Assert.Equal(2, users.Count);
        Assert.Equal("$.Users[0].Profile.Address.City",
            users[0].AsObject()["Profile"].AsObject()["Address"].AsObject()["City"].GetPath());
        Assert.Equal("$.Users[1].Roles[0]", users[1].AsObject()["Roles"].AsArray()[0].GetPath());

        Assert.Equal(
            """{"Users":[{"Id":1,"Name":"user1","Roles":["reader"],"Profile":{"Age":21,"Address":{"City":"osaka"}}},{"Id":2,"Name":"user2","Roles":["admin","reader","writer"],"Profile":{"Age":22,"Address":{"City":"city2"}}}]}""",
            root.ToJsonString());
    }

    [Fact]
    public void ParsedDocument_EditInPlace_ThenRoundTrip()
    {
        using var doc = JsonDocument.Parse(
            """{"config":{"servers":[{"host":"a","port":80},{"host":"b","port":81}],"retry":3},"tags":["x","y"]}""");

        var config = doc.RootElement.GetProperty("config").AsObject();
        var servers = config["servers"].AsArray();

        servers[0].AsObject()["port"] = 8080;
        servers.AddObject().Add("host", "c");
        servers[2].AsObject().Add("port", 82);
        config.Remove("retry");
        config.AddObject("timeout").Add("ms", 500);

        var tags = doc.RootElement.GetProperty("tags").AsArray();
        tags.Insert(1, "inserted");
        Assert.True(tags.Remove(tags[0]));

        var json = doc.RootElement.ToJsonString();

        Assert.Equal(
            """{"config":{"servers":[{"host":"a","port":8080},{"host":"b","port":81},{"host":"c","port":82}],"timeout":{"ms":500}},"tags":["inserted","y"]}""",
            json);

        using var reparsed = JsonDocument.Parse(json);
        Assert.Equal(json, reparsed.RootElement.ToJsonString());
        Assert.Equal(82, reparsed.RootElement.GetProperty("config").GetProperty("servers").AsArray()[2].AsElement().GetProperty("port").GetInt32());
    }

    [Fact]
    public void ReplaceContainerWithScalarAndBack_KeepsSiblingsAndPaths()
    {
        var root = new DObject
        {
            { "Before", 1 },
            { "Target", new DArray { 1, 2, 3 } },
            { "After", "tail" }
        };

        root["Target"].ReplaceWith("scalar");
        Assert.Equal("""{"Before":1,"Target":"scalar","After":"tail"}""", root.ToJsonString());

        root["Target"].ReplaceWith(new DObject { { "Inner", new DArray { true, false } } });
        var inner = root["Target"].AsObject()["Inner"].AsArray();
        inner.Add(DValue.Create((int?)null));

        Assert.Equal("$.Target.Inner[2]", inner[2].GetPath());
        Assert.Equal("""{"Before":1,"Target":{"Inner":[true,false,null]},"After":"tail"}""", root.ToJsonString());
    }

    [Fact]
    public void DeepClone_IsIndependentFromSource()
    {
        var source = new DObject();
        var list = source.AddArray("List");
        list.AddObject().Add("V", 1);
        list.AddArray().Add(2);

        var clone = source.DeepClone();

        list[0].AsObject()["V"] = 100;
        list[1].AsArray().Add(3);
        source.Add("Extra", true);

        clone["List"].AsArray()[0].AsObject()["V"] = -1;

        Assert.Equal("""{"List":[{"V":100},[2,3]],"Extra":true}""", source.ToJsonString());
        Assert.Equal("""{"List":[{"V":-1},[2]]}""", clone.ToJsonString());
        Assert.Equal("$.List[1][0]", clone["List"].AsArray()[1].AsArray()[0].GetPath());
    }

    [Fact]
    public void ParentAndRoot_NavigateFromLeafToTop()
    {
        var root = new DObject();
        var leaf = root.AddObject("A").AddArray("B").AddObject();
        leaf.Add("C", 42);

        var value = leaf["C"];

        Assert.Equal("$.A.B[0].C", value.GetPath());
        Assert.Equal("$.A.B[0]", value.Parent!.Value.GetPath());
        Assert.Equal("$.A.B", value.Parent!.Value.Parent!.Value.GetPath());
        Assert.Equal(root.AsValue(), value.Root);
        Assert.Null(root.AsValue().Parent);
    }

    [Fact]
    public void MapWithMixedKeys_EnumerateAndMutate()
    {
        var root = new DObject();
        var map = root.AddMap("Map");
        map.Add(1, "one");
        map.Add("two", 2);
        map.AddArray(3).Add("three");
        map.AddObject(true).Add("Nested", 1.5);

        Assert.Equal(4, map.Count);

        map.SetValue("ONE", 0);
        map.RemoveAt(1);

        var keys = new List<string?>();

        foreach (var pair in map)
        {
            keys.Add(pair.Key.AsElement().ToString());
        }

        Assert.Equal(3, keys.Count);
        Assert.Equal("ONE", map[0].Value.AsElement().GetString());
        Assert.Equal("three", map[1].Value.AsArray()[0].AsElement().GetString());
        Assert.Equal(1.5, map[2].Value.AsObject()["Nested"].AsElement().GetDouble());
    }

    [Fact]
    public void LargeArray_RemoveRangeAndInsert_KeepsOrder()
    {
        var array = new DArray();

        for (var i = 0; i < 100; i++)
        {
            array.Add(i);
        }

        array.RemoveRange(10, 80);
        array.Insert(5, "mid");

        for (var i = 0; i < 3; i++)
        {
            array.AddObject().Add("I", i);
        }

        Assert.Equal(24, array.Count);
        Assert.Equal("mid", array[5].AsElement().GetString());
        Assert.Equal(90, array[11].AsElement().GetInt32());
        Assert.Equal(99, array[20].AsElement().GetInt32());
        Assert.Equal(2, array[23].AsObject()["I"].AsElement().GetInt32());
        Assert.Equal("$[23].I", array[23].AsObject()["I"].GetPath());
    }

    [Fact]
    public void RemovedChildHandle_BecomesInvalid_AndSiblingsRemainValid()
    {
        var root = new DObject();
        var removed = root.AddObject("Removed");
        removed.Add("X", 1);
        var kept = root.AddObject("Kept");
        kept.Add("Y", 2);

        Assert.True(root.Remove("Removed"));

        Assert.False(removed.IsValid);
        Assert.Throws<ObjectDisposedException>(() => removed.Add("Z", 3));
        Assert.True(kept.IsValid);
        Assert.Equal("$.Kept.Y", kept["Y"].GetPath());
        Assert.Equal("""{"Kept":{"Y":2}}""", root.ToJsonString());
    }

    [Fact]
    public void DomToObject_AfterComplexEdits()
    {
        var root = new DObject();
        root.Add("Name", "root");
        var items = root.AddArray("Items");
        items.AddObject().Add("Value", 1);
        items.AddObject().Add("Value", 2);
        items[0].AsObject()["Value"] = 10;
        items.Insert(1, (DValue)new DObject { { "Value", 5 } });

        var model = root.To<Model>();

        Assert.NotNull(model);
        Assert.Equal("root", model.Name);
        Assert.Equal(new[] { 10, 5, 2 }, model.Items!.Select(x => x.Value).ToArray());
    }

    public sealed class Model
    {
        public string? Name { get; set; }

        public List<Item>? Items { get; set; }
    }

    public sealed class Item
    {
        public int Value { get; set; }
    }
}