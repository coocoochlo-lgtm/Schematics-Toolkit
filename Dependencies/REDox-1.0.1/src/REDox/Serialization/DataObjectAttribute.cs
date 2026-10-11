// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public class DataObjectAttribute : DataAttribute
{
    public Type? NamingPolicyType { get; set; }
}