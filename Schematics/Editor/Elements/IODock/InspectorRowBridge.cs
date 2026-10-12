using Remedy.Schematics;
using UnityEditor;

/// <summary>
/// Connects the Inspector's IO Dock-style rows (<see cref="InspectorRows"/>) to the Schematic Graph editor: clicking a
/// row shows it in the Inspector (with its Network Sync flag) and finds its nodes in the open graph; dragging it drops
/// it into a graph like an IO Dock row.
/// </summary>
[InitializeOnLoad]
internal static class InspectorRowBridge
{
    static InspectorRowBridge()
    {
        InspectorRows.ColorOfType = DockRows.ColorOf;
        InspectorRows.Click = row =>
        {
            var item = ToItem(row);
            // Shown after the Inspector finishes this pass (it's the one drawing the row).
            EditorApplication.delayCall += () =>
            {
                foreach (var window in UnityEngine.Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
                    if (window != null && window.SchematicGraph != null && (row.Graph == null || row.Graph == window.SchematicGraph))
                    {
                        window.Reveal(item);
                        break;
                    }
                ScopeItemInspector.Show(item);
            };
        };
        InspectorRows.Drag = row =>
        {
            var item = ToItem(row);
            DragAndDrop.PrepareStartDrag();
            DragAndDrop.SetGenericData(DockItem.DragKey, item);
            DragAndDrop.objectReferences = item.Signal != null ? new UnityEngine.Object[] { item.Signal } : System.Array.Empty<UnityEngine.Object>();
            DragAndDrop.StartDrag(item.DisplayName);
        };
    }

    private static DockItem ToItem(InspectorRows.Row row) => new()
    {
        Kind = row.Kind switch
        {
            InspectorRows.RowKind.GraphVariable => DockItem.ItemKind.GraphVariable,
            InspectorRows.RowKind.Signal => DockItem.ItemKind.Signal,
            _ => DockItem.ItemKind.Variable,
        },
        Behavior = row.Behavior,
        Path = row.Path ?? "",
        Name = row.Name,
        ValueType = row.ValueType,
        Signal = row.Signal,
        Direction = row.Direction,
        Graph = row.Graph,
        VariableId = row.VariableId,
    };
}
