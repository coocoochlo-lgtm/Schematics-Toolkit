// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum NumberHandling
{
    Strict = 0,
    AllowReadingFromString = 1,
    WriteAsString = 2,
    AllowNamedFloatingPointLiterals = 4
}