using System;
using System.Text;
using System.Text.Json;
using REDox.Json;

namespace REDox.Serialization.SystemTextJson.Tests;

public sealed class JsonWriterTest
{
    private readonly ITestOutputHelper _output;

    public JsonWriterTest(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void WriteJson()
    {
        var sjson = string.Empty;
        var djson = string.Empty;
        var myId = Guid.NewGuid();

        {
            using var buffer = new DocumentWriter();

            var property1 = JsonEncodedText.Encode("property1");
            var property2 = JsonEncodedText.Encode("property2");

            {
                using var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
                {
                    Indented = true
                });

                writer.WriteStartObject();
                writer.WriteBoolean(property1, true);
                writer.WriteNumber(property2, 123);
                writer.WriteString("property3", myId);
                writer.WriteString("あいう", "𩸽\n");
                writer.WriteEndObject();
            }

            sjson = Encoding.UTF8.GetString(buffer.ToArray());
        }


        {
            using var writer = new JsonWriter(new SystemTextJsonSerializerSettings(), new JsonWriteOptions
            {
                WriteIndented = true
            });

            writer.WriteStartMap();
            writer.WriteString("property1");
            writer.WriteBoolean(true);
            writer.WriteString("property2");
            writer.WriteInt32(123);
            writer.WriteString("property3");
            writer.WriteGuid(myId);
            writer.WriteString("あいう");
            writer.WriteString("𩸽\n");
            writer.WriteEndMap();

            djson = writer.EncodeToString();
        }

        _output.WriteLine(sjson);
        _output.WriteLine(djson);

        Assert.Equal(sjson, djson);
    }
}