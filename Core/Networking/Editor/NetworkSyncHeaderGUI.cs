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

            // A Schematic Object needs its relay for its networked graph, Variables, Signals and Behavior state.
            var schematicObject = gameObject.GetComponent<SchematicObject>();
            if (schematicObject != null && !gameObject.TryGetComponent(out SchematicNetworkGraph _))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.HelpBox("This Schematic Object's networked graph, Variables and Signals can't sync yet (it needs a Schematic Network Graph).", MessageType.Warning);
                    if (GUILayout.Button("Fix", GUILayout.Width(50), GUILayout.Height(38)))
                        Undo.AddComponent<SchematicNetworkGraph>(gameObject);
                }
            }

            // Who moves it: the server/authority, or whoever owns it (vehicles: the driver).
            if (gameObject.TryGetComponent(out NetworkTransform networkTransform))
            {
                bool ownerMoves = networkTransform.AuthorityMode == NetworkTransform.AuthorityModes.Owner;
                EditorGUI.BeginChangeCheck();
                ownerMoves = EditorGUILayout.ToggleLeft(new GUIContent("Simulated by Owner",
                    "Whoever owns this object moves it (its NetworkTransform uses owner authority, and its Box3D body is simulated by the owner). " +
                    "For things players drive or carry, like vehicles: no input lag for the driver. Off: the server (authority) moves it."), ownerMoves);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(networkTransform, "Simulated by Owner");
                    networkTransform.AuthorityMode = ownerMoves ? NetworkTransform.AuthorityModes.Owner : NetworkTransform.AuthorityModes.Server;
                    EditorUtility.SetDirty(networkTransform);
                }
                if (!ownerMoves && WantsOwnerSimulation(schematicObject))
                    EditorGUILayout.HelpBox("A Behavior on this object (e.g. a vehicle) works best simulated by its owner.", MessageType.Info);
            }
        }

        // Any attached Behavior marked [NetworkOwnerSimulated] (vehicles).
        private static bool WantsOwnerSimulation(SchematicObject schematicObject)
        {
            if (schematicObject == null || schematicObject.Behaviors == null)
                return false;
            foreach (var list in schematicObject.Behaviors.Values)
            {
                if (list?.Items == null) continue;
                foreach (var attachment in list.Items)
                {
                    var type = attachment?.Metadata != null ? attachment.Metadata.BehaviorType.Type : null;
                    if (type != null && System.Attribute.IsDefined(type, typeof(NetworkOwnerSimulatedAttribute), true))
                        return true;
                }
            }
            return false;
        }

        private static void Enable(GameObject gameObject)
        {
            Undo.SetCurrentGroupName("Enable Network Sync");
            if (!gameObject.TryGetComponent(out NetworkObject _))
                Undo.AddComponent<NetworkObject>(gameObject);
            var schematicObject = gameObject.GetComponent<SchematicObject>();
            if (!gameObject.TryGetComponent(out NetworkTransform networkTransform))
            {
                networkTransform = Undo.AddComponent<NetworkTransform>(gameObject);
                // Vehicles and the like: the driver (owner) moves it.
                if (networkTransform != null && WantsOwnerSimulation(schematicObject))
                    networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            }
            if (gameObject.TryGetComponent(out Box3DBody _) && !gameObject.TryGetComponent(out NetworkBox3DBody _))
                Undo.AddComponent<NetworkBox3DBody>(gameObject);

            // Every Schematic Object gets its relay: networked graph, Variables, Signals and Behavior state.
            if (schematicObject != null && !gameObject.TryGetComponent(out SchematicNetworkGraph _))
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
