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
