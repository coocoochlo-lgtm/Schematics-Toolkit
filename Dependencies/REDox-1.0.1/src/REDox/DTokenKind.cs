// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public enum DTokenKind
{
    // 000x: ignore / control or metadata
    Control = 0b0000,
    Trivia = 0b0001,

    // 001x: literal primitive
    Boolean = 0b0010,
    Null = 0b0011,

    // 010x: number
    Integer = 0b0100,
    Float = 0b0101,

    // 011x: extended number
    BigNumber = 0b0110,
    InlineFloat = 0b0111,

    // 100x: text
    String = 0b1000,
    Symbol = 0b1001,

    // 101x: binary
    ByteString = 0b1010,
    Timestamp = 0b1011

    // 11x_: container
}

public enum TriviaKind
{
    Default,
    Whitespace,
    BlockComment,
    LineComment,
    Separator,
    Tag,
    Style,
    Inherit = 7
}

// 0b0010
public enum BooleanKind
{
    False,
    True
}

// 0b0011
public enum NullKind
{
    Default,
    Undefined
}

// 0b0100
public enum IntegerKind
{
    Default,
    Hexadecimal,
    Octal,
    Binary,
    Unsigned,
    Inherit = 7
}

// 0b0101
public enum FloatKind
{
    Default,
    Half,
    Single,
    Decimal,
    Inherit = 7
}

// 0b0110
public enum BigNumberKind
{
    Default,
    Int128,
    UInt128,
    Integer,
    Decimal,
    Float,
    Hexadecimal,
    Inherit = 7
}

// 0b1000
public enum StringKind
{
    Default,
    Literal,
    DoubleQuote,
    SingleQuote,
    MultilineDoubleQuote,
    MultilineSingleQuote,
    Inherit = 7
}

// 0b1001
public enum SymbolKind
{
    Default = 0,
    Identifier = 1,
    Reference = 2,
    TypeName = 3,
    Metadata = 4,
    Inherit = 7
}

// 0b1010
public enum ByteStringKind
{
    Default,
    Base64,
    Base64Url,
    Base16,
    Guid,
    Raw,
    Inherit = 7
}

// 0b1011
public enum TimestampKind
{
    Default,
    OffsetDateTime,
    LocalDateTime,
    LocalDate,
    LocalTime,
    Inherit = 7
}