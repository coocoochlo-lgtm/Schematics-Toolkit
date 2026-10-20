// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Toml;

public readonly record struct TomlDocumentOptions
{
    public bool PreserveTrivia { get; init; }

    public bool EnableValueValidation { get; init; }

    public bool AutoDetectEncoding { get; init; }

    public int MaxDepth { get; init; }
}