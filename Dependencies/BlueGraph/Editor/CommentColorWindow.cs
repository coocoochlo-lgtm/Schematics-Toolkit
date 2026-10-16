using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// A small floating window for picking a comment's color: the ready-made palette as swatches, plus a color field
    /// that opens Unity's color picker for anything else. It stays open while you try colors, and closes itself when
    /// the comment goes away (deleted, or rebuilt by an undo).
    /// </summary>
    public class CommentColorWindow : EditorWindow
    {
        private CommentView target;
        private ColorField field;

        public static void Open(CommentView comment, Vector2 screenPoint)
        {
            var window = CreateInstance<CommentColorWindow>();
            window.titleContent = new GUIContent("Comment Color");
            window.target = comment;
            var size = new Vector2(250, 84);
            window.minSize = window.maxSize = size;
            window.ShowUtility();
            window.position = new Rect(screenPoint, size);
            window.Build();
        }

        private void Build()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = rootVisualElement.style.paddingRight = 8;
            rootVisualElement.style.paddingTop = rootVisualElement.style.paddingBottom = 8;
            if (target == null)
                return;

            var swatches = new VisualElement();
            swatches.style.flexDirection = FlexDirection.Row;
            swatches.style.flexWrap = Wrap.Wrap;
            swatches.style.marginBottom = 6;
            foreach (var (colorName, color) in Comment.Palette)
            {
                var picked = color;
                var swatch = new Button(() => Pick(picked)) { tooltip = colorName, text = string.Empty };
                swatch.style.width = 20;
                swatch.style.height = 20;
                swatch.style.marginLeft = swatch.style.marginRight = 2;
                swatch.style.backgroundColor = picked;
                swatch.style.borderTopLeftRadius = swatch.style.borderTopRightRadius =
                    swatch.style.borderBottomLeftRadius = swatch.style.borderBottomRightRadius = 10;
                swatches.Add(swatch);
            }
            rootVisualElement.Add(swatches);

            field = new ColorField("Custom") { showAlpha = false, showEyeDropper = true, hdr = false };
            field.SetValueWithoutNotify(target.Target.Color);
            field.RegisterValueChangedCallback(e => Pick(e.newValue));
            rootVisualElement.Add(field);

            // Close once the comment is gone.
            rootVisualElement.schedule.Execute(() =>
            {
                if (target == null || target.panel == null)
                    Close();
            }).Every(250);
        }

        private void Pick(Color color)
        {
            if (target == null || target.panel == null)
            {
                Close();
                return;
            }
            target.SetColor(color);
            field?.SetValueWithoutNotify(target.Target.Color);
        }

        // Domain reloads drop the reference to the comment; nothing to edit then.
        private void CreateGUI()
        {
            if (target == null)
                rootVisualElement.schedule.Execute(Close);
        }
    }
}
