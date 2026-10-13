using System;
using UnityEditor;
using UnityEngine;

namespace Remedy.Framework.IntTagEditor
{
    /// <summary>
    /// The small "Add Tag" popup: type a name, press Enter, and the new tag (with a freshly allocated id) is picked.
    /// </summary>
    public sealed class AddIntTagWindow : EditorWindow
    {
        private const string FieldName = "IntTagNameField";

        private string _name = "";
        private string _error;
        private bool _focused;
        private Action<int> _onCreated;

        /// <summary>Opens the popup in the middle of the editor. <paramref name="onCreated"/> gets the new tag's id.</summary>
        public static void Open(Action<int> onCreated, string suggestedName = "")
        {
            var window = CreateInstance<AddIntTagWindow>();
            window.titleContent = new GUIContent("Add Int Tag");
            window._onCreated = onCreated;
            window._name = suggestedName ?? "";

            var size = new Vector2(320, 96);
            // Menu callbacks run outside OnGUI (no mouse position), so center on the main editor window instead.
            var main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center.x - size.x * 0.5f, main.center.y - size.y * 0.5f, size.x, size.y);
            window.minSize = window.maxSize = size;
            window.ShowUtility();
            window.Focus();
        }

        private void OnGUI()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    Create();
                    e.Use();
                    return;
                }
                if (e.keyCode == KeyCode.Escape)
                {
                    Close();
                    e.Use();
                    return;
                }
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("New tag name", EditorStyles.miniBoldLabel);
            GUI.SetNextControlName(FieldName);
            EditorGUI.BeginChangeCheck();
            _name = EditorGUILayout.TextField(_name);
            if (EditorGUI.EndChangeCheck())
                _error = null;
            if (!_focused)
            {
                EditorGUI.FocusTextInControl(FieldName);
                _focused = true;
            }

            if (!string.IsNullOrEmpty(_error))
                EditorGUILayout.LabelField(_error, EditorStyles.miniLabel);
            else
            {
                var data = IntTagsData.Instance;
                int nextId = data != null ? data.NextId : IntTags.FirstUserTag;
                EditorGUILayout.LabelField($"Will get id {nextId}. Use '/' to group tags (e.g. Enemies/Boss).", EditorStyles.miniLabel);
            }

            GUILayout.FlexibleSpace();
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(70)))
                    Close();
                if (GUILayout.Button("Add", GUILayout.Width(70)))
                    Create();
            }
            EditorGUILayout.Space(4);
        }

        private void Create()
        {
            int id = IntTagsEditorData.AddTag(_name, out _error);
            if (id == IntTags.Untagged)
            {
                Repaint();
                return;
            }
            var callback = _onCreated;
            _onCreated = null;
            Close();
            callback?.Invoke(id);
        }
    }
}
