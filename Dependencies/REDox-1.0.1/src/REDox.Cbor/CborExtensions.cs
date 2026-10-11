// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace REDox.Cbor;

public static class CborExtensions
{
    extension(DValue doxValue)
    {
        public static DValue ParseCbor(ReadOnlySpan<byte> cbor, SerializerSettings? settings = null,
            CborDocumentOptions options = default)
        {
            using var doc = CborDocument.Parse(cbor, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseCbor(Stream stream, SerializerSettings? settings = null,
            CborDocumentOptions options = default)
        {
            using var doc = CborDocument.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }


        public byte[] ToCbor(CborWriteOptions options = default)
        {
            if (doxValue.GetToken().IsIgnore)
            {
                return [];
            }

            return CborDocument.Encode(doxValue.AsElement(), options);
        }
    }

    extension(DElement element)
    {
        public byte[] ToCbor(CborWriteOptions options = default)
        {
            if (element.Token.IsIgnore)
            {
                return [];
            }

            return CborDocument.Encode(element, options);
        }
    }
}