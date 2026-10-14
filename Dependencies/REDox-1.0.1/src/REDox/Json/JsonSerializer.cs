// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.IO;
using System.Text;

namespace REDox.Json;

public sealed class JsonSerializer : Serializer
{
    public static TValue? Deserialize<TValue>(Stream stream, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = Helper.ReadStream(stream, out var len, (settings ?? SerializerSettings.Default).DefaultBufferSize);

        try
        {
            return Deserialize<TValue>(buffer.AsMemory(0, len), settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue? Deserialize<TValue>(string json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        return Deserialize<TValue>(json.AsSpan(), settings, options);
    }

    public static TValue? Deserialize<TValue>(ReadOnlySpan<char> json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var maxBufferSize = Encoding.UTF8.GetMaxByteCount(json.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxBufferSize);

        try
        {
            var len = Encoding.UTF8.GetBytes(json, buffer.AsSpan());
            return Deserialize<TValue>(buffer.AsMemory(0, len), settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue? Deserialize<TValue>(byte[] utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        return Deserialize<TValue>(utf8Json.AsMemory(), settings, options);
    }

    public static TValue? Deserialize<TValue>(ReadOnlySpan<byte> utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        try
        {
            utf8Json.CopyTo(buffer);
            return Deserialize<TValue>(buffer.AsMemory(0, utf8Json.Length), settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue? Deserialize<TValue>(ReadOnlyMemory<byte> utf8Json, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonDocument>.Get(() =>
            new JsonDocument(SerializerSettings.Default));
        var doc = cache.Value;
        doc.ParseInternal(utf8Json, settings, options);

        return DeserializeInternal<TValue>(doc.RootElement);
    }


    public static object? Deserialize(string json, Type returnType, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        return Deserialize(json.AsSpan(), returnType, settings, options);
    }

    public static object? Deserialize(ReadOnlySpan<char> json, Type returnType, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var maxBufferSize = Encoding.UTF8.GetMaxByteCount(json.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxBufferSize);

        try
        {
            var len = Encoding.UTF8.GetBytes(json, buffer.AsSpan());
            return Deserialize(buffer.AsMemory(0, len), returnType, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static object? Deserialize(byte[] utf8Json, Type returnType, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        return Deserialize(utf8Json.AsMemory(), returnType, settings, options);
    }

    public static object? Deserialize(ReadOnlySpan<byte> utf8Json, Type returnType, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        try
        {
            utf8Json.CopyTo(buffer);
            return Deserialize(buffer.AsMemory(0, utf8Json.Length), returnType, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static object? Deserialize(ReadOnlyMemory<byte> utf8Json, Type returnType,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonDocument>.Get(() =>
            new JsonDocument(SerializerSettings.Default));
        var doc = cache.Value;
        doc.ParseInternal(utf8Json, settings, options);

        return DeserializeInternal(doc.RootElement, returnType);
    }

    public static TValue DeserializeTo<TValue>(Stream stream, TValue target, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = Helper.ReadStream(stream, out var len, (settings ?? SerializerSettings.Default).DefaultBufferSize);

        try
        {
            return DeserializeTo(buffer.AsMemory(0, len), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue DeserializeTo<TValue>(string json, TValue target, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        return DeserializeTo(json.AsSpan(), target, settings, options);
    }

    public static TValue DeserializeTo<TValue>(ReadOnlySpan<char> json, TValue target,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var maxBufferSize = Encoding.UTF8.GetMaxByteCount(json.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxBufferSize);

        try
        {
            var len = Encoding.UTF8.GetBytes(json, buffer.AsSpan());
            return DeserializeTo(buffer.AsMemory(0, len), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue DeserializeTo<TValue>(byte[] utf8Json, TValue target, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        return DeserializeTo(utf8Json.AsMemory(), target, settings, options);
    }

    public static TValue DeserializeTo<TValue>(ReadOnlySpan<byte> utf8Json, TValue target,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        try
        {
            utf8Json.CopyTo(buffer);
            return DeserializeTo(buffer.AsMemory(0, utf8Json.Length), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue DeserializeTo<TValue>(ReadOnlyMemory<byte> utf8Json, TValue target,
        SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonDocument>.Get(() =>
            new JsonDocument(SerializerSettings.Default));

        var doc = cache.Value;
        doc.ParseInternal(utf8Json, settings, options);

        return DeserializeToInternal(doc.RootElement, target);
    }


    public static object DeserializeTo(string json, object target, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(json);

        return DeserializeTo(json.AsSpan(), target, settings, options);
    }

    public static object DeserializeTo(ReadOnlySpan<char> json, object target, SerializerSettings? settings = null,
        JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);

        var maxBufferSize = Encoding.UTF8.GetMaxByteCount(json.Length);
        var buffer = ArrayPool<byte>.Shared.Rent(maxBufferSize);

        try
        {
            var len = Encoding.UTF8.GetBytes(json, buffer.AsSpan());
            return DeserializeTo(buffer.AsMemory(0, len), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static object DeserializeTo(byte[] utf8Json, object target,
        SerializerSettings? settings = null, JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        return DeserializeTo(utf8Json.AsMemory(), target, settings, options);
    }

    public static object DeserializeTo(ReadOnlySpan<byte> utf8Json, object target,
        SerializerSettings? settings = null, JsonDocumentOptions options = default)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(utf8Json.Length);

        try
        {
            utf8Json.CopyTo(buffer);
            return DeserializeTo(buffer.AsMemory(0, utf8Json.Length), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }


    public static object DeserializeTo(ReadOnlyMemory<byte> utf8Json, object target,
        SerializerSettings? settings = null, JsonDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonDocument>.Get(() =>
            new JsonDocument(SerializerSettings.Default));

        var doc = cache.Value;
        doc.ParseInternal(utf8Json, settings, options);

        return DeserializeToInternal(doc.RootElement, target.GetType(), target);
    }

    public static void Serialize(Stream stream, object? value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        Serialize(stream, value, value?.GetType() ?? typeof(object), settings, options);
    }

    public static void Serialize(Stream stream, object? value, Type inputType, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(stream, settings, options);
        settings.GetConverter(inputType).WriteObject(writer, inputType, value);
        writer.Dispose();
    }

    public static void Serialize<TValue>(Stream stream, TValue value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(stream, settings, options);
        writer.WriteValue(value);
        writer.Dispose();
    }

    public static void Serialize(IBufferWriter<byte> bufferWriter, object? value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        Serialize(bufferWriter, value, value?.GetType() ?? typeof(object), settings, options);
    }

    public static void Serialize(IBufferWriter<byte> bufferWriter, object? value, Type inputType,
        SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(bufferWriter, settings, options);
        settings.GetConverter(inputType).WriteObject(writer, inputType, value);
        writer.Dispose();
    }

    public static void Serialize<TValue>(IBufferWriter<byte> bufferWriter, TValue value,
        SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(bufferWriter);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(bufferWriter, settings, options);
        writer.WriteValue(value);
        writer.Dispose();
    }

    public static byte[] SerializeToUtf8Bytes(object? value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        return SerializeToUtf8Bytes(value, value?.GetType() ?? typeof(object), settings, options);
    }

    public static byte[] SerializeToUtf8Bytes(object? value, Type inputType, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(settings, options);
        settings.GetConverter(inputType).WriteObject(writer, inputType, value);
        return writer.Encode();
    }

    public static byte[] SerializeToUtf8Bytes<TValue>(TValue value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(settings, options);
        writer.WriteValue(value);
        return writer.Encode();
    }

    public static string Serialize(object? value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        return Serialize(value, value?.GetType() ?? typeof(object), settings, options);
    }

    public static string Serialize(object? value, Type inputType, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(settings, options);
        settings.GetConverter(inputType).WriteObject(writer, inputType, value);
        return writer.EncodeToString();
    }

    public static string Serialize<TValue>(TValue value, SerializerSettings? settings = null,
        JsonWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using var cache = Helper.InstanceCache<JsonWriter>.Get(() => new JsonWriter());

        var writer = cache.Value;
        writer.Reset(settings, options);
        writer.WriteValue(value);
        return writer.EncodeToString();
    }
}