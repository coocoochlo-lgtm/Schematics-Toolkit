using System.Text.Json;
using REDox.Serialization;

namespace REDox.Tests;

public class ParallelDeserializeTest
{
    private static readonly SerializerSettings s_settings = new DoxSerializerSettings
    {
        ParallelOptions = new ParallelDeserializeOptions
        {
            ParallelDeserializeEnabled = true
        }
    };

    [Fact]
    public void ParallelDeserialize()
    {
        var values = new double[10000];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = i;
        }

        var utf8Bytes = JsonSerializer.SerializeToUtf8Bytes(values);

        var result = Json.JsonSerializer.Deserialize<double[]>(utf8Bytes, s_settings);

        Assert.Equal(values, result);
    }
}