// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.IO;
using System.Text;
using REDox.Serialization;

namespace REDox.Yaml;

public sealed class YamlDocument : Document
{
    private readonly HashSet<uint> _flowStyle = new();
    private ReadOnlyMemory<byte> _source;

    private YamlDocument(SerializerSettings settings) : base(settings)
    {
    }

    public YamlDocument Duplicate()
    {
        return CreateSnapshot<YamlDocument>();
    }

    public static YamlDocument Parse(Stream stream, SerializerSettings settings, YamlDocumentOptions options = default)
    {
        var length = stream.Length;

        var bytes = new byte[(int)length];

        stream.ReadExactly(bytes.AsSpan());

        return Parse(bytes, settings, options);
    }

    public static YamlDocument Parse(string yaml, SerializerSettings settings, YamlDocumentOptions options = default)
    {
        return Parse(Encoding.UTF8.GetBytes(yaml), settings, options);
    }

    public static YamlDocument Parse(byte[] utf8Yaml, SerializerSettings settings,
        YamlDocumentOptions options = default)
    {
        return Parse(utf8Yaml.AsMemory(), settings, options);
    }

    public static YamlDocument Parse(ReadOnlyMemory<byte> utf8Yaml, SerializerSettings settings,
        YamlDocumentOptions options = default)
    {
        var doc = new YamlDocument(settings);

        doc.Read(utf8Yaml, options);

        return doc;
    }


    public static byte[] Encode(DElement element, YamlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, reader, reader.RootId, options);
            return writer.Encode();
        }
    }

    public static string EncodeToString(DElement element, YamlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(reader.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, reader, reader.RootId, options);
            return writer.EncodeToString();
        }
    }

    public static void EncodeTo(DElement element, Stream stream, YamlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(stream, reader.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    public static void EncodeTo(DElement element, IBufferWriter<byte> bufferWriter,
        YamlWriteOptions options = default)
    {
        using (var cache = Helper.InstanceCache<Utf8TextWriter>.Get(() => new Utf8TextWriter()))
        {
            var writer = cache.Value;
            var reader = new DataReader(element);

            writer.Reset(bufferWriter, reader.Settings, options.TextWriteOptions with { WriteIndented = true });
            Write(writer, reader, reader.RootId, options);
            writer.Dispose();
        }
    }

    private void Read(ReadOnlyMemory<byte> bytes, YamlDocumentOptions options)
    {
        _source = bytes;

        EnsureCapacity(_source.Length / 16);

        var rootId = ParseYaml(_source.Span, options);

        RootId = rootId;
    }

    private uint ParseScalar(ReadOnlySpan<byte> span, int offset)
    {
        //trim white space
        span = span.TrimEnd(new[] { (byte)' ', (byte)'\n' });

        if (Utf8Parser.TryParse(span, out float fvalue, out var bytesConsumed))
        {
            return AllocToken(DToken.Make(DTokenVariant.Float,
                DToken.EncodeLengthOffsetPayload(bytesConsumed, offset)));
        }

        return AllocToken(DToken.Make(DTokenVariant.String, DToken.EncodeLengthOffsetPayload(span.Length, offset)));
    }

    private uint ParseYaml(ReadOnlySpan<byte> yaml, YamlDocumentOptions options)
    {
        const int Capacity = 64;

        using var stack =
            new Helper.LocalStack<(uint pid, uint lid, int indent)>(
                stackalloc (uint pid, uint lid, int indent)[Capacity], options.MaxDepth);

        var index = 0;
        var parentId = 0U;
        var latestId = 0U;
        var tokenId = 0U;
        var indent = -1;
        var scalarOffset = -1;
        var lineOffset = 0;

        for (;;)
        {
            var c = index < yaml.Length ? yaml[index] : 0;

            switch ((char)c)
            {
                case '\n':
                    index++;
                    lineOffset = index;
                    break;
                case '\0':
                case ',':
                case '#':
                    if (scalarOffset >= 0)
                    {
                        var offset = scalarOffset;
                        var span = yaml.Slice(offset, index - offset);

                        tokenId = ParseScalar(span, offset);

                        if (!GetToken(parentId).IsContainer)
                        {
                            if (parentId == 0)
                            {
                                return tokenId;
                            }

                            throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                        }

                        IncToken(parentId);

                        if (latestId != 0)
                        {
                            LinkToken(latestId, tokenId);
                        }

                        latestId = tokenId;
                        scalarOffset = -1;
                    }

                    index++;

                    if (c == 0)
                    {
                        while (stack.Count > 0)
                        {
                            (parentId, latestId, indent) = stack.Pop();
                        }

                        return parentId;
                    }

                    if (c == '#')
                    {
                        var offset = index;
                        while (index < yaml.Length)
                        {
                            c = yaml[index];
                            if (c == '\n')
                            {
                                break;
                            }

                            index++;
                        }
                    }

                    break;
                case '-':
                    if (yaml[index + 1] <= 0x20)
                    {
                        if (scalarOffset >= 0)
                        {
                            var offset = scalarOffset;
                            var span = yaml.Slice(offset, index - offset);

                            tokenId = ParseScalar(span, offset);

                            if (!GetToken(parentId).IsContainer)
                            {
                                if (parentId == 0)
                                {
                                    return tokenId;
                                }

                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            IncToken(parentId);

                            if (latestId != 0)
                            {
                                LinkToken(latestId, tokenId);
                            }

                            latestId = tokenId;
                            scalarOffset = -1;
                        }

                        var curIndent = index - lineOffset + 1;

                        if (indent < 0)
                        {
                            if (parentId == 0)
                            {
                                parentId = AllocToken(DToken.MakeArray(0));
                                indent = curIndent;
                            }
                        }
                        else
                        {
                            if (indent < curIndent)
                            {
                                tokenId = AllocToken(DToken.MakeArray(0));

                                if (!GetToken(parentId).IsContainer)
                                {
                                    throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                                }

                                IncToken(parentId);

                                if (latestId != 0)
                                {
                                    LinkToken(latestId, tokenId);
                                }

                                latestId = tokenId;

                                stack.Push((parentId, latestId, indent));

                                parentId = tokenId;
                                indent = curIndent;
                            }

                            while (indent > curIndent)
                            {
                                (parentId, latestId, indent) = stack.Pop();
                            }
                        }

                        index++;
                        scalarOffset = -1;
                    }
                    else
                    {
                        if (index + 2 < yaml.Length && yaml[index + 1] == '-' && yaml[index + 2] == '-')
                        {
                            //begin document
                            index += 3;
                        }
                        else
                        {
                            goto default;
                        }
                    }

                    break;
                case ':':
                    if (index + 1 >= yaml.Length || yaml[index + 1] <= 0x20)
                    {
                        var offset = scalarOffset;
                        var curIndent = offset - lineOffset;

                        var span = yaml.Slice(offset, index - offset);

                        if (indent < 0)
                        {
                            if (parentId == 0)
                            {
                                parentId = AllocToken(DToken.MakeMap(0));
                                indent = curIndent;
                            }
                        }
                        else
                        {
                            if (indent < curIndent)
                            {
                                tokenId = AllocToken(DToken.MakeMap(0));

                                if (!GetToken(parentId).IsContainer)
                                {
                                    throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                                }

                                IncToken(parentId);

                                if (latestId != 0)
                                {
                                    LinkToken(latestId, tokenId);
                                }

                                latestId = tokenId;

                                stack.Push((parentId, latestId, indent));

                                parentId = tokenId;
                                indent = curIndent;
                            }

                            while (indent > curIndent)
                            {
                                (parentId, latestId, indent) = stack.Pop();
                            }
                        }


                        tokenId = ParseScalar(span, offset);

                        if (latestId != 0)
                        {
                            LinkToken(latestId, tokenId);
                        }

                        latestId = tokenId;

                        index++;
                        scalarOffset = -1;
                    }
                    else
                    {
                        goto default;
                    }

                    break;
                case '[': //begin array
                case '{': //begin map
                    {
                        if (scalarOffset >= 0)
                        {
                            goto default;
                        }

                        tokenId = AllocToken(c == '[' ? DToken.MakeArray(0) : DToken.MakeMap(0));
                        _flowStyle.Add(tokenId);

                        if (parentId == 0)
                        {
                            parentId = tokenId;
                        }
                        else
                        {
                            if (!GetToken(parentId).IsContainer)
                            {
                                throw new ParseException(this, ParseException.ErrorCode.InvalidFormat, index);
                            }

                            IncToken(parentId);

                            if (latestId != 0)
                            {
                                LinkToken(latestId, tokenId);
                            }

                            latestId = tokenId;
                        }

                        stack.Push((parentId, latestId, indent));
                        parentId = tokenId;
                        latestId = 0;
                        indent = -1;

                        index++;
                    }
                    break;
                case ']': //end array
                case '}': //end map
                    {
                        if (indent >= 0 || !GetToken(parentId).IsContainer)
                        {
                            goto default;
                        }

                        if (scalarOffset >= 0)
                        {
                            var offset = scalarOffset;
                            var span = yaml.Slice(offset, index - offset);

                            tokenId = ParseScalar(span, offset);

                            IncToken(parentId);

                            if (latestId != 0)
                            {
                                LinkToken(latestId, tokenId);
                            }

                            latestId = tokenId;
                            scalarOffset = -1;
                        }

                        tokenId = parentId;
                        (parentId, latestId, indent) = stack.Pop();
                        index++;
                    }
                    break;
                default:
                    if (c > 0x20)
                    {
                        if (scalarOffset < 0)
                        {
                            scalarOffset = index;
                        }
                        else
                        {
                            if (scalarOffset - lineOffset < 0)
                            {
                                var curIndent = index - lineOffset;

                                if (indent >= curIndent)
                                {
                                    var offset = scalarOffset;
                                    var span = yaml.Slice(offset, lineOffset - offset - 1);

                                    tokenId = ParseScalar(span, offset);

                                    if (!GetToken(parentId).IsContainer)
                                    {
                                        if (parentId == 0)
                                        {
                                            return tokenId;
                                        }

                                        throw new ParseException(this, ParseException.ErrorCode.InvalidFormat,
                                            index);
                                    }

                                    IncToken(parentId);

                                    if (latestId != 0)
                                    {
                                        LinkToken(latestId, tokenId);
                                    }

                                    latestId = tokenId;
                                    scalarOffset = index;
                                }

                                while (indent > curIndent)
                                {
                                    (parentId, latestId, indent) = stack.Pop();
                                }
                            }
                        }
                    }

                    index++;
                    break;
            }
        }
    }

    private static void Write(Utf8TextWriter writer, in DataReader reader, uint id, YamlWriteOptions options)
    {
        WriteYaml(writer, reader, reader.Document as YamlDocument, id, ref options);
        writer.WriteIndentNewLine();
    }

    private static void WriteYaml(Utf8TextWriter writer, in DataReader reader, YamlDocument? doc, uint tokenId,
        ref YamlWriteOptions options)
    {
        var token = reader.GetToken(tokenId);

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                if (doc != null && doc._flowStyle.Contains(tokenId))
                {
                    var values = reader.EnumerateArray(tokenId);
                    var separator = false;

                    writer.WriteUtf8Byte((byte)'[');
                    if (reader.GetValueCount(tokenId) > 0)
                    {
                        writer.PushIndent();
                        writer.WriteIndentNewLine();
                        foreach (var valueId in values)
                        {
                            if (separator)
                            {
                                writer.WriteUtf8Byte((byte)',');
                                writer.WriteIndentNewLine();
                            }

                            WriteYaml(writer, reader, doc, valueId, ref options);
                            separator = true;
                        }

                        writer.PopIndent();
                        writer.WriteIndentNewLine();
                    }

                    writer.WriteUtf8Byte((byte)']');
                }
                else
                {
                    var count = reader.GetValueCount(tokenId);

                    foreach (var valueId in reader.EnumerateArray(tokenId))
                    {
                        writer.WriteUtf8Byte((byte)'-');

                        writer.PushIndent();
                        if (reader.GetToken(valueId).Type == DTokenType.Map)
                        {
                            writer.WriteIndentNewLine();
                        }
                        else
                        {
                            writer.WriteUtf8Byte((byte)' ');
                        }

                        WriteYaml(writer, reader, doc, valueId, ref options);
                        writer.PopIndent();

                        if (--count > 0)
                        {
                            writer.WriteIndentNewLine();
                        }
                    }
                }
            }
            else
            {
                if (doc != null && doc._flowStyle.Contains(tokenId))
                {
                    writer.WriteUtf8Byte((byte)'{');

                    if (reader.GetValueCount(tokenId) > 0)
                    {
                        writer.PushIndent();
                        writer.WriteIndentNewLine();

                        var pairs = reader.EnumerateMap(tokenId);
                        var separator = false;

                        foreach (var pair in pairs)
                        {
                            if (separator)
                            {
                                writer.WriteUtf8Byte((byte)',');
                                writer.WriteIndentNewLine();
                            }

                            WriteYaml(writer, reader, doc, pair.Key, ref options);
                            writer.WriteUtf8Byte((byte)':');
                            writer.WriteUtf8Byte((byte)' ');
                            WriteYaml(writer, reader, doc, pair.Value, ref options);
                            separator = true;
                        }

                        writer.PopIndent();
                        writer.WriteIndentNewLine();
                    }

                    writer.WriteUtf8Byte((byte)'}');
                }
                else
                {
                    var count = reader.GetValueCount(tokenId);

                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        WriteYaml(writer, reader, doc, kv.Key, ref options);

                        if (reader.GetToken(kv.Value).IsContainer)
                        {
                            writer.WriteUtf8Byte((byte)':');
                            writer.PushIndent();
                            writer.WriteIndentNewLine();
                            WriteYaml(writer, reader, doc, kv.Value, ref options);
                            writer.PopIndent();
                        }
                        else
                        {
                            writer.WriteUtf8Byte((byte)':');
                            writer.WriteUtf8Byte((byte)' ');
                            WriteYaml(writer, reader, doc, kv.Value, ref options);
                        }

                        if (--count > 0)
                        {
                            writer.WriteIndentNewLine();
                        }
                    }
                }
            }
        }
        else
        {
            switch (token.Kind)
            {
                case DTokenKind.Float:
                    writer.WriteDouble(reader.ReadDouble(tokenId));
                    break;
                case DTokenKind.Integer:
                    if (token.Variant == DTokenVariant.IntegerUnsigned)
                    {
                        writer.WriteUInt64(reader.ReadUInt64(tokenId));
                    }
                    else
                    {
                        writer.WriteInt64(reader.ReadInt64(tokenId));
                    }

                    break;
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    writer.WriteString(reader.ReadUtf8String(tokenId));
                    break;
            }
        }
    }

    protected override double DecodeFloat(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var src = _source.Span.Slice(param.offset, param.length);

        if (Utf8Parser.TryParse(src, out double value, out var bytes))
        {
            return value;
        }

        if (src.Length == 3 && src[0] == 'I' && src[1] == 'N' && src[2] == 'F')
        {
            return double.PositiveInfinity;
        }

        if (src.Length == 4 && src[0] == '-' && src[1] == 'I' && src[2] == 'N' && src[3] == 'F')
        {
            return double.NegativeInfinity;
        }

        if (Utf8Parser.TryParse(src, out bool bvalue, out var bbytes))
        {
            return bvalue ? 1 : 0;
        }

        throw new InvalidCastException();
    }

    protected override long DecodeInteger(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var span = _source.Span.Slice(param.offset, param.length);

        if (token.Variant == DTokenVariant.IntegerUnsigned)
        {
            if (Utf8Parser.TryParse(span, out ulong uvalue, out var ubytes))
            {
                return (long)uvalue;
            }
        }
        else
        {
            if (Utf8Parser.TryParse(span, out long value, out var bytes))
            {
                return value;
            }
        }

        throw new InvalidCastException();
    }

    protected override ReadOnlySpan<byte> DecodeTrivia(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);
        var src = _source.Span.Slice(param.offset, param.length);

        return src;
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var param = DToken.DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    private class ParseException : DocumentParseException
    {
        public enum ErrorCode
        {
            None,
            InvalidFormat
        }

        private ErrorCode _ErrorCode;

        public ParseException(YamlDocument doc, ErrorCode error, int offset) : base(nameof(YamlDocument), offset)
        {
            _ErrorCode = error;
        }
    }
}