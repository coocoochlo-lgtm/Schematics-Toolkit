// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox.Serialization;

[AttributeUsage(AttributeTargets.Constructor)]
public class DataConstructorAttribute : DataAttribute
{
}