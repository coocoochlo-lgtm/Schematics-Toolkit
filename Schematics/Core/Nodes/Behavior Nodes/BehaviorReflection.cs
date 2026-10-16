using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// What graph nodes can reach on a Schematic Behavior type: its Variables and its public methods. Built by reflection
/// once per Behavior type and cached, so nodes never reflect again while the game runs.
/// </summary>
public sealed class BehaviorReflection
{
    private static readonly Dictionary<Type, BehaviorReflection> _cache = new();

    public static BehaviorReflection Get(Type behaviorType)
    {
        if (behaviorType == null || !typeof(SchematicBehavior).IsAssignableFrom(behaviorType))
            return null;
        lock (_cache)
        {
            if (!_cache.TryGetValue(behaviorType, out var reflection))
                _cache[behaviorType] = reflection = new BehaviorReflection(behaviorType);
            return reflection;
        }
    }

    public Type BehaviorType { get; }
    public IReadOnlyList<VariableInfo> Variables => _variables;
    public IReadOnlyList<MethodEntry> Methods => _methods;

    private readonly List<VariableInfo> _variables = new();
    private readonly List<MethodEntry> _methods = new();
    private readonly Dictionary<string, MethodEntry> _methodsBySignature = new();

    public readonly struct VariableInfo
    {
        public readonly string Name;
        public readonly Type Type;
        /// <summary>The Behavior's Properties asset (see <see cref="SchematicPropertiesAttribute"/>).</summary>
        public readonly bool IsProperties;
        public VariableInfo(string name, Type type, bool isProperties) { Name = name; Type = type; IsProperties = isProperties; }
    }

    public sealed class MethodEntry
    {
        public MethodInfo Method { get; }
        /// <summary>Stable identifier saved by nodes: <c>Name(ParamType,ParamType)</c>.</summary>
        public string Signature { get; }
        public string DisplayName { get; }
        /// <summary>True when the first parameter is the <see cref="BehaviorInstance"/>, filled in by the node.</summary>
        public bool TakesInstance { get; }
        /// <summary>The parameters shown as input pins (the instance parameter excluded).</summary>
        public ParameterInfo[] Inputs { get; }
        public Type ReturnType => Method.ReturnType;
        public bool HasResult => Method.ReturnType != typeof(void);

        internal MethodEntry(MethodInfo method)
        {
            Method = method;
            var parameters = method.GetParameters();
            TakesInstance = parameters.Length > 0 && parameters[0].ParameterType == typeof(BehaviorInstance);
            Inputs = TakesInstance ? parameters[1..] : parameters;

            var names = new string[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
                names[i] = parameters[i].ParameterType.Name;
            Signature = method.Name + "(" + string.Join(",", names) + ")";

            var shown = new string[Inputs.Length];
            for (int i = 0; i < Inputs.Length; i++)
                shown[i] = Inputs[i].Name;
            DisplayName = method.Name + "(" + string.Join(", ", shown) + ")";
        }
    }

    private BehaviorReflection(Type behaviorType)
    {
        BehaviorType = behaviorType;

        // Variables: every VariableHandle<T> field, base classes first, named the way the Behavior names them.
        var chain = new List<Type>();
        for (var type = behaviorType; type != null && type != typeof(SchematicBehavior); type = type.BaseType)
            chain.Insert(0, type);
        foreach (var type in chain)
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                if (field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(VariableHandle<>))
                {
                    var name = SchematicBehavior.CleanFieldName(field.Name);
                    var valueType = field.FieldType.GetGenericArguments()[0];
                    bool isProperties = typeof(UnityEngine.ScriptableObject).IsAssignableFrom(valueType)
                        && (field.IsDefined(typeof(SchematicPropertiesAttribute), true) || name == "Properties");
                    _variables.Add(new VariableInfo(name, valueType, isProperties));
                }

        // Methods: public instance methods the Behavior itself declares (not lifecycle overrides or SchematicBehavior's own API).
        foreach (var method in behaviorType.GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.IsSpecialName || method.IsGenericMethodDefinition || method.ContainsGenericParameters)
                continue;
            var declaring = method.DeclaringType;
            if (declaring == typeof(object) || declaring == typeof(SchematicBehavior) || !typeof(SchematicBehavior).IsAssignableFrom(declaring))
                continue;
            if (method.GetBaseDefinition().DeclaringType == typeof(SchematicBehavior))
                continue;
            bool supported = true;
            foreach (var parameter in method.GetParameters())
                if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.ParameterType.IsPointer)
                    supported = false;
            if (!supported)
                continue;

            var entry = new MethodEntry(method);
            if (_methodsBySignature.ContainsKey(entry.Signature))
                continue;
            _methods.Add(entry);
            _methodsBySignature[entry.Signature] = entry;
        }
        _methods.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
    }

    public MethodEntry FindMethod(string signature)
        => signature != null && _methodsBySignature.TryGetValue(signature, out var entry) ? entry : null;

    public bool IsPropertiesVariable(string name)
    {
        foreach (var variable in _variables)
            if (variable.Name == name)
                return variable.IsProperties;
        return false;
    }

    public Type FindVariableType(string name)
    {
        foreach (var variable in _variables)
            if (variable.Name == name)
                return variable.Type;
        return null;
    }
}
