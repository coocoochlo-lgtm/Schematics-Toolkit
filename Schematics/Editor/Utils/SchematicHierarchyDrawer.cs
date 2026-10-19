using BlueGraph.Editor;
using Remedy.Schematics;
using SchematicAssets;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class SchematicHierarchyDrawer
{
    static SchematicHierarchyDrawer()
    {
#if UNITY_6000_2_OR_NEWER
        // Unity 6.2 replaced the int-id hierarchy callback with one that passes an EntityId.
        EditorApplication.hierarchyWindowItemByEntityIdOnGUI += (entityId, selectionRect) =>
            OnHierarchyGUI(EditorUtility.EntityIdToObject(entityId) as GameObject, selectionRect);
#else
        EditorApplication.hierarchyWindowItemOnGUI += (instanceID, selectionRect) =>
            OnHierarchyGUI(EditorObjectIds.ToObject(instanceID) as GameObject, selectionRect);
#endif
    }

    private static GUIContent _graphButton;

    // Drawing only reads: the Hierarchy repaints constantly, and changing the object or its graph from here would mark
    // things dirty (and in Prefab Mode cleared the graph's prefab link, since a prefab's contents have no source).
    static void OnHierarchyGUI(GameObject go, Rect selectionRect)
    {
        if (go == null) return;

        var controller = go.GetComponent<SchematicObject>();
        if (controller == null) return;

        var graph = controller.SchematicGraph;
        if (graph == null)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);
            graph = prefab != null ? SchematicAssetManager.Load<SchematicGraph>(prefab, "", "") : null;
        }
        if (graph == null)
            return;

        _graphButton ??= new GUIContent(EditorGUIUtility.IconContent("d_SceneViewFx").image, "Open this object's Schematic Graph");
        if (_graphButton.image == null)
            _graphButton.text = "G";
        Rect buttonRect = new Rect(selectionRect.xMax - 20, selectionRect.y, 18, selectionRect.height);
        if (GUI.Button(buttonRect, _graphButton, EditorStyles.miniButton))
            GraphAssetHandler.OnOpenGraph(graph);
    }
}
