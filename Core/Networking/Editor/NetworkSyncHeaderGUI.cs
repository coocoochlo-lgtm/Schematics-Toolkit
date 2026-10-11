#if SCHEMATICS_NETCODE
using Box3D.Hybrid;
using Remedy.Schematics;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

namespace Remedy.Networking.Editor
{
    /// <summary>
    /// A "Network Sync" toggle under every GameObject's header. Turning it on makes the object sync for all players:
    /// a NetworkObject and NetworkTransform, plus a Network Box3D Body for physics objects and a Schematic Network
    /// Graph for Schematic Objects. Turning it off removes them again.
    /// </summary>
    [InitializeOnLoad]
    internal static class NetworkSyncHeaderGUI
    {
        static NetworkSyncHeaderGUI()
        {
            UnityEditor.Editor.finishedDefaultHeaderGUI += OnHeaderGUI;
        }

        private static void OnHeaderGUI(UnityEditor.Editor editor)
        {
            if (editor.targets.Length != 1 || editor.target is not GameObject gameObject)
                return;
            // Prefab assets are edited in Prefab Mode, like the rest of the toolkit.
            if (PrefabUtility.IsPartOfPrefabAsset(gameObject) || EditorUtility.IsPersistent(gameObject))
                return;

            bool synced = gameObject.TryGetComponent(out NetworkObject _);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                bool wanted = EditorGUILayout.ToggleLeft(new GUIContent("Network Sync",
                    "Sync this object for every player (Netcode for GameObjects): its Transform, its Box3D body if it has one, and its Schematic graph if that's marked Networked."),
                    synced);
                if (EditorGUI.EndChangeCheck())
                {
                    if (wanted) Enable(gameObject);
                    else Disable(gameObject);
                }
            }

            if (!synced)
                return;

            // A Networked graph needs its relay.
            var schematicObject = gameObject.GetComponent<SchematicObject>();
            bool graphNetworked = schematicObject != null && schematicObject.SchematicGraph != null && schematicObject.SchematicGraph.Networked;
            if (graphNetworked && !gameObject.TryGetComponent(out SchematicNetworkGraph _))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("This object's graph is Networked but can't sync yet.", MessageType.Warning);
                    if (GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(38)))
                        Undo.AddComponent<SchematicNetworkGraph>(gameObject);
                }
            }
        }

        private static void Enable(GameObject gameObject)
        {
            Undo.SetCurrentGroupName("Enable Network Sync");
            if (!gameObject.TryGetComponent(out NetworkObject _))
                Undo.AddComponent<NetworkObject>(gameObject);
            if (!gameObject.TryGetComponent(out NetworkTransform _))
                Undo.AddComponent<NetworkTransform>(gameObject);
            if (gameObject.TryGetComponent(out Box3DBody _) && !gameObject.TryGetComponent(out NetworkBox3DBody _))
                Undo.AddComponent<NetworkBox3DBody>(gameObject);

            var schematicObject = gameObject.GetComponent<SchematicObject>();
            if (schematicObject != null && schematicObject.SchematicGraph != null && schematicObject.SchematicGraph.Networked
                && !gameObject.TryGetComponent(out SchematicNetworkGraph _))
                Undo.AddComponent<SchematicNetworkGraph>(gameObject);
        }

        private static void Disable(GameObject gameObject)
        {
            Undo.SetCurrentGroupName("Disable Network Sync");
            // Dependents first: they require the NetworkObject.
            if (gameObject.TryGetComponent(out NetworkBox3DBody body)) Undo.DestroyObjectImmediate(body);
            if (gameObject.TryGetComponent(out SchematicNetworkGraph graph)) Undo.DestroyObjectImmediate(graph);
            if (gameObject.TryGetComponent(out NetworkTransform transform)) Undo.DestroyObjectImmediate(transform);
            foreach (var other in gameObject.GetComponents<NetworkBehaviour>())
                if (other != null)
                {
                    Debug.LogWarning($"{gameObject.name} still has {other.GetType().Name}, which needs its NetworkObject; remove it first.", gameObject);
                    return;
                }
            if (gameObject.TryGetComponent(out NetworkObject networkObject)) Undo.DestroyObjectImmediate(networkObject);
        }
    }
}
#endif
