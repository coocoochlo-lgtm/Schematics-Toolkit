using System;
using System.Text.Json;
using REDox.Json;

namespace REDox.Serialization.SystemTextJson.Tests;

public sealed class ExceptionTest
{
    private readonly ITestOutputHelper _output;

    public ExceptionTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void NestTest()
    {
        var item = new Item { SubItem = new Item { SubItem = new Item { SubItem = new Item() } } };

        var json = System.Text.Json.JsonSerializer.Serialize(item);

        for (var i = 0; i < 10; i++)
        {
            var settings = new JsonSerializerOptions
            {
                MaxDepth = i
            };

            try
            {
                var src = System.Text.Json.JsonSerializer.Serialize(item, settings);

                var tgt = Json.JsonSerializer.Serialize(item, new SystemTextJsonSerializerSettings(settings));

                Assert.Equal(src, tgt);

                _output.WriteLine(i.ToString());
            }
            catch (Exception)
            {
                Assert.ThrowsAny<Exception>(() =>
                {
                    Json.JsonSerializer.Serialize(item, new SystemTextJsonSerializerSettings(settings),
                        new JsonWriteOptions
                        {
                            MaxDepth = i
                        });
                });
            }
        }

        for (var i = 0; i < 10; i++)
        {
            var settings = new JsonSerializerOptions
            {
                MaxDepth = i
            };

            try
            {
                System.Text.Json.JsonSerializer.Deserialize<Item>(json, settings);

                _output.WriteLine(i.ToString());
            }
            catch (Exception)
            {
                Assert.ThrowsAny<Exception>(() =>
                {
                    Json.JsonSerializer.Deserialize<Item>(json, new SystemTextJsonSerializerSettings(settings),
                        new Json.JsonDocumentOptions
                        {
                            MaxDepth = i
                        });
                });
            }
        }
    }

    public record Item
    {
        public Item? SubItem { get; set; }
    }
}