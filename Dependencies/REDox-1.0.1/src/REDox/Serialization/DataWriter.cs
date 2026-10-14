// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using REDox.Serialization.Metadata;

namespace REDox.Serialization;

public abstract class DataWriter
{
    private ReferenceResolver? _referenceResolver;

    protected DataWriter()
    {
        Settings = SerializerSettings.Default;
    }

    public SerializerSettings Settings { get; private set; }

    public bool RequiresStringKeys { get; protected set; }

    protected void Reset(SerializerSettings settings)
    {
        Settings = settings;

        if (settings.PreserveReferencesHandling != PreserveReferencesHandling.None ||
            settings.ReferenceLoopHandling != ReferenceLoopHandling.Serialize)
        {
            _referenceResolver = settings.CreateReferenceResolver();
        }
        else
        {
            _referenceResolver = null;
        }
    }

    public void WriteValue<T>(T value)
    {
        var converter = Settings.GetRootConverter<T>();

        if (converter is DataConverter<T> conv)
        {
            conv.Write(this, value);
        }
        else
        {
            converter.WriteObject(this, typeof(T), value);
        }
    }

    public void WriteObject(object? value, Type inputType)
    {
        Settings.GetConverter(value?.GetType() ?? inputType).WriteObject(this, inputType, value);
    }

    public void WriteTypeDiscriminator(DataContract td)
    {
        WriteSymbol(td.TypeDiscriminatorPropertyName, SymbolKind.Metadata);
        if (td.TypeDiscriminator.Symbol != null)
        {
            WriteSymbol(td.TypeDiscriminator.Symbol, SymbolKind.TypeName);
        }
        else
        {
            WriteInt32(td.TypeDiscriminator.Id);
        }
    }

    public abstract void WriteNull();

    public abstract void WriteChar(char value);

    protected internal virtual void WriteBooleanValues(ReadOnlySpan<bool> values)
    {
        foreach (var value in values)
        {
            WriteBoolean(value);
        }
    }

    protected internal virtual void WriteSingleValues(ReadOnlySpan<float> values)
    {
        foreach (var value in values)
        {
            WriteSingle(value);
        }
    }

    protected internal virtual void WriteDoubleValues(ReadOnlySpan<double> values)
    {
        foreach (var value in values)
        {
            WriteDouble(value);
        }
    }

    protected internal virtual void WriteInt32Values(ReadOnlySpan<int> values)
    {
        foreach (var value in values)
        {
            WriteInt32(value);
        }
    }

    protected internal virtual void WriteInt64Values(ReadOnlySpan<long> values)
    {
        foreach (var value in values)
        {
            WriteInt64(value);
        }
    }

    protected internal virtual void WriteUInt32Values(ReadOnlySpan<uint> values)
    {
        foreach (var value in values)
        {
            WriteUInt32(value);
        }
    }

    protected internal virtual void WriteUInt64Values(ReadOnlySpan<ulong> values)
    {
        foreach (var value in values)
        {
            WriteUInt64(value);
        }
    }

    protected internal virtual void WriteGuidValues(ReadOnlySpan<Guid> values)
    {
        foreach (var value in values)
        {
            WriteGuid(value);
        }
    }

    protected internal virtual void WriteStringValues(ReadOnlySpan<string?> values)
    {
        foreach (var value in values)
        {
            if (value == null)
            {
                WriteNull();
            }
            else
            {
                WriteString(value);
            }
        }
    }

    protected internal virtual void WritePropertyInt32(Utf8Symbol propertyName, int value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteInt32(value);
    }

    protected internal virtual void WritePropertyUInt32(Utf8Symbol propertyName, uint value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteUInt32(value);
    }

    protected internal virtual void WritePropertyInt64(Utf8Symbol propertyName, long value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteInt64(value);
    }

    protected internal virtual void WritePropertyUInt64(Utf8Symbol propertyName, ulong value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteUInt64(value);
    }

    protected internal virtual void WritePropertyBoolean(Utf8Symbol propertyName, bool value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteBoolean(value);
    }

    protected internal virtual void WritePropertySingle(Utf8Symbol propertyName, float value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteSingle(value);
    }

    protected internal virtual void WritePropertyDouble(Utf8Symbol propertyName, double value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteDouble(value);
    }

    protected internal virtual void WritePropertyGuid(Utf8Symbol propertyName, Guid value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        WriteGuid(value);
    }

    protected internal virtual void WritePropertyString(Utf8Symbol propertyName, string? value)
    {
        WriteSymbol(propertyName, SymbolKind.Identifier);
        if (value != null)
        {
            WriteString(value);
        }
        else
        {
            WriteNull();
        }
    }

    public virtual void WriteSymbol(string value, SymbolKind kind = SymbolKind.Default)
    {
        WriteString(value);
    }

    public virtual void WriteSymbol(ReadOnlySpan<char> value, SymbolKind kind = SymbolKind.Default)
    {
        WriteString(value);
    }

    public virtual void WriteSymbol(ReadOnlySpan<byte> utf8Bytes, SymbolKind kind = SymbolKind.Default)
    {
        WriteString(utf8Bytes);
    }

    public abstract void WriteSymbol(Utf8Symbol value, SymbolKind kind = SymbolKind.Default);


    public virtual void WriteUInt32(uint value)
    {
        WriteUInt64(value);
    }

    public virtual void WriteInt32(int value)
    {
        WriteInt64(value);
    }

    public abstract void WriteInt64(long value);

    public abstract void WriteUInt64(ulong value);

    public abstract void WriteHalf(Half value);

    public abstract void WriteSingle(float value);

    public abstract void WriteDouble(double value);

    public abstract void WriteDecimal(decimal value);

    public abstract void WriteBigNumber(ReadOnlySpan<byte> value, BigNumberKind kind = BigNumberKind.Default);

    public abstract void WriteBoolean(bool value);

    public abstract void WriteString(string value);

    public abstract void WriteString(ReadOnlySpan<char> value);

    public abstract void WriteString(ReadOnlySpan<byte> utf8Bytes);

    public virtual void WriteNumberString(ReadOnlySpan<byte> utf8Bytes)
    {
        WriteString(utf8Bytes);
    }

    public abstract void WriteByteString(ReadOnlySpan<byte> value, ByteStringKind kind = ByteStringKind.Default);

    public abstract void WriteDateTime(DateTime value);

    public abstract void WriteDateTimeOffset(DateTimeOffset value);

    public abstract void WriteGuid(Guid value);

    public abstract void WriteStartMap(int? definiteLength);

    public abstract void WriteEndMap();

    public abstract void WriteStartArray(int? definiteLength);

    public abstract void WriteEndArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool CheckReferenced(object value)
    {
        if (_referenceResolver == null)
        {
            return false;
        }

        return _referenceResolver.CheckReferenced(value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryWriteReference(object value)
    {
        if (_referenceResolver == null)
        {
            _referenceResolver = Settings.CreateReferenceResolver();
        }

        return _referenceResolver.TryWriteReference(this, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteReferenceId()
    {
        if (_referenceResolver == null)
        {
            _referenceResolver = Settings.CreateReferenceResolver();
        }

        _referenceResolver.WriteReferenceId(this);
    }

    public bool IsCycleReference(object? value)
    {
        if (value == null)
        {
            return false;
        }

        return _referenceResolver!.IsCycleReference(value);
    }

    public void PushCycleReference(object value)
    {
        _referenceResolver!.PushCycleReference(value);
    }

    public void PopCycleReference()
    {
        _referenceResolver!.PopCycleReference();
    }

    public virtual void HandleException(Exception e, object? instance, DataContract? contract, object? member)
    {
        if (e is SerializationException)
        {
            ExceptionDispatchInfo.Capture(e).Throw();
        }

        var errorCode = SerializationError.FailedToWrite;

        if (e is ReferenceLoopException)
        {
            if (Settings.ReferenceLoopHandling == ReferenceLoopHandling.Ignore)
            {
                return;
            }

            errorCode = SerializationError.ReferenceLoopDetected;
        }

        var error = new SerializationException(errorCode, e)
        {
            Member = member,
            Target = instance,
            Contract = contract
        };

        if (Settings.Error != null)
        {
            var args = new SerializationErrorEventArgs(error);

            Settings.Error(this, args);

            if (args.Handled)
            {
                return;
            }
        }

        throw error;
    }
}

static class DataWriterExtension
{
    private static DataConverter? GetDerivedConverter(SerializerSettings settings, DataContract contract, Type type,
        DataProperty? property)
    {
        foreach (var derivedType in contract.DerivedTypes)
        {
            if (derivedType.Type == type)
            {
                if (derivedType.Converter != null)
                {
                    return derivedType.Converter;
                }

                return settings.GetConverter(type, property);
            }
        }

        return null;
    }

    public static bool WritePolymorphic<T>(this DataWriter writer, DataContract contract, DataProperty? property,
        Type type, T value)
    {
        if (contract.DerivedTypes.Count > 0)
        {
            for (;;)
            {
                var converter = GetDerivedConverter(writer.Settings, contract, type, property);

                if (converter != null)
                {
                    converter.WriteObject(writer, typeof(T), value);
                    return true;
                }

                switch (contract.UnknownDerivedTypeHandling)
                {
                    case UnknownDerivedTypeHandling.FailSerialization:
                        throw new InvalidOperationException(
                            $"Type '{type}' is not a registered derived type of '{contract.Type}'.");
                    case UnknownDerivedTypeHandling.FallBackToNearestAncestor:
                        if (type.BaseType == null || type.BaseType == typeof(T))
                        {
                            return false;
                        }

                        type = type.BaseType;
                        break;
                    default:
                        return false;
                }
            }
        }

        writer.Settings.GetConverter(type, property).WriteObject(writer, typeof(T), value);

        return true;
    }

    public static Scope BeginWriteCollection(this DataWriter writer, int count, DataContract td,
        bool typeNeeded,
        bool isReference, object value)
    {
        if (isReference && writer.TryWriteReference(value))
        {
            return default;
        }

        var cycleCheck = td.RequiresCycleCheck;

        if (cycleCheck)
        {
            if (writer.IsCycleReference(value))
            {
                if (writer.Settings.ReferenceLoopHandling == ReferenceLoopHandling.Null)
                {
                    writer.WriteNull();
                }

                if (writer.Settings.ReferenceLoopHandling == ReferenceLoopHandling.Error)
                {
                    throw new ReferenceLoopException();
                }

                return default;
            }

            writer.PushCycleReference(value);
        }

        var typed = td.AlwaysWriteTypeDiscriminator || (td.IsPolymorphic && typeNeeded);

        if (typed)
        {
            if (isReference)
            {
                writer.WriteStartMap(count + 2);
                writer.WriteReferenceId();
                writer.WriteTypeDiscriminator(td);
            }
            else
            {
                writer.WriteStartMap(count + 1);
                writer.WriteTypeDiscriminator(td);
            }
        }
        else
        {
            if (isReference)
            {
                writer.WriteStartMap(count + 1);
                writer.WriteReferenceId();
            }
            else
            {
                writer.WriteStartMap(count);
            }
        }

        return new Scope(writer, false, true, cycleCheck);
    }

    public static Scope BeginWriteArray(this DataWriter writer, int count, DataContract td, bool typeNeeded,
        bool isReference, object value)
    {
        if (isReference && writer.TryWriteReference(value))
        {
            return default;
        }

        var cycleCheck = td.RequiresCycleCheck;

        if (cycleCheck)
        {
            if (writer.IsCycleReference(value))
            {
                if (writer.Settings.ReferenceLoopHandling == ReferenceLoopHandling.Null)
                {
                    writer.WriteNull();
                }

                if (writer.Settings.ReferenceLoopHandling == ReferenceLoopHandling.Error)
                {
                    throw new ReferenceLoopException();
                }

                return default;
            }

            writer.PushCycleReference(value);
        }

        var typed = td.AlwaysWriteTypeDiscriminator || (td.IsPolymorphic && typeNeeded);

        if (typed || isReference)
        {
            if (isReference)
            {
                writer.WriteStartMap(typed ? 3 : 2);
                writer.WriteReferenceId();
            }
            else
            {
                writer.WriteStartMap(2);
            }

            if (typed)
            {
                writer.WriteTypeDiscriminator(td);
            }

            writer.WriteSymbol(Utf8Helper.ValuesTag, SymbolKind.Metadata);
            writer.WriteStartArray(count);

            return new Scope(writer, true, true, cycleCheck);
        }

        writer.WriteStartArray(count);

        return new Scope(writer, true, false, cycleCheck);
    }

    public readonly ref struct Scope
    {
        public Scope(DataWriter w, bool isArray, bool isObject, bool cycleCheck)
        {
            _writer = w;
            _isObject = isObject;
            _isArray = isArray;
            _cycleCheck = cycleCheck;
        }

        public bool Skipped => _writer == null;

        public void Dispose()
        {
            if (_isArray)
            {
                _writer.WriteEndArray();
            }

            if (_isObject)
            {
                _writer.WriteEndMap();
            }

            if (_cycleCheck)
            {
                _writer.PopCycleReference();
            }
        }

        private readonly DataWriter _writer;
        private readonly bool _isObject;
        private readonly bool _isArray;
        private readonly bool _cycleCheck;
    }
}