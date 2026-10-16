// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Csv;

public readonly record struct CsvWriteOptions
{
    private readonly Utf8TextWriteOptions _textWriteOptions;

    public bool WriteBom
    {
        get => _textWriteOptions.WriteBom;
        init => _textWriteOptions = _textWriteOptions with { WriteBom = value };
    }

    public string NewLine
    {
        get => _textWriteOptions.NewLine;
        init => _textWriteOptions = _textWriteOptions with { NewLine = value };
    }

    public bool IncludeHeaderInFirstRow { get; init; }

    public char SeparatorChar { get; init; }

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;
}