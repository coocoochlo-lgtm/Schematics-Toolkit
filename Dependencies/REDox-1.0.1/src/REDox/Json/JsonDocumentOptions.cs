// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Json;

public readonly record struct JsonDocumentOptions
{
    public bool UseNewlineDelimitedFormat { get; init; }

    public bool AllowTrailingCommas { get; init; }

    public bool EnableValueValidation { get; init; }

    public int MaxDepth { get; init; }
}