// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using REDox.Json;

namespace REDox.Serialization.DataContractJson;

sealed class ExtensibleDataObjectConverter : DataConverterFactory
{
    private readonly System.Runtime.Serialization.Json.DataContractJsonSerializerSettings _settings;

    public ExtensibleDataObjectConverter(
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public override bool CanConvert(Type type)
    {
        return !type.IsInterface && !type.IsAbstract && type.IsAssignableTo(typeof(IExtensibleDataObject));
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var converterType = typeof(ExtensibleDataObjectConverter<>).MakeGenericType(type);

        return (DataConverter)Activator.CreateInstance(converterType, _settings)!;
    }
}

sealed class ExtensibleDataObjectConverter<T> : DataConverter<T>
{
    private readonly DataContractJsonSerializer _serializer;

    public ExtensibleDataObjectConverter(
        System.Runtime.Serialization.Json.DataContractJsonSerializerSettings settings)
    {
        _serializer = new DataContractJsonSerializer(typeof(T), settings);
    }

    public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
    {
        if (reader.GetToken(tokenId).Kind == DTokenKind.Null)
        {
            return default;
        }

        var json = JsonDocument.Encode(reader.ReadElement(tokenId));

        using var stream = new MemoryStream(json, false);

        lock (_serializer)
        {
            return (T?)_serializer.ReadObject(stream);
        }
    }

    public override void Write(DataWriter writer, T? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        byte[] json;

        using (var stream = new MemoryStream())
        {
            lock (_serializer)
            {
                _serializer.WriteObject(stream, value);
            }

            json = stream.ToArray();
        }

        var document = JsonDocument.Parse(json, writer.Settings);

        writer.WriteObject(document.RootElement, typeof(DElement));
    }
}