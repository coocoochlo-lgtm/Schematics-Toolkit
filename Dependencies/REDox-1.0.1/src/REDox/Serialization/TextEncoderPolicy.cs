// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;

namespace REDox.Serialization;

public sealed class TextEncoderPolicy
{
    private const int BmpBitOffset = 47;

    private const string AsciiPunctuation =
        "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";

    private static readonly TextEscapeMask[] s_ascii =
        CreateAsciiTable();


    private static int _rootConverterId;

    public static readonly TextEncoderPolicy Minimum = new(TextEscapeMask.None);

    private ITextEncoder?[] _encoderTable = new ITextEncoder[16];

    public TextEncoderPolicy(TextEscapeMask escapeMask)
    {
        EscapeMask = escapeMask;
        EscapeRanges = ReadOnlyMemory<TextEscapeRange>.Empty;
    }

    public TextEncoderPolicy(TextEscapeRange[] escapeRanges, TextEscapeMask escapeMask = TextEscapeMask.None)
    {
        foreach (var range in escapeRanges)
        {
            for (var i = 0; i < range.Length; i++)
            {
                escapeMask |= GetEscapeMask((char)(range.FirstCodePoint + i));
            }
        }

        EscapeMask = escapeMask;
        EscapeRanges = (TextEscapeRange[])escapeRanges.Clone();
    }

    public TextEscapeMask EscapeMask { get; }

    public TextEscapeMask UnicodeEscapes { get; init; }

    public bool UpperCaseHexEscapes { get; init; }

    public ReadOnlyMemory<TextEscapeRange> EscapeRanges { get; }

    public static TextEscapeMask GetEscapeMask(string text)
    {
        var mask = TextEscapeMask.None;
        foreach (var c in text)
        {
            mask |= GetEscapeMask(c);
        }

        return mask;
    }

    public static TextEscapeMask GetEscapeMask(char ch)
    {
        if (ch < 0x80)
        {
            return s_ascii[ch];
        }

        if (ch < 0xD800 || ch > 0xDFFF)
        {
            return (TextEscapeMask)(
                1UL << (BmpBitOffset + (ch >> 12)));
        }

        return TextEscapeMask.NonBmp;
    }

    private static TextEscapeMask[] CreateAsciiTable()
    {
        var table = new TextEscapeMask[128];

        for (var i = 0x00; i <= 0x1F; ++i)
        {
            table[i] = TextEscapeMask.OtherC0;
        }

        table[0x00] = TextEscapeMask.Nul;
        table[0x07] = TextEscapeMask.Bell;
        table[0x08] = TextEscapeMask.Backspace;
        table[0x09] = TextEscapeMask.HorizontalTab;
        table[0x0A] = TextEscapeMask.LineFeed;
        table[0x0B] = TextEscapeMask.VerticalTab;
        table[0x0C] = TextEscapeMask.FormFeed;
        table[0x0D] = TextEscapeMask.CarriageReturn;
        table[0x1B] = TextEscapeMask.Escape;
        table[0x7F] = TextEscapeMask.Delete;
        table[' '] = TextEscapeMask.Space;

        for (int c = '0'; c <= '9'; ++c)
        {
            table[c] = TextEscapeMask.Digit;
        }

        for (int c = 'A'; c <= 'Z'; ++c)
        {
            table[c] = TextEscapeMask.Upper;
        }

        for (int c = 'a'; c <= 'z'; ++c)
        {
            table[c] = TextEscapeMask.Lower;
        }

        for (var i = 0; i < AsciiPunctuation.Length; ++i)
        {
            int ch = AsciiPunctuation[i];

            table[ch] = (TextEscapeMask)(1UL << i);
        }

        return table;
    }

    public T GetEncoder<T>() where T : class, ITextEncoder<T>
    {
        var id = RootEncoder<T>.Id;

        var table = _encoderTable;

        if ((uint)id < (uint)table.Length)
        {
            var encoder = table[id];

            if (encoder != null)
            {
                return (T)encoder;
            }
        }

        if (id >= table.Length)
        {
            lock (_encoderTable)
            {
                table = _encoderTable;

                if (id >= table.Length)
                {
                    Array.Resize(
                        ref _encoderTable,
                        Math.Max(table.Length * 2, id + 1)
                    );
                }
            }
        }

        {
            var encoder = T.Create(this);

            _encoderTable[id] = encoder;

            return encoder;
        }
    }

    private static class RootEncoder<T> where T : ITextEncoder
    {
        public static readonly int Id;

        static RootEncoder()
        {
            Id = Interlocked.Increment(ref _rootConverterId) - 1;
        }
    }
}