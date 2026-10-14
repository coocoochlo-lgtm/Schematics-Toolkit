using System;
using System.Buffers.Binary;
using System.Runtime.ExceptionServices;
using REDox.Serialization;

namespace REDox.Tests;

public class DoxDocumentTest
{
    private const int ExtendedLength = 0x1ffffff + 1;

    [Fact]
    public void TryParse_ReturnsDocumentForValidDox()
    {
        var dox = DoxSerializer.Serialize("hello");

        var result = DoxDocument.TryParse(dox.AsMemory(), out var parsed);

        using (parsed)
        {
            Assert.True(result);
            Assert.NotNull(parsed);
            Assert.Equal("hello", parsed.RootElement.To<string>());
        }
    }

    [Fact]
    public void TryParse_ReadOnlySpanKeepsParsedDataAlive()
    {
        var dox = DoxSerializer.Serialize("hello");

        var result = DoxDocument.TryParse(dox.AsSpan(), out var parsed);
        dox.AsSpan().Clear();

        using (parsed)
        {
            Assert.True(result);
            Assert.NotNull(parsed);
            Assert.Equal("hello", parsed.RootElement.To<string>());
        }
    }

    [Fact]
    public void TryParse_ReturnsFalseForInvalidDox()
    {
        var result = DoxDocument.TryParse("not dox"u8, out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_ReturnsFalseForTruncatedDox()
    {
        var dox = DoxSerializer.Serialize("hello");

        var result = DoxDocument.TryParse(dox.AsSpan(0, dox.Length - 1), out var parsed);

        Assert.False(result);
        Assert.Null(parsed);
    }

    [Fact]
    public void TryParse_InvalidFooterDoesNotThrowInternally()
    {
        var dox = DoxSerializer.Serialize("hello");
        BinaryPrimitives.WriteInt32LittleEndian(dox.AsSpan(dox.Length - 8), int.MaxValue);

        var threadId = Environment.CurrentManagedThreadId;
        var firstChanceExceptionCount = 0;

        void OnFirstChanceException(object? _, FirstChanceExceptionEventArgs __)
        {
            if (Environment.CurrentManagedThreadId == threadId)
            {
                firstChanceExceptionCount++;
            }
        }

        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

        bool result;
        DoxDocument? parsed;
        try
        {
            result = DoxDocument.TryParse(dox, out parsed);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        }

        using (parsed)
        {
            Assert.False(result);
            Assert.Null(parsed);
            Assert.Equal(0, firstChanceExceptionCount);
        }
    }

    [Fact]
    public void Encode_PreservesByteStringVariant()
    {
        byte[] value = [0, 1, 2, 0xfe, 0xff];
        var array = new DArray { DValue.Create(value, ByteStringKind.Raw) };

        var dox = DoxDocument.Encode(array.AsElement());

        using var parsed = DoxDocument.Parse(dox);
        var result = parsed.RootElement.AsArray()[0];

        Assert.Equal(DTokenVariant.ByteStringRaw, result.GetToken().Variant);
        Assert.True(value.AsSpan().SequenceEqual((ReadOnlySpan<byte>)result));
    }

    [Fact]
    public void Encode_RoundTripsByteStringBeyondInlineLengthPayload()
    {
        var value = CreateBytes(ExtendedLength);
        var array = new DArray { DValue.Create(value, ByteStringKind.Raw) };

        var dox = DoxDocument.Encode(array.AsElement());

        using var parsed = DoxDocument.Parse(dox);
        var result = parsed.RootElement.AsArray()[0];

        Assert.Equal(DTokenVariant.ByteStringRaw, result.GetToken().Variant);
        Assert.True(value.AsSpan().SequenceEqual((ReadOnlySpan<byte>)result));
    }

    private static byte[] CreateBytes(int length)
    {
        var value = new byte[length];

        for (var i = 0; i < value.Length; i++)
        {
            value[i] = (byte)i;
        }

        return value;
    }
}

public class DoxSerializerDocumentTest
{
    private const int ExtendedLength = 0x1ffffff + 1;

    private static readonly DoxSerializerSettings s_settings = new()
    {
        Converters =
        [
            new BigNumberPayloadConverter(),
            new ByteStringPayloadConverter()
        ]
    };

    public static TheoryData<BigNumberKind, DTokenVariant> BigNumberVariants()
    {
        return new TheoryData<BigNumberKind, DTokenVariant>
        {
            { BigNumberKind.Default, DTokenVariant.BigNumber },
            { BigNumberKind.Integer, DTokenVariant.BigNumberInteger },
            { BigNumberKind.Decimal, DTokenVariant.BigNumberDecimal },
            { BigNumberKind.Float, DTokenVariant.BigNumberFloat },
            { BigNumberKind.Int128, DTokenVariant.BigNumberInt128 },
            { BigNumberKind.UInt128, DTokenVariant.BigNumberUInt128 },
            { BigNumberKind.Hexadecimal, DTokenVariant.BigNumberHexadecimal }
        };
    }

    public static TheoryData<ByteStringKind, DTokenVariant> ByteStringVariants()
    {
        return new TheoryData<ByteStringKind, DTokenVariant>
        {
            { ByteStringKind.Default, DTokenVariant.ByteString },
            { ByteStringKind.Base64, DTokenVariant.ByteStringBase64 },
            { ByteStringKind.Base64Url, DTokenVariant.ByteStringBase64Url },
            { ByteStringKind.Base16, DTokenVariant.ByteStringBase16 },
            { ByteStringKind.Guid, DTokenVariant.ByteStringGuid },
            { ByteStringKind.Raw, DTokenVariant.ByteStringRaw }
        };
    }

    [Theory]
    [MemberData(nameof(BigNumberVariants))]
    public void Serialize_PreservesBigNumberVariant(BigNumberKind kind, DTokenVariant expectedVariant)
    {
        var value = new BigNumberPayload("123456789012345678901234567890"u8.ToArray(), kind);

        var dox = DoxSerializer.Serialize(value, s_settings);

        using var parsed = DoxDocument.Parse(dox, s_settings);
        Assert.Equal(expectedVariant, parsed.RootElement.Token.Variant);

        var result = DoxSerializer.Deserialize<BigNumberPayload>(dox, s_settings);
        Assert.NotNull(result);
        Assert.Equal(kind, result.Kind);
        Assert.True(value.Bytes.AsSpan().SequenceEqual(result.Bytes));
    }

    [Theory]
    [MemberData(nameof(ByteStringVariants))]
    public void Serialize_PreservesByteStringVariant(ByteStringKind kind, DTokenVariant expectedVariant)
    {
        var value = new ByteStringPayload([0, 1, 2, 0xfe, 0xff], kind);

        var dox = DoxSerializer.Serialize(value, s_settings);

        using var parsed = DoxDocument.Parse(dox, s_settings);
        Assert.Equal(expectedVariant, parsed.RootElement.Token.Variant);

        var result = DoxSerializer.Deserialize<ByteStringPayload>(dox, s_settings);
        Assert.NotNull(result);
        Assert.Equal(kind, result.Kind);
        Assert.True(value.Bytes.AsSpan().SequenceEqual(result.Bytes));
    }

    [Fact]
    public void Serialize_RoundTripsBigNumberBeyondInlineLengthPayload()
    {
        var value = new BigNumberPayload(CreateBytes(ExtendedLength), BigNumberKind.Integer);

        var dox = DoxSerializer.Serialize(value, s_settings);

        using var parsed = DoxDocument.Parse(dox, s_settings);
        Assert.Equal(DTokenVariant.BigNumberInteger, parsed.RootElement.Token.Variant);

        var result = DoxSerializer.Deserialize<BigNumberPayload>(dox, s_settings);
        Assert.NotNull(result);
        Assert.Equal(BigNumberKind.Integer, result.Kind);
        Assert.True(value.Bytes.AsSpan().SequenceEqual(result.Bytes));
    }

    private static byte[] CreateBytes(int length)
    {
        var value = new byte[length];

        for (var i = 0; i < value.Length; i++)
        {
            value[i] = (byte)i;
        }

        return value;
    }

    private sealed record BigNumberPayload(byte[] Bytes, BigNumberKind Kind);

    private sealed record ByteStringPayload(byte[] Bytes, ByteStringKind Kind);

    private sealed class BigNumberPayloadConverter : DataConverter<BigNumberPayload>
    {
        public override BigNumberPayload Read(in DataReader reader, uint tokenId, BigNumberPayload? existingValue)
        {
            return new BigNumberPayload(
                reader.ReadBigNumber(tokenId).ToArray(),
                reader.GetToken(tokenId).BigNumberKind);
        }

        public override void Write(DataWriter writer, BigNumberPayload? value)
        {
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteBigNumber(value.Bytes, value.Kind);
        }
    }

    private sealed class ByteStringPayloadConverter : DataConverter<ByteStringPayload>
    {
        public override ByteStringPayload Read(in DataReader reader, uint tokenId, ByteStringPayload? existingValue)
        {
            return new ByteStringPayload(
                reader.ReadByteString(tokenId).ToArray(),
                reader.GetToken(tokenId).ByteStringKind);
        }

        public override void Write(DataWriter writer, ByteStringPayload? value)
        {
            ArgumentNullException.ThrowIfNull(value);
            writer.WriteByteString(value.Bytes, value.Kind);
        }
    }
}