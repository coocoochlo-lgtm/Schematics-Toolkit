using System;
using System.IO;
using System.Text;
using System.Text.Json;
using REDox.Json;

namespace REDox.Tests;

public class JsonWriterCompatibility
{
    [Fact]
    public void WriteDepthOverflow()
    {
        const int MaxDepth = 65;

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var ms = new MemoryStream();
            using var sw = new Utf8JsonWriter(ms, new JsonWriterOptions { MaxDepth = MaxDepth - 1 });

            for (var i = 0; i < MaxDepth; i++)
            {
                sw.WriteStartObject();
                sw.WritePropertyName("value");
                sw.WriteNumberValue(42);

                if (i + 1 < MaxDepth)
                {
                    sw.WritePropertyName("obj");
                }
            }

            for (var i = 0; i < MaxDepth; i++)
            {
                sw.WriteEndObject();
            }

            sw.Flush();
        });

        Assert.Throws<InvalidOperationException>(() =>
        {
            using var dw = new JsonWriter(SerializerSettings.Default, new JsonWriteOptions
            {
                MaxDepth = MaxDepth - 1
            });

            for (var i = 0; i < MaxDepth; i++)
            {
                dw.WriteStartMap();
                dw.WriteString("value");
                dw.WriteInt32(42);

                if (i + 1 < MaxDepth)
                {
                    dw.WriteString("obj");
                }
            }

            for (var i = 0; i < MaxDepth; i++)
            {
                dw.WriteEndMap();
            }

            dw.Flush();
        });
    }

    [Fact]
    public void WriteJson()
    {
        using var dw = new JsonWriter(SerializerSettings.Default);

        dw.WriteStartMap();
        dw.WriteString("message");
        dw.WriteString("Hello, World!");
        dw.WriteString("value");
        dw.WriteInt64(42);

        dw.WriteString("data");

        DValue.Create(new[] { 1, 2, 3 }).WriteTo(dw);

        dw.WriteEndMap();

        var json = dw.EncodeToString();

        using var ms = new MemoryStream();
        using var sw = new Utf8JsonWriter(ms);

        sw.WriteStartObject();
        sw.WritePropertyName("message");
        sw.WriteStringValue("Hello, World!");
        sw.WritePropertyName("value");
        sw.WriteNumberValue(42);
        sw.WritePropertyName("data");

        System.Text.Json.JsonSerializer.SerializeToElement(new[] { 1, 2, 3 }).WriteTo(sw);

        sw.WriteEndObject();
        sw.Flush();

        Console.WriteLine(json);

        var expectedJson = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Equal(expectedJson, json);
    }
}