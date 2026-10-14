// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;

namespace REDox.Json;

public static class JsonExtensions
{
    private static JsonValueKind GetJsonValueKind(DToken token)
    {
        var kind = token.Kind;

        if (kind.IsStringEncoded)
        {
            return JsonValueKind.String;
        }

        if (kind.IsNumeric)
        {
            return JsonValueKind.Number;
        }

        if (token.Type == DTokenType.Array)
        {
            return JsonValueKind.Array;
        }

        if (token.Type == DTokenType.Map)
        {
            return JsonValueKind.Object;
        }

        if (kind == DTokenKind.Null)
        {
            return JsonValueKind.Null;
        }

        if (kind == DTokenKind.Boolean)
        {
            return token.Variant == DTokenVariant.BooleanTrue ? JsonValueKind.True : JsonValueKind.False;
        }

        return JsonValueKind.Undefined;
    }

    extension(DElement element)
    {
        public JsonValueKind ValueKind => GetJsonValueKind(element.Token);

        public string ToJsonString(JsonWriteOptions options = default)
        {
            if (!element.IsValid)
            {
                return string.Empty;
            }

            return JsonDocument.EncodeToString(element, options);
        }
    }

    extension(DValue doxValue)
    {
        public static DValue ParseJson(string json, SerializerSettings? settings = null,
            JsonDocumentOptions options = default)
        {
            using var doc = JsonDocument.Parse(json, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseJson(ReadOnlySpan<byte> utf8Json, SerializerSettings? settings = null,
            JsonDocumentOptions options = default)
        {
            using var doc = JsonDocument.Parse(utf8Json, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseJson(Stream stream, SerializerSettings? settings = null,
            JsonDocumentOptions options = default)
        {
            using var doc = JsonDocument.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseJson5(string json5, SerializerSettings? settings = null,
            Json5DocumentOptions options = default)
        {
            using var doc = Json5Document.Parse(json5, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseJson5(ReadOnlySpan<byte> json5, SerializerSettings? settings = null,
            Json5DocumentOptions options = default)
        {
            using var doc = Json5Document.Parse(json5, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public static DValue ParseJson5(Stream stream, SerializerSettings? settings = null,
            Json5DocumentOptions options = default)
        {
            using var doc = Json5Document.Parse(stream, settings, options);

            return doc.Duplicate().RootElement.AsValue();
        }

        public JsonValueKind GetValueKind()
        {
            return GetJsonValueKind(doxValue.GetToken());
        }

        public string ToJsonString(JsonWriteOptions options = default)
        {
            if (doxValue.GetToken().Kind == DTokenKind.Control)
            {
                return string.Empty;
            }

            return doxValue.AsElement().ToJsonString(options);
        }
    }

    extension(DContainer container)
    {
        public JsonValueKind GetValueKind()
        {
            return GetJsonValueKind(container.AsElement().Token);
        }

        public string ToJsonString(JsonWriteOptions options = default)
        {
            return container.AsElement().ToJsonString(options);
        }
    }
}