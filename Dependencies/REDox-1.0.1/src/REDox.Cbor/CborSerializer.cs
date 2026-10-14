// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;

namespace REDox.Cbor;

public sealed class CborSerializer : Serializer
{
    public static TValue? Deserialize<TValue>(ReadOnlyMemory<byte> bytes, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborDocument>.Get(() =>
                   new CborDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeInternal<TValue>(doc.RootElement);
        }
    }

    public static object? Deserialize(ReadOnlyMemory<byte> bytes, Type returnType, SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborDocument>.Get(() =>
                   new CborDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeInternal(doc.RootElement, returnType);
        }
    }

    public static TValue DeserializeTo<TValue>(ReadOnlyMemory<byte> bytes, TValue target,
        SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborDocument>.Get(() =>
                   new CborDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeToInternal<TValue>(doc.RootElement, target);
        }
    }

    public static object DeserializeTo(ReadOnlyMemory<byte> bytes, Type returnType, object target,
        SerializerSettings? settings = null,
        CborDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborDocument>.Get(() =>
                   new CborDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeToInternal(doc.RootElement, returnType, target);
        }
    }

    public static void Serialize<TValue>(Stream stream, TValue value, SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            writer.WriteValue(value);
            writer.Dispose();
        }
    }

    public static void Serialize<TValue>(IBufferWriter<byte> bufferWriter, TValue value,
        SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            writer.WriteValue(value);
            writer.Dispose();
        }
    }

    public static byte[] Serialize<TValue>(TValue value, SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            writer.WriteValue(value);
            return writer.Encode();
        }
    }

    public static void Serialize(Stream stream, object? value, Type inputType, SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            writer.Dispose();
        }
    }

    public static void Serialize(IBufferWriter<byte> bufferWriter, object? value, Type inputType,
        SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            writer.Dispose();
        }
    }

    public static byte[] Serialize(object? value, Type inputType, SerializerSettings? settings = null,
        CborWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<CborWriter>.Get(() => new CborWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            return writer.Encode();
        }
    }
}