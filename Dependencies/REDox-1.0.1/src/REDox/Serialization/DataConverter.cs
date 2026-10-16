// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Threading;
using REDox.Serialization.Metadata;

namespace REDox.Serialization;

public abstract class DataConverter
{
    protected internal abstract Type TargetType { get; }

    protected internal virtual DataConverter ResolvePropertyConverter(DataProperty property,
        SerializerSettings settings)
    {
        return this;
    }

    public virtual bool CanConvert(Type type)
    {
        return type == TargetType;
    }

    public abstract object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue);

    public abstract void WriteObject(DataWriter writer, Type objectType, object? value);

    public abstract object ReadObjectAsPropertyName(in DataReader reader, uint tokenId);

    public abstract void WriteObjectAsPropertyName(DataWriter writer, object value);

    protected Scope EnterScope(SerializerSettings settings)
    {
        return new Scope(settings.LockObj);
    }

    protected readonly ref struct Scope
    {
        private readonly object _lockObj;

        internal Scope(object lockObj)
        {
            _lockObj = lockObj;
            Monitor.Enter(_lockObj);
        }

        public void Dispose()
        {
            Monitor.Exit(_lockObj);
        }
    }
}

public abstract class DataConverter<T> : DataConverter
{
    protected internal sealed override Type TargetType => typeof(T);

    public override bool CanConvert(Type type)
    {
        return type == typeof(T);
    }

    public override string ToString()
    {
        return TargetType.ToString();
    }

    public abstract T? Read(in DataReader reader, uint tokenId, T? existingValue);

    public abstract void Write(DataWriter writer, T? value);

    public virtual T ReadAsPropertyName(in DataReader reader, uint tokenId)
    {
        throw new NotSupportedException();
    }

    public virtual void WriteAsPropertyName(DataWriter writer, T value)
    {
        throw new NotSupportedException();
    }

    public sealed override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
    {
        return ReadAsPropertyName(in reader, tokenId)!;
    }

    public sealed override void WriteObjectAsPropertyName(DataWriter writer, object value)
    {
        WriteAsPropertyName(writer, (T)value);
    }

    public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
    {
        if (existingValue == null)
        {
            return Read(reader, tokenId, default);
        }

        return Read(reader, tokenId, (T?)existingValue);
    }

    public override void WriteObject(DataWriter writer, Type objectType, object? value)
    {
        Write(writer, (T?)value);
    }
}

public abstract class DataConverterFactory : DataConverter
{
    protected internal sealed override Type TargetType => throw new NotSupportedException();

    public abstract DataConverter CreateConverter(Type type, SerializerSettings settings);

    public sealed override object? ReadObject(in DataReader reader, Type objectType, uint tokenId,
        object? existingValue)
    {
        throw new NotSupportedException();
    }

    public sealed override void WriteObject(DataWriter writer, Type objectType, object? value)
    {
        throw new NotSupportedException();
    }

    public sealed override void WriteObjectAsPropertyName(DataWriter writer, object? value)
    {
        throw new NotSupportedException();
    }

    public sealed override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
    {
        throw new NotSupportedException();
    }
}

public abstract class DataCollectionConverterFactory : DataConverterFactory
{
    public sealed override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        return CreateCollectionConverter(type, null, settings);
    }

    public abstract DataConverter CreateCollectionConverter(Type type, DataConverter? itemConverter,
        SerializerSettings settings);
}