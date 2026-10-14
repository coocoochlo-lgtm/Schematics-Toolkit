// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace REDox.Images;

public class BitmapDocument : Document
{
    private BitmapDocumentOptions _options;
    private ReadOnlyMemory<byte> _source;

    private BitmapDocument(SerializerSettings settings) : base(settings)
    {
    }

    public static BitmapDocument Parse(Stream bitmap, SerializerSettings settings,
        BitmapDocumentOptions options = default)
    {
        var length = bitmap.Length;

        var bytes = new byte[(int)length];

        bitmap.ReadExactly(bytes.AsSpan());

        return Parse(bytes, settings, options);
    }

    public static BitmapDocument Parse(byte[] json, SerializerSettings settings,
        BitmapDocumentOptions options = default)
    {
        return Parse(json.AsMemory(), settings, options);
    }

    public static BitmapDocument Parse(ReadOnlyMemory<byte> json, SerializerSettings settings,
        BitmapDocumentOptions options = default)
    {
        var doc = new BitmapDocument(settings);

        doc._options = options;
        doc.Read(json);

        return doc;
    }

    private int GetStringId<T>(T value) where T : struct, Enum
    {
        var ivalue = Unsafe.As<T, int>(ref value);

        if (typeof(T) == typeof(BitmapFileHeader))
        {
            return ((int)Section.FileHeader << 8) | ivalue;
        }

        if (typeof(T) == typeof(BitmapInfoHeader))
        {
            return ((int)Section.InfoHeader << 8) | ivalue;
        }

        if (typeof(T) == typeof(Colors))
        {
            return ((int)Section.Colors << 8) | ivalue;
        }

        return 0;
    }

    private int GetStringId(Section value)
    {
        return (int)value << 8;
    }

    private uint AddPropertyUInt32<T>(uint parentId, T type, ReadOnlySpan<byte> span) where T : struct, Enum
    {
        var value = BinaryPrimitives.ReadUInt32LittleEndian(span);

        AllocToken(DToken.Make(DTokenKind.String, GetStringId(type)));
        AllocToken(DToken.Make(DTokenKind.Integer, value));
        IncToken(parentId);

        return value;
    }

    private ushort AddPropertyUInt16<T>(uint parentId, T type, ReadOnlySpan<byte> span) where T : struct, Enum
    {
        var value = BinaryPrimitives.ReadUInt16LittleEndian(span);

        AllocToken(DToken.Make(DTokenKind.String, GetStringId(type)));
        AllocToken(DToken.Make(DTokenKind.Integer, value));
        IncToken(parentId);

        return value;
    }

    private uint ParseBitmapFileHeader(ReadOnlySpan<byte> bitmapFileHeader, out uint offBits)
    {
        var latestId = AllocToken(DToken.MakeMap(0));

        AddPropertyUInt16(latestId, BitmapFileHeader.Type, bitmapFileHeader.Slice(0));
        AddPropertyUInt32(latestId, BitmapFileHeader.Size, bitmapFileHeader.Slice(2));
        AddPropertyUInt32(latestId, BitmapFileHeader.Reserved1, bitmapFileHeader.Slice(6));
        AddPropertyUInt32(latestId, BitmapFileHeader.Reserved2, bitmapFileHeader.Slice(8));
        offBits = AddPropertyUInt32(latestId, BitmapFileHeader.OffBits, bitmapFileHeader.Slice(10));

        return latestId;
    }

    private void Read(ReadOnlyMemory<byte> buffer)
    {
        _source = buffer;

        EnsureCapacity(256);

        var bytes = buffer.Span;
        var rootId = AllocToken(DToken.MakeMap(0));

        RootId = rootId;

        AllocToken(DToken.Make(DTokenKind.String, GetStringId(BitmapFileHeader.BitmapFileHeader)));

        var latestId = ParseBitmapFileHeader(bytes.Slice(0, 14), out var offBits);
        IncToken(rootId);

        var bitmapInfoHeader = bytes.Slice(14);

        LinkToken(latestId, AllocToken(DToken.Make(DTokenKind.String, GetStringId(Section.InfoHeader))));
        IncToken(rootId);
        latestId = AllocToken(DToken.MakeMap(0));

        var size = AddPropertyUInt32(latestId, BitmapInfoHeader.Size, bitmapInfoHeader.Slice(0));

        var lines = 0;
        var lineSize = 0;

        if (size == 12)
        {
            var width = AddPropertyUInt16(latestId, BitmapInfoHeader.Width, bitmapInfoHeader.Slice(4));
            var height = AddPropertyUInt16(latestId, BitmapInfoHeader.Height, bitmapInfoHeader.Slice(6));
            var planes = AddPropertyUInt16(latestId, BitmapInfoHeader.Planes, bitmapInfoHeader.Slice(8));
            var bitCount = AddPropertyUInt16(latestId, BitmapInfoHeader.BitCount, bitmapInfoHeader.Slice(10));

            lines = height;
            lineSize = (width * bitCount + 31) / 32;

            if (bitCount <= 8)
            {
                LinkToken(latestId, AllocToken(DToken.Make(DTokenKind.String, GetStringId(Section.Colors))));
                IncToken(rootId);

                var colors = 1 << bitCount;

                latestId = AllocToken(DToken.MakeArray(colors));

                var prevId = 0U;

                for (var i = 0; i < colors; i++)
                {
                    var color = bitmapInfoHeader.Slice(14 + (int)size + i * 3);
                    var tokenId = AllocToken(DToken.MakeMap(3));

                    if (prevId != 0)
                    {
                        LinkToken(prevId, tokenId);
                    }

                    prevId = tokenId;

                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Blue)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[0]));
                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Green)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[1]));
                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Red)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[2]));
                }
            }
        }
        else
        {
            var width = (int)AddPropertyUInt32(latestId, BitmapInfoHeader.Width, bitmapInfoHeader.Slice(4));
            var height = (int)AddPropertyUInt32(latestId, BitmapInfoHeader.Height, bitmapInfoHeader.Slice(8));
            var planes = AddPropertyUInt16(latestId, BitmapInfoHeader.Planes, bitmapInfoHeader.Slice(12));
            var bitCount = AddPropertyUInt16(latestId, BitmapInfoHeader.BitCount, bitmapInfoHeader.Slice(14));

            var compression = AddPropertyUInt32(latestId, BitmapInfoHeader.Compression, bitmapInfoHeader.Slice(16));
            var sizeImage = AddPropertyUInt32(latestId, BitmapInfoHeader.SizeImage, bitmapInfoHeader.Slice(20));
            var xPelsPerMeter = AddPropertyUInt32(latestId, BitmapInfoHeader.XPelsPerMeter, bitmapInfoHeader.Slice(24));
            var yPelsPerMeter = AddPropertyUInt32(latestId, BitmapInfoHeader.YPelsPerMeter, bitmapInfoHeader.Slice(28));

            var clrUsed = AddPropertyUInt32(latestId, BitmapInfoHeader.ClrUsed, bitmapInfoHeader.Slice(32));
            var clrImportabt = AddPropertyUInt32(latestId, BitmapInfoHeader.ClrImportant, bitmapInfoHeader.Slice(36));

            if (size > 40)
            {
                var redMask = AddPropertyUInt32(latestId, BitmapInfoHeader.RedMask, bitmapInfoHeader.Slice(40));
                var greenMask = AddPropertyUInt32(latestId, BitmapInfoHeader.GreenMask, bitmapInfoHeader.Slice(44));
                var blueMask = AddPropertyUInt32(latestId, BitmapInfoHeader.GreenMask, bitmapInfoHeader.Slice(48));
                var alphaMask = AddPropertyUInt32(latestId, BitmapInfoHeader.GreenMask, bitmapInfoHeader.Slice(52));
                var csType = AddPropertyUInt32(latestId, BitmapInfoHeader.CSType, bitmapInfoHeader.Slice(56));

                AllocToken(DToken.Make(DTokenKind.String, GetStringId(BitmapInfoHeader.Endpoints)));
                AllocToken(DToken.Make(DTokenKind.Integer,
                    BinaryPrimitives.ReadUInt32LittleEndian(bitmapInfoHeader.Slice(60))));
                IncToken(latestId);

                var gammaRed = AddPropertyUInt32(latestId, BitmapInfoHeader.GammaRed, bitmapInfoHeader.Slice(96));
                var gammaGreen = AddPropertyUInt32(latestId, BitmapInfoHeader.GammaGreen, bitmapInfoHeader.Slice(100));
                var gammaBlue = AddPropertyUInt32(latestId, BitmapInfoHeader.GammaBlue, bitmapInfoHeader.Slice(104));
            }

            lines = Math.Abs(height);
            lineSize = (width * bitCount + 31) / 32;

            if (clrUsed > 0)
            {
                LinkToken(latestId, AllocToken(DToken.Make(DTokenKind.String, GetStringId(Section.Colors))));
                IncToken(rootId);

                latestId = AllocToken(DToken.MakeArray((int)clrUsed));

                var prevId = 0U;

                for (var i = 0; i < clrUsed; i++)
                {
                    var color = bitmapInfoHeader.Slice(14 + (int)size + i * 4);
                    var tokenId = AllocToken(DToken.MakeMap(3));

                    if (prevId != 0)
                    {
                        LinkToken(prevId, tokenId);
                    }

                    prevId = tokenId;

                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Blue)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[0]));
                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Green)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[1]));
                    AllocToken(DToken.Make(DTokenKind.String, GetStringId(Colors.Red)));
                    AllocToken(DToken.Make(DTokenKind.Integer, color[2]));
                }
            }
        }

        if (lineSize > 0)
        {
            LinkToken(latestId, AllocToken(DToken.Make(DTokenKind.String, GetStringId(Section.Lines))));
            IncToken(rootId);

            latestId = AllocToken(DToken.MakeArray(lines));

            for (var i = 0; i < lines; i++)
            {
                var offset = (int)offBits + i * lineSize;
                var span = bytes.Slice(offset, lineSize);
                var param = EncodeLengthOffsetPayload(lineSize, offset);

                AllocToken(DToken.Make(DTokenKind.ByteString, param));
            }
        }
        else
        {
            LinkToken(latestId, AllocToken(DToken.Make(DTokenKind.String, GetStringId(Section.CompressedData))));
            IncToken(rootId);

            AllocToken(DToken.Make(DTokenKind.ByteString, 0));
        }
    }

    protected override ReadOnlySpan<byte> DecodeUtf8Bytes(DToken token)
    {
        var stringId = (int)token;
        var section = (Section)(stringId >> 8);
        var id = stringId & 0xff;

        if (id > 0)
        {
            switch (section)
            {
                case Section.FileHeader:
                    return Encoding.UTF8.GetBytes(((BitmapFileHeader)id).ToString());
                case Section.InfoHeader:
                    return Encoding.UTF8.GetBytes(((BitmapInfoHeader)id).ToString());
                case Section.Colors:
                    return Encoding.UTF8.GetBytes(((Colors)id).ToString());
            }
        }

        return Encoding.UTF8.GetBytes(section.ToString());
    }

    protected override long DecodeInteger(DToken token)
    {
        return (int)token;
    }

    protected override ReadOnlySpan<byte> DecodeByteString(DToken token)
    {
        var param = DecodeLengthOffsetPayload(token);

        return _source.Span.Slice(param.offset, param.length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long EncodeLengthOffsetPayload(int length, int offset)
    {
        Debug.Assert(length <= 0x1ffffff, "Parameter overflow");

        var payload = (long)offset;

        return payload | ((long)length << 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static (int length, int offset) DecodeLengthOffsetPayload(DToken param)
    {
        return ((int)(((long)param >> 31) & 0x1ffffff), (int)((long)param & 0x7fffffff));
    }

    private enum Section
    {
        FileHeader,
        InfoHeader,
        Colors,
        Lines,
        CompressedData
    }

    private enum BitmapFileHeader
    {
        BitmapFileHeader,
        Type,
        Size,
        Reserved1,
        Reserved2,
        OffBits
    }

    private enum BitmapInfoHeader
    {
        Undefined,

        //BitmapCoreHeader
        Size,
        Width,
        Height,
        Planes,
        BitCount,

        //BitmapInfoHeader
        Compression,
        SizeImage,
        XPelsPerMeter,
        YPelsPerMeter,
        ClrUsed,
        ClrImportant,

        //V4
        RedMask,
        GreenMask,
        BlueMask,
        AlphaMask,
        CSType,
        Endpoints,
        GammaRed,
        GammaGreen,
        GammaBlue,

        //V5
        Intent,
        ProfileData,
        ProfileSize,
        Reserved
    }

    private enum Colors
    {
        Colors,
        Blue,
        Green,
        Red
    }
}