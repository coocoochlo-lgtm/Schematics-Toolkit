// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using REDox.Json;

namespace REDox.Serialization.NewtonsoftJson;

/// <summary>
///     Adapts a <see cref="JsonConverter" /> to a <see cref="DataConverter" />.
/// </summary>
public sealed class JsonConverterAdapter : DataConverterFactory
{
    private readonly Newtonsoft.Json.JsonSerializer _serializer;

    public JsonConverterAdapter(JsonConverter converter, JsonSerializerSettings? settings = null)
    {
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _serializer = settings != null
            ? Newtonsoft.Json.JsonSerializer.Create(settings)
            : Newtonsoft.Json.JsonSerializer.CreateDefault();
    }

    public JsonConverter Converter { get; }

    public override bool CanConvert(Type type)
    {
        return Converter.CanConvert(type);
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var adapterType = typeof(JsonConverterAdapter<>).MakeGenericType(type);

        return (DataConverter)Activator.CreateInstance(adapterType, Converter, _serializer)!;
    }
}

/// <summary>
///     Bridges a <see cref="JsonConverter" /> to the REDox serialization pipeline.
/// </summary>
public sealed class JsonConverterAdapter<T> : DataConverter<T>
{
    private readonly JsonConverter _converter;
    private readonly Newtonsoft.Json.JsonSerializer _serializer;

    public JsonConverterAdapter(JsonConverter converter, Newtonsoft.Json.JsonSerializer? serializer = null)
    {
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _serializer = serializer ?? Newtonsoft.Json.JsonSerializer.CreateDefault();
    }

    public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
    {
        if (!_converter.CanRead)
        {
            throw new NotSupportedException($"'{_converter.GetType()}' does not support reading.");
        }

        var json = JsonDocument.Encode(reader.ReadElement(tokenId));

        using var textReader = new StringReader(Encoding.UTF8.GetString(json));
        using var jsonReader = new JsonTextReader(textReader)
        {
            DateParseHandling = _serializer.DateParseHandling,
            DateTimeZoneHandling = _serializer.DateTimeZoneHandling,
            FloatParseHandling = _serializer.FloatParseHandling,
            Culture = _serializer.Culture,
            CloseInput = false
        };

        if (!jsonReader.Read())
        {
            return default;
        }

        if (jsonReader.TokenType == JsonToken.Null)
        {
            return default;
        }

        var value = _converter.ReadJson(jsonReader, typeof(T), existingValue, _serializer);

        return value == null ? default : (T?)value;
    }

    public override void Write(DataWriter writer, T? value)
    {
        if (value == null || !_converter.CanWrite)
        {
            writer.WriteNull();
            return;
        }

        var tokenWriter = new JTokenWriter
        {
            DateFormatHandling = _serializer.DateFormatHandling,
            DateTimeZoneHandling = _serializer.DateTimeZoneHandling,
            FloatFormatHandling = _serializer.FloatFormatHandling,
            Culture = _serializer.Culture
        };

        _converter.WriteJson(tokenWriter, value, _serializer);

        JTokenConverter.WriteJToken(writer, tokenWriter.Token);
    }
}