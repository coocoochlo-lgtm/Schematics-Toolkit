// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace REDox.Serialization.SystemTextJson;

public sealed class JsonConverterAdapter : DataConverterFactory
{
    private readonly JsonSerializerOptions _options;

    public JsonConverterAdapter(JsonConverter converter, JsonSerializerOptions? options = null)
    {
        Converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _options = options ?? JsonSerializerOptions.Default;
    }

    public JsonConverter Converter { get; }

    public override bool CanConvert(Type type)
    {
        return Converter.CanConvert(type);
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        var converter = Converter;

        if (converter is JsonConverterFactory factory)
        {
            converter = factory.CreateConverter(type, _options)
                        ?? throw new InvalidOperationException(
                            $"'{factory.GetType()}' cannot create a converter for '{type}'.");
        }

        var adapterType = typeof(AdapterConverter<>).MakeGenericType(type);

        return (DataConverter)Activator.CreateInstance(adapterType, converter, _options)!;
    }

    private sealed class AdapterConverter<T> : DataConverter<T>
    {
        private static readonly JsonElementConverter s_elementConverter = new();

        private readonly JsonConverter<T> _converter;
        private readonly JsonSerializerOptions _options;

        public AdapterConverter(JsonConverter converter, JsonSerializerOptions? options = null)
        {
            _converter = converter as JsonConverter<T>
                         ?? throw new ArgumentException(
                             $"'{converter.GetType()}' is not a converter for '{typeof(T)}'.", nameof(converter));
            _options = options ?? JsonSerializerOptions.Default;
        }

        public override T ReadAsPropertyName(in DataReader reader, uint tokenId)
        {
            var propertyName = reader.ReadString(tokenId);
            var buffer = new ArrayBufferWriter<byte>();

            using (var utf8Writer = new Utf8JsonWriter(buffer))
            {
                utf8Writer.WriteStartObject();
                utf8Writer.WritePropertyName(propertyName);
                utf8Writer.WriteNullValue();
                utf8Writer.WriteEndObject();
            }

            var utf8Reader = new Utf8JsonReader(buffer.WrittenSpan);

            if (!utf8Reader.Read() || utf8Reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            if (!utf8Reader.Read() || utf8Reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            return _converter.ReadAsPropertyName(ref utf8Reader, typeof(T), _options);
        }

        public override void WriteAsPropertyName(DataWriter writer, T value)
        {
            if (value is null)
            {
                throw new JsonException();
            }

            var buffer = new ArrayBufferWriter<byte>();

            using (var utf8Writer = new Utf8JsonWriter(buffer))
            {
                utf8Writer.WriteStartObject();
                _converter.WriteAsPropertyName(utf8Writer, value, _options);
                utf8Writer.WriteNullValue();
                utf8Writer.WriteEndObject();
            }

            var utf8Reader = new Utf8JsonReader(buffer.WrittenSpan);

            if (!utf8Reader.Read() || utf8Reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException();
            }

            if (!utf8Reader.Read() || utf8Reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException();
            }

            writer.WriteString(utf8Reader.GetString() ?? throw new JsonException());
        }

        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            var json = Json.JsonDocument.Encode(reader.ReadElement(tokenId));

            var utf8Reader = new Utf8JsonReader(json);

            if (!utf8Reader.Read())
            {
                return default;
            }

            if (utf8Reader.TokenType == JsonTokenType.Null && !_converter.HandleNull)
            {
                return default;
            }

            return _converter.Read(ref utf8Reader, typeof(T), _options);
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null && !_converter.HandleNull)
            {
                writer.WriteNull();
                return;
            }

            var buffer = new ArrayBufferWriter<byte>();

            using (var utf8Writer = new Utf8JsonWriter(buffer))
            {
                _converter.Write(utf8Writer, value!, _options);
            }

            using var document = JsonDocument.Parse(buffer.WrittenMemory);

            s_elementConverter.Write(writer, document.RootElement);
        }
    }
}