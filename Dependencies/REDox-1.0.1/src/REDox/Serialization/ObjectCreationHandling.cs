// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum ObjectCreationHandling
{
    Replace = 0,
    ReuseArray = 1,
    ReuseObject = 2,
    ReuseStruct = 4,
    Reuse = 7,
    WhenReadOnly = 8
}