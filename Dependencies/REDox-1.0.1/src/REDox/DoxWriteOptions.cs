// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public readonly record struct DoxWriteOptions
{
    public bool PreserveTrivia { get; init; }
}