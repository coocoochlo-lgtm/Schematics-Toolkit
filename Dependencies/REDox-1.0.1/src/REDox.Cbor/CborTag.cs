// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

namespace REDox.Cbor;

public enum CborTag
{
    Invalid = -1,
    DateTimeString = 0,
    UnixTimeSeconds = 1,
    UnsignedBigNum = 2,
    NegativeBigNum = 3,
    DecimalFraction = 4,
    BigFloat = 5,

    Base64UrlLaterEncoding = 21,
    Base64StringLaterEncoding = 22,
    Base16StringLaterEncoding = 23,
    EncodedCborDataItem = 24,

    Uri = 32,
    Base64Url = 33,
    Base64 = 34,
    Regex = 35,
    MimeMessage = 36,
    Uuid = 37,
    SelfDescribeCbor = 55799
}