// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Ini;

public readonly record struct IniDocumentOptions
{
    public bool PreserveTrivia { get; init; }

    public bool AllowInlineComments { get; init; }

    public bool AllowDuplicateKeys { get; init; }
}