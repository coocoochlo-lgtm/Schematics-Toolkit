// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;

namespace REDox.Serialization.Converters;

sealed class SpanTypeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (!type.IsGenericType)
        {
            return false;
        }

        var genericDef = type.GetGenericTypeDefinition();

        if ( /*genericDef == typeof(System.Span<>) ||
            genericDef == typeof(System.ReadOnlySpan<>) ||*/
            genericDef == typeof(Memory<>) ||
            genericDef == typeof(ReadOnlyMemory<>))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var genericDef = type.GetGenericTypeDefinition();

        if (genericDef == typeof(ReadOnlyMemory<>))
        {
            return ConverterHelper.CreateConverter(
                typeof(ReadOnlyMemoryConverter<>).MakeGenericType(type.GetGenericArguments()[0]), settings);
        }

        if (genericDef == typeof(Memory<>))
        {
            return ConverterHelper.CreateConverter(
                typeof(MemoryConverter<>).MakeGenericType(type.GetGenericArguments()[0]), settings);
        }

        throw new NotSupportedException($"The type '{type}' is not supported by SpanTypeConverter.");
    }


    private class MemoryConverter<T> : DataConverter<Memory<T?>>
    {
        private readonly DataConverter<T> _valueConverter;

        public MemoryConverter(SerializerSettings settings)
        {
            _valueConverter = (DataConverter<T>)settings.GetConverter(typeof(T));
        }

        public override Memory<T?> Read(in DataReader reader, uint tokenId, Memory<T?> existingValue)
        {
            if (typeof(T) == typeof(byte))
            {
                var data = reader.ReadByteString(tokenId).ToArray().AsMemory();

                return Unsafe.As<Memory<byte>, Memory<T?>>(ref data);
            }
            else
            {
                var data = new T?[reader.GetValueCount(tokenId)];
                var i = 0;

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    data[i++] = _valueConverter.Read(reader, valueId, default);
                }

                return data!.AsMemory();
            }
        }

        public override void Write(DataWriter writer, Memory<T?> value)
        {
            if (typeof(T) == typeof(byte))
            {
                writer.WriteByteString(Unsafe.As<Memory<T?>, Memory<byte>>(ref value).Span);
            }
            else
            {
                writer.WriteStartArray(value.Length);
                foreach (var v in value.Span)
                {
                    _valueConverter.Write(writer, v);
                }

                writer.WriteEndArray();
            }
        }
    }

    private class ReadOnlyMemoryConverter<T> : DataConverter<ReadOnlyMemory<T?>>
    {
        private readonly DataConverter<T> _valueConverter;

        public ReadOnlyMemoryConverter(SerializerSettings settings)
        {
            _valueConverter = (DataConverter<T>)settings.GetConverter(typeof(T));
        }

        public override ReadOnlyMemory<T?> Read(in DataReader reader, uint tokenId,
            ReadOnlyMemory<T?> existingValue)
        {
            if (typeof(T) == typeof(byte))
            {
                var data = (ReadOnlyMemory<byte>)reader.ReadByteString(tokenId).ToArray().AsMemory();

                return Unsafe.As<ReadOnlyMemory<byte>, ReadOnlyMemory<T?>>(ref data);
            }
            else
            {
                var data = new T?[reader.GetValueCount(tokenId)];
                var i = 0;

                foreach (var valueId in reader.EnumerateArray(tokenId))
                {
                    data[i++] = _valueConverter.Read(reader, valueId, default);
                }

                return data!.AsMemory();
            }
        }

        public override void Write(DataWriter writer, ReadOnlyMemory<T?> value)
        {
            if (typeof(T) == typeof(byte))
            {
                writer.WriteByteString(Unsafe.As<ReadOnlyMemory<T?>, ReadOnlyMemory<byte>>(ref value).Span);
            }
            else
            {
                writer.WriteStartArray(value.Length);
                foreach (var v in value.Span)
                {
                    _valueConverter.Write(writer, v);
                }

                writer.WriteEndArray();
            }
        }
    }
}