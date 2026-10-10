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

    static void OnHierarchyGUI(GameObject go, Rect selectionRect)
    {
        if (go == null) return;

        var controller = go.GetComponent<SchematicObject>();
        if (controller == null/* || controller?.SchematicGraphs.Length > 0*/) return;

        var prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);

        if (controller.SchematicGraph == null)
            controller.SchematicGraph = SchematicAssetManager.Load<SchematicGraph>(prefab,"","");
        if (controller.SchematicGraph == null)
            return;

        controller.SchematicGraph.Prefab = PrefabUtility.GetCorrespondingObjectFromSource(go);

        Rect buttonRect = new Rect(selectionRect.xMax - 20, selectionRect.y, 18, selectionRect.height);
        if (GUI.Button(buttonRect, "§", EditorStyles.miniButton))
        {
            GraphAssetHandler.OnOpenGraph(controller.SchematicGraph);
        }
    }
}
