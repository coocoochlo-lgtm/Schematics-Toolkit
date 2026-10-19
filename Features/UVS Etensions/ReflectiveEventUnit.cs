using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Unity.VisualScripting;
using UnityEngine;

namespace Remedy.Framework
{
    [SerializationVersion("A")]
    public class ReflectiveEventUnit<T> : EventUnit<SerializableDictionary<string, object>> where T : ReflectiveEventUnit<T>, IUnit
    {
        private static Dictionary<string, object> arguments = new();
        public static object InvokationTarget;
        public ValueInput EventTarget;
        protected override bool register => true;
        public static Dictionary<Type, string> EventNames = new();
        private static GameObject _nullObject;
        protected static GameObject nullObject
        {
            get
            {
                if (_nullObject == null)
                    _nullObject = new GameObject();
                _nullObject.hideFlags = HideFlags.HideAndDontSave;
                return _nullObject;
            }
        }

        public override EventHook GetHook(GraphReference reference)
        {
            var name = GetType().Name;
            if (!EventNames.ContainsKey(typeof(T)))
                EventNames.Add(typeof(T), name);
            return new EventHook(name);
        }

        protected static void ModularInvoke(object invokationTarget, params (string Name, object Value)[] args)
        {
            InvokationTarget = invokationTarget;

            if (!EventNames.ContainsKey(typeof(T)))
                EventNames.Add(typeof(T), typeof(T).Name);

            arguments.Clear();
            foreach (var arg in args)
                arguments.Add(arg.Item1, arg.Item2);

            SerializableDictionary<string, object> argDictionary = new();
            foreach (var arg in args) argDictionary.Add(arg.Name, arg.Value);

            EventBus.Trigger(EventNames[typeof(T)], argDictionary);
        }

        protected override void AssignArguments(Flow flow, SerializableDictionary<string, object> parameters)
        {
            foreach (var param in parameters.Keys)
            {
                var field = this.GetType()
                    .GetField(param);

                if (field == null)
                {
                    Debug.LogWarning($"Parameter {param} was not found as a Value Output to the Reflective Event Unit");
                    continue;
                }


                OutputTypeAttribute outputTypeAttribute = field.GetCustomAttribute<OutputTypeAttribute>();

                if (outputTypeAttribute != null)
                {
                    Type outputType = outputTypeAttribute.OutputType;
                    var outputName = Regex.Replace(field.Name, "(?<!^)([A-Z])", " $1");

                    if (valueOutputs.Contains(outputName))
                    {
                        var fieldValue = (ValueOutput)field.GetValue(this);
                        flow.SetValue(fieldValue, parameters[param]);
                    }


                }
                else
                {
                    Debug.LogWarning($"The Field of {this.GetType().Name} for Argument {param} does not have the OutputType attribute.");
                }
            }
        }

        protected override void Definition()
        {
            base.Definition();

            EventTarget = ValueInput<GameObject>("Target Object");

            Type type = GetType();

            FieldInfo[] fields = type.GetFields();

            foreach (FieldInfo field in fields)
            {
                OutputTypeAttribute outputTypeAttribute = field.GetCustomAttribute<OutputTypeAttribute>();

                if (outputTypeAttribute != null)
                {
                    Type outputType = outputTypeAttribute.OutputType;
                    object fieldValue = field.GetValue(this);
                    field.SetValue(this, ValueOutput(outputType, Regex.Replace(field.Name, "(?<!^)([A-Z])", " $1")));
                }
            }
        }

        protected override bool ShouldTrigger(Flow flow, SerializableDictionary<string, object> args)
        {
            if (EventTarget == null || EventTarget.connection == null) return true;
            var target = flow.GetValue<object>(EventTarget);
            return target == InvokationTarget || target == null;
        }
    }
}