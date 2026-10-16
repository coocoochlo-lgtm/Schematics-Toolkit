// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Text;
using System.Text.Json.Nodes;
using REDox.Json;

namespace REDox.Serialization.SystemTextJson;

public sealed class JsonNodeConverter : DataConverterFactory
{
    public override bool CanConvert(Type type)
    {
        if (type.IsAssignableTo(typeof(JsonNode)))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(JsonNode))
        {
            return new NodeConverter();
        }

        if (type == typeof(JsonArray))
        {
            return new ArrayConverter();
        }

        if (type == typeof(JsonObject))
        {
            return new ObjectConverter();
        }

        if (type.IsAssignableTo(typeof(JsonValue)))
        {
            return (DataConverter)Activator.CreateInstance(typeof(ValueConverter<>).MakeGenericType(type))!;
        }

        throw new NotSupportedException($"The type {type} is not supported.");
    }

    private static JsonNode? ReadNode(in DataReader reader, uint tokenId)
    {
        var token = reader.GetToken(tokenId);

        var json = JsonDocument.Encode(reader.ReadElement(tokenId));

        return JsonNode.Parse(json);
    }

    private static void WriteJsonObject(DataWriter writer, JsonObject value)
    {
        writer.WriteStartMap(value.Count);
        foreach (var kv in value)
        {
            writer.WriteString(kv.Key);
            WriteNode(writer, kv.Value);
        }

        writer.WriteEndMap();
    }

    private static void WriteJsonArray(DataWriter writer, JsonArray value)
    {
        writer.WriteStartArray(value.Count);
        foreach (var element in value)
        {
            WriteNode(writer, element);
        }

        writer.WriteEndArray();
    }

    private static void WriteJsonValue(DataWriter writer, JsonValue value)
    {
        switch (value.GetValueKind())
        {
            case System.Text.Json.JsonValueKind.Number:
                writer.WriteBigNumber(Encoding.UTF8.GetBytes(value.ToJsonString()));
                break;
            case System.Text.Json.JsonValueKind.True:
            case System.Text.Json.JsonValueKind.False:
                writer.WriteBoolean((bool)value);
                break;
            case System.Text.Json.JsonValueKind.String:
                writer.WriteString((string)value!);
                break;
            case System.Text.Json.JsonValueKind.Null:
                writer.WriteNull();
                break;
        }
    }

    private static void WriteNode(DataWriter writer, JsonNode? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        if (value is JsonObject obj)
        {
            WriteJsonObject(writer, obj);
        }
        else
        {
            if (value is JsonArray arr)
            {
                WriteJsonArray(writer, arr);
            }
            else
            {
                if (value is JsonValue val)
                {
                    WriteJsonValue(writer, val);
                }
                else
                {
                    throw new NotSupportedException($"The type {value.GetType()} is not supported.");
                }
            }
        }
    }

    private class NodeConverter : DataConverter<JsonNode>
    {
        public override JsonNode? Read(in DataReader reader, uint tokenId, JsonNode? existingValue)
        {
            return ReadNode(reader, tokenId);
        }

        public override void Write(DataWriter writer, JsonNode? value)
        {
            WriteNode(writer, value);
        }
    }

    private class ArrayConverter : DataConverter<JsonArray>
    {
        public override JsonArray? Read(in DataReader reader, uint tokenId, JsonArray? existingValue)
        {
            return (JsonArray?)ReadNode(reader, tokenId);
        }

        public override void Write(DataWriter writer, JsonArray? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteJsonArray(writer, value);
            }
        }
    }

    private class ObjectConverter : DataConverter<JsonObject>
    {
        public override JsonObject? Read(in DataReader reader, uint tokenId, JsonObject? existingValue)
        {
            return (JsonObject?)ReadNode(reader, tokenId);
        }

        public override void Write(DataWriter writer, JsonObject? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteJsonObject(writer, value);
            }
        }
    }

    private class ValueConverter<T> : DataConverter<T> where T : JsonValue
    {
        public override T? Read(in DataReader reader, uint tokenId, T? existingValue)
        {
            return (T?)ReadNode(reader, tokenId);
        }

        public override void Write(DataWriter writer, T? value)
        {
            if (value == null)
            {
                writer.WriteNull();
            }
            else
            {
                WriteJsonValue(writer, value);
            }
        }
    }
}