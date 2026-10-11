// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;

namespace REDox;

class DoxNodeDocument : Document
{
    public DoxNodeDocument(SerializerSettings settings) : base(settings, false)
    {
    }

    public DoxNodeDocument(DArray container, SerializerSettings settings) : base(settings, false)
    {
        CreateExtension(container, container.Capacity, DToken.MakeExtendContainer(DTokenType.Array, 0));
    }

    public DoxNodeDocument(DObject container, SerializerSettings settings) : base(settings, false)
    {
        CreateExtension(container, container.Capacity * 2, DToken.MakeExtendContainer(DTokenType.Map, 0));
    }

    public DoxNodeDocument(DMap container, SerializerSettings settings) : base(settings, false)
    {
        CreateExtension(container, container.Capacity * 2, DToken.MakeExtendContainer(DTokenType.Map, 0));
    }

    public static DoxNodeDocument Create(DValue value)
    {
        var doc = new DoxNodeDocument(SerializerSettings.Default);
        doc.RootId = doc.JoinValue(0, value);
        return doc;
    }

    public static DoxNodeDocument CreateFrom<T>(T value, SerializerSettings settings)
    {
        var doc = new DoxNodeDocument(settings);

        if (value != null)
        {
            doc.RootId = doc.SerializeToTokens<T>(0, value, settings);
        }
        else
        {
            doc.RootId = doc.AllocToken(DToken.Make(DTokenKind.Null, 0));
        }

        return doc;
    }

    public static DoxNodeDocument CreateFromObject(object? value, Type inputType, SerializerSettings settings)
    {
        var doc = new DoxNodeDocument(settings);

        if (value != null)
        {
            doc.RootId = doc.SerializeToTokens(0, value, inputType, settings);
        }
        else
        {
            doc.RootId = doc.AllocToken(DToken.Make(DTokenKind.Null, 0));
        }

        return doc;
    }
}