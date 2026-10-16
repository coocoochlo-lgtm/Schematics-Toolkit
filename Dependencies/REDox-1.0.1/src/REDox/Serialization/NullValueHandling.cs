// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum NullValueHandling
{
    Include = 0,
    IgnoreWrite = 1,
    IgnoreRead = 2,
    Ignore = 3
}