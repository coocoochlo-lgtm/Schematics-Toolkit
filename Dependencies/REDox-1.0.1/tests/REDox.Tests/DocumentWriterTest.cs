using System;
using System.IO;

namespace REDox.Tests;

public class DocumentWriterTest
{
    [Fact]
    public void FlushTwice_AfterPlaceholderCrossesBuffer_DoesNotReplayOldBufferedChunks()
    {
        using var stream = new MemoryStream();
        using var writer = new DocumentWriter(stream, 32);

        writer.WriteByte(0x01);

        writer.PushPlaceholder(2);

        // PushPlaceholder 後の tail 書き込みで CommitBuffer() を踏ませる。
        // この時点では placeholder が未 patch なので、旧 buffer が _buffers に退避される。
        writer.WriteBytes(new byte[]
        {
            0x02, 0x03, 0x04, 0x05, 0x06, 0x07
        });

        writer.PopAndPatch(new byte[]
        {
            0xAA, 0xBB
        });

        writer.Flush();

        Assert.Equal(
            new byte[]
            {
                0x01,
                0xAA, 0xBB,
                0x02, 0x03, 0x04, 0x05, 0x06, 0x07
            },
            stream.ToArray());

        writer.WriteByte(0xCC);
        writer.Flush();

        // 正しい期待値:
        // 1回目 flush の内容 + 2回目の 0xCC のみ。
        //
        // 現行実装では _buffers が flush 後も残るため、
        // 2回目 Flush で 0x01, 0xAA, 0xBB が再出力される。
        Assert.Equal(
            new byte[]
            {
                0x01,
                0xAA, 0xBB,
                0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
                0xCC
            },
            stream.ToArray());
    }

    [Fact]
    public void Dispose_WithUnpatchedPlaceholder_ThrowsButReturnsBuffers()
    {
        var stream = new MemoryStream();
        var writer = new DocumentWriter(stream, 32);

        writer.WriteByte(0x01);
        writer.PushPlaceholder(2);
        writer.WriteByte(0x02);

        Assert.Throws<InvalidOperationException>(() => writer.Dispose());

        Assert.Equal(Array.Empty<byte>(), stream.ToArray());
    }

    [Fact]
    public void Flush_AfterPlaceholderCrossesBuffer_BytesWrittenMatchesLogicalLength()
    {
        using var stream = new MemoryStream();
        using var writer = new DocumentWriter(stream, 32);

        writer.WriteByte(0x01);
        writer.PushPlaceholder(2);

        writer.WriteBytes(new byte[]
        {
            0x02, 0x03, 0x04, 0x05, 0x06, 0x07
        });

        writer.PopAndPatch(new byte[]
        {
            0xAA, 0xBB
        });

        writer.Flush();

        var expected = new byte[]
        {
            0x01,
            0xAA, 0xBB,
            0x02, 0x03, 0x04, 0x05, 0x06, 0x07
        };

        Assert.Equal(expected, stream.ToArray());

        // 正しくは論理出力長 9。
        // 現行実装では CommitStream() が _bytesCommitted に列挙長を再加算するため、
        // BytesWritten が過大になる。
        Assert.Equal(expected.Length, writer.BytesWritten);
        Assert.Equal(stream.Length, writer.BytesWritten);
    }

    [Fact]
    public void ToArray_WithUnpatchedPlaceholder_Throws()
    {
        using var writer = new DocumentWriter(32);

        writer.WriteByte(0x01);
        writer.PushPlaceholder(2);
        writer.WriteByte(0x02);

        // 未 patch placeholder がある状態で ToArray すると、
        // 現行実装では placeholder 部分を欠落させた不完全データを返せてしまう。
        Assert.Throws<InvalidOperationException>(() => writer.ToArray());
    }

    [Fact]
    public void CopyTo_WithUnpatchedPlaceholder_Throws()
    {
        using var writer = new DocumentWriter(32);

        writer.WriteByte(0x01);
        writer.PushPlaceholder(2);
        writer.WriteByte(0x02);

        var destination = new byte[16];

        // ToArray と同様、未 patch placeholder がある状態での出力は禁止したい。
        Assert.Throws<InvalidOperationException>(() => writer.CopyTo(destination));
    }

    [Fact]
    public void Constructor_WithNegativeBufferSize_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentWriter(-1));
    }
}