// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.MessagePack;

enum MessagePackCode : byte
{
    FixMap = 0x80,
    FixArray = 0x90,
    FixStr = 0xa0,
    Nil = 0xc0,
    NeverUsed = 0xc1,
    False = 0xc2,
    True = 0xc3,
    Bin8 = 0xc4,
    Bin16 = 0xc5,
    Bin32 = 0xc6,
    Ext8 = 0xc7,
    Ext16 = 0xc8,
    Ext32 = 0xc9,
    Float32 = 0xca,
    Float64 = 0xcb,
    Uint8 = 0xcc,
    Uint16 = 0xcd,
    Uint32 = 0xce,
    Uint64 = 0xcf,
    Int8 = 0xd0,
    Int16 = 0xd1,
    Int32 = 0xd2,
    Int64 = 0xd3,
    FixExt1 = 0xd4,
    FixExt2 = 0xd5,
    FixExt4 = 0xd6,
    FixExt8 = 0xd7,
    FixExt16 = 0xd8,
    Str8 = 0xd9,
    Str16 = 0xda,
    Str32 = 0xdb,
    Array16 = 0xdc,
    Array32 = 0xdd,
    Map16 = 0xde,
    Map32 = 0xdf
}