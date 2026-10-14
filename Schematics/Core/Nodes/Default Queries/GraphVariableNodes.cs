using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Remedy.Schematics
{
    internal static class GraphVariableStorage
    {
        public static VariableData GetVariableData(SchematicGraph graph, string variableName, VariableData variable)
        {
            if (variable != null)
                return variable;

            if (string.IsNullOrEmpty(variableName))
                throw new InvalidOperationException("A graph variable name is required.");

            if (graph == null || !graph.TryGetVariable(variableName, out var resolvedVariable))
                throw new KeyNotFoundException($"Graph variable '{variableName}' was not found.");

            return resolvedVariable;
        }

        public static object ReadValue(Union value)
        {
            switch (value.Type)
            {
                case Union.ValueType.Null: return null;
                case Union.ValueType.Bool: return value.GetValue<bool>();
                case Union.ValueType.Int: return value.GetValue<int>();
                case Union.ValueType.Float: return value.GetValue<float>();
                case Union.ValueType.Vector2: return value.GetValue<Vector2>();
                case Union.ValueType.Vector3: return value.GetValue<Vector3>();
                case Union.ValueType.Vector4: return value.GetValue<Vector4>();
                case Union.ValueType.Color: return value.GetValue<Color>();
                case Union.ValueType.Color32: return value.GetValue<Color32>();
                case Union.ValueType.Quaternion: return value.GetValue<Quaternion>();
                case Union.ValueType.LayerMask: return value.GetValue<LayerMask>();
                case Union.ValueType.String: return value.GetValue<string>();
                case Union.ValueType.GameObject: return value.GetValue<GameObject>();
                case Union.ValueType.Transform: return value.GetValue<Transform>();
                case Union.ValueType.Component: return value.GetValue<Component>();
                case Union.ValueType.Material: return value.GetValue<Material>();
                case Union.ValueType.Texture: return value.GetValue<Texture>();
                case Union.ValueType.AudioClip: return value.GetValue<AudioClip>();
                case Union.ValueType.PhysicsMaterial: return value.GetValue<PhysicsMaterial>();
                case Union.ValueType.Array: return value.GetValue<Array>();
                case Union.ValueType.List: return value.GetValue<List<Union>>();
                case Union.ValueType.Dictionary: return value.GetValue<Dictionary<string, Union>>();
                case Union.ValueType.Scene: return value.GetScene();
                case Union.ValueType.Object: return value.GetValue<UnityEngine.Object>();
                default: throw new ArgumentOutOfRangeException(nameof(value), value.Type, "Unsupported graph variable value type.");
            }
        }

        public static Union CreateUnion(object value)
        {
            if (value == null) return Union.Null;
            if (value is bool boolValue) return new Union(boolValue);
            if (value is int intValue) return new Union(intValue);
            if (value is float floatValue) return new Union(floatValue);
            if (value is Vector2 vector2Value) return new Union(vector2Value);
            if (value is Vector3 vector3Value) return new Union(vector3Value);
            if (value is Vector4 vector4Value) return new Union(vector4Value);
            if (value is Color colorValue) return new Union(colorValue);
            if (value is Color32 color32Value) return new Union(color32Value);
            if (value is Quaternion quaternionValue) return new Union(quaternionValue);
            if (value is LayerMask layerMaskValue) return new Union(layerMaskValue);
            if (value is string stringValue) return new Union(stringValue);
            if (value is GameObject gameObjectValue) return new Union(gameObjectValue);
            if (value is Component componentValue) return new Union(componentValue);
            if (value is PhysicsMaterial physicsMaterialValue) return new Union(physicsMaterialValue);
            if (value is Material materialValue) return CreateUnityObjectUnion(materialValue, Union.ValueType.Material);
            if (value is Texture textureValue) return CreateUnityObjectUnion(textureValue, Union.ValueType.Texture);
            if (value is AudioClip audioClipValue) return CreateUnityObjectUnion(audioClipValue, Union.ValueType.AudioClip);
            if (value is UnityEngine.Object objectValue) return CreateUnityObjectUnion(objectValue, Union.ValueType.Object);
            if (value is List<Union> listValue) return new Union(listValue);
            if (value is Dictionary<string, Union> dictionaryValue) return new Union(dictionaryValue);
            if (value is Array arrayValue) return new Union(arrayValue);
            if (value is Scene sceneValue) return new Union(sceneValue);
            if (value is Union unionValue) return unionValue;

            throw new ArgumentException($"Values of type '{value.GetType().FullName}' cannot be stored in a graph variable.", nameof(value));
        }

        private static Union CreateUnityObjectUnion(UnityEngine.Object value, Union.ValueType type)
        {
            var union = new Union { Type = type };
            union.SetValue<object>(value);
            return union;
        }

        public static void WriteValue(VariableData variable, Union value)
        {
            for (var i = 0; i < variable.Parameters.Contents.Count; i++)
            {
                if (variable.Parameters.Contents[i].Name == "Value")
                {
                    variable.Parameters[i] = value;
                    return;
                }
            }

            variable.Parameters.AddParameter("Value", value);
        }
    }

    [Serializable]
    public abstract class GraphVariableNode : SchematicGraphNode
    {
        [Editable]
        public string VariableName;

        [Editable]
        public VariableData Variable;

        protected VariableData GetVariableData()
        {
            return GraphVariableStorage.GetVariableData(Schematic, VariableName, Variable);
        }
    }

    [Serializable]
    [Node(Path = "Variables"), Tags("Default")]
    public class GetGraphVariableNode : GraphVariableNode
    {
        [Output("Value")]
        public object Value;

        public override object OnRequestValue(Port port)
        {
            var variable = GetVariableData();
            var storedValue = variable.Parameters["Value"];
            Value = GraphVariableStorage.ReadValue(storedValue);
            return Value;
        }
    }

    [Serializable]
    [Node(Path = "Variables"), Tags("Default")]
    public class SetGraphVariableNode : SchematicActionNode
    {
        [Editable]
        public string VariableName;

        [Editable]
        public VariableData Variable;

        [Input("Value", Editable = true)]
        public object Value;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var variable = GraphVariableStorage.GetVariableData(Schematic, VariableName, Variable);
            var value = HasInputConnections(nameof(Value))
                ? GetInputValueUnion(nameof(Value))
                : GraphVariableStorage.CreateUnion(Value);
            GraphVariableStorage.WriteValue(variable, value);
        }
    }
}
