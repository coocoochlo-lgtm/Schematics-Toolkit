// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Cbor;

enum CborMajorType
{
    PlusInteger,
    MinusInteger,
    Binary,
    String,
    Array,
    Map,
    Tag,
    Other
}