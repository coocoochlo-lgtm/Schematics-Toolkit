// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Log"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Debug"", ""description"": ""Writes a message to the Console (normal, warning or error): its Text, then its Value when one is connected (or put {0} in Text to place the Value there). Both can be typed in or connected."", ""inputs"": [{""name"": ""Text"", ""type"": ""string""}, {""name"": ""Value"", ""type"": ""object""}], ""use_for"": [""debugging"", ""checking a flow ran"", ""watching a value change (health, speed, input)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [NodeColor(NodeColors.Debug)]
    [Node(Path = "Debug"), Tags("Default")] 
    public class Log : SchematicActionNode
    {
        public enum DebugType
        {
            Normal,
            Error,
            Warning
        }


        [Input("Text", Editable = true)]
        public string Text;
        /// <summary>Any value to show after the Text (or where it says {0}): a number, a vector, an object, a list...</summary>
        [Input("Value", Editable = false)]
        public object Value;
        [Tooltip("How it shows in the Console: a normal message, a yellow warning, or a red error.")]
        [Editable]
        public DebugType Type = DebugType.Normal;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            string text = GetInputValue("Text", Text) ?? "";
            if (HasInputConnections("Value"))
            {
                string value = Describe(GetInputValue<object>("Value", null));
                text = text.Contains("{0}") ? text.Replace("{0}", value)
                    : string.IsNullOrEmpty(text) ? value
                    : text.TrimEnd() + " " + value;
            }

            // Clicking the message in the Console selects the object that ran the graph.
            if (Type == DebugType.Normal)
                Debug.Log(text, instance);
            else if (Type == DebugType.Error)
                Debug.LogError(text, instance);
            else if (Type == DebugType.Warning)
                Debug.LogWarning(text, instance);
        }

        public override void OnValidate()
        {
            // Logs saved before Text and Value were inputs don't have their ports.
            EnsureDataPort("Text", typeof(string), PortDirection.Input);
            EnsureDataPort("Value", typeof(object), PortDirection.Input);
            base.OnValidate();
        }

        // A value as readable text: Unity objects by name, lists as [a, b, c], nothing as "null".
        private static string Describe(object value)
        {
            switch (value)
            {
                case null: return "null";
                case Remedy.Framework.Union union:
                    var unboxed = SignalArgs.ToObject(union);
                    return unboxed is Remedy.Framework.Union ? union.ToString() : Describe(unboxed);
                case UnityEngine.Object unityObject: return unityObject != null ? unityObject.name : "null (destroyed)";
                case string text: return text;
                case System.Collections.IEnumerable items:
                    var parts = new System.Collections.Generic.List<string>();
                    foreach (var item in items)
                        parts.Add(Describe(item));
                    return "[" + string.Join(", ", parts) + "]";
                default: return value.ToString();
            }
        }
    }
}