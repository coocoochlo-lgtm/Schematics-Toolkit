// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.DataContractJson;

/// <summary>
///     Wraps a converter and reads dictionary keys via the public static Parse(string) method,
///     matching DataContractJsonSerializer behavior.
/// </summary>
sealed class ParseKeyConverterFactory : DataConverterFactory
{
    private readonly DataConverterFactory _factory;

    public ParseKeyConverterFactory(DataConverterFactory factory)
    {
        _factory = factory;
    }

    public override bool CanConvert(Type type)
    {
        return _factory.CanConvert(type);
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var converter = _factory.CreateConverter(type, settings);

        return Wrap(type, converter, settings);
    }

    internal static DataConverter Wrap(Type type, DataConverter converter, SerializerSettings settings)
    {
        if (typeof(DataConverter<>).MakeGenericType(type).IsInstanceOfType(converter))
        {
            return (DataConverter)Activator.CreateInstance(typeof(ParseKeyConverter<>).MakeGenericType(type),
                converter)!;
        }

        return new ParseKeyConverter(type, converter);
    }

    internal static MethodInfo? GetParseMethod(Type type)
    {
        return type.GetMethod("Parse", BindingFlags.Public | BindingFlags.Static, new[] { typeof(string) });
    }

    internal static object Parse(Type type, MethodInfo? parseMethod, string? value)
    {
        if (parseMethod == null)
        {
            throw new NotSupportedException(
                $"The key type '{type}' does not have a public static Parse method.");
        }

        if (value == null)
        {
            throw new InvalidCastException();
        }

        var result = parseMethod.Invoke(null, new object[] { value });

        if (result == null)
        {
            throw new InvalidCastException();
        }

        return result;
    }
}

sealed class ParseKeyConverter<T> : DataConverter<T>
{
    private readonly DataConverter<T> _converter;
    private readonly MethodInfo? _parseMethod;

    public ParseKeyConverter(DataConverter<T> converter)
    {
        _converter = converter;
        _parseMethod = ParseKeyConverterFactory.GetParseMethod(typeof(T));
    }

    protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
        SerializerSettings settings)
    {
        var converter = _converter.ResolvePropertyConverter(property, settings);

        if (ReferenceEquals(converter, _converter))
        {
            return this;
        }

        return ParseKeyConverterFactory.Wrap(typeof(T), converter, settings);
    }

    public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
    {
        return (T)ParseKeyConverterFactory.Parse(typeof(T), _parseMethod, reader.ReadString(tokenId));
    }

    public override void WriteAsPropertyName(DataWriter writer, T value)
    {
        _converter.WriteAsPropertyName(writer, value);
    }

    public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
    {
        return _converter.Read(reader, tokenId, existingValue);
    }

    public override void Write(DataWriter writer, T? value)
    {
        _converter.Write(writer, value);
    }

    public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
    {
        return _converter.ReadObject(reader, objectType, tokenId, existingValue);
    }

    public override void WriteObject(DataWriter writer, Type objectType, object? value)
    {
        _converter.WriteObject(writer, objectType, value);
    }
}

sealed class ParseKeyConverter : DataConverter
{
    private readonly DataConverter _converter;
    private readonly MethodInfo? _parseMethod;

    public ParseKeyConverter(Type type, DataConverter converter)
    {
        TargetType = type;
        _converter = converter;
        _parseMethod = ParseKeyConverterFactory.GetParseMethod(type);
    }

    protected internal override Type TargetType { get; }

    protected internal override DataConverter ResolvePropertyConverter(DataProperty property,
        SerializerSettings settings)
    {
        var converter = _converter.ResolvePropertyConverter(property, settings);

        if (ReferenceEquals(converter, _converter))
        {
            return this;
        }

        return new ParseKeyConverter(TargetType, converter);
    }

    public override object? ReadObject(in DataReader reader, Type objectType, uint tokenId, object? existingValue)
    {
        return _converter.ReadObject(reader, objectType, tokenId, existingValue);
    }

    public override void WriteObject(DataWriter writer, Type objectType, object? value)
    {
        _converter.WriteObject(writer, objectType, value);
    }

    public override object ReadObjectAsPropertyName(in DataReader reader, uint tokenId)
    {
        return ParseKeyConverterFactory.Parse(TargetType, _parseMethod, reader.ReadString(tokenId));
    }

    public override void WriteObjectAsPropertyName(DataWriter writer, object value)
    {
        _converter.WriteObjectAsPropertyName(writer, value);
    }
}