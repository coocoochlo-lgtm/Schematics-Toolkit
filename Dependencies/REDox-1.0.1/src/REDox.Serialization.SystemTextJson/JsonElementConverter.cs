// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using System.Text.Json;

namespace REDox.Serialization.SystemTextJson;

public sealed class JsonElementConverter : DataConverter<JsonElement>
{
    public override JsonElement Read(in DataReader reader, uint tokenId, JsonElement existingValue)
    {
        var json = Json.JsonDocument.Encode(reader.ReadElement(tokenId));

        var utf8JsonReader = new Utf8JsonReader(json);

        return JsonElement.ParseValue(ref utf8JsonReader);
    }

    public override void Write(DataWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer))
                {
                    writer.WriteInt64(integer);
                }
                else
                {
                    writer.WriteBigNumber(Encoding.UTF8.GetBytes(value.GetRawText()));
                }

                break;
            case JsonValueKind.True:
                writer.WriteBoolean(true);
                break;
            case JsonValueKind.False:
                writer.WriteBoolean(false);
                break;
            case JsonValueKind.String:
                {
                    var str = value.GetString();

                    if (str == null)
                    {
                        writer.WriteNull();
                    }
                    else
                    {
                        writer.WriteString(str);
                    }
                }
                break;
            case JsonValueKind.Object:
                {
                    var count = 0;
                    foreach (var kv in value.EnumerateObject())
                    {
                        count++;
                    }

                    writer.WriteStartMap(count);
                    foreach (var kv in value.EnumerateObject())
                    {
                        writer.WriteString(kv.Name);
                        Write(writer, kv.Value);
                    }

                    writer.WriteEndMap();
                }
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray(value.GetArrayLength());
                foreach (var element in value.EnumerateArray())
                {
                    Write(writer, element);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.Null:
                writer.WriteNull();
                break;
        }
    }
}