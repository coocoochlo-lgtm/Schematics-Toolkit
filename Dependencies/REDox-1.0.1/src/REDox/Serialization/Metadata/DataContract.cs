// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Reflection;

namespace REDox.Serialization.Metadata;

public sealed record DataContract
{
    private DataConverter? _converter;

    public DataContract(Type type, SerializerSettings settings)
    {
        Type = type;
        RequiresCycleCheck = settings.ReferenceLoopHandling != ReferenceLoopHandling.Serialize && !type.IsValueType;
        NumberHandling = settings.NumberHandling;

        if (type == typeof(string))
        {
            RequiresCycleCheck = false;
        }
    }

    public DataConverter? Converter
    {
        get => _converter;
        init => _converter = value;
    }

    public Type Type { get; }

    public Utf8Symbol TypeDiscriminatorPropertyName { get; init; } = Utf8Helper.TypeTag;

    public TypeDiscriminator TypeDiscriminator { get; init; }

    public IReadOnlyList<MethodInfo> OnDeserializingCallbacks { get; init; } = Array.Empty<MethodInfo>();

    public IReadOnlyList<MethodInfo> OnDeserializedCallbacks { get; init; } = Array.Empty<MethodInfo>();

    public IReadOnlyList<MethodInfo> OnSerializingCallbacks { get; init; } = Array.Empty<MethodInfo>();

    public IReadOnlyList<MethodInfo> OnSerializedCallbacks { get; init; } = Array.Empty<MethodInfo>();

    public ConstructorInfo? Constructor { get; init; }

    public IReadOnlyList<DataProperty> Properties { get; init; } = Array.Empty<DataProperty>();

    public IReadOnlyList<DataContract> DerivedTypes { get; init; } = Array.Empty<DataContract>();

    public DataProperty? ExtensionData { get; init; }

    public bool IsReference { get; init; }

    internal bool RequiresCycleCheck { get; init; }

    public bool IsPolymorphic { get; init; }

    public bool AlwaysWriteTypeDiscriminator { get; init; }

    public NumberHandling NumberHandling { get; init; }

    public UnknownDerivedTypeHandling UnknownDerivedTypeHandling { get; init; }

    public UnmappedMemberHandling UnmappedMemberHandling { get; init; }

    public bool IgnoreUnrecognizedTypeDiscriminators { get; init; }

    internal DataConverter FixConverter(DataConverter converter)
    {
        _converter = converter;

        return converter;
    }

    public override string ToString()
    {
        return Type.ToString();
    }
}