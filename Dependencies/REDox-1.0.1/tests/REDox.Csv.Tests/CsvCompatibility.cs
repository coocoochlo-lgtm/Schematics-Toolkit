using System;
using REDox.Json;

namespace REDox.Csv.Tests;

public class CsvCompatibility
{
    [Fact]
    public void ParseCsv()
    {
        var csv = """
                  name,age
                  def,66
                  abc,44
                  """;

        using var doc = CsvDocument.Parse(csv, SerializerSettings.Default);

        Console.WriteLine(doc.RootElement.ToJsonString());

        using var doc2 = CsvDocument.Parse(csv, SerializerSettings.Default,
            new CsvDocumentOptions
            {
                HasHeaderRecord = true
            });

        Console.WriteLine(doc2.RootElement.ToJsonString());
    }

    [Fact]
    public void WriteObjectArray()
    {
        var persons = new[]
        {
            new ParsonData("Mike", 43),
            new ParsonData("Fami", 66)
        };

        var doc3 = DValue.From(persons);

        Console.WriteLine(doc3.ToJsonString());

        Console.WriteLine(CsvDocument.EncodeToString(doc3));
    }

    [Fact]
    public void SimpleCSV()
    {
        var json = @"{ ""data"" : [
{    ""id"":1,    ""name"":""Johnson, Smith, and Jones Co.""  },
{    ""id"":2,    ""name"":""Sam \""Mad Dog\"" Smith""  },
{    ""id"":3,    ""name"":""Barney & Company""  },
{    ""id"":4,    ""name"":""Johnson's Automotive""  }
] }
";
        var doc = JsonDocument.Parse(json, SerializerSettings.Default);

        Assert.Equal("",
            CsvDocument.EncodeToString(doc.RootElement, new CsvWriteOptions { IncludeHeaderInFirstRow = true }));
    }

    private record ParsonData(string Name, int Age);
}