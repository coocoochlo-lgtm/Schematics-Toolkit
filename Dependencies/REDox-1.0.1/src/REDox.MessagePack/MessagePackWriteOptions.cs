// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.MessagePack;

public readonly record struct MessagePackWriteOptions
{
    public int MaxDepth { get; init; }

    public bool OldSpec { get; init; }

    public bool PreserveExtension { get; init; }
}