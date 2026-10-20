// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using REDox.Serialization.Metadata;

namespace REDox.Serialization;

public readonly ref struct DataReader
{
    private readonly DElement _element;
    private readonly ReferenceResolver? _referenceResolver;

    public SerializerSettings Settings => Document.Settings;

    public Document Document => _element.Document;

    public uint RootId => _element.Id;

    public DElement RootElement => _element;

    public bool IsNullToken(uint tokenId)
    {
        return GetToken(tokenId).Kind == DTokenKind.Null;
    }

    public DataReader(DElement element)
    {
        _element = element;

        if (Settings.PreserveReferencesHandling != PreserveReferencesHandling.None)
        {
            _referenceResolver = Settings.CreateReferenceResolver();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ReadBoolean(uint tokenId)
    {
        if (!Document.TryGetBooleanExact(tokenId, out var value))
        {
            value = Document.GetBooleanValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T? ReadValue<T>(uint tokenId, T? existingValue)
    {
        var converter = Document.Settings.GetRootConverter<T>();

        if (converter is DataConverter<T> conv)
        {
            return conv.Read(this, tokenId, existingValue);
        }

        return (T?)converter.ReadObject(in this, typeof(T), tokenId, existingValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ReadObject(uint tokenId, Type outputType, object? existingValue)
    {
        return Document.Settings.GetConverter(outputType).ReadObject(in this, outputType, tokenId, existingValue);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DElement ReadElement(uint tokenId)
    {
        return new DElement(Document, tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadTrivia(uint tokenId)
    {
        return Document.GetTriviaValue(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Guid ReadGuid(uint tokenId)
    {
        if (!Document.TryGetGuidExact(tokenId, out var value))
        {
            value = Document.GetGuidValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public char ReadChar(uint tokenId)
    {
        return Document.GetCharValue(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public sbyte ReadSByte(uint tokenId)
    {
        if (!Document.TryGetSignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetSignedIntegerValue(tokenId);
        }

        return checked((sbyte)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public short ReadInt16(uint tokenId)
    {
        if (!Document.TryGetSignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetSignedIntegerValue(tokenId);
        }

        return checked((short)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadInt32(uint tokenId)
    {
        if (!Document.TryGetSignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetSignedIntegerValue(tokenId);
        }

        return checked((int)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long ReadInt64(uint tokenId)
    {
        if (!Document.TryGetSignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetSignedIntegerValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte(uint tokenId)
    {
        if (!Document.TryGetUnsignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetUnsignedIntegerValue(tokenId);
        }

        return checked((byte)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort ReadUInt16(uint tokenId)
    {
        if (!Document.TryGetUnsignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetUnsignedIntegerValue(tokenId);
        }

        return checked((ushort)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint ReadUInt32(uint tokenId)
    {
        if (!Document.TryGetUnsignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetUnsignedIntegerValue(tokenId);
        }

        return checked((uint)value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong ReadUInt64(uint tokenId)
    {
        if (!Document.TryGetUnsignedIntegerExact(tokenId, out var value))
        {
            value = Document.GetUnsignedIntegerValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float ReadSingle(uint tokenId)
    {
        if (!Document.TryGetFloatingExact(tokenId, out var value))
        {
            value = Document.GetFloatingValue(tokenId);
        }

        return (float)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public double ReadDouble(uint tokenId)
    {
        if (!Document.TryGetFloatingExact(tokenId, out var value))
        {
            value = Document.GetFloatingValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal ReadDecimal(uint tokenId)
    {
        if (!Document.TryGetDecimalExact(tokenId, out var value))
        {
            value = Document.GetDecimalValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Half ReadHalf(uint tokenId)
    {
        if (!Document.TryGetFloatingExact(tokenId, out var value))
        {
            value = Document.GetFloatingValue(tokenId);
        }

        return (Half)value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadBigNumber(uint tokenId)
    {
        return Document.GetBigNumberValue(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string ReadString(uint tokenId)
    {
        if (!Document.TryGetStringExact(tokenId, out var value))
        {
            return Document.GetStringValue(tokenId);
        }

        return value!;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadUtf8String(uint tokenId)
    {
        if (!Document.TryGetUtf8BytesExact(tokenId, out var value))
        {
            value = Document.GetUtf8BytesValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadDateTime(uint tokenId)
    {
        if (!Document.TryGetDateTimeExact(tokenId, out var value))
        {
            value = Document.GetDateTimeValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset ReadDateTimeOffset(uint tokenId)
    {
        if (!Document.TryGetDateTimeOffsetExact(tokenId, out var value))
        {
            value = Document.GetDateTimeOffsetValue(tokenId);
        }

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<byte> ReadByteString(uint tokenId)
    {
        return Document.GetByteStringValue(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Document.KeyValueEnumerator EnumerateMap(uint tokenId)
    {
        return Document.EnumerateKeyValue(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Document.ValueEnumerator EnumerateArray(uint tokenId)
    {
        return Document.EnumerateValue(tokenId);
    }

    public Document.TriviaTokenEnumerator EnumerateTrivia(uint tokenId)
    {
        return Document.EnumerateTrivia(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetValueCount(uint tokenId)
    {
        return Document.GetValueCount(tokenId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DToken GetToken(uint tokenId)
    {
        return Document.GetToken(tokenId);
    }

    public void HandleException(SerializationError errorCode, Exception? e, object? instance, DataContract? contract,
        object? member,
        uint tokenId)
    {
        if (e is SerializationException)
        {
            ExceptionDispatchInfo.Capture(e).Throw();
        }

        var error = new SerializationException(errorCode, e)
        {
            Member = member,
            Contract = contract,
            Target = instance
        };

        if (Document.Settings.Error != null)
        {
            var args = new SerializationErrorEventArgs(error);
            Document.Settings.Error(Document, args);
            if (args.Handled)
            {
                return;
            }
        }

        throw error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AddReference(uint tokenId, object value)
    {
        _referenceResolver?.AddReference(in this, tokenId, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? ReadReference(uint tokenId)
    {
        return _referenceResolver?.ResolveReference(in this, tokenId);
    }
}

static class DataReaderExtension
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadTypedArray<T>(this in DataReader reader, DataContract contract, uint tokenId, ref T? value)
    {
        object? boxed = value;
        if (!reader.ReadTypedArray(contract, typeof(T), tokenId, ref boxed))
        {
            return false;
        }

        value = (T?)boxed;
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool ReadTypedObject<T>(this in DataReader reader, DataContract contract, uint tokenId, uint valueId,
        ref T? value)
    {
        object? boxed = value;
        if (!reader.ReadTypedObject(contract, typeof(T), tokenId, valueId, ref boxed))
        {
            return false;
        }

        value = (T?)boxed;
        return true;
    }

    public static bool ReadTypedObject(this in DataReader reader, DataContract contract, Type objectType, uint tokenId,
        uint valueId, ref object? value)
    {
        var token = reader.GetToken(valueId);

        DataConverter? converter = null;

        if (token.Type == DTokenType.Text)
        {
            converter =
                reader.Settings.GetConverter(contract, reader.ReadUtf8String(valueId));
        }
        else if (token.Kind == DTokenKind.Integer)
        {
            converter =
                reader.Settings.GetConverter(contract, reader.ReadInt32(valueId));
        }

        if (converter != null && converter.TargetType != objectType)
        {
            value = converter.ReadObject(in reader, objectType, tokenId, value);
            return true;
        }

        return false;
    }

    public static bool ReadTypedArray(this in DataReader reader, DataContract contract, Type objectType, uint tokenId,
        ref object? value)
    {
        var token = reader.GetToken(tokenId);

        if (token.Kind == DTokenKind.Null)
        {
            return true;
        }

        if (token.Type == DTokenType.Map)
        {
            uint typeId = 0;
            uint valuesId = 0;
            uint refId = 0;
            foreach (var kv in reader.EnumerateMap(tokenId))
            {
                var name = reader.ReadUtf8String(kv.Key);
                if (name.SequenceEqual(contract.TypeDiscriminatorPropertyName))
                {
                    typeId = kv.Value;
                }
                else
                {
                    if (name.SequenceEqual(Utf8Helper.ValuesTag))
                    {
                        valuesId = kv.Value;
                    }
                    else
                    {
                        if (name.SequenceEqual(Utf8Helper.IdTag))
                        {
                            refId = kv.Value;
                        }
                        else
                        {
                            if (name.SequenceEqual(Utf8Helper.RefTag))
                            {
                                value = reader.ReadReference(kv.Value);
                                return true;
                            }
                        }
                    }
                }
            }

            DataConverter? converter;

            if (typeId == 0)
            {
                converter = reader.Settings.GetConverter(objectType);
            }
            else
            {
                converter = reader.Settings.GetConverter(contract, reader.ReadUtf8String(typeId));
            }

            if (value != null && !value.GetType().IsAssignableTo(converter.TargetType))
            {
                value = null;
            }

            if (valuesId == 0)
            {
                throw new InvalidOperationException();
            }

            value = converter.ReadObject(in reader, objectType, valuesId, value);

            if (refId > 0 && value != null)
            {
                reader.AddReference(refId, value);
            }

            return true;
        }

        return false;
    }
}