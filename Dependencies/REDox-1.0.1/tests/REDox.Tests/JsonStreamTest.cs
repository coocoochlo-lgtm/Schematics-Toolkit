using System.IO;
using System.Text.Json;

namespace REDox.Tests;

public sealed class JsonStreamTest
{
    private readonly int[] _data = [];
    private readonly ITestOutputHelper _output;

    public JsonStreamTest(ITestOutputHelper output)
    {
        _output = output;

        _data = new int[100000];
        for (var i = 0; i < _data.Length; i++)
        {
            _data[i] = i;
        }
    }

    [Fact]
    public void SerializeToStream()
    {
        using var sms = new MemoryStream();
        JsonSerializer.Serialize(sms, _data);

        using var dms = new MemoryStream();
        Json.JsonSerializer.Serialize(dms, _data, SerializerSettings.Default);

        Assert.Equal(sms.ToArray(), dms.ToArray());

        _output.WriteLine(dms.Length.ToString());
    }
}