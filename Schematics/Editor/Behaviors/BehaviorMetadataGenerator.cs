using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Keeps a <see cref="BehaviorMetadata"/> asset in sync with every <see cref="SchematicBehavior"/> type after each script reload.
/// Metadata is never deleted automatically, so values assigned in the Inspector survive a Behavior failing to compile.
/// </summary>
[InitializeOnLoad]
public static class BehaviorMetadataGenerator
{
    // Under Resources so BehaviorMetadata.Get can find them in builds.
    public const string Folder = "Assets/Content/Resources/Schematics/Behaviors";

    static BehaviorMetadataGenerator()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                RefreshAll();
        };
    }

    public static IEnumerable<Type> BehaviorTypes => TypeCache.GetTypesDerivedFrom<SchematicBehavior>()
        .Where(type => !type.IsAbstract && !type.IsGenericTypeDefinition);

    [MenuItem("Tools/Remedy/Schematics/Refresh Behavior Metadata")]
    public static void RefreshAll()
    {
        var existing = FindAll();
        bool changed = false;

        foreach (var type in BehaviorTypes)
        {
            existing.TryGetValue(type, out var metadata);
            changed |= Refresh(type, ref metadata);
        }

        if (changed)
            AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Gets the Metadata for a Behavior type, creating or updating it as needed.
    /// </summary>
    public static BehaviorMetadata GetOrCreate(Type behaviorType)
    {
        FindAll().TryGetValue(behaviorType, out var metadata);
        if (Refresh(behaviorType, ref metadata))
            AssetDatabase.SaveAssets();
        return metadata;
    }

    private static Dictionary<Type, BehaviorMetadata> FindAll()
    {
        var result = new Dictionary<Type, BehaviorMetadata>();
        foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(BehaviorMetadata)))
        {
            var metadata = AssetDatabase.LoadAssetAtPath<BehaviorMetadata>(AssetDatabase.GUIDToAssetPath(guid));
            var type = metadata != null ? metadata.BehaviorType.Type : null;
            if (type != null && !result.ContainsKey(type))
                result.Add(type, metadata);
        }
        return result;
    }

    private static bool Refresh(Type type, ref BehaviorMetadata metadata)
    {
        SchematicBehavior behavior;
        try
        {
            behavior = SchematicBehavior.Create(type);
        }
        catch (Exception exception)
        {
            Debug.LogError($"Couldn't create {type.Name} to read its Variables: {exception.Message}");
            return false;
        }

        bool created = false;
        if (metadata == null)
        {
            EnsureFolder(Folder);
            metadata = ScriptableObject.CreateInstance<BehaviorMetadata>();
            metadata.BehaviorType = type;
            AssetDatabase.CreateAsset(metadata, AssetDatabase.GenerateUniqueAssetPath($"{Folder}/{type.Name}.asset"));
            created = true;
        }

        var before = EditorJsonUtility.ToJson(metadata);

        metadata.BehaviorType = type;
        RefreshVariables(behavior, metadata);
        RefreshSignals(type, metadata);

        if (created || EditorJsonUtility.ToJson(metadata) != before)
        {
            EditorUtility.SetDirty(metadata);
            return true;
        }
        return false;
    }

    private static void RefreshVariables(SchematicBehavior behavior, BehaviorMetadata metadata)
    {
        var previous = new Dictionary<string, VariableDefinition>();
        foreach (var definition in metadata.Variables)
            if (definition != null && definition.Name != null)
                previous[definition.Name] = definition;

        var variables = new List<VariableDefinition>();
        foreach (var variable in behavior.Variables)
        {
            // Inspector-assigned defaults are kept as long as the Variable keeps its name and type.
            if (!previous.TryGetValue(variable.Name, out var definition) || definition.Type.Type != variable.ValueType)
                definition = new VariableDefinition { Name = variable.Name, Type = variable.ValueType };

            definition.Default ??= new SchematicValue();
            if (!definition.OverrideDefault)
                definition.Default.Set(variable.BoxedDefault);

            variables.Add(definition);
        }
        metadata.Variables = variables;
    }

    // Signals use the Runtime assembly's Signal types and attributes. They're matched by name so this
    // editor assembly only needs to reference Schematics Core.
    private static void RefreshSignals(Type type, BehaviorMetadata metadata)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        var signals = new List<SignalDefinition>();
        for (var current = type; current != null && current != typeof(SchematicBehavior); current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags))
            {
                if (field.FieldType.GetInterface("ISignal") == null)
                    continue;

                var attributes = field.GetCustomAttributes(true);
                var definition = new SignalDefinition
                {
                    Name = field.Name,
                    Direction = HasAttribute(attributes, "InputSignalAttribute") ? SignalDirection.Input
                              : HasAttribute(attributes, "OutputSignalAttribute") ? SignalDirection.Output
                              : SignalDirection.Unspecified,
                };

                var parameterNames = attributes
                    .Where(attribute => attribute.GetType().Name == "ParameterAttribute")
                    .Select(attribute => attribute.GetType().GetField("Name")?.GetValue(attribute) as string)
                    .ToArray();

                var parameterTypes = field.FieldType.IsGenericType ? field.FieldType.GetGenericArguments() : Type.EmptyTypes;
                for (int i = 0; i < parameterTypes.Length; i++)
                {
                    definition.Parameters.Add(new SignalParameterDefinition
                    {
                        Name = i < parameterNames.Length && !string.IsNullOrEmpty(parameterNames[i]) ? parameterNames[i] : $"Arg {i + 1}",
                        Type = parameterTypes[i],
                    });
                }

                signals.Add(definition);
            }
        }
        metadata.Signals = signals;
    }

    private static bool HasAttribute(object[] attributes, string typeName)
        => attributes.Any(attribute => attribute.GetType().Name == typeName);

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;

        var parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
