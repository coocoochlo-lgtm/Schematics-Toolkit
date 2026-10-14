// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Reflection;

namespace REDox.Serialization.Metadata;

public sealed record DataProperty
{
    public DataProperty(MemberInfo memberInfo)
    {
        Info = memberInfo;
        Name = memberInfo.Name;

        if (Info is FieldInfo fieldInfo)
        {
            if ((fieldInfo.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) != 0)
            {
                Writable = false;
            }

            IsPublic = (fieldInfo.Attributes & FieldAttributes.FieldAccessMask) == FieldAttributes.Public;
            PropertyType = fieldInfo.FieldType;
            IsStatic = fieldInfo.IsStatic;
            IsReadOnly = (fieldInfo.Attributes & (FieldAttributes.InitOnly | FieldAttributes.Literal)) != 0;
        }
        else
        {
            var propInfo = Info as PropertyInfo;

            if (propInfo == null)
            {
                throw new ArgumentException(
                    $"'{memberInfo.DeclaringType}.{memberInfo.Name}' is neither a field nor a property.",
                    nameof(memberInfo));
            }

            var method = propInfo.GetMethod != null ? propInfo.GetMethod : propInfo.SetMethod;

            if (method == null)
            {
                throw new ArgumentException(
                    $"Property '{memberInfo.DeclaringType}.{memberInfo.Name}' has neither a getter nor a setter.",
                    nameof(memberInfo));
            }

            if (propInfo.SetMethod == null)
            {
                Writable = false;
                IsReadOnly = true;
            }
            else
            {
                IsReadOnly = (propInfo.SetMethod.Attributes & MethodAttributes.MemberAccessMask) !=
                             MethodAttributes.Public;
            }

            if (propInfo.GetMethod == null)
            {
                Readable = false;
            }

            IsPublic = (method.Attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;
            PropertyType = propInfo.PropertyType;
            IsStatic = method.IsStatic;
        }
    }

    public MemberInfo Info { get; init; }

    public MethodInfo? ConditionalMethod { get; init; }

    public string Name { get; init; }

    public int Order { get; init; }

    public int Depth { get; init; }

    public bool Writable { get; init; } = true;

    public bool Readable { get; init; } = true;

    public bool IsRequired { get; init; }

    public ReferenceLoopHandling ReferenceLoopHandling { get; init; }

    public DefaultValueHandling DefaultValueHandling { get; init; }

    public NullValueHandling NullValueHandling { get; init; }

    public ObjectCreationHandling ObjectCreationHandling { get; init; }

    public NumberHandling? NumberHandling { get; init; }

    public object? DefaultValue { get; init; }

    public DataConverter? Converter { get; init; }


    public bool IsPublic { get; }

    public bool IsStatic { get; }

    public Type PropertyType { get; }

    public bool IsReadOnly { get; init; }

    public void SetValue(object? obj, object? value)
    {
        if (Info is FieldInfo fieldInfo)
        {
            fieldInfo.SetValue(obj, value);
        }
        else
        {
            if (Info is PropertyInfo propInfo)
            {
                propInfo.SetValue(obj, value);
            }
            else
            {
                throw new InvalidOperationException(
                    $"Cannot set a value on member '{Info.DeclaringType}.{Name}' of type {Info.MemberType}.");
            }
        }
    }

    public object? GetValue(object? obj)
    {
        if (Info is FieldInfo fieldInfo)
        {
            return fieldInfo.GetValue(obj);
        }

        if (Info is PropertyInfo propInfo)
        {
            return propInfo.GetValue(obj);
        }

        throw new InvalidOperationException(
            $"Cannot get a value from member '{Info.DeclaringType}.{Name}' of type {Info.MemberType}.");
    }

    public void SetValue(TypedReference obj, object value)
    {
        if (Info is FieldInfo fieldInfo)
        {
            fieldInfo.SetValueDirect(obj, value);
        }
        else
        {
            SetValue(TypedReference.ToObject(obj), value);
        }
    }


    public object? GetValue(TypedReference obj)
    {
        if (Info is FieldInfo fieldInfo)
        {
            return fieldInfo.GetValueDirect(obj);
        }

        return GetValue(TypedReference.ToObject(obj));
    }
}