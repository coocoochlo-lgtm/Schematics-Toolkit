// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Runtime.CompilerServices;
using REDox.Serialization;

namespace REDox;

public abstract class Serializer
{
    protected internal static TValue? DeserializeInternal<TValue>(DElement element)
    {
        var reader = new DataReader(element);

        try
        {
            return reader.ReadValue<TValue>(reader.RootId, default);
        }
        catch (Exception e)
        {
            reader.HandleException(SerializationError.FailedToRead, e, null,
                reader.Settings.GetContract(typeof(TValue)), null,
                element.Id);
            return default;
        }
    }

    protected internal static object? DeserializeInternal(DElement element, Type type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var converter = element.Document.Settings.GetConverter(type);
        var reader = new DataReader(element);

        try
        {
            return converter.ReadObject(reader, type, reader.RootId, null);
        }
        catch (Exception e)
        {
            reader.HandleException(SerializationError.FailedToRead, e, null, reader.Settings.GetContract(type), null,
                element.Id);
            return null;
        }
    }

    protected internal static TValue DeserializeToInternal<TValue>(DElement element, TValue target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var reader = new DataReader(element);

        try
        {
            var result = reader.ReadValue(reader.RootId, target);

            if (result == null)
            {
                return target;
            }

            return result;
        }
        catch (Exception e)
        {
            reader.HandleException(SerializationError.FailedToRead, e, null,
                reader.Settings.GetContract(typeof(TValue)), null,
                element.Id);
            return target;
        }
    }

    protected internal static object DeserializeToInternal(DElement element, Type type, object target)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(target);

        var converter = element.Document.Settings.GetConverter(type);
        var reader = new DataReader(element);

        try
        {
            var result = converter.ReadObject(reader, type, reader.RootId, target);

            if (result == null)
            {
                return target;
            }

            return result;
        }
        catch (Exception e)
        {
            reader.HandleException(SerializationError.FailedToRead, e, null, reader.Settings.GetContract(type), null,
                element.Id);
            return target;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected static DataConverter GetRootConverter<T>(SerializerSettings settings)
    {
        return settings.GetRootConverter<T>();
    }
}