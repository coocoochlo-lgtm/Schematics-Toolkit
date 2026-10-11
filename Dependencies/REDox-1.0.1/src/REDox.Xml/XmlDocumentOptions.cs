// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Xml;

public readonly record struct XmlDocumentOptions
{
    public bool PreserveTrivia { get; init; }

    public int MaxDepth { get; init; }
}