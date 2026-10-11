// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.Converters;

sealed class BinaryConverter : DataConverterFactory
{
    public BinaryConverter()
    {
        Kind = ByteStringKind.Default;
    }

    public BinaryConverter(ByteStringKind kind)
    {
        Kind = kind;
    }

    public ByteStringKind Kind { get; set; } = ByteStringKind.Default;

    public override bool CanConvert(Type type)
    {
        return type == typeof(byte[]);
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        return new Converter(settings) { Kind = Kind };
    }

    private class Converter : DataConverter<byte[]>
    {
        private readonly DataContract _contract;

        public Converter(SerializerSettings settings)
        {
            _contract = settings.GetContract(typeof(byte[]));
        }

        public ByteStringKind Kind { get; init; } = ByteStringKind.Default;

        public override byte[]? Read(in DataReader reader, uint tokenId, byte[]? existingValue)
        {
            var token = reader.GetToken(tokenId);

            if (token.IsContainer)
            {
                if (token.Type == DTokenType.Map)
                {
                    foreach (var kv in reader.EnumerateMap(tokenId))
                    {
                        var name = reader.ReadUtf8String(kv.Key);

                        if (name.SequenceEqual(Utf8Helper.ValueTag))
                        {
                            return Read(reader, kv.Value, existingValue);
                        }
                    }

                    throw new InvalidOperationException();
                }

                var bytes = new byte[reader.GetValueCount(tokenId)];
                var index = 0;

                foreach (var elementId in reader.EnumerateArray(tokenId))
                {
                    bytes[index++] = reader.ReadByte(elementId);
                }

                return bytes;
            }

            switch (token.Kind)
            {
                case DTokenKind.Null:
                    return null;
                case DTokenKind.ByteString:
                    return reader.ReadByteString(tokenId).ToArray();
                case DTokenKind.String:
                case DTokenKind.Symbol:
                    {
                        var ss = reader.ReadString(tokenId);
                        if (ss == null)
                        {
                            return null;
                        }

                        return Convert.FromBase64String(ss);
                    }
                default:
                    throw new InvalidOperationException();
            }
        }

        public override void Write(DataWriter writer, byte[]? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteData(writer, value, false);
            }
        }

        public override void WriteObject(DataWriter writer, Type objectType, object? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteData(writer, (byte[])value, objectType != typeof(byte[]));
            }
        }

        private void WriteData(DataWriter writer, byte[] value, bool typeNeeded)
        {
            var typed = _contract.AlwaysWriteTypeDiscriminator || (typeNeeded && _contract.IsPolymorphic);

            if (typed)
            {
                writer.WriteStartMap(2);
                writer.WriteTypeDiscriminator(_contract);
                writer.WriteSymbol(Utf8Helper.ValueTag, SymbolKind.Metadata);
                writer.WriteByteString(value, Kind);
                writer.WriteEndMap();
            }
            else
            {
                writer.WriteByteString(value, Kind);
            }
        }
    }
}