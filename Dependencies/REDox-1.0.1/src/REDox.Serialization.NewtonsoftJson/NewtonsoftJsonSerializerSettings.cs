// SPDX-FileCopyrightText: 2026 CAPCOM CO., LTD.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using REDox.Serialization.Converters;
using REDox.Serialization.Metadata;

namespace REDox.Serialization.NewtonsoftJson;

public class NewtonsoftJsonSerializerSettings : DoxSerializerSettings
{
    private static readonly DataConverter[] DefaultConverters = new DataConverter[]
    {
        new DrawingTypeConverter(),
        new NumericsTypeConverter(),
        new JTokenConverter(),
        new BinaryConverter(),
        new EnumTypeConverter(),
        new DoxTypeConverter(),
        new BuiltInTypeConverter(),
        new ArrayConverter(),
        new GenericCollectionConverter
        {
            IgnoreProducerConsumerCollectionInterface = true
        },
        new CollectionConverter(),
        new SerializableTypeConverter(),
        new ValueTypeConverter(),
        new ClassTypeConverter()
    };

    private static readonly TextEncoderPolicy EscapeNonAscii = new(TextEscapeMask.AllNonAscii);

    private static readonly TextEncoderPolicy EscapeHtml = new(TextEscapeMask.LessThan |
                                                               TextEscapeMask.GreaterThan |
                                                               TextEscapeMask.Ampersand |
                                                               TextEscapeMask.SingleQuote)
    {
        UnicodeEscapes = TextEscapeMask.DoubleQuote
    };

    private readonly JsonSerializerSettings _jsonSettings;

    public NewtonsoftJsonSerializerSettings(JsonSerializerSettings settings)
    {
        _jsonSettings = settings;

        var referenceLoop = ReferenceLoopHandling.Serialize;

        switch (settings.ReferenceLoopHandling)
        {
            case Newtonsoft.Json.ReferenceLoopHandling.Serialize:
                referenceLoop = ReferenceLoopHandling.Serialize;
                break;
            case Newtonsoft.Json.ReferenceLoopHandling.Ignore:
                referenceLoop = ReferenceLoopHandling.Ignore;
                break;
            case Newtonsoft.Json.ReferenceLoopHandling.Error:
                referenceLoop = ReferenceLoopHandling.Error;
                break;
        }

        var dateFormat = DateFormatHandling.IsoDateFormat;

        var dateTimeZone = DateTimeZoneHandling.RoundtripKind;

        switch (settings.DateTimeZoneHandling)
        {
            case Newtonsoft.Json.DateTimeZoneHandling.Utc:
                dateTimeZone = DateTimeZoneHandling.Utc;
                break;
            case Newtonsoft.Json.DateTimeZoneHandling.Local:
                dateTimeZone = DateTimeZoneHandling.Local;
                break;
            case Newtonsoft.Json.DateTimeZoneHandling.Unspecified:
                dateTimeZone = DateTimeZoneHandling.Unspecified;
                break;
        }

        var nullValue = settings.NullValueHandling == Newtonsoft.Json.NullValueHandling.Ignore
            ? NullValueHandling.Ignore
            : NullValueHandling.Include;
        var defaultValue = DefaultValueHandling.Include;

        switch (settings.DefaultValueHandling)
        {
            case Newtonsoft.Json.DefaultValueHandling.Ignore:
                defaultValue = DefaultValueHandling.Ignore | DefaultValueHandling.IgnoreRead;
                break;
            case Newtonsoft.Json.DefaultValueHandling.IgnoreAndPopulate:
                defaultValue = DefaultValueHandling.IgnoreAndPopulate | DefaultValueHandling.IgnoreRead;
                break;
            case Newtonsoft.Json.DefaultValueHandling.Populate:
                defaultValue = DefaultValueHandling.Populate;
                break;
        }

        if (settings.DateFormatHandling == Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat)
        {
            dateFormat = DateFormatHandling.MicrosoftDateFormat;
        }
        else
        {
            dateFormat = DateFormatHandling.IsoDateFormat;
        }

        TypeNameHandling = settings.TypeNameHandling;

        if (settings.SerializationBinder != null && settings.SerializationBinder is not DefaultSerializationBinder)
        {
            SerializationBinder = settings.SerializationBinder;
        }

        var ignoreSerializableInterface = false;
        var ignoreSerializableAttribute = true;

        var overrideSpecifiedNames = false;

        NamingPolicy? namingPolicy = null;
        NamingPolicy? dictionaryKeyPolicy = null;

        if (settings.ContractResolver is DefaultContractResolver resolver)
        {
            ignoreSerializableInterface = resolver.IgnoreSerializableInterface;
            ignoreSerializableAttribute = resolver.IgnoreSerializableAttribute;

            if (resolver.NamingStrategy != null)
            {
                namingPolicy = new PropertyNamingStrategy(resolver.NamingStrategy);

                if (resolver.NamingStrategy.ProcessDictionaryKeys)
                {
                    dictionaryKeyPolicy = new DictionaryKeyStrategy(resolver.NamingStrategy);
                }

                overrideSpecifiedNames = resolver.NamingStrategy.OverrideSpecifiedNames;
            }
        }

        string? dateFormatString = null;

        if (settings.DateFormatString != "yyyy'-'MM'-'dd'T'HH':'mm':'ss.FFFFFFFK")
        {
            dateFormatString = settings.DateFormatString;
        }

        var exsistingValue = ObjectCreationHandling.ReuseObject;

        if (settings.ObjectCreationHandling == Newtonsoft.Json.ObjectCreationHandling.Replace)
        {
            exsistingValue = ObjectCreationHandling.Replace;
        }

        var converters = new List<DataConverter>();
        if (settings.Converters != null)
        {
            foreach (var conv in settings.Converters)
            {
                if (conv is Newtonsoft.Json.Converters.StringEnumConverter)
                {
                    converters.Add(new StringEnumConverter());
                }
                else
                {
                    converters.Add(new JsonConverterAdapter(conv, settings));
                }
            }
        }

        var preserved = PreserveReferencesHandling.None;

        if (settings.PreserveReferencesHandling != Newtonsoft.Json.PreserveReferencesHandling.None)
        {
            if ((settings.PreserveReferencesHandling & Newtonsoft.Json.PreserveReferencesHandling.Objects) != 0)
            {
                preserved |= PreserveReferencesHandling.Objects | PreserveReferencesHandling.Structs;
            }

            if ((settings.PreserveReferencesHandling & Newtonsoft.Json.PreserveReferencesHandling.Arrays) != 0)
            {
                preserved |= PreserveReferencesHandling.Arrays | PreserveReferencesHandling.Collections;
            }

            preserved |= PreserveReferencesHandling.IgnoreReadOnly;
        }

        var floatFormat = FloatFormatHandling.SpecialFloatAsString;

        switch (settings.FloatFormatHandling)
        {
            case Newtonsoft.Json.FloatFormatHandling.Symbol:
                floatFormat = FloatFormatHandling.SpecialFloatAsSymbol;
                break;
            case Newtonsoft.Json.FloatFormatHandling.DefaultValue:
                floatFormat = FloatFormatHandling.SpecialFloatAsDefaultValue;
                break;
        }

        var stringEscape = TextEncoderPolicy.Minimum;

        switch (settings.StringEscapeHandling)
        {
            case StringEscapeHandling.EscapeHtml:
                stringEscape = EscapeHtml;
                break;
            case StringEscapeHandling.EscapeNonAscii:
                stringEscape = EscapeNonAscii;
                break;
        }

        EventHandler<SerializationErrorEventArgs>? errorHandler = null;

        if (settings.Error != null)
        {
            errorHandler = (sender, e) => { e.Handled = true; };
        }

        Culture = settings.Culture;
        EmptyArrayHandling = EmptyArrayHandling.Unique;
        PropertyNameCaseInsensitive = true;
        IgnoreSerializableInterface = ignoreSerializableInterface;
        IgnoreSerializableAttribute = ignoreSerializableAttribute;
        OverrideSpecifiedNames = overrideSpecifiedNames;
        SimpleTypeAssemblyName = settings.TypeNameAssemblyFormatHandling == TypeNameAssemblyFormatHandling.Simple;
        Error = errorHandler;
        PropertyNamingPolicy = namingPolicy;
        DictionaryKeyPolicy = dictionaryKeyPolicy;
        DateFormatHandling = dateFormat;
        DateFormatString = dateFormatString;
        ConstructorHandling = ConstructorHandling.IgnoreStructDefaultConstructor;
        DateTimeZoneHandling = dateTimeZone;
        IncludeDerivedProperties = true;
        NullValueHandling = nullValue;
        DefaultValueHandling = defaultValue;
        ObjectCreationHandling = exsistingValue;
        TextEncoderPolicy = stringEscape;
        Converters = converters;
        PreserveReferencesHandling = preserved;
        FloatFormatHandling = floatFormat | FloatFormatHandling.AlwaysIncludeDecimal;
        NumberHandling = NumberHandling.AllowReadingFromString;
        IncludeFields = true;
        ReferenceLoopHandling = referenceLoop;
    }

    public TypeNameHandling TypeNameHandling { get; init; }

    public bool SimpleTypeAssemblyName { get; init; } = true;

    public ISerializationBinder? SerializationBinder { get; init; }

    protected override DataContract ResolveContract(DataContract contract, ReadOnlySpan<byte> typeName)
    {
        var name = Utf8Helper.GetUtf16String(typeName);

        Type? type;

        if (SerializationBinder != null)
        {
            SplitFullyQualifiedTypeName(name, out var bindTypeName, out var bindAssemblyName);
            type = SerializationBinder.BindToType(bindAssemblyName, bindTypeName);
        }
        else
        {
            type = Type.GetType(name);
        }

        if (type == null)
        {
            throw new InvalidOperationException();
        }

        return GetContract(type);
    }

    private static void SplitFullyQualifiedTypeName(string fullyQualifiedTypeName, out string typeName,
        out string? assemblyName)
    {
        var scope = 0;
        var assemblyDelimiterIndex = -1;

        for (var i = 0; i < fullyQualifiedTypeName.Length; i++)
        {
            switch (fullyQualifiedTypeName[i])
            {
                case '[':
                    scope++;
                    break;
                case ']':
                    scope--;
                    break;
                case ',':
                    if (scope == 0)
                    {
                        assemblyDelimiterIndex = i;
                    }

                    break;
            }

            if (assemblyDelimiterIndex >= 0)
            {
                break;
            }
        }

        if (assemblyDelimiterIndex >= 0)
        {
            typeName = fullyQualifiedTypeName.Substring(0, assemblyDelimiterIndex).Trim();
            assemblyName = fullyQualifiedTypeName.Substring(assemblyDelimiterIndex + 1).Trim();
        }
        else
        {
            typeName = fullyQualifiedTypeName.Trim();
            assemblyName = null;
        }
    }

    private string GetTypeDiscriminatorName(Type type)
    {
        if (SerializationBinder != null)
        {
            SerializationBinder.BindToName(type, out var assemblyName, out var typeName);

            if (assemblyName != null || typeName != null)
            {
                return assemblyName == null ? typeName! : typeName + ", " + assemblyName;
            }
        }

        return SimpleTypeAssemblyName
            ? GetSimpleTypeName(type)
            : type.AssemblyQualifiedName!;
    }

    private static string GetSimpleTypeName(Type type)
    {
        var aqn = type.AssemblyQualifiedName!;

        for (;;)
        {
            var separateIndex = aqn.IndexOf(", Version");
            if (separateIndex > 0)
            {
                var endIndex = aqn.IndexOf(']', separateIndex);
                if (endIndex > 0)
                {
                    aqn = aqn.Remove(separateIndex, endIndex - separateIndex);
                }
                else
                {
                    aqn = aqn.Substring(0, separateIndex);
                }
            }
            else
            {
                break;
            }
        }

        return aqn;
    }

    private static bool IsArrayType(Type type)
    {
        if (type == typeof(byte[]))
        {
            return false;
        }

        if (type.IsArray)
        {
            return true;
        }

        if (type == typeof(string))
        {
            return false;
        }

        if (type == typeof(BitArray) ||
            type == typeof(Queue) ||
            type == typeof(Stack))
        {
            return true;
        }

        if (type.IsGenericType)
        {
            var genType = type.GetGenericTypeDefinition();

            if (genType == typeof(Dictionary<,>))
            {
                return false;
            }

            if (genType == typeof(IList<>))
            {
                return false;
            }
        }

        foreach (var inf in type.GetInterfaces())
        {
            if (inf.IsGenericType && inf.GetGenericTypeDefinition() == typeof(IDictionary<,>))
            {
                return false;
            }
        }

        foreach (var inf in type.GetInterfaces())
        {
            if (inf.IsGenericType && inf.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return true;
            }

            if (inf == typeof(IList))
            {
                return true;
            }
        }

        return false;
    }

    protected override DataContract ResolveContract(Type type)
    {
        var baseContract = base.ResolveContract(type);

        var numberHandling = baseContract.NumberHandling;
        var constructor = baseContract.Constructor;

        foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (ctor.IsDefined(typeof(JsonConstructorAttribute)))
            {
                constructor = ctor;
                break;
            }
        }

        if (type == typeof(Half) || type == typeof(Int128) || type == typeof(UInt128))
        {
            numberHandling = NumberHandling.WriteAsString | NumberHandling.AllowReadingFromString;
        }

        var isPolymorphic = baseContract.IsPolymorphic;
        var alwaysWriteTypeDiscriminator = baseContract.AlwaysWriteTypeDiscriminator;

        if (TypeNameHandling != TypeNameHandling.None)
        {
            isPolymorphic = true;

            if (TypeNameHandling != TypeNameHandling.Auto)
            {
                var isArray = IsArrayType(type);

                alwaysWriteTypeDiscriminator = true;

                if ((TypeNameHandling & TypeNameHandling.Arrays) == 0 && isArray)
                {
                    alwaysWriteTypeDiscriminator = false;
                    isPolymorphic = false;
                }

                if ((TypeNameHandling & TypeNameHandling.Objects) == 0 && !isArray)
                {
                    alwaysWriteTypeDiscriminator = false;
                    isPolymorphic = false;
                }
            }
        }

        var props = new List<DataProperty>(baseContract.Properties.Count);
        var extensionData = baseContract.ExtensionData;

        foreach (var baseProp in baseContract.Properties)
        {
            var prop = baseProp;

            if (prop.Info.IsDefined(typeof(JsonIgnoreAttribute), true))
            {
                continue;
            }

            if (prop.Info.IsDefined(typeof(JsonExtensionDataAttribute), true))
            {
                extensionData = prop;
                continue;
            }

            var jsonProperty = prop.Info.GetCustomAttribute<JsonPropertyAttribute>(true);
            if (jsonProperty != null)
            {
                prop = ApplyJsonPropertyAttribute(prop, jsonProperty);
            }
            else
            {
                var namingStrategy = GetJsonObjectNamingStrategy(prop.Info.DeclaringType);
                if (namingStrategy != null)
                {
                    prop = prop with
                    {
                        Name = namingStrategy.GetPropertyName(prop.Info.Name, false)
                    };
                }
            }

            if (!prop.IsReadOnly && constructor != null && IsReadOnlyMember(prop.Info) &&
                !HasCreatorParameter(constructor, prop.Name))
            {
                prop = prop with { IsReadOnly = true };
            }

            if (prop.Converter != null)
            {
                props.Add(prop);
                continue;
            }

            if (prop.PropertyType == typeof(Half) || prop.PropertyType == typeof(Int128) ||
                prop.PropertyType == typeof(UInt128))
            {
                prop = prop with
                {
                    NumberHandling = NumberHandling.WriteAsString | NumberHandling.AllowReadingFromString
                };
            }

            var attr = prop.Info.GetCustomAttribute<JsonConverterAttribute>(true);
            if (attr != null)
            {
                var adapter = CreateConverterFromAttribute(attr);
                if (adapter != null)
                {
                    prop = prop with
                    {
                        Converter = adapter is DataConverterFactory factory
                            ? factory.CreateConverter(prop.PropertyType, this)
                            : adapter
                    };
                }
            }
            else if (jsonProperty?.ItemConverterType != null)
            {
                var itemConverter = CreateConverterFromType(jsonProperty.ItemConverterType,
                    jsonProperty.ItemConverterParameters);
                if (itemConverter != null &&
                    ResolveConverter(prop.PropertyType) is DataCollectionConverterFactory collectionFactory)
                {
                    prop = prop with
                    {
                        Converter = collectionFactory.CreateCollectionConverter(prop.PropertyType, itemConverter, this)
                    };
                }
            }

            props.Add(prop);
        }

        var contract = baseContract with
        {
            TypeDiscriminatorPropertyName = Utf8Helper.TypeTag,
            TypeDiscriminator =
            new TypeDiscriminator(new Utf8Symbol(GetTypeDiscriminatorName(type))),
            NumberHandling = numberHandling,
            Constructor = constructor,
            IsPolymorphic = isPolymorphic,
            AlwaysWriteTypeDiscriminator = alwaysWriteTypeDiscriminator,
            Properties = props,
            ExtensionData = extensionData
        };

        return contract;
    }

    private static bool IsReadOnlyMember(MemberInfo info)
    {
        return info switch
        {
            PropertyInfo propInfo => propInfo.SetMethod is not { IsPublic: true },
            FieldInfo fieldInfo => fieldInfo.IsInitOnly || fieldInfo.IsLiteral,
            _ => false
        };
    }

    private static bool HasCreatorParameter(MethodBase constructor, string name)
    {
        // Newtonsoft.Json matches creator parameters against property names case-sensitively.
        foreach (var param in constructor.GetParameters())
        {
            if (string.Equals(param.Name, name, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static DataProperty ApplyJsonPropertyAttribute(DataProperty prop, JsonPropertyAttribute attribute)
    {
        var hasSpecifiedName = !string.IsNullOrEmpty(attribute.PropertyName);
        var name = hasSpecifiedName ? attribute.PropertyName! : prop.Info.Name;
        var namingStrategy = GetJsonPropertyNamingStrategy(attribute) ??
                             GetJsonObjectNamingStrategy(prop.Info.DeclaringType);

        if (namingStrategy != null)
        {
            name = namingStrategy.GetPropertyName(name, hasSpecifiedName);
        }
        else if (!hasSpecifiedName)
        {
            name = prop.Name;
        }

        var defaultValueHandling = prop.DefaultValueHandling;

        if (attribute.DefaultValueHandling != Newtonsoft.Json.DefaultValueHandling.Include)
        {
            defaultValueHandling = ConvertDefaultValueHandling(attribute.DefaultValueHandling);
        }

        var nullValueHandling = prop.NullValueHandling;

        if (attribute.NullValueHandling == Newtonsoft.Json.NullValueHandling.Ignore)
        {
            nullValueHandling = NullValueHandling.Ignore;
        }

        var objectCreationHandling = prop.ObjectCreationHandling;

        if (IsNamedArgumentSpecified(prop.Info, nameof(JsonPropertyAttribute.ObjectCreationHandling)))
        {
            objectCreationHandling = attribute.ObjectCreationHandling == Newtonsoft.Json.ObjectCreationHandling.Replace
                ? ObjectCreationHandling.Replace
                : ObjectCreationHandling.ReuseObject;
        }

        return prop with
        {
            Name = name,
            Order = attribute.Order,
            IsRequired = attribute.Required is Required.Always or Required.AllowNull,
            NullValueHandling = nullValueHandling,
            DefaultValueHandling = defaultValueHandling,
            ObjectCreationHandling = objectCreationHandling
        };
    }

    private static bool IsNamedArgumentSpecified(MemberInfo info, string argumentName)
    {
        foreach (var data in info.GetCustomAttributesData())
        {
            if (data.AttributeType != typeof(JsonPropertyAttribute))
            {
                continue;
            }

            foreach (var arg in data.NamedArguments)
            {
                if (arg.MemberName == argumentName)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static DefaultValueHandling ConvertDefaultValueHandling(Newtonsoft.Json.DefaultValueHandling handling)
    {
        return handling switch
        {
            Newtonsoft.Json.DefaultValueHandling.Ignore =>
                DefaultValueHandling.Ignore | DefaultValueHandling.IgnoreRead,
            Newtonsoft.Json.DefaultValueHandling.IgnoreAndPopulate => DefaultValueHandling.IgnoreAndPopulate |
                                                                      DefaultValueHandling.IgnoreRead,
            Newtonsoft.Json.DefaultValueHandling.Populate => DefaultValueHandling.Populate,
            _ => DefaultValueHandling.Include
        };
    }

    private static NamingStrategy? GetJsonObjectNamingStrategy(Type? type)
    {
        return CreateNamingStrategy(type?.GetCustomAttribute<JsonObjectAttribute>(true));
    }

    private static NamingStrategy? GetJsonPropertyNamingStrategy(JsonPropertyAttribute attribute)
    {
        if (attribute.NamingStrategyType == null)
        {
            return null;
        }

        return CreateNamingStrategy(attribute.NamingStrategyType, attribute.NamingStrategyParameters);
    }

    private static NamingStrategy? CreateNamingStrategy(JsonContainerAttribute? attribute)
    {
        if (attribute?.NamingStrategyType == null)
        {
            return null;
        }

        return CreateNamingStrategy(attribute.NamingStrategyType, attribute.NamingStrategyParameters);
    }

    private static NamingStrategy? CreateNamingStrategy(Type strategyType, object[]? parameters)
    {
        return parameters is { Length: > 0 }
            ? (NamingStrategy?)Activator.CreateInstance(strategyType, parameters)
            : (NamingStrategy?)Activator.CreateInstance(strategyType);
    }

    private DataConverter? CreateConverterFromAttribute(JsonConverterAttribute attribute)
    {
        return CreateConverterFromType(attribute.ConverterType, attribute.ConverterParameters);
    }

    private DataConverter? CreateConverterFromType(Type converterType, object[]? converterParameters)
    {
        if (converterType == typeof(Newtonsoft.Json.Converters.StringEnumConverter))
        {
            return new StringEnumConverter();
        }

        var converter = converterParameters is { Length: > 0 }
            ? (JsonConverter?)Activator.CreateInstance(converterType, converterParameters)
            : (JsonConverter?)Activator.CreateInstance(converterType);

        if (converter == null)
        {
            return null;
        }

        return new JsonConverterAdapter(converter, _jsonSettings);
    }

    protected override DataConverter ResolveConverter(Type type)
    {
        var typeAttr = type.GetCustomAttribute<JsonConverterAttribute>(false);
        if (typeAttr != null)
        {
            var attrConverter = CreateConverterFromAttribute(typeAttr);
            if (attrConverter is DataConverterFactory attrFactory)
            {
                return attrFactory.CreateConverter(type, this);
            }

            if (attrConverter != null)
            {
                return attrConverter;
            }
        }

        foreach (var converter in Converters)
        {
            if (converter.CanConvert(type))
            {
                return converter;
            }
        }

        if (type == typeof(object) && UnknownObjectTypeHandling == UnknownObjectTypeHandling.Default)
        {
            return new ObjectConverter(this);
        }

        foreach (var converter in DefaultConverters)
        {
            if (IgnoreSerializableInterface && converter is SerializableTypeConverter)
            {
                continue;
            }

            if (converter.CanConvert(type))
            {
                return converter;
            }
        }

        throw new InvalidOperationException();
    }


    private class PropertyNamingStrategy : NamingPolicy
    {
        private readonly NamingStrategy _namingStrategy;

        public PropertyNamingStrategy(NamingStrategy strategy)
        {
            _namingStrategy = strategy;
        }

        public override string ConvertName(string name)
        {
            return _namingStrategy.GetPropertyName(name, false);
        }
    }

    private class DictionaryKeyStrategy : NamingPolicy
    {
        private readonly NamingStrategy _namingStrategy;

        public DictionaryKeyStrategy(NamingStrategy strategy)
        {
            _namingStrategy = strategy;
        }

        public override string ConvertName(string name)
        {
            return _namingStrategy.GetDictionaryKey(name);
        }
    }
}