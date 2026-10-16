// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Buffers.Text;
using System.Text;
using System.Xml;

namespace REDox.Serialization.DataContractJson;

sealed class TimeSpanConverter : DataConverter<TimeSpan>
{
    public override TimeSpan Read(in DataReader reader, uint tokenId, TimeSpan existingValue)
    {
        var source = reader.ReadUtf8String(tokenId);

        if (Utf8Parser.TryParse(source, out TimeSpan value, out var bytes))
        {
            return value;
        }

        return XmlConvert.ToTimeSpan(Encoding.UTF8.GetString(source));
    }

    public override void Write(DataWriter writer, TimeSpan value)
    {
        writer.WriteString(XmlConvert.ToString(value));
    }
}