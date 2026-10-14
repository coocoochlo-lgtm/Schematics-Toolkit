// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Xml;

public readonly record struct XmlWriteOptions
{
    private readonly Utf8TextWriteOptions _textWriteOptions;

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

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;

    public bool PreserveTrivia { get; init; }

    public int MaxDepth { get; init; }
}