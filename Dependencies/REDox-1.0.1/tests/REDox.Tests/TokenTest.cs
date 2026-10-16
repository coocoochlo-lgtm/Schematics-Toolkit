namespace REDox.Tests;

public class TokenTest
{
    [Theory]
    [InlineData(2)]
    [InlineData(float.MinValue)]
    [InlineData(float.MaxValue)]
    [InlineData(1.23)]
    [InlineData(-1.24)]
    public void SingleLiteral(float value)
    {
        var token = DToken.MakeExtendInlineSingle(FloatKind.Inherit, value);

        var result = DToken.DecodeInlineSinglePayload(token);

        Assert.Equal(value, result);

        var d = new DArray { value };

        Assert.Equal(value, (float)d[0]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(-1)]
    [InlineData(100000000)]
    [InlineData(-100000000)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(DToken.MinInlineInteger)]
    [InlineData(DToken.MaxInlineInteger)]
    public void IntegerLiteral(long value)
    {
        var token = DToken.MakeExtendInlineInteger(IntegerKind.Hexadecimal, value);

        var result = DToken.DecodeInlineIntegerPayload(token);

        Assert.Equal(value, result);
        Assert.Equal(IntegerKind.Hexadecimal, token.IntegerKind);
    }

    [Fact]
    public void LiteralTest()
    {
        var token1 = DToken.MakeExtendLiteral(DTokenVariant.BooleanTrue, 0);
        Assert.Equal(DTokenType.Literal, token1.Type);
        Assert.Equal(DTokenKind.Boolean, token1.Kind);
        Assert.Equal(DTokenVariant.BooleanTrue, token1.Variant);
        Assert.True(token1.IsExtendInlineLiteral);

        var token2 = DToken.MakeExtendLiteral(DTokenVariant.BooleanFalse, 0);
        Assert.Equal(DTokenType.Literal, token2.Type);
        Assert.Equal(DTokenKind.Boolean, token2.Kind);
        Assert.Equal(DTokenVariant.BooleanFalse, token2.Variant);
        Assert.True(token2.IsExtendInlineLiteral);

        var token3 = DToken.MakeExtendLiteral(DTokenVariant.Null, 0);
        Assert.Equal(DTokenType.Literal, token3.Type);
        Assert.Equal(DTokenKind.Null, token3.Kind);
        Assert.Equal(DTokenVariant.Null, token3.Variant);
        Assert.True(token3.IsExtendInlineLiteral);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(0, 1)]
    [InlineData(100, 100)]
    [InlineData(10000, 10000)]
    [InlineData(0xfffffff, 0xfffffff)]
    [InlineData(0x3fffffff, 0xfffffff)]
    public void EmptyToken(uint emptyId, uint extendId)
    {
        var token = DToken.MakeEmpty(emptyId);

        Assert.True(token.IsEmptyToken);
        Assert.False(token.IsEmptyExtend);
        Assert.Equal(DTokenType.Ignore, token.Type);
        Assert.Equal(DTokenKind.Control, token.Kind);
        Assert.True(token.IsIgnore);
        Assert.Equal(emptyId, token.EmptyId);
        Assert.True(token.IsExtended);

        var token2 = DToken.MakeEmptyExtend(emptyId, extendId);
        Assert.False(token2.IsEmptyToken);
        Assert.True(token2.IsEmptyExtend);
        Assert.Equal(DTokenType.Ignore, token2.Type);
        Assert.Equal(DTokenKind.Control, token2.Kind);
        Assert.True(token.IsIgnore);
        Assert.Equal(emptyId, token2.EmptyId);
        Assert.Equal(extendId, token2.ExtendId);
        Assert.True(token.IsExtended);

        TestContext.Current.TestOutputHelper?.WriteLine(token.ToString());
        TestContext.Current.TestOutputHelper?.WriteLine(token2.ToString());
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(1.3)]
    [InlineData(1.333333)]
    [InlineData(-0.25)]
    [InlineData(-0.23)]
    [InlineData(1234555.00000000001)]
    [InlineData(-99999999999999.99999999)]
    [InlineData(-1234555.00000000001)]
    [InlineData(123456789.987654321)]
    public void FloatLiteral(double value)
    {
        var dox = DoxSerializer.Serialize(value, SerializerSettings.Default);

        var result = DoxSerializer.Deserialize<double>(dox, SerializerSettings.Default);

        Assert.Equal(value, result);

        var payload = DToken.EncodeInlineFloatPayload(value);

        if (payload >= 0)
        {
            var token = DToken.MakeInlineFloat(payload);
            var decoded = DToken.DecodeInlineFloatPayload(token);

            Assert.Equal(result, decoded);
        }

        if (payload >= 0)
        {
            var token2 = DToken.MakeExtendInlineFloat(payload);

            Assert.Equal(DTokenType.ExtNumber, token2.Type);
            Assert.Equal(DTokenKind.InlineFloat, token2.Kind);
            Assert.True(token2.IsExtended);

            var decoed = DToken.DecodeInlineFloatPayload(token2);
            Assert.Equal(result, decoed);
        }

        var arr = new DArray();
        arr.Add(value);

        foreach (var token in arr.Document.GetTokens())
        {
            TestContext.Current.TestOutputHelper?.WriteLine(token.ToString());
        }

        Assert.Equal(value, (double)arr[0]);
    }
}