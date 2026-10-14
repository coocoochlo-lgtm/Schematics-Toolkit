using UnityEditor;
using UnityEngine;

namespace Remedy.LevelStreaming.Editor
{
    /// <summary>
    /// Draws a <see cref="LevelReference"/> on one line: the kind (Scene or Prefab) and a picker for it. Picking a
    /// scene stores its path and GUID, and a scene that was renamed or moved is followed by its GUID.
    /// </summary>
    [CustomPropertyDrawer(typeof(LevelReference))]
    public class LevelReferenceDrawer : PropertyDrawer
    {
        private const float KindWidth = 64f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label) => EditorGUIUtility.singleLineHeight;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var kind = property.FindPropertyRelative(nameof(LevelReference.Kind));
            var path = property.FindPropertyRelative(nameof(LevelReference.ScenePath));
            var guid = property.FindPropertyRelative("_sceneGuid");
            var prefab = property.FindPropertyRelative(nameof(LevelReference.Prefab));

            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);
            int indent = EditorGUI.indentLevel;
            EditorGUI.indentLevel = 0;

            var kindRect = new Rect(position.x, position.y, KindWidth, position.height);
            var fieldRect = new Rect(position.x + KindWidth + 2f, position.y, position.width - KindWidth - 2f, position.height);
            EditorGUI.PropertyField(kindRect, kind, GUIContent.none);

            if (kind.enumValueIndex == (int)LevelReference.LevelKind.Prefab)
            {
                EditorGUI.ObjectField(fieldRect, prefab, typeof(GameObject), GUIContent.none);
            }
            else
            {
                var scene = FindScene(path, guid);
                // A path or name that doesn't lead to a scene asset (e.g. typed in code) shows as text, so it isn't lost.
                bool unresolved = scene == null && !string.IsNullOrEmpty(path.stringValue);
                if (unresolved)
                {
                    var textRect = new Rect(fieldRect.x, fieldRect.y, fieldRect.width - 22f, fieldRect.height);
                    EditorGUI.PropertyField(textRect, path, GUIContent.none);
                    var pickRect = new Rect(fieldRect.xMax - 20f, fieldRect.y, 20f, fieldRect.height);
                    var picked = EditorGUI.ObjectField(pickRect, GUIContent.none, null, typeof(SceneAsset), false);
                    if (picked != null)
                        Assign(path, guid, picked as SceneAsset);
                }
                else
                {
                    EditorGUI.BeginChangeCheck();
                    var picked = (SceneAsset)EditorGUI.ObjectField(fieldRect, GUIContent.none, scene, typeof(SceneAsset), false);
                    if (EditorGUI.EndChangeCheck())
                        Assign(path, guid, picked);
                }
            }

            EditorGUI.indentLevel = indent;
            EditorGUI.EndProperty();
        }

        private static void Assign(SerializedProperty path, SerializedProperty guid, SceneAsset scene)
        {
            string assetPath = scene != null ? AssetDatabase.GetAssetPath(scene) : "";
            path.stringValue = assetPath;
            guid.stringValue = string.IsNullOrEmpty(assetPath) ? "" : AssetDatabase.AssetPathToGUID(assetPath);
        }

        // The GUID wins (it survives renames and moves); the path is kept in step with it.
        private static SceneAsset FindScene(SerializedProperty path, SerializedProperty guid)
        {
            if (!string.IsNullOrEmpty(guid.stringValue))
            {
                string current = AssetDatabase.GUIDToAssetPath(guid.stringValue);
                if (!string.IsNullOrEmpty(current))
                {
                    var byGuid = AssetDatabase.LoadAssetAtPath<SceneAsset>(current);
                    if (byGuid != null)
                    {
                        if (path.stringValue != current)
                            path.stringValue = current;
                        return byGuid;
                    }
                }
            }
            if (string.IsNullOrEmpty(path.stringValue))
                return null;
            var byPath = AssetDatabase.LoadAssetAtPath<SceneAsset>(path.stringValue);
            if (byPath != null && string.IsNullOrEmpty(guid.stringValue))
                guid.stringValue = AssetDatabase.AssetPathToGUID(path.stringValue);
            return byPath;
        }
    }
}
