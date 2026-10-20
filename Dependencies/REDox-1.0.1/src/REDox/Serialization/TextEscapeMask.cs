// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum TextEscapeMask : ulong
{
    None = 0,

    // ASCII punctuation
    // bit 0-31
    Exclamation = 1UL << 0, // !
    DoubleQuote = 1UL << 1, // "
    Hash = 1UL << 2, // #
    Dollar = 1UL << 3, // $
    Percent = 1UL << 4, // %
    Ampersand = 1UL << 5, // &
    SingleQuote = 1UL << 6, // '
    OpenParen = 1UL << 7, // (
    CloseParen = 1UL << 8, // )
    Asterisk = 1UL << 9, // *
    Plus = 1UL << 10, // +
    Comma = 1UL << 11, // ,
    Hyphen = 1UL << 12, // -
    Period = 1UL << 13, // .
    Slash = 1UL << 14, // /

    Colon = 1UL << 15, // :
    Semicolon = 1UL << 16, // ;
    LessThan = 1UL << 17, // <
    Equals = 1UL << 18, // =
    GreaterThan = 1UL << 19, // >
    Question = 1UL << 20, // ?
    At = 1UL << 21, // @

    OpenBracket = 1UL << 22, // [
    Backslash = 1UL << 23, // \
    CloseBracket = 1UL << 24, // ]
    Caret = 1UL << 25, // ^
    Underscore = 1UL << 26, // _
    Backtick = 1UL << 27, // `

    OpenBrace = 1UL << 28, // {
    Pipe = 1UL << 29, // |
    CloseBrace = 1UL << 30, // }
    Tilde = 1UL << 31, // ~


    // ASCII classes
    // bit 32-35
    Space = 1UL << 32, // U+0020
    Digit = 1UL << 33, // 0-9
    Upper = 1UL << 34, // A-Z
    Lower = 1UL << 35, // a-z


    // ASCII controls
    // bit 36-46
    Nul = 1UL << 36, // U+0000
    Bell = 1UL << 37, // U+0007
    Backspace = 1UL << 38, // U+0008
    HorizontalTab = 1UL << 39, // U+0009
    LineFeed = 1UL << 40, // U+000A
    VerticalTab = 1UL << 41, // U+000B
    FormFeed = 1UL << 42, // U+000C
    CarriageReturn = 1UL << 43, // U+000D
    Escape = 1UL << 44, // U+001B

    // Other U+0001-U+001F
    OtherC0 = 1UL << 45,
    Delete = 1UL << 46, // U+007F

    // Non-ASCII BMP
    // bit 47-62

    // Supplementary planes
    // bit 63
    NonBmp = 1UL << 63, // U+10000-U+10FFFF

    // Composite masks
    AsciiPunctuation = 0x0000_0000_FFFF_FFFFUL,

    Letters =
        Upper |
        Lower,

    C0Controls =
        Nul |
        Bell |
        Backspace |
        HorizontalTab |
        LineFeed |
        VerticalTab |
        FormFeed |
        CarriageReturn |
        Escape |
        OtherC0,

    AsciiControls =
        C0Controls |
        Delete,

    // bit 0-46
    AllAscii =
        0x0000_7FFF_FFFF_FFFFUL,

    // bit 47-62
    AllNonAsciiBmp =
        0x7FFF_8000_0000_0000UL,

    // bit 47-63
    AllNonAscii =
        0xFFFF_8000_0000_0000UL,

    All = ulong.MaxValue
}