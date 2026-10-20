// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using Newtonsoft.Json.Linq;

namespace REDox.Serialization.NewtonsoftJson;

sealed class JTokenConverter : DataConverterFactory
{
    private static readonly JObjectConverter _jobjectConverter = new();
    private static readonly JArrayConverter _arrayConverter = new();
    private static readonly JValueConverter _valueConverter = new();
    private static readonly JTokenConverterImpl _tokenConverter = new();
    private static readonly JContainerConverter _containerConverter = new();
    private static readonly JConstructorConverter _ConstructorConverter = new();
    private static readonly JPropertyConverter _propertyConverter = new();

    public override bool CanConvert(Type type)
    {
        if (type.IsAssignableTo(typeof(JToken)))
        {
            return true;
        }

        return false;
    }

    public override DataConverter CreateConverter(Type type, SerializerSettings settings)
    {
        if (type == typeof(JObject))
        {
            return _jobjectConverter;
        }

        if (type == typeof(JArray))
        {
            return _arrayConverter;
        }

        if (type == typeof(JValue))
        {
            return _valueConverter;
        }

        if (type == typeof(JToken))
        {
            return _tokenConverter;
        }

        if (type == typeof(JContainer))
        {
            return _containerConverter;
        }

        if (type == typeof(JConstructor))
        {
            return _ConstructorConverter;
        }

        if (type == typeof(JProperty))
        {
            return _propertyConverter;
        }

        throw new NotSupportedException($"The type '{type}' is not supported by JTokenConverter.");
    }

    internal static void WriteJToken(DataWriter writer, JToken? value)
    {
        if (value == null)
        {
            writer.WriteNull();
            return;
        }

        switch (value.Type)
        {
            case JTokenType.Array:
                {
                    var arr = (JArray)value;
                    writer.WriteStartArray(arr.Count);
                    foreach (var v in arr)
                    {
                        WriteJToken(writer, v);
                    }

                    writer.WriteEndArray();
                }
                break;
            case JTokenType.Object:
                {
                    var obj = (JObject)value;
                    writer.WriteStartMap(obj.Count);
                    foreach (var kv in obj)
                    {
                        writer.WriteString(kv.Key);
                        WriteJToken(writer, kv.Value);
                    }

                    writer.WriteEndMap();
                }
                break;
            case JTokenType.Null:
                writer.WriteNull();
                break;
            case JTokenType.String:
                writer.WriteString((string)value!);
                break;
            case JTokenType.Integer:
                writer.WriteInt64((long)value);
                break;
            case JTokenType.Boolean:
                writer.WriteBoolean((bool)value);
                break;
            case JTokenType.TimeSpan:
                writer.WriteString(((TimeSpan)value).ToString("c"));
                break;
            case JTokenType.Date:
                writer.WriteDateTime((DateTime)value);
                break;
            case JTokenType.Bytes:
                writer.WriteByteString((byte[])value!);
                break;
            case JTokenType.Float:
                writer.WriteDouble((double)value);
                break;
            case JTokenType.Uri:
                writer.WriteString(((Uri)value!).OriginalString);
                break;
            case JTokenType.Guid:
                writer.WriteGuid((Guid)value);
                break;
            default:
                writer.WriteNull();
                break;
        }
    }

    private class JObjectConverter : DataConverter<JObject>
    {
        public override JObject? Read(in DataReader reader, uint tokenId, JObject? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JObject? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JArrayConverter : DataConverter<JArray>
    {
        public override JArray? Read(in DataReader reader, uint tokenId, JArray? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JArray? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JValueConverter : DataConverter<JValue>
    {
        public override JValue? Read(in DataReader reader, uint tokenId, JValue? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JValue? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JTokenConverterImpl : DataConverter<JToken>
    {
        public override JToken? Read(in DataReader reader, uint tokenId, JToken? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JToken? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JContainerConverter : DataConverter<JContainer>
    {
        public override JContainer? Read(in DataReader reader, uint tokenId, JContainer? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JContainer? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JConstructorConverter : DataConverter<JConstructor>
    {
        public override JConstructor? Read(in DataReader reader, uint tokenId, JConstructor? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JConstructor? value)
        {
            WriteJToken(writer, value);
        }
    }

    private class JPropertyConverter : DataConverter<JProperty>
    {
        public override JProperty? Read(in DataReader reader, uint tokenId, JProperty? existingValue)
        {
            throw new NotImplementedException();
        }

        public override void Write(DataWriter writer, JProperty? value)
        {
            WriteJToken(writer, value);
        }
    }
}