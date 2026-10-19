// project armada

#pragma warning disable 0414

using BlueGraph;
using UnityEngine;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using System;

namespace Remedy.Schematics
{
    [SchematicDoc(@"{""name"": ""Log"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Debug"", ""description"": ""Writes a message to the Console (normal, warning or error)."", ""use_for"": [""debugging"", ""checking a flow ran""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
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


        [Editable]
        public string Text;
        [Editable]
        public DebugType Type = DebugType.Normal;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
                if (Type == DebugType.Normal)
                    Debug.Log(Text);
                else if (Type == DebugType.Error)
                    Debug.LogError(Text);
                else if (Type == DebugType.Warning)
                    Debug.LogWarning(Text);
        }
    }
}