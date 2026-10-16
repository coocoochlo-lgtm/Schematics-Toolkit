// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[Flags]
public enum FloatFormatHandling
{
    SpecialFloatAsString = 0,
    SpecialFloatAsSymbol = 1,
    SpecialFloatAsXmlSymbol = 2,
    SpecialFloatAsDefaultValue = 3,
    SpecialFloatMask = 7,

    AlwaysIncludeDecimal = 8
}