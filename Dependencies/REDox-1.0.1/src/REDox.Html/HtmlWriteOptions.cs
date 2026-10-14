// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Html;

public readonly record struct HtmlWriteOptions
{
    public bool WriteBom { get; init; }

    public bool PreserveTrivia { get; init; }
}