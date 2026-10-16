// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Ini;

public readonly record struct IniWriteOptions
{
    private readonly Utf8TextWriteOptions _textWriteOptions;
    public bool PreserveTrivia { get; init; }

    public bool WriteSpaces { get; init; }

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

    internal Utf8TextWriteOptions TextWriteOptions => _textWriteOptions;
}