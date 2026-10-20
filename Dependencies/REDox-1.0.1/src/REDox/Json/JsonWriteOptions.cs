// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Json;

public readonly record struct JsonWriteOptions
{
    private readonly int _maxDepth;
    private readonly Utf8TextWriteOptions _textWriteOptions;

    public bool UseNewlineDelimitedFormat { get; init; }

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

    internal byte[] Utf8NewLine => _textWriteOptions.Utf8NewLine;
}