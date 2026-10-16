using System.Reflection;
using BlueGraph.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// Shows <see cref="IntTag"/> fields on graph nodes (editable input ports and [Editable] fields) as the same tag
    /// dropdown the Inspector uses, by registering a control with BlueGraph's control factory.
    /// </summary>
    [InitializeOnLoad]
    internal static class IntTagGraphControl
    {
        static IntTagGraphControl()
        {
            ControlElementFactory.RegisterControl(typeof(IntTag), Create);
        }

        private static VisualElement Create(FieldInfo fieldInfo, NodeView view, string label)
        {
            var root = new VisualElement();
            root.AddToClassList("unity-base-field");
            root.style.flexDirection = FlexDirection.Row;

            if (!string.IsNullOrEmpty(label))
            {
                var labelElement = new Label(label);
                labelElement.AddToClassList("unity-base-field__label");
                root.Add(labelElement);
            }

            var button = new Button { tooltip = "Int Tag" };
            button.AddToClassList("unity-base-popup-field__input");
            button.style.flexGrow = 1;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            root.Add(button);

            void Refresh()
            {
                var tag = (IntTag)fieldInfo.GetValue(view.Target);
                button.text = IntTags.NameOf(tag.Value) + "  ▾";
                button.tooltip = $"Int Tag id {tag.Value}";
            }

            void Assign(int id)
            {
                var graph = view.Target.Graph;
                if (graph != null)
                    Undo.RecordObject(graph, "Change Int Tag");
                fieldInfo.SetValue(view.Target, new IntTag(id));
                view.OnPropertyChange();
                if (graph != null)
                    EditorUtility.SetDirty(graph);
                Refresh();
            }

            button.clicked += () =>
            {
                var tag = (IntTag)fieldInfo.GetValue(view.Target);
                IntTagMenu.Show(button.worldBound, tag.Value, Assign);
            };

            // Renamed or added tags show up without reopening the graph.
            void OnChanged() => Refresh();
            root.RegisterCallback<AttachToPanelEvent>(_ => { IntTagsData.Changed -= OnChanged; IntTagsData.Changed += OnChanged; Refresh(); });
            root.RegisterCallback<DetachFromPanelEvent>(_ => IntTagsData.Changed -= OnChanged);

            Refresh();
            return root;
        }
    }
}
