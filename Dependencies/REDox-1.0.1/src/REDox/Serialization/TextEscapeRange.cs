// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

public readonly struct TextEscapeRange : IEquatable<TextEscapeRange>
{
    public TextEscapeRange(int firstCodePoint, int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(firstCodePoint);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstCodePoint, 0xFFFF);
        ArgumentOutOfRangeException.ThrowIfNegative(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, 0x10000 - firstCodePoint);

        FirstCodePoint = firstCodePoint;
        Length = length;
    }

    public int FirstCodePoint { get; }

    public int Length { get; }

    public static TextEscapeRange Create(char firstCharacter, char lastCharacter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lastCharacter, firstCharacter);

        return new TextEscapeRange(firstCharacter, 1 + lastCharacter - firstCharacter);
    }

    public bool Equals(TextEscapeRange other)
    {
        return FirstCodePoint == other.FirstCodePoint && Length == other.Length;
    }

    public override bool Equals(object? obj)
    {
        return obj is TextEscapeRange other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(FirstCodePoint, Length);
    }

    public static bool operator ==(TextEscapeRange left, TextEscapeRange right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(TextEscapeRange left, TextEscapeRange right)
    {
        return !left.Equals(right);
    }
}