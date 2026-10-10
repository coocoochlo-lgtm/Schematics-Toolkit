using Remedy.Framework;
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
        RefreshSignals(behavior, metadata);

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
                definition.Default.Set(variable.BoxedDefault, variable.ValueType);

            variables.Add(definition);
        }
        metadata.Variables = variables;
    }

    // Signals are the SignalHandle fields the Behavior declared. Each gets a SignalData asset stored inside the
    // Metadata asset, which both the Behavior and Schematic Graphs use. Existing assets are kept by name so graph
    // nodes keep pointing at them.
    private static void RefreshSignals(SchematicBehavior behavior, BehaviorMetadata metadata)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        var previous = new Dictionary<string, SignalDefinition>();
        foreach (var definition in metadata.Signals)
            if (definition != null && definition.Name != null)
                previous[definition.Name] = definition;

        var signals = new List<SignalDefinition>();
        foreach (var signal in behavior.Signals)
        {
            var field = FindField(behavior.GetType(), signal.Name, flags);
            var attributes = field != null ? field.GetCustomAttributes(true) : Array.Empty<object>();

            previous.TryGetValue(signal.Name, out var existing);
            var definition = new SignalDefinition
            {
                Name = signal.Name,
                Direction = HasAttribute(attributes, "InputSignalAttribute") ? SignalDirection.Input
                          : HasAttribute(attributes, "OutputSignalAttribute") ? SignalDirection.Output
                          : SignalDirection.Unspecified,
                Asset = existing?.Asset,
            };

            // Parameter names come from [Parameter("Name", typeof(T))], in order.
            var parameterNames = attributes
                .Where(attribute => attribute.GetType().Name == "ParameterAttribute")
                .Select(attribute => attribute.GetType().GetField("Name")?.GetValue(attribute) as string)
                .ToArray();

            for (int i = 0; i < signal.ParameterTypes.Length; i++)
            {
                definition.Parameters.Add(new SignalParameterDefinition
                {
                    Name = i < parameterNames.Length && !string.IsNullOrEmpty(parameterNames[i]) ? parameterNames[i] : $"Arg {i + 1}",
                    Type = signal.ParameterTypes[i],
                });
            }

            definition.Asset = SyncSignalAsset(metadata, definition);
            signals.Add(definition);
            previous.Remove(signal.Name);
        }

        // Signals that were removed from code take their assets with them.
        foreach (var removed in previous.Values)
        {
            if (removed.Asset != null && AssetDatabase.IsSubAsset(removed.Asset))
            {
                AssetDatabase.RemoveObjectFromAsset(removed.Asset);
                UnityEngine.Object.DestroyImmediate(removed.Asset, true);
            }
        }

        metadata.Signals = signals;
    }

    private static SignalData SyncSignalAsset(BehaviorMetadata metadata, SignalDefinition definition)
    {
        var asset = definition.Asset;
        var assetName = $"{metadata.BehaviorType.Type?.Name}.{definition.Name}";

        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<SignalData>();
            asset.name = assetName;
            AssetDatabase.AddObjectToAsset(asset, metadata);
        }

        var before = EditorJsonUtility.ToJson(asset);

        asset.name = assetName;
        asset.Name = definition.Name;
        asset.Parameters.Contents.Clear();
        foreach (var parameter in definition.Parameters)
            asset.Parameters.AddParameter(parameter.Name, new Union { Type = SignalArgs.ValueTypeOf(parameter.Type.Type) });
        asset.RuntimeArgs = new Union[definition.Parameters.Count];

        if (EditorJsonUtility.ToJson(asset) != before)
            EditorUtility.SetDirty(asset);
        return asset;
    }

    private static FieldInfo FindField(Type type, string name, BindingFlags flags)
    {
        for (var current = type; current != null && current != typeof(SchematicBehavior); current = current.BaseType)
        {
            var field = current.GetField(name, flags | BindingFlags.DeclaredOnly)
                     ?? current.GetField($"<{name}>k__BackingField", flags | BindingFlags.DeclaredOnly);
            if (field != null)
                return field;
        }
        return null;
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
