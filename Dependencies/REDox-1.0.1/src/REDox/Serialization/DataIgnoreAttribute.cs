// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Enum | AttributeTargets.Field |
                AttributeTargets.Interface |
                AttributeTargets.Property | AttributeTargets.Struct)]
public class DataIgnoreAttribute : DataAttribute
{
}