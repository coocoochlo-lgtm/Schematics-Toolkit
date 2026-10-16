using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using REDox.Json;
using REDox.Tests;

namespace REDox.Serialization.SystemTextJson.Tests;

public sealed class TextJsonCompatibility
{
    public static IEnumerable<JsonSerializerOptions> Settings
    {
        get
        {
            yield return new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            yield return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(new TextEncoderSettings())
            };

            yield return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(new TextEncoderSettings(UnicodeRanges.BasicLatin))
            };

            yield return new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(new TextEncoderSettings(UnicodeRanges.BasicLatin,
                    UnicodeRanges.Hiragana))
            };

            yield return new JsonSerializerOptions
            {
                WriteIndented = true
            };

            yield return new JsonSerializerOptions
            {
                WriteIndented = true,
                IndentCharacter = '\t',
                IndentSize = 4
            };

            yield return new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PreferredObjectCreationHandling = JsonObjectCreationHandling.Populate
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                IncludeFields = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                IgnoreReadOnlyFields = true,
                IncludeFields = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                IgnoreReadOnlyProperties = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                Converters = { new JsonStringEnumConverter() },
                IgnoreReadOnlyProperties = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };


            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseUpper,
                DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseLower,
                DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseLower,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            yield return new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.KebabCaseUpper,
                DictionaryKeyPolicy = JsonNamingPolicy.KebabCaseUpper,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            yield return new JsonSerializerOptions
            {
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
                UnknownTypeHandling = JsonUnknownTypeHandling.JsonNode,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            yield return new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };

            /*
             TODO:
            yield return new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            */

            yield return new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles
            };

            yield return new JsonSerializerOptions
            {
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals | JsonNumberHandling.WriteAsString |
                                 JsonNumberHandling.AllowReadingFromString
            };
        }
    }

    public static IEnumerable<object[]> GetDeserializeData()
    {
        foreach (var settings in Settings)
        {
            foreach (var inst in TestData.GetInstances())
            {
                if (settings.ReferenceHandler == null)
                {
                    if (inst is RefLoop)
                    {
                        continue;
                    }
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
                if (settings.ReferenceHandler == null)
                {
                    if (inst is RefLoop)
                    {
                        continue;
                    }
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

    [Theory]
    [MemberData(nameof(GetSerializeData))]
    public void Serialize(object inst, JsonSerializerOptions settings)
    {
        var doxsettings = new SystemTextJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, out var sjson))
        {
            return;
        }

        Assert.NotNull(sjson);

        var tjson = Json.JsonSerializer.Serialize(inst, type, doxsettings, new JsonWriteOptions
        {
            WriteIndented = settings.WriteIndented,
            IndentCharacter = settings.IndentCharacter,
            NewLine = settings.NewLine,
            IndentSize = settings.IndentSize
        });

        Assert.Equal(sjson, tjson);

        var tjson2 = Json.JsonSerializer.Serialize(inst, type,
            new SystemTextJsonSerializerSettings(settings) { AllowDynamicGenericConverters = false },
            new JsonWriteOptions
            {
                WriteIndented = settings.WriteIndented,
                IndentCharacter = settings.IndentCharacter,
                NewLine = settings.NewLine,
                IndentSize = settings.IndentSize
            });

        Assert.Equal(sjson, tjson2);
    }

    [Theory]
    [MemberData(nameof(GetDeserializeData))]
    public void Deserialize(object inst, JsonSerializerOptions settings)
    {
        var doxsettings = new SystemTextJsonSerializerSettings(settings);

        var type = inst.GetType();

        if (!TryCreateJson(type, inst, settings, out var json))
        {
            return;
        }

        Assert.NotNull(json);

        if (!TryParseJson(type, json, settings, out var sinst))
        {
            return;
        }

        if (!TryCreateJson(type, sinst, settings, out var sjson))
        {
            return;
        }

        var dinst = Json.JsonSerializer.Deserialize(json, type, doxsettings);

        if (!TryCreateJson(type, dinst, settings, out var djson))
        {
            return;
        }

        Assert.Equal(sjson, djson);

        var dinst2 = Json.JsonSerializer.Deserialize(json, type,
            new SystemTextJsonSerializerSettings(settings) { AllowDynamicGenericConverters = false });

        if (!TryCreateJson(type, dinst2, settings, out var djson2))
        {
            return;
        }

        Assert.Equal(sjson, djson2);
    }

    [Theory]
    [MemberData(nameof(GetTransitionData))]
    public void Transition(object src, object dst, JsonSerializerOptions settings)
    {
        var doxsettings = new SystemTextJsonSerializerSettings(settings);

        var stype = src.GetType();
        var dtype = dst.GetType();

        if (!TryCreateJson(stype, src, settings, out var sjson))
        {
            return;
        }

        Assert.NotNull(sjson);

        var djson = Json.JsonSerializer.Serialize(src, stype, doxsettings, new JsonWriteOptions
        {
            WriteIndented = settings.WriteIndented,
            IndentCharacter = settings.IndentCharacter,
            NewLine = settings.NewLine,
            IndentSize = settings.IndentSize
        });

        Assert.Equal(sjson, djson);

        if (!TryParseJson(dst.GetType(), sjson, settings, out var sinst))
        {
            return;
        }

        if (!TryCreateJson(dtype, sinst, settings, out var sjson2))
        {
            return;
        }

        var doc = Json.JsonDocument.Parse(djson, doxsettings);

        var dinst = doc.RootElement.ToObject(dtype);

        var djson2 = Json.JsonSerializer.Serialize(dinst, dtype, doxsettings, new JsonWriteOptions
        {
            WriteIndented = settings.WriteIndented,
            IndentCharacter = settings.IndentCharacter,
            NewLine = settings.NewLine,
            IndentSize = settings.IndentSize
        });

        Assert.Equal(sjson2, djson2);
        Assert.Equal(sinst?.GetType(), dinst?.GetType());
    }

    private bool TryCreateJson(Type type, object? inst, JsonSerializerOptions settings, out string? json)
    {
        try
        {
            json = System.Text.Json.JsonSerializer.Serialize(inst, type, settings);
            return true;
        }
        catch (Exception)
        {
            json = null;
            return false;
        }
    }

    private bool TryParseJson(Type type, string json, JsonSerializerOptions settings, out object? value)
    {
        try
        {
            value = System.Text.Json.JsonSerializer.Deserialize(json, type, settings);
            return true;
        }
        catch (Exception)
        {
            value = null;
            return false;
        }
    }
}