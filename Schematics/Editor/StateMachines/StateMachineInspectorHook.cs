using Remedy.Schematics;
using UnityEditor;
using UnityEngine;

/// <summary>Adds "Open State Machine" under the State Machine Behavior in the Schematic Object's Inspector.</summary>
[InitializeOnLoad]
internal static class StateMachineInspectorHook
{
    static StateMachineInspectorHook()
    {
        SchematicBehaviorsHeaderGUI.ExtraAttachmentGUI += (root, gameObject, attachment) =>
        {
            if (attachment?.Metadata == null || attachment.Metadata.BehaviorType.Type != typeof(StateMachine))
                return;

            var definition = StateMachineEditorUtility.DefinitionFor(root, gameObject, create: false);
            if (GUILayout.Button(definition != null ? "Open State Machine" : "Create State Machine"))
            {
                definition = StateMachineEditorUtility.DefinitionFor(root, gameObject, create: true);
                if (definition != null)
                    StateMachineWindow.Open(definition);
                GUIUtility.ExitGUI();
            }
        };
    }
}
