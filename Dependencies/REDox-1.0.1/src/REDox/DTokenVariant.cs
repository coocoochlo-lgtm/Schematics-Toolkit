// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public enum DTokenVariant
{
    Undefined = 0,

    Trivia = (DTokenKind.Trivia << 3) | TriviaKind.Default,
    TriviaWhitespace = (DTokenKind.Trivia << 3) | TriviaKind.Whitespace,
    TriviaBlockComment = (DTokenKind.Trivia << 3) | TriviaKind.BlockComment,
    TriviaLineComment = (DTokenKind.Trivia << 3) | TriviaKind.LineComment,
    TriviaSeparator = (DTokenKind.Trivia << 3) | TriviaKind.Separator,
    TriviaTag = (DTokenKind.Trivia << 3) | TriviaKind.Tag,
    TriviaStyle = (DTokenKind.Trivia << 3) | TriviaKind.Style,

    BooleanFalse = (DTokenKind.Boolean << 3) | BooleanKind.False,
    BooleanTrue = (DTokenKind.Boolean << 3) | BooleanKind.True,

    Null = (DTokenKind.Null << 3) | NullKind.Default,
    NullUndefined = (DTokenKind.Null << 3) | NullKind.Undefined,

    String = (DTokenKind.String << 3) | StringKind.Default,
    StringLiteral = (DTokenKind.String << 3) | StringKind.Literal,
    StringDoubleQuote = (DTokenKind.String << 3) | StringKind.DoubleQuote,
    StringSingleQuote = (DTokenKind.String << 3) | StringKind.SingleQuote,
    StringMultilineDoubleQuote = (DTokenKind.String << 3) | StringKind.MultilineDoubleQuote,
    StringMultilineSingleQuote = (DTokenKind.String << 3) | StringKind.MultilineSingleQuote,

    Symbol = (DTokenKind.Symbol << 3) | SymbolKind.Default,
    SymbolIdentifier = (DTokenKind.Symbol << 3) | SymbolKind.Identifier,
    SymbolReference = (DTokenKind.Symbol << 3) | SymbolKind.Reference,
    SymbolTypeName = (DTokenKind.Symbol << 3) | SymbolKind.TypeName,
    SymbolMetadata = (DTokenKind.Symbol << 3) | SymbolKind.Metadata,

    Float = (DTokenKind.Float << 3) | FloatKind.Default,
    FloatHalf = (DTokenKind.Float << 3) | FloatKind.Half,
    FloatSingle = (DTokenKind.Float << 3) | FloatKind.Single,
    FloatDecimal = (DTokenKind.Float << 3) | FloatKind.Decimal,

    Integer = (DTokenKind.Integer << 3) | IntegerKind.Default,
    IntegerHexadecimal = (DTokenKind.Integer << 3) | IntegerKind.Hexadecimal,
    IntegerOctal = (DTokenKind.Integer << 3) | IntegerKind.Octal,
    IntegerBinary = (DTokenKind.Integer << 3) | IntegerKind.Binary,
    IntegerUnsigned = (DTokenKind.Integer << 3) | IntegerKind.Unsigned,

    BigNumber = (DTokenKind.BigNumber << 3) | BigNumberKind.Default,
    BigNumberInteger = (DTokenKind.BigNumber << 3) | BigNumberKind.Integer,
    BigNumberDecimal = (DTokenKind.BigNumber << 3) | BigNumberKind.Decimal,
    BigNumberFloat = (DTokenKind.BigNumber << 3) | BigNumberKind.Float,
    BigNumberInt128 = (DTokenKind.BigNumber << 3) | BigNumberKind.Int128,
    BigNumberUInt128 = (DTokenKind.BigNumber << 3) | BigNumberKind.UInt128,
    BigNumberHexadecimal = (DTokenKind.BigNumber << 3) | BigNumberKind.Hexadecimal,

    ByteString = (DTokenKind.ByteString << 3) | ByteStringKind.Default,
    ByteStringBase64 = (DTokenKind.ByteString << 3) | ByteStringKind.Base64,
    ByteStringBase64Url = (DTokenKind.ByteString << 3) | ByteStringKind.Base64Url,
    ByteStringBase16 = (DTokenKind.ByteString << 3) | ByteStringKind.Base16,
    ByteStringGuid = (DTokenKind.ByteString << 3) | ByteStringKind.Guid,
    ByteStringRaw = (DTokenKind.ByteString << 3) | ByteStringKind.Raw,

    Timestamp = (DTokenKind.Timestamp << 3) | TimestampKind.Default,
    TimestampOffsetDateTime = (DTokenKind.Timestamp << 3) | TimestampKind.OffsetDateTime,
    TimestampLocalDateTime = (DTokenKind.Timestamp << 3) | TimestampKind.LocalDateTime,
    TimestampLocalDate = (DTokenKind.Timestamp << 3) | TimestampKind.LocalDate,
    TimestampLocalTime = (DTokenKind.Timestamp << 3) | TimestampKind.LocalTime
}