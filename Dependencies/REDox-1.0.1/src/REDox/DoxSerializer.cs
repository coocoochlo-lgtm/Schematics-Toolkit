// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using REDox.Serialization;

namespace REDox;

public sealed class DoxSerializer : Serializer
{
    public static bool DeepEquals<T>(T a, T b, SerializerSettings? settings = null)
    {
        if (Equals(a, b))
        {
            return true;
        }

        var src = Serialize(a, settings);
        var tgt = Serialize(b, settings);

        if (src.Length != tgt.Length)
        {
            return false;
        }

        return src.AsSpan().SequenceEqual(tgt);
    }

    public static T DeepCopy<T>(T src, SerializerSettings? settings = null)
    {
        var bytes = Serialize(src, settings);

        var result = Deserialize<T>(bytes, settings);

        if (result == null)
        {
            throw new InvalidOperationException();
        }

        return result;
    }

    public static TValue? Deserialize<TValue>(Stream stream, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
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

    public static object? Deserialize(Stream stream, Type returnType, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

        try
        {
            return Deserialize(buffer.AsMemory(0, len), returnType, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue DeserializeTo<TValue>(Stream stream, TValue target, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

        try
        {
            return DeserializeTo(buffer.AsMemory(0, len), target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static object DeserializeTo(Stream stream, Type returnType, object target,
        SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        settings ??= SerializerSettings.Default;

        var buffer = Helper.ReadStream(stream, out var len, settings.DefaultBufferSize);

        try
        {
            return DeserializeTo(buffer.AsMemory(0, len), returnType, target, settings, options);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public static TValue? Deserialize<TValue>(ReadOnlyMemory<byte> bytes, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxDocument>.Get(() =>
                   new DoxDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings);

            return DeserializeInternal<TValue>(doc.RootElement);
        }
    }

    public static object? Deserialize(ReadOnlyMemory<byte> bytes, Type returnType, SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxDocument>.Get(() =>
                   new DoxDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings);

            return DeserializeInternal(doc.RootElement, returnType);
        }
    }

    public static TValue DeserializeTo<TValue>(ReadOnlyMemory<byte> bytes, TValue target,
        SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxDocument>.Get(() =>
                   new DoxDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings);

            return DeserializeToInternal<TValue>(doc.RootElement, target);
        }
    }

    public static object DeserializeTo(ReadOnlyMemory<byte> bytes, Type returnType, object target,
        SerializerSettings? settings = null,
        DoxDocumentOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxDocument>.Get(() =>
                   new DoxDocument(SerializerSettings.Default)))
        {
            var doc = cache.Value;
            doc.ParseInternal(bytes, settings);

            return DeserializeToInternal(doc.RootElement, returnType, target);
        }
    }

    public static void Serialize<TValue>(Stream stream, TValue value, SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            writer.WriteHeader();
            writer.WriteValue(value);
            writer.WriteFooter();
            writer.Flush();
        }
    }

    public static void Serialize<TValue>(IBufferWriter<byte> bufferWriter, TValue value,
        SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            writer.WriteHeader();
            writer.WriteValue(value);
            writer.WriteFooter();
            writer.Flush();
        }
    }

    public static byte[] Serialize<TValue>(TValue value, SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            writer.WriteHeader();
            writer.WriteValue(value);
            writer.WriteFooter();
            return writer.Encode();
        }
    }

    public static void Serialize(Stream stream, object value, Type returnType, SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(stream, settings, options);
            writer.WriteHeader();
            settings.GetConverter(returnType).WriteObject(writer, returnType, value);
            writer.WriteFooter();
            writer.Flush();
        }
    }

    public static void Serialize(IBufferWriter<byte> bufferWriter, object value, Type returnType,
        SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(bufferWriter, settings, options);
            writer.WriteHeader();
            settings.GetConverter(returnType).WriteObject(writer, returnType, value);
            writer.WriteFooter();
            writer.Flush();
        }
    }

    public static byte[] Serialize(object value, Type returnType, SerializerSettings? settings = null,
        DoxWriteOptions options = default)
    {
        settings ??= SerializerSettings.Default;

        using (var cache = Helper.InstanceCache<DoxWriter>.Get(() => new DoxWriter()))
        {
            var writer = cache.Value;
            writer.Reset(settings, options);
            writer.WriteHeader();
            settings.GetConverter(returnType).WriteObject(writer, returnType, value);
            writer.WriteFooter();
            return writer.Encode();
        }
    }

    private class DoxWriter : DataWriter
    {
        private readonly Dictionary<string, DToken> _dict = new(ReferenceEqualityComparer.Instance);
        private readonly DocumentWriter _writer;
        private uint _latestId;
        private uint _parentId;
        private int _tokenCount;

        private DToken[] _tokens;

        public DoxWriter()
        {
            _writer = new DocumentWriter();
            _tokens = new DToken[4096];
        }

        public void Reset(SerializerSettings settings, DoxWriteOptions options = default)
        {
            Reset(null, null, settings, options);
        }

        public void Reset(Stream stream, SerializerSettings settings, DoxWriteOptions options = default)
        {
            Reset(stream, null, settings, options);
        }

        public void Reset(IBufferWriter<byte> writer, SerializerSettings settings, DoxWriteOptions options = default)
        {
            Reset(null, writer, settings, options);
        }

        private void Reset(Stream? stream, IBufferWriter<byte>? writer, SerializerSettings settings,
            DoxWriteOptions options)
        {
            base.Reset(settings);

            _writer.ResetInternal(settings.DefaultBufferSize, stream, writer);
            _parentId = 0;
            _latestId = 0;
            _tokenCount = 0;
            _dict.Clear();
        }

        public byte[] Encode()
        {
            return _writer.ToArray();
        }

        public void Flush()
        {
            _writer.Flush();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long Pack(uint parentId, uint latestId)
        {
            return ((long)parentId << 32) | latestId;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private (uint parentId, uint latestId) Unpack(long packed)
        {
            return ((uint)(packed >> 32), (uint)(packed & 0xffffffff));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void NextElement()
        {
            if (_parentId != 0)
            {
                _tokens[_parentId].Increment();
            }
        }

        public void WriteHeader()
        {
            DoxDocument.WriteHeader(_writer);

            WriteToken(default);
        }

        public void WriteFooter()
        {
            DoxDocument.WriteFooter(_writer, _tokens.AsSpan(0, _tokenCount));
        }

        private uint WriteToken(DToken token)
        {
            var tokenId = (uint)_tokenCount;

            if (_tokenCount >= _tokens.Length)
            {
                Array.Resize(ref _tokens, _tokens.Length * 2);
            }

            _tokens[_tokenCount++] = token;

            if (_latestId != 0)
            {
                var latest = _tokens[(int)_latestId];

                if (latest.IsContainer)
                {
                    latest.LinkId = tokenId;
                    _tokens[(int)_latestId] = latest;
                }
            }

            _latestId = tokenId;

            return tokenId;
        }

        public override void WriteNull()
        {
            NextElement();
            WriteToken(DToken.Make(DTokenVariant.Null, 0));
        }

        public override void WriteChar(char value)
        {
            Span<byte> bytes = stackalloc byte[4];
            var len = Encoding.UTF8.GetBytes(new ReadOnlySpan<char>(ref value), bytes);

            WriteString(bytes.Slice(0, len));
        }

        public override void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default)
        {
            NextElement();
            WriteToken(DoxDocument.WriteBigNumber(_writer, value, kind));
        }

        public override void WriteDateTime(DateTime value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteDateTime(_writer, value, TimestampKind.Default));
        }

        public override void WriteDateTimeOffset(DateTimeOffset value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteDateTimeOffset(_writer, value));
        }

        public override void WriteGuid(Guid value)
        {
            Span<byte> guidBytes = stackalloc byte[16];
            value.TryWriteBytes(guidBytes, true, out _);
            WriteByteString(guidBytes, ByteStringKind.Guid);
        }

        public override void WriteStartArray(int? definiteLength)
        {
            NextElement();

            var tokenId = WriteToken(DToken.MakeArray(0));

            _writer.PushContext(Pack(_parentId, _latestId));

            _parentId = tokenId;
            _latestId = 0;
        }

        public override void WriteEndArray()
        {
            if (_writer.ContextDepth > 0)
            {
                var state = Unpack(_writer.PopContext());

                _parentId = state.parentId;
                _latestId = state.latestId;
            }
        }

        public override void WriteStartMap(int? definiteLength)
        {
            NextElement();

            var tokenId = WriteToken(DToken.MakeMap(0));

            _writer.PushContext(Pack(_parentId, _latestId));

            _parentId = tokenId;
            _latestId = 0;
        }

        public override void WriteEndMap()
        {
            ref var token = ref _tokens[_parentId];

            Debug.Assert(token.Type == DTokenType.Map);
            Debug.Assert((token.Count & 1) == 0);

            token.Count = token.Count / 2;

            if (_writer.ContextDepth > 0)
            {
                var state = Unpack(_writer.PopContext());

                _parentId = state.parentId;
                _latestId = state.latestId;
            }
        }

        public override void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default)
        {
            NextElement();
            WriteToken(DoxDocument.WriteByteString(_writer, value, kind));
        }

        public override void WriteDecimal(decimal value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteDecimal(_writer, value));
        }

        public override void WriteInt64(long value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteInt64(_writer, value));
        }

        public override void WriteUInt64(ulong value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteUInt64(_writer, value));
        }

        public override void WriteHalf(Half value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteHalf(_writer, value));
        }

        public override void WriteSingle(float value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteSingle(_writer, value));
        }

        public override void WriteDouble(double value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteDouble(_writer, value));
        }

        public override void WriteBoolean(bool value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteBoolean(_writer, value));
        }

        public override void WriteString(ReadOnlySpan<byte> utf8Bytes)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, utf8Bytes, DTokenVariant.String));
        }

        public override void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default)
        {
            NextElement();

            ref var token = ref CollectionsMarshal.GetValueRefOrAddDefault(_dict, value.Symbol, out var exists);

            if (!exists)
            {
                token = DoxDocument.WriteString(_writer, value, kind.ToVariant());
            }

            WriteToken(token);
        }

        public override void WriteString(string value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, value, DTokenVariant.String));
        }

        public override void WriteString(ReadOnlySpan<char> value)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, value, DTokenVariant.String));
        }

        public override void WriteSymbol(string value, SymbolKind kind = SymbolKind.Default)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, value, kind.ToVariant()));
        }

        public override void WriteSymbol(ReadOnlySpan<byte> utf8Bytes, SymbolKind kind = SymbolKind.Default)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, utf8Bytes, kind.ToVariant()));
        }

        public override void WriteSymbol(ReadOnlySpan<char> value, SymbolKind kind = SymbolKind.Default)
        {
            NextElement();
            WriteToken(DoxDocument.WriteString(_writer, value, kind.ToVariant()));
        }
    }
}