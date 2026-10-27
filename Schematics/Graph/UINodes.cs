using BlueGraph;
using Remedy.Framework;
using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>A reference to an element in the UI Builder hierarchy, identified by its UXML name.</summary>
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "UI Element", Path = "UI/Elements"), Tags("UI")]
    public sealed class UIElementReferenceNode : SchematicActionNode
    {
        public const string NamePort = "Element Name";
        [Tooltip("Name of the element in the UI Builder hierarchy.")]
        public string ElementName;

        [Output(NamePort)] public string Name;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
            => SetOutputValue(NamePort, new Union(ElementName ?? string.Empty));
    }

    /// <summary>Fires when the named UI Toolkit Button is clicked in a running Schematic UI.</summary>
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On UI Button Clicked", Path = "Events/UI"), Tags("UI")]
    public sealed class OnUIButtonClicked : SchematicEventNode
    {
        [Tooltip("Name of the element in the UI Builder hierarchy.")]
        public string ElementName;

        [Output("Element Name", typeof(string))]
        public string ClickedElement;
    }

    /// <summary>Changes the text of a named UI Toolkit TextElement.</summary>
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set UI Text", Path = "UI/Elements"), Tags("UI")]
    public sealed class SetUIText : SchematicActionNode
    {
        [Tooltip("Name of the element in the UI Builder hierarchy.")]
        public string ElementName;

        [Input("Element Name", typeof(string))]
        public string Element;
        [Input("Text", typeof(string))]
        public string Text;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var runner = SchematicGraphRunner.RunnersOn(instance, (SchematicGraph)Graph).Find(r => r is ISchematicUIRunner) as ISchematicUIRunner;
            var elementName = GetInputValue("Element Name", ElementName);
            var text = runner?.QueryElement(elementName) as TextElement;
            if (text != null)
                text.text = GetInputValue<string>("Text");
        }
    }

    /// <summary>Shows or hides a named UI Toolkit element.</summary>
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set UI Visible", Path = "UI/Elements"), Tags("UI")]
    public sealed class SetUIVisible : SchematicActionNode
    {
        [Tooltip("Name of the element in the UI Builder hierarchy.")]
        public string ElementName;

        [Input("Element Name", typeof(string))]
        public string Element;
        [Input("Visible", typeof(bool))]
        public bool Visible = true;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var runner = SchematicGraphRunner.RunnersOn(instance, (SchematicGraph)Graph).Find(r => r is ISchematicUIRunner) as ISchematicUIRunner;
            var elementName = GetInputValue("Element Name", ElementName);
            var element = runner?.QueryElement(elementName) as VisualElement;
            if (element != null)
                element.style.display = GetInputValue<bool>("Visible") ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }

    /// <summary>Enables or disables a named UI Toolkit element.</summary>
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set UI Enabled", Path = "UI/Elements"), Tags("UI")]
    public sealed class SetUIEnabled : SchematicActionNode
    {
        [Tooltip("Name of the element in the UI Builder hierarchy.")]
        public string ElementName;

        [Input("Element Name", typeof(string))]
        public string Element;
        [Input("Enabled", typeof(bool))]
        public bool Enabled = true;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var runner = SchematicGraphRunner.RunnersOn(instance, (SchematicGraph)Graph).Find(r => r is ISchematicUIRunner) as ISchematicUIRunner;
            var elementName = GetInputValue("Element Name", ElementName);
            var element = runner?.QueryElement(elementName) as VisualElement;
            if (element != null)
                element.SetEnabled(GetInputValue<bool>("Enabled"));
        }
    }
}
