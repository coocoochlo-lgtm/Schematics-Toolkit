// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Serialization.Metadata;

public readonly struct TypeDiscriminator
{
    public TypeDiscriminator(Utf8Symbol symbol)
    {
        Id = 0;
        Symbol = symbol;
    }

    public TypeDiscriminator(int id)
    {
        Id = id;
        Symbol = null;
    }

    public Utf8Symbol? Symbol { get; }

    public int Id { get; }
}