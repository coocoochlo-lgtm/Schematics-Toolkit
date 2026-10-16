// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace REDox.MessagePack;

public static class MessagePackExtensions
{
    extension(DValue doxValue)
    {
        public static DValue ParseMessagePack(ReadOnlySpan<byte> messagePack, SerializerSettings? settings = null,
            MessagePackDocumentOptions options = default)
        {
            using var doc = MessagePackDocument.Parse(messagePack, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseMessagePack(Stream stream, SerializerSettings? settings = null,
            MessagePackDocumentOptions options = default)
        {
            using var doc = MessagePackDocument.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }


        public byte[] ToMessagePack(MessagePackWriteOptions options = default)
        {
            if (doxValue.GetToken().IsIgnore)
            {
                return [];
            }

            return MessagePackDocument.Encode(doxValue.AsElement(), options);
        }
    }

    extension(DElement element)
    {
        public byte[] ToMessagePack(MessagePackWriteOptions options = default)
        {
            if (element.Token.IsIgnore)
            {
                return [];
            }

            return MessagePackDocument.Encode(element, options);
        }
    }
}