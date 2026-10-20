using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using REDox.Json;
using REDox.Tests;

namespace REDox.Serialization.NewtonsoftJson.Tests;

public class NewtonsoftJsonCompatibility
{
    private readonly ITestOutputHelper _output;

    public NewtonsoftJsonCompatibility(ITestOutputHelper output)
    {
        _output = output;
    }

    public static IEnumerable<JsonSerializerSettings> Settings
    {
        get
        {
            yield return new JsonSerializerSettings();

            yield return new JsonSerializerSettings
            {
                PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.All,
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            };

            yield return new JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new SnakeCaseNamingStrategy()
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    IgnoreSerializableInterface = true
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    IgnoreSerializableAttribute = false
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    SerializeCompilerGeneratedMembers = true
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = false
                    }
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessDictionaryKeys = true
                    }
                }
            };

            yield return new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy
                    {
                        ProcessExtensionDataNames = true
                    }
                }
            };

            yield return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All
            };

            yield return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects
            };

            yield return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Arrays
            };

            yield return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto
            };

            yield return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                TypeNameAssemblyFormatHandling = TypeNameAssemblyFormatHandling.Full
            };

            yield return new JsonSerializerSettings
            {
                FloatFormatHandling = Newtonsoft.Json.FloatFormatHandling.DefaultValue
            };

            yield return new JsonSerializerSettings
            {
                FloatFormatHandling = Newtonsoft.Json.FloatFormatHandling.Symbol
            };

            yield return new JsonSerializerSettings
            {
                FloatParseHandling = FloatParseHandling.Decimal
            };


            yield return new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.DateTime
            };

            yield return new JsonSerializerSettings
            {
                Culture = CultureInfo.GetCultureInfo("sv-SE"),
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                Culture = CultureInfo.GetCultureInfo("ar-SA"),
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.CurrentCulture,
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.CurrentCulture,
                DateParseHandling = DateParseHandling.DateTime
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.DateTime
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Local
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Unspecified
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Local
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.RoundtripKind
            };

            yield return new JsonSerializerSettings
            {
                DateFormatHandling = Newtonsoft.Json.DateFormatHandling.MicrosoftDateFormat,
                DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Unspecified
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy"
            };

            yield return new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.None
            };

            yield return new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.DateTime
            };

            yield return new JsonSerializerSettings
            {
                DateParseHandling = DateParseHandling.DateTimeOffset
            };

            yield return new JsonSerializerSettings
            {
                DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
            };

            yield return new JsonSerializerSettings
            {
                DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.IgnoreAndPopulate
            };

            yield return new JsonSerializerSettings
            {
                DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Populate
            };

            yield return new JsonSerializerSettings
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore
            };

            yield return new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeHtml
            };

            yield return new JsonSerializerSettings
            {
                StringEscapeHandling = StringEscapeHandling.EscapeNonAscii
            };

            yield return new JsonSerializerSettings
            {
                ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Replace
            };

            yield return new JsonSerializerSettings
            {
                ObjectCreationHandling = Newtonsoft.Json.ObjectCreationHandling.Reuse
            };

            yield return new JsonSerializerSettings
            {
                Converters = { new StringEnumConverter() }
            };

            yield return new JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Serialize,
                PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.All
            };

            /*
            yield return new JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Serialize,
                PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.Objects
            };

            yield return new JsonSerializerSettings
            {
                ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Serialize,
                PreserveReferencesHandling = Newtonsoft.Json.PreserveReferencesHandling.Arrays
            };
            */

            yield return new JsonSerializerSettings
            {
                ConstructorHandling = Newtonsoft.Json.ConstructorHandling.AllowNonPublicDefaultConstructor
            };

            /*
            yield return new JsonSerializerSettings
            {
                Error = (sender, e) => { e.ErrorContext.Handled = true; }
            };
            */

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.CurrentCulture
            };

            yield return new JsonSerializerSettings
            {
                DateFormatString = "yyyy MMMM dd",
                Culture = CultureInfo.InvariantCulture
            };

            /*
            yield return new JsonSerializerSettings()
            {
                MetadataPropertyHandling = MetadataPropertyHandling.Ignore
            };
            */

            /*
            yield return new JsonSerializerSettings()
            {
                MetadataPropertyHandling = MetadataPropertyHandling.ReadAhead
            };
            */
        }
    }


    public static IEnumerable<object[]> GetDeserializeData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetInstances())
            {
                if (inst is SpanTest || inst is ObjectMember)
                {
                    //TODO
                    continue;
                }

                yield return new[] { inst, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetSerializeData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetSerializeInstances())
            {
                if (inst is RefLoop)
                {
                    //TODO
                    continue;
                }

                yield return new[] { inst, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetTransitionData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetTransitionInstances())
            {
                yield return new[] { inst.src, inst.dest, settings };
            }
        }
    }

    public static IEnumerable<object[]> GetExtensionDataAttributeSettings()
    {
        yield return new object[] { new JsonSerializerSettings() };

        yield return new object[]
        {
            new JsonSerializerSettings
            {
                NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore,
                DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore
            }
        };

        yield return new object[]
        {
            new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            }
        };
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void Serialize(object inst, JsonSerializerSettings settings)
    {
        var doxsettings = new NewtonsoftJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, false, out var sjson))
        {
            return;
        }

        var tjson = Json.JsonSerializer.Serialize(inst, type, doxsettings, new JsonWriteOptions
        {
            MaxDepth = 1000
        });

        Assert.Equal(sjson, tjson);

        var tjson2 = Json.JsonSerializer.Serialize(inst, type,
            new NewtonsoftJsonSerializerSettings(settings) { AllowDynamicGenericConverters = false },
            new JsonWriteOptions
            {
                MaxDepth = 1000
            });

        Assert.Equal(sjson, tjson2);
    }

    [Theory]
    [MemberData(nameof(GetDeserializeData))]
    public void Deserialize(object inst, JsonSerializerSettings settings)
    {
        var doxsettings = new NewtonsoftJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, false, out var json))
        {
            return;
        }

        Assert.NotNull(json);

        if (!TryParseJson(type, json, settings, out var sinst))
        {
            return;
        }

        if (!TryCreateJson(type, sinst, settings, false, out var sjson))
        {
            return;
        }

        var dinst = Json.JsonSerializer.Deserialize(json, type, doxsettings, new JsonDocumentOptions
        {
            MaxDepth = settings.MaxDepth.HasValue ? settings.MaxDepth.Value : int.MaxValue
        });

        if (!TryCreateJson(type, dinst, settings, false, out var djson))
        {
            return;
        }

        Assert.Equal(sjson, djson);

        var dinst2 = Json.JsonSerializer.Deserialize(json, type,
            new NewtonsoftJsonSerializerSettings(settings) { AllowDynamicGenericConverters = false },
            new JsonDocumentOptions
            {
                MaxDepth = settings.MaxDepth.HasValue ? settings.MaxDepth.Value : int.MaxValue
            });

        if (!TryCreateJson(type, dinst2, settings, false, out var djson2))
        {
            return;
        }

        Assert.Equal(sjson, djson2);
    }

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void SerializeIndented(object inst, JsonSerializerSettings settings)
    {
        var doxsettings = new NewtonsoftJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, true, out var sjson))
        {
            return;
        }

        var tjson = Json.JsonSerializer.Serialize(inst, type, doxsettings,
            new JsonWriteOptions { WriteIndented = true, MaxDepth = 1000 });

        Assert.Equal(sjson, tjson);
    }


    [Theory]
    [MemberData(nameof(GetTransitionData))]
    public void Transition(object src, object dst, JsonSerializerSettings settings)
    {
        var doxsettings = new NewtonsoftJsonSerializerSettings(settings);

        var stype = src.GetType();
        var dtype = dst.GetType();

        if (!TryCreateJson(stype, src, settings, false, out var sjson))
        {
            return;
        }

        Assert.NotNull(sjson);

        if (!TryParseJson(dst.GetType(), sjson, settings, out var sinst))
        {
            return;
        }

        if (!TryCreateJson(dtype, sinst, settings, false, out var sjson2))
        {
            return;
        }

        var djson = Json.JsonSerializer.Serialize(src, stype, doxsettings);

        Assert.Equal(sjson, djson);

        var doc = JsonDocument.Parse(djson, doxsettings, new JsonDocumentOptions
        {
            MaxDepth = settings.MaxDepth ?? 1000
        });

        var dinst = doc.RootElement.ToObject(dtype);

        var djson2 = Json.JsonSerializer.Serialize(dinst, dtype, doxsettings);

        Assert.Equal(sjson2, djson2);
        Assert.Equal(sinst?.GetType(), dinst?.GetType());
    }

    [Theory]
    [MemberData(nameof(GetExtensionDataAttributeSettings))]
    public void ExtensionDataAndAttributesSerialize(JsonSerializerSettings settings)
    {
        var inst = new AttributeHeavyExtensionData
        {
            Id = 42,
            DisplayName = "Primary",
            HiddenWhenNull = null,
            RetryCount = 13,
            Ignored = "not-written",
            Extra =
            {
                ["passthrough_number"] = 100,
                ["passthrough_object"] = new Dictionary<string, object?>
                {
                    ["nested_value"] = true,
                    ["nested_text"] = "ok"
                },
                ["passthrough_array"] = new object?[] { 1, "two", false }
            }
        };

        AssertSerializeMatchesNewtonsoft(inst, settings);
    }

    [Theory]
    [MemberData(nameof(GetExtensionDataAttributeSettings))]
    public void ExtensionDataAndAttributesDeserialize(JsonSerializerSettings settings)
    {
        const string json = """
                            {
                              "id": 7,
                              "displayName": "From JSON",
                              "hidden_when_null": "visible",
                              "retry_count": 21,
                              "ignored": "captured as extension data",
                              "passthrough_number": 100,
                              "passthrough_object": {
                                "nested_value": true,
                                "nested_text": "ok"
                              },
                              "passthrough_array": [1, "two", false]
                            }
                            """;

        var type = typeof(AttributeHeavyExtensionData);

        Assert.True(TryParseJson(type, json, settings, out var sinst));

        var dinst = Json.JsonSerializer.Deserialize(json, type, new NewtonsoftJsonSerializerSettings(settings));

        Assert.NotNull(dinst);
        AssertSerializeMatchesNewtonsoft(sinst!, settings);
        AssertSerializeMatchesNewtonsoft(dinst, settings);
    }

    private void AssertSerializeMatchesNewtonsoft(object inst, JsonSerializerSettings settings)
    {
        var type = inst.GetType();

        Assert.True(TryCreateJson(type, inst, settings, false, out var sjson));

        var tjson = Json.JsonSerializer.Serialize(inst, type, new NewtonsoftJsonSerializerSettings(settings));

        Assert.Equal(sjson, tjson);
    }

    public static IEnumerable<object[]> GetSerializationBinderSettings()
    {
        yield return new object[]
        {
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                SerializationBinder = new AliasSerializationBinder(true)
            }
        };

        yield return new object[]
        {
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Objects,
                SerializationBinder = new AliasSerializationBinder(false)
            }
        };

        yield return new object[]
        {
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                SerializationBinder = new AliasSerializationBinder(true)
            }
        };

        yield return new object[]
        {
            new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.All,
                SerializationBinder = new AliasSerializationBinder(false)
            }
        };
    }

    [Theory]
    [MemberData(nameof(GetSerializationBinderSettings))]
    public void SerializationBinderSerialize(JsonSerializerSettings settings)
    {
        var inst = new BinderContainer
        {
            Animal = new BinderDog { Name = "Pochi", Breed = "Shiba" }
        };

        var type = inst.GetType();

        Assert.True(TryCreateJson(type, inst, settings, false, out var sjson));

        var tjson = Json.JsonSerializer.Serialize(inst, type, new NewtonsoftJsonSerializerSettings(settings));

        Assert.Equal(sjson, tjson);
        Assert.Contains("BinderDog", tjson);
        Assert.DoesNotContain(typeof(BinderDog).FullName!, tjson);
    }

    [Theory]
    [MemberData(nameof(GetSerializationBinderSettings))]
    public void SerializationBinderDeserialize(JsonSerializerSettings settings)
    {
        var inst = new BinderContainer
        {
            Animal = new BinderDog { Name = "Pochi", Breed = "Shiba" }
        };

        var type = inst.GetType();

        Assert.True(TryCreateJson(type, inst, settings, false, out var json));
        Assert.True(TryParseJson(type, json!, settings, out var sinst));

        var dinst = Json.JsonSerializer.Deserialize(json!, type, new NewtonsoftJsonSerializerSettings(settings));

        var dcontainer = Assert.IsType<BinderContainer>(dinst);
        var dog = Assert.IsType<BinderDog>(dcontainer.Animal);
        Assert.Equal("Pochi", dog.Name);
        Assert.Equal("Shiba", dog.Breed);

        Assert.True(TryCreateJson(type, sinst, settings, false, out var sjson));
        Assert.True(TryCreateJson(type, dinst, settings, false, out var djson));
        Assert.Equal(sjson, djson);
    }

    private bool TryCreateJson(Type type, object? inst, JsonSerializerSettings settings, bool indented,
        out string? json)
    {
        try
        {
            json = JsonConvert.SerializeObject(inst, type, indented ? Formatting.Indented : Formatting.None, settings);
            return true;
        }
        catch (Exception)
        {
            json = null;
            return false;
        }
    }

    private bool TryParseJson(Type type, string json, JsonSerializerSettings settings, out object? value)
    {
        try
        {
            value = JsonConvert.DeserializeObject(json, type, settings);
            return true;
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }

    private sealed class AliasSerializationBinder : ISerializationBinder
    {
        private const string AssemblyAlias = "BinderAssembly";

        private static readonly Dictionary<string, Type> AliasToType = new()
        {
            ["BinderContainer"] = typeof(BinderContainer),
            ["BinderAnimal"] = typeof(BinderAnimal),
            ["BinderDog"] = typeof(BinderDog)
        };

        private readonly bool _includeAssembly;

        public AliasSerializationBinder(bool includeAssembly)
        {
            _includeAssembly = includeAssembly;
        }

        public Type BindToType(string? assemblyName, string typeName)
        {
            if (_includeAssembly && assemblyName != AssemblyAlias)
            {
                throw new JsonSerializationException($"Unexpected assembly '{assemblyName}'.");
            }

            if (!_includeAssembly && assemblyName != null)
            {
                throw new JsonSerializationException($"Unexpected assembly '{assemblyName}'.");
            }

            return AliasToType.TryGetValue(typeName, out var type)
                ? type
                : throw new JsonSerializationException($"Unknown type '{typeName}'.");
        }

        public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
        {
            assemblyName = _includeAssembly ? AssemblyAlias : null;
            typeName = serializedType.Name;
        }
    }

    public sealed class BinderContainer
    {
        public BinderAnimal? Animal { get; set; }
    }

    public class BinderAnimal
    {
        public string? Name { get; set; }
    }

    public sealed class BinderDog : BinderAnimal
    {
        public string? Breed { get; set; }
    }

    [JsonObject(NamingStrategyType = typeof(SnakeCaseNamingStrategy))]
    private sealed class AttributeHeavyExtensionData
    {
        [JsonProperty("id", Order = -20, Required = Required.Always)]
        public int Id { get; set; } = 7;

        [JsonProperty(Order = -10, NamingStrategyType = typeof(CamelCaseNamingStrategy))]
        public string DisplayName { get; set; } = "Default";

        [JsonProperty(NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore)]
        public string? HiddenWhenNull { get; set; }

        [DefaultValue(13)]
        [JsonProperty(DefaultValueHandling = Newtonsoft.Json.DefaultValueHandling.Ignore)]
        public int RetryCount { get; set; } = 13;

        [JsonIgnore] public string Ignored { get; set; } = "ignored";

        [JsonExtensionData] public IDictionary<string, object?> Extra { get; set; } = new Dictionary<string, object?>();
    }
}