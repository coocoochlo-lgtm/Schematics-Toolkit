using Remedy.Common;
using Remedy.Schematics;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Remedy.LevelDesign
{
    public class TriggerVolumeCreator : EditorWindow
    {
        private int selectedIndex = 0;
        private string[] triggerNames;

        [MenuItem("Tools/Level Design/Create Trigger Volume...", false, 10)]
        public static void ShowWindow()
        {
            var window = GetWindow<TriggerVolumeCreator>("Create Trigger Volume");
            Vector2 size = new Vector2(300, 150);
            window.minSize = size;
            window.maxSize = size;
            window.Show();
        }

        private void OnEnable()
        {
            List<string> triggerNameList = new();
            if (GameData.Instance != null && GameData.Instance.Triggers != null)
            {
                foreach(var triggerType in GameData.Instance.Triggers.TriggerTypes)
                {
                    triggerNameList.Add(triggerType.Name);
                }
            }
            triggerNames = triggerNameList.ToArray();
        }

        private void OnGUI()
        {
            if (triggerNames == null || triggerNames.Length == 0)
            {
                EditorGUILayout.LabelField("Trigger types not loaded.");
                return;
            }

            EditorGUILayout.LabelField("Select Trigger Type", EditorStyles.boldLabel);
            selectedIndex = EditorGUILayout.Popup(selectedIndex, triggerNames);

            if (GUILayout.Button("Create Trigger Volume"))
            {
                var go = new GameObject("Trigger Volume - " + triggerNames[selectedIndex]);
                var schematic = go.AddComponent<SchematicObject>();
                var volume = schematic.AttachBehavior<TriggerVolume>();
                go.GetComponent<BoxCollider>().isTrigger = true;
                volume.SetOverride("TriggerType", selectedIndex, typeof(int));

                Undo.RegisterCreatedObjectUndo(go, "Create Trigger Volume");
                Selection.activeGameObject = go;
                Close();
            }
        }
    }

}