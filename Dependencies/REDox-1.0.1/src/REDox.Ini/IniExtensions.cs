// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace REDox.Ini;

public static class IniExtensions
{
    extension(DValue doxValue)
    {
        public static DValue ParseIni(string ini, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(ini, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseIni(ReadOnlySpan<byte> ini, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(ini, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseIni(Stream stream, SerializerSettings? settings = null,
            IniDocumentOptions options = default)
        {
            using var doc = IniDocument.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public string ToIni(IniWriteOptions options = default)
        {
            if (doxValue.GetToken().IsIgnore)
            {
                return string.Empty;
            }

            return IniDocument.EncodeToString(doxValue.AsElement(), options);
        }
    }

    extension(DElement element)
    {
        public string ToIni(IniWriteOptions options = default)
        {
            if (element.Token.IsIgnore)
            {
                return string.Empty;
            }

            return IniDocument.EncodeToString(element, options);
        }
    }
}