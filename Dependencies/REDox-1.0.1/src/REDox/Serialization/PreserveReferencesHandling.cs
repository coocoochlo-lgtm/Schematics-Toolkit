// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum PreserveReferencesHandling
{
    None,
    Objects = 1,
    Structs = 2,
    Arrays = 4,
    Collections = 8,
    All = 15,

    IgnoreReadOnly = 32
}