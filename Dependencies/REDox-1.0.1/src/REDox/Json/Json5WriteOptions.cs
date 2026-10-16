// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Json;

public readonly record struct Json5WriteOptions
{
    private readonly int _maxDepth;
    private readonly byte _propertyNameStyle;
    private readonly byte _stringStyle;
    private readonly Utf8TextWriteOptions _textWriteOptions;

    public Json5QuoteStyle PropertyNameStyle
    {
        get => (Json5QuoteStyle)_propertyNameStyle;
        init => _propertyNameStyle = (byte)value;
    }

    public Json5QuoteStyle StringStyle
    {
        get => (Json5QuoteStyle)_stringStyle;
        init => _stringStyle = (byte)value;
    }

    public bool PreserveTrivia { get; init; }

    public bool WriteBom
    {
        get => _textWriteOptions.WriteBom;
        init => _textWriteOptions = _textWriteOptions with { WriteBom = value };
    }

    public bool WriteIndented
    {
        get => _textWriteOptions.WriteIndented;
        init => _textWriteOptions = _textWriteOptions with { WriteIndented = value };
    }

    public char IndentCharacter
    {
        get => _textWriteOptions.IndentCharacter;
        init => _textWriteOptions = _textWriteOptions with { IndentCharacter = value };
    }

    public string NewLine
    {
        get => _textWriteOptions.NewLine;
        init => _textWriteOptions = _textWriteOptions with { NewLine = value };
    }

    public int IndentSize
    {
        get => _textWriteOptions.IndentSize;
        init => _textWriteOptions = _textWriteOptions with { IndentSize = value };
    }

    public int MaxDepth
    {
        get => _maxDepth;
        init
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "MaxDepth must be zero or positive.");
            }

            _maxDepth = value;
        }
    }

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;
}