using System.IO;
using System.Text;
using System.Threading.Tasks;
using REDox.Json;

namespace REDox.Tests;

public sealed class SequenceParse
{
    private readonly ITestOutputHelper _output;

    public SequenceParse(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SequenceJsonRead()
    {
        // 巨大な配列を持つストリームのシミュレーション
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(@"[1, 2, 3, 4, 5]"));

        // IAsyncEnumerable で1要素ずつ列挙
        var items = System.Text.Json.JsonSerializer.DeserializeAsyncEnumerable<int>(stream,
            cancellationToken: TestContext.Current.CancellationToken);

        await foreach (var item in items)
        {
            _output.WriteLine($"読み込んだ要素: {item}");
            // ここで非同期処理が可能
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var items2 = JsonSequence.DeserializeAsync<int>(stream, SerializerSettings.Default,
            cancellationToken: TestContext.Current.CancellationToken);

        await foreach (var item in items2)
        {
            _output.WriteLine($"読み込んだ要素: {item}");
            // ここで非同期処理が可能
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        var items3 = JsonSequence.ParseAsync(stream, SerializerSettings.Default,
            cancellationToken: TestContext.Current.CancellationToken);

        await foreach (var item in items3)
        {
            _output.WriteLine($"読み込んだ要素: {item}");

            // ここで非同期処理が可能
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
    }
}