using UnityEngine;

namespace Remedy.UI
{
    /// <summary>
    /// A UI element rendered on the UI layer at a depth set by its Layer. Extend it for specific UI elements and
    /// override <see cref="InitiateAndSubscribeEvents"/>/<see cref="UnSubscribeEvents"/> for their setup.
    /// </summary>
    [SchematicBehaviorPath("UI/UI Component")]
    public class UIComponent : SchematicBehavior
    {
        [Tooltip("The UI Layer to render this Prefab's Canvas on.")]
        public readonly VariableHandle<int> Layer = 0;

        protected override void OnStart(BehaviorInstance instance)
        {
            UIManager.AddUIComponent(instance, Layer.Value);
            InitiateAndSubscribeEvents(instance);
        }

        protected override void OnDisable(BehaviorInstance instance)
        {
            UIManager.RemoveUIComponent(instance, Layer.Value);
            UnSubscribeEvents(instance);
        }

        protected virtual void InitiateAndSubscribeEvents(BehaviorInstance instance)
        { }

        protected virtual void UnSubscribeEvents(BehaviorInstance instance)
        { }
    }
}
