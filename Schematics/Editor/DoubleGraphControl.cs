using System.Reflection;
using BlueGraph.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Remedy.Schematics
{
    /// <summary>
    /// Shows double fields on graph nodes (editable input ports and [Editable] fields) as a number box, which BlueGraph
    /// doesn't do by itself. The comparison nodes type their numbers in as doubles so nothing typed is rounded.
    /// </summary>
    [InitializeOnLoad]
    internal static class DoubleGraphControl
    {
        static DoubleGraphControl()
        {
            ControlElementFactory.RegisterControl(typeof(double), Create);
        }

        private static VisualElement Create(FieldInfo fieldInfo, NodeView view, string label)
        {
            var field = new DoubleField(label);
            field.SetValueWithoutNotify((double)fieldInfo.GetValue(view.Target));
            field.RegisterValueChangedCallback(change =>
            {
                fieldInfo.SetValue(view.Target, change.newValue);
                view.OnPropertyChange();
            });
            return field;
        }
    }
}
