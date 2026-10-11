// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;

namespace REDox.Serialization;

public sealed class Utf8Symbol
{
    public Utf8Symbol(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        Symbol = string.Intern(symbol);
        Utf8Bytes = Utf8Helper.GetUtf8String(symbol);
        EscapeMask = TextEncoderPolicy.GetEscapeMask(symbol);
    }

    internal string Symbol { get; }

    internal byte[] Utf8Bytes { get; }

    public TextEscapeMask EscapeMask { get; }

    public int ByteLength
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => AsSpan().Length;
    }

    public static implicit operator ReadOnlySpan<byte>(Utf8Symbol value)
    {
        return value.Utf8Bytes;
    }

    public static explicit operator string(Utf8Symbol value)
    {
        return value.Symbol;
    }

    public override string ToString()
    {
        return Symbol;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> AsSpan()
    {
        return Utf8Bytes;
    }
}