// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum ConstructorHandling
{
    Default = 0,
    IgnoreStructDefaultConstructor = 1,
    AllowNonPublicDefaultConstructor = 2
}