// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox;

public enum DTokenType
{
    //000 : ignore
    Ignore = 0b000,

    //001 : literal primitive
    Literal = 0b001,

    //01x : numeric
    Number = 0b010,
    ExtNumber = 0b011,

    //10x : text / binary
    Text = 0b100,
    Binary = 0b101,

    //11x : container
    Map = 0b110,
    Array = 0b111
}