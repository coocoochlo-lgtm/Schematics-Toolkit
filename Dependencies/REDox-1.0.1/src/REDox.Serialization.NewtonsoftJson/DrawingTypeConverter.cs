// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Drawing;

namespace REDox.Serialization.NewtonsoftJson;

sealed class DrawingTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type == typeof(Color))
        {
            return true;
        }

        if (type == typeof(Point))
        {
            return true;
        }

        if (type == typeof(Size) || type == typeof(SizeF))
        {
            return true;
        }

        if (type == typeof(Rectangle))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(Color))
        {
            return new ColorConverter();
        }

        if (type == typeof(Point))
        {
            return new PointConverter();
        }

        if (type == typeof(Size))
        {
            return new SizeConverter();
        }

        if (type == typeof(Rectangle))
        {
            return new RectangleConverter();
        }

        if (type == typeof(PointF))
        {
            return new PointFConverter();
        }

        if (type == typeof(SizeF))
        {
            return new SizeFConverter();
        }

        if (type == typeof(RectangleF))
        {
            return new RectangleFConverter();
        }

        throw new NotSupportedException("Type not supported: " + type.FullName);
    }

    private static int ReadArray(ReadOnlySpan<byte> str, Span<int> dest)
    {
        var count = 0;

        var pt = 0;
        while (pt < str.Length)
        {
            if (str[pt] == ',' || str[pt] <= 0x20)
            {
                pt++;
                continue;
            }

            if (Utf8Parser.TryParse(str.Slice(pt), out dest[count], out var bytesConsumed))
            {
                pt += bytesConsumed;
                count++;
            }
            else
            {
                return count;
            }
        }

        return count;
    }

    private static void WriteArray(DataWriter writer, ReadOnlySpan<int> src)
    {
        Span<byte> buf = stackalloc byte[src.Length * 16];
        var pt = 0;

        for (var i = 0; i < src.Length; i++)
        {
            if (i > 0)
            {
                buf[pt++] = (byte)',';
                buf[pt++] = (byte)' ';
            }

            if (Utf8Formatter.TryFormat(src[i], buf.Slice(pt), out var bytesWritten))
            {
                pt += bytesWritten;
            }
        }

        writer.WriteString(buf.Slice(0, pt));
    }

    private static int ReadArray(ReadOnlySpan<byte> str, Span<float> dest)
    {
        var count = 0;

        var pt = 0;
        while (pt < str.Length)
        {
            if (str[pt] == ',' || str[pt] <= 0x20)
            {
                pt++;
                continue;
            }

            if (Utf8Parser.TryParse(str.Slice(pt), out dest[count], out var bytesConsumed))
            {
                pt += bytesConsumed;
                count++;
            }
            else
            {
                return count;
            }
        }

        return count;
    }

    private static void WriteArray(DataWriter writer, ReadOnlySpan<float> src)
    {
        Span<byte> buf = stackalloc byte[src.Length * 16];
        var pt = 0;

        for (var i = 0; i < src.Length; i++)
        {
            if (i > 0)
            {
                buf[pt++] = (byte)',';
                buf[pt++] = (byte)' ';
            }

            if (Utf8Formatter.TryFormat(src[i], buf.Slice(pt), out var bytesWritten))
            {
                pt += bytesWritten;
            }
        }

        writer.WriteString(buf.Slice(0, pt));
    }

    private class PointConverter : DataConverter<Point>
    {
        public override Point Read(in DataReader reader, uint tokenId, Point existingValue)
        {
            Span<int> list = stackalloc int[2];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 2)
            {
                throw new FormatException();
            }

            return new Point(list[0], list[1]);
        }

        public override void Write(DataWriter writer, Point value)
        {
            Span<int> list = stackalloc int[2];
            list[0] = value.X;
            list[1] = value.Y;
            WriteArray(writer, list);
        }
    }

    private class SizeConverter : DataConverter<Size>
    {
        public override Size Read(in DataReader reader, uint tokenId, Size existingValue)
        {
            Span<int> list = stackalloc int[2];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 2)
            {
                throw new FormatException();
            }

            return new Size(list[0], list[1]);
        }

        public override void Write(DataWriter writer, Size value)
        {
            Span<int> list = stackalloc int[2];
            list[0] = value.Width;
            list[1] = value.Height;
            WriteArray(writer, list);
        }
    }

    private class RectangleConverter : DataConverter<Rectangle>
    {
        public override Rectangle Read(in DataReader reader, uint tokenId, Rectangle existingValue)
        {
            Span<int> list = stackalloc int[4];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 4)
            {
                throw new FormatException();
            }

            return new Rectangle(list[0], list[1], list[2], list[3]);
        }

        public override void Write(DataWriter writer, Rectangle value)
        {
            Span<int> list = stackalloc int[4];
            list[0] = value.X;
            list[1] = value.Y;
            list[2] = value.Width;
            list[3] = value.Height;
            WriteArray(writer, list);
        }
    }

    private class PointFConverter : DataConverter<PointF>
    {
        public override PointF Read(in DataReader reader, uint tokenId, PointF existingValue)
        {
            Span<float> list = stackalloc float[2];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 2)
            {
                throw new FormatException();
            }

            return new PointF(list[0], list[1]);
        }

        public override void Write(DataWriter writer, PointF value)
        {
            Span<float> list = stackalloc float[2];
            list[0] = value.X;
            list[1] = value.Y;
            WriteArray(writer, list);
        }
    }

    private class SizeFConverter : DataConverter<SizeF>
    {
        public override SizeF Read(in DataReader reader, uint tokenId, SizeF existingValue)
        {
            Span<float> list = stackalloc float[2];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 2)
            {
                throw new FormatException();
            }

            return new SizeF(list[0], list[1]);
        }

        public override void Write(DataWriter writer, SizeF value)
        {
            Span<float> list = stackalloc float[2];
            list[0] = value.Width;
            list[1] = value.Height;
            WriteArray(writer, list);
        }
    }

    private class RectangleFConverter : DataConverter<RectangleF>
    {
        public override RectangleF Read(in DataReader reader, uint tokenId, RectangleF existingValue)
        {
            Span<float> list = stackalloc float[4];
            var count = ReadArray(reader.ReadUtf8String(tokenId), list);
            if (count != 4)
            {
                throw new FormatException();
            }

            return new RectangleF(list[0], list[1], list[2], list[3]);
        }

        public override void Write(DataWriter writer, RectangleF value)
        {
            Span<float> list = stackalloc float[4];
            list[0] = value.X;
            list[1] = value.Y;
            list[2] = value.Width;
            list[3] = value.Height;
            WriteArray(writer, list);
        }
    }

    private class ColorConverter : DataConverter<Color>
    {
        public override Color Read(in DataReader reader, uint tokenId, Color existingValue)
        {
            var ss = reader.ReadString(tokenId);

            if (ss == null)
            {
                return default;
            }

            var color = Color.FromName(ss);

            if (color.ToArgb() == 0)
            {
                Span<int> list = stackalloc int[4];
                var count = ReadArray(reader.ReadUtf8String(tokenId), list);

                switch (count)
                {
                    case 3:
                        return Color.FromArgb(list[0], list[1], list[2]);
                    case 4:
                        return Color.FromArgb(list[0], list[1], list[2], list[3]);
                    default:
                        throw new FormatException(ss);
                }
            }

            return color;
        }

        public override void Write(DataWriter writer, Color value)
        {
            if (value.IsKnownColor)
            {
                writer.WriteString(value.Name);
            }
            else
            {
                if (value.A == 255)
                {
                    Span<int> list = stackalloc int[3];
                    list[0] = value.R;
                    list[1] = value.G;
                    list[2] = value.B;
                    WriteArray(writer, list);
                }
                else
                {
                    Span<int> list = stackalloc int[4];
                    list[0] = value.A;
                    list[1] = value.R;
                    list[2] = value.G;
                    list[3] = value.B;
                    WriteArray(writer, list);
                }
            }
        }
    }
}