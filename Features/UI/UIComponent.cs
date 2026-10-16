using UnityEngine;

namespace Remedy.UI
{
    /// <summary>
    /// A UI element rendered on the UI layer at a depth set by its Layer. Extend it for specific UI elements and
    /// override <see cref="InitiateAndSubscribeEvents"/>/<see cref="UnSubscribeEvents"/> for their setup.
    /// </summary>
    [SchematicDoc(@"{""name"": ""UI Component"", ""kind"": ""behavior"", ""menu"": ""UI/UI Component"", ""description"": ""A UI element rendered on the UI layer at a depth set by its Layer. Extend it for specific UI elements and override InitiateAndSubscribeEvents/UnSubscribeEvents for their setup."", ""variables"": [""Layer""], ""use_for"": [""custom UI elements""], ""notes"": ""Usually extended rather than used directly.""}")]
    [SchematicBehaviorPath("UI/UI Component")]
    public class UIComponent : SchematicBehavior
    {
        [SchematicDoc(@"{""name"": ""Layer"", ""kind"": ""variable"", ""behavior"": ""UI Component"", ""type"": ""int"", ""default"": ""0"", ""description"": ""The UI Layer to render this Prefab's Canvas on.""}")]
        [Tooltip("The UI Layer to render this Prefab's Canvas on.")]
        public readonly VariableHandle<int> Layer = 0;

        protected override void OnStart()
        {
            UIManager.AddUIComponent(Self, Layer.Value);
            InitiateAndSubscribeEvents(Self);
        }

        protected override void OnDisable()
        {
            UIManager.RemoveUIComponent(Self, Layer.Value);
            UnSubscribeEvents(Self);
        }

        protected virtual void InitiateAndSubscribeEvents(BehaviorInstance instance)
        { }

        protected virtual void UnSubscribeEvents(BehaviorInstance instance)
        { }
    }
}
