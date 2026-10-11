using System.Text.Json.Nodes;
using BenchmarkDotNet.Attributes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using REDox.Json;

namespace REDox.Benchmarks;

[MemoryDiagnoser]
public class DomJsonEdit
{
    public static string s_json = """
                                  {
                                    "id": 1001,
                                    "name": "Slime",
                                    "enabled": true,
                                    "hp": 30,
                                    "mp": 0,
                                    "position": {
                                      "x": 12.5,
                                      "y": -3.25,
                                      "z": 0
                                    },
                                    "tags": [
                                      "enemy",
                                      "low-level",
                                      "green"
                                    ],
                                    "drops": [
                                      {
                                        "itemId": "coin",
                                        "rate": 0.75,
                                        "count": 3
                                      },
                                      {
                                        "itemId": "gel",
                                        "rate": 0.25,
                                        "count": 1
                                      }
                                    ],
                                    "ai": {
                                      "type": "wander",
                                      "params": {
                                        "radius": 5,
                                        "speed": 1.2
                                      }
                                    },
                                    "notes": null
                                  }
                                  """;

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public int REDoxSimpleJsonParse()
    {
        var root = DValue.ParseJson(s_json);

        return root.Count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public string REDoxSimpleJsonEdit()
    {
        var root = DValue.ParseJson(s_json);

        root["name"] = "King Slime";
        root["hp"] = 300;
        root["position"]["y"].ReplaceWith(-10.0);
        root["tags"].AsArray().RemoveAt(1);
        root["tags"].AsArray().Add("boss");
        root["drops"][0]["count"].ReplaceWith(99);
        root["drops"].AsArray().Add(new DObject
        {
            { "itemId", "crown" },
            { "rate", 0.01 },
            { "count", 1 }
        });
        root["ai"] = new DObject
        {
            { "type", "charge" },
            { "params", new DObject { { "cooldown", 3.5 }, { "power", 10 } } }
        };
        root["notes"] = "promoted";

        return root.ToJsonString();
    }


    [Benchmark]
    [BenchmarkCategory(nameof(REDox))]
    public string REDoxSimpleJsonEdit2()
    {
        var root = DValue.ParseJson(s_json);

        root["name"] = "King Slime";
        root["hp"] = 300;
        root["position"]["y"].ReplaceWith(-10.0);
        root["tags"].AsArray().RemoveAt(1);
        root["tags"].AsArray().Add("boss");
        root["drops"][0]["count"].ReplaceWith(99);
        root["drops"].AsArray().Add(new
        {
            itemId = "crown",
            rate = 0.01,
            count = 1
        });
        root["ai"] = DValue.Create(new
        {
            type = "charge",
            @params = new
            {
                cooldown = 3.5, power = 10
            }
        });
        root["notes"] = "promoted";

        return root.ToJsonString();
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public int STJSimpleJsonParse()
    {
        var root = JsonNode.Parse(s_json);

        return root!.AsObject().Count;
    }

    [Benchmark]
    [BenchmarkCategory("STJ")]
    public string STJSimpleJsonEdit()
    {
        var root = JsonNode.Parse(s_json)!.AsObject();

        root["name"] = "King Slime";
        root["hp"] = 300;
        root["position"]!["y"]!.ReplaceWith(-10.0);
        root["tags"]!.AsArray().RemoveAt(1);
        root["tags"]!.AsArray().Add("boss");
        root["drops"]![0]!["count"]!.ReplaceWith(99);
        root["drops"]!.AsArray().Add(new JsonObject
        {
            { "itemId", "crown" },
            { "rate", 0.01 },
            { "count", 1 }
        });
        root["ai"] = new JsonObject
        {
            { "type", "charge" },
            { "params", new JsonObject { { "cooldown", 3.5 }, { "power", 10 } } }
        };
        root["notes"] = "promoted";

        return root.ToJsonString();
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Newtonsoft))]
    public int NTJSimpleJsonParse()
    {
        var root = JObject.Parse(s_json);

        return root.Count;
    }

    [Benchmark]
    [BenchmarkCategory(nameof(Newtonsoft))]
    public string NTJSimpleJsonEdit()
    {
        var root = JObject.Parse(s_json);

        root["name"] = "King Slime";
        root["hp"] = 300;
        root["position"]!["y"]!.Replace(-10.0);
        ((JArray)root["tags"]!).RemoveAt(1);
        ((JArray)root["tags"]!).Add("boss");
        root["drops"]![0]!["count"]!.Replace(99);
        ((JArray)root["drops"]!).Add(new JObject
        {
            { "itemId", "crown" },
            { "rate", 0.01 },
            { "count", 1 }
        });
        root["ai"] = new JObject
        {
            { "type", "charge" },
            { "params", new JObject { { "cooldown", 3.5 }, { "power", 10 } } }
        };
        root["notes"] = "promoted";

        return root.ToString(Formatting.None);
    }
}