// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum DefaultValueHandling
{
    Include = 0,
    Ignore = 1,
    Populate = 2,
    IgnoreAndPopulate = 3,

    // Keeps the current member value when the deserialized value equals the default value.
    IgnoreRead = 4
}