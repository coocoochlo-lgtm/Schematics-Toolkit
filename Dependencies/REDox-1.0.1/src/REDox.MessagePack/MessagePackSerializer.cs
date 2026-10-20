// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;

namespace REDox.MessagePack;

public sealed class MessagePackSerializer : Serializer
{
    public static TValue? Deserialize<TValue>(Stream stream, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

        try
        {
            return Deserialize<TValue>(buffer.AsMemory(0, len), settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue? Deserialize<TValue>(ReadOnlyMemory<byte> bytes, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackDocument>.Get(() =>
                   new MessagePackDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeInternal<TValue>(doc.RootElement);
        }
    }

    public static object? Deserialize(ReadOnlyMemory<byte> bytes, Type returnType, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackDocument>.Get(() =>
                   new MessagePackDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeInternal(doc.RootElement, returnType);
        }
    }


    public static TValue DeserializeTo<TValue>(Stream stream, TValue target, SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(target);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

        try
        {
            return DeserializeTo<TValue>(buffer.AsMemory(0, len), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue DeserializeTo<TValue>(ReadOnlyMemory<byte> bytes, TValue target,
        SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackDocument>.Get(() =>
                   new MessagePackDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeToInternal<TValue>(doc.RootElement, target);
        }
    }

    public static object DeserializeTo(ReadOnlyMemory<byte> bytes, Type returnType, object target,
        SerializerSettings? settings = null,
        MessagePackDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(target);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackDocument>.Get(() =>
                   new MessagePackDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings, options);

            return DeserializeToInternal(doc.RootElement, returnType, target);
        }
    }

    public static void Serialize<TValue>(Stream stream, TValue value, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            writer.WriteValue(value);
            writer.Dispose();
        }
    }

    public static void Serialize<TValue>(IBufferWriter<byte> bufferWriter, TValue value,
        SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            writer.WriteValue(value);
            writer.Dispose();
        }
    }

    public static byte[] Serialize<TValue>(TValue value, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            writer.WriteValue(value);
            return writer.Encode();
        }
    }

    public static void Serialize(Stream stream, object? value, Type inputType, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            writer.Dispose();
        }
    }

    public static void Serialize(IBufferWriter<byte> bufferWriter, object? value, Type inputType,
        SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            writer.Dispose();
        }
    }

    public static byte[] Serialize(object? value, Type inputType, SerializerSettings? settings = null,
        MessagePackWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<MessagePackWriter>.Get(() => new MessagePackWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            settings.GetConverter(inputType).WriteObject(writer, inputType, value);
            return writer.Encode();
        }
    }
}