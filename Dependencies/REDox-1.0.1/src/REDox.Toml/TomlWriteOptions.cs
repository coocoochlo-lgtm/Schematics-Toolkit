// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Toml;

public readonly record struct TomlWriteOptions
{
    private const int DefaultCollapseLevel = 1;
    private const int DefaultInlineTableLevelLevel = 2;
    private readonly int? _collapseLevel;
    private readonly int? _inlineTableLevel;

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

    public int CollapseLevel
    {
        get => _collapseLevel == null ? DefaultCollapseLevel : _collapseLevel.Value;
        init => _collapseLevel = value;
    }

    public int InlineTableLevel
    {
        get => _inlineTableLevel == null ? DefaultInlineTableLevelLevel : _inlineTableLevel.Value;
        init => _inlineTableLevel = value;
    }

    public int MaxDepth { get; init; }
}