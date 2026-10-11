// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using Newtonsoft.Json.Linq;
using REDox.Json;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.NewtonsoftJson;

sealed class ObjectConverter : DataConverter<object>
{
    private readonly DataContract _contract;

    public ObjectConverter(SerializerSettings settings)
    {
        IsReference = (settings.PreserveReferencesHandling & PreserveReferencesHandling.Objects) != 0;

        _contract = settings.GetContract(typeof(object));
    }

    public bool IsReference { get; set; }

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

        if (token.IsContainer)
        {
            if (token.Type == DTokenType.Array)
            {
                if (existingValue != null && existingValue.GetType() != typeof(object))
                {
                    return reader.ReadObject(tokenId, existingValue.GetType(), null);
                }

                return JToken.Parse(JsonSerializer.Serialize(reader.ReadElement(tokenId), reader.Settings));
            }

            if (existingValue != null && existingValue.GetType() != typeof(object))
            {
                return reader.ReadObject(tokenId, existingValue.GetType(), existingValue);
            }

            var obj = new JObject();
            var refId = 0U;

            foreach (var kv in reader.EnumerateMap(tokenId))
            {
                var name = reader.ReadUtf8String(kv.Key);

                if (name.SequenceEqual(_contract.TypeDiscriminatorPropertyName))
                {
                    if (_contract.IsPolymorphic)
                    {
                        var converter =
                            reader.Settings.GetConverter(_contract, reader.ReadUtf8String(kv.Value));

                        if (converter.TargetType == typeof(object))
                        {
                            return new DObject(reader.Settings).AsElement();
                        }

                        return converter.ReadObject(reader, converter.TargetType, tokenId, null);
                    }
                }
                else if (name.SequenceEqual(Utf8Helper.RefTag))
                {
                    return reader.ReadReference(kv.Value);
                }
                else if (name.SequenceEqual(Utf8Helper.IdTag))
                {
                    refId = kv.Value;
                }
                else
                {
                    obj[reader.ReadString(kv.Key)] =
                        JToken.Parse(JsonSerializer.Serialize(reader.ReadElement(kv.Value), reader.Settings));
                }
            }

            if (refId > 0)
            {
                reader.AddReference(refId, obj);
            }

            return obj;
        }

        switch (token.Kind)
        {
            case DTokenKind.BigNumber:
                return reader.ReadDouble(tokenId);
            case DTokenKind.String:
            case DTokenKind.Symbol:
                return reader.ReadString(tokenId);
            case DTokenKind.Integer:
                return reader.ReadInt64(tokenId);
            case DTokenKind.Float:
                {
                    if (token.Variant == DTokenVariant.FloatDecimal)
                    {
                        return reader.ReadDecimal(tokenId);
                    }

                    var value = reader.ReadDouble(tokenId);

                    return value;
                }
            case DTokenKind.Boolean:
                return reader.ReadBoolean(tokenId);
            case DTokenKind.Timestamp:
                return reader.ReadDateTime(tokenId);
            case DTokenKind.ByteString:
                return reader.ReadByteString(tokenId).ToArray();
            case DTokenKind.Null:
                return null;
            default:
                return null;
        }
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
                if (IsReference && writer.TryWriteReference(value))
                {
                    return;
                }

                var typed = _contract.AlwaysWriteTypeDiscriminator;

                if (typed)
                {
                    if (IsReference)
                    {
                        writer.WriteStartMap(2);
                        writer.WriteReferenceId();
                    }
                    else
                    {
                        writer.WriteStartMap(1);
                    }

                    writer.WriteTypeDiscriminator(_contract);
                }
                else
                {
                    if (IsReference)
                    {
                        writer.WriteStartMap(1);
                        writer.WriteReferenceId();
                    }
                    else
                    {
                        writer.WriteStartMap(0);
                    }
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