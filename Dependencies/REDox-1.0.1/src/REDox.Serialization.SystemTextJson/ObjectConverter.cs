// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.SystemTextJson;

sealed class ObjectConverter : DataConverter<object>
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode
    };

    private readonly DataContract _contract;
    private readonly bool _toNode;

    public ObjectConverter(SystemTextJsonSerializerSettings settings)
    {
        _contract = settings.GetContract(typeof(object));
        _toNode = settings.UnknownTypeHandling == JsonUnknownTypeHandling.JsonNode;
    }

    public override object ReadAsPropertyName(in DataReader reader, uint tokenId)
    {
        var value = reader.ReadString(tokenId);

        if (value == null)
        {
            throw new InvalidOperationException();
        }

        return value;
    }

    public override void WriteAsPropertyName(DataWriter writer, object value)
    {
        var name = value.ToString();

        if (name == null)
        {
            throw new InvalidOperationException();
        }

        if (writer.Settings.DictionaryKeyPolicy != null)
        {
            name = writer.Settings.DictionaryKeyPolicy.ConvertName(name);
        }

        writer.WriteString(name);
    }

    public override object? Read(in DataReader reader, uint tokenId, object? existingValue)
    {
        var token = reader.GetToken(tokenId);

        if (token.Kind == DTokenKind.Null)
        {
            return null;
        }

        if (token.Kind == DTokenKind.Control)
        {
            return null;
        }

        var json = Json.JsonDocument.Encode(reader.ReadElement(tokenId));

        var utf8JsonReader = new Utf8JsonReader(json);

        if (_toNode)
        {
            return JsonSerializer.Deserialize<object>(ref utf8JsonReader, s_options);
        }

        return JsonElement.ParseValue(ref utf8JsonReader);
    }

    public override void Write(DataWriter writer, object? value)
    {
        if (value == null)
        {
            writer.WriteNull();
        }
        else
        {
            var type = value.GetType();

            if (type == typeof(object))
            {
                var typed = _contract.AlwaysWriteTypeDiscriminator;

                if (typed)
                {
                    writer.WriteStartMap(1);
                    writer.WriteTypeDiscriminator(_contract);
                }
                else
                {
                    writer.WriteStartMap(0);
                }

                writer.WriteEndMap();
            }
            else
            {
                writer.WriteObject(value, typeof(object));
            }
        }
    }
}