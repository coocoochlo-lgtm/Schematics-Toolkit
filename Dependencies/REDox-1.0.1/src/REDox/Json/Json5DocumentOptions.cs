// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Json;

public readonly record struct Json5DocumentOptions
{
    public bool EnableValueValidation { get; init; }

    public bool PreserveTrivia { get; init; }

    public int MaxDepth { get; init; }
}