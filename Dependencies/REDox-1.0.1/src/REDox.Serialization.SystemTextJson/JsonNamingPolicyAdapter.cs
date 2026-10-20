// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.Json;

namespace REDox.Serialization.SystemTextJson;

public sealed class JsonNamingPolicyAdapter : NamingPolicy
{
    public JsonNamingPolicyAdapter(JsonNamingPolicy policy)
    {
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    public JsonNamingPolicy Policy { get; }

    public override string ConvertName(string name)
    {
        return Policy.ConvertName(name);
    }
}