using Remedy.Schematics;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Connects the graph debugger to the editor: when a flow stops, its graph is brought up with the node framed, and the
/// open graph windows redraw their breakpoint boxes and debug panels whenever anything changes.
/// </summary>
[InitializeOnLoad]
public static class FlowDebuggerEditor
{
    private static Flow _lastShown;
    private static SchematicGraphNode _lastShownNode;
    private static bool _refreshQueued;

    static FlowDebuggerEditor()
    {
        FlowDebugger.Changed += QueueRefresh;
        EditorApplication.playModeStateChanged += state =>
        {
            // Stopped flows hold on to objects that are about to be destroyed.
            if (state == PlayModeStateChange.ExitingPlayMode)
                FlowDebugger.Reset();
        };
    }

    // Changes arrive in the middle of gameplay code; the windows are updated once it's done.
    private static void QueueRefresh()
    {
        if (_refreshQueued)
            return;
        _refreshQueued = true;
        EditorApplication.delayCall += Refresh;
    }

    private static void Refresh()
    {
        _refreshQueued = false;

        var flow = FlowDebugger.Current;
        bool newStop = flow != null && (flow != _lastShown || flow.PausedNode != _lastShownNode);
        _lastShown = flow;
        _lastShownNode = flow?.PausedNode;

        // A newly stopped flow's graph comes up even if it wasn't open.
        if (newStop && flow.Graph != null && SchematicGraphEditorWindow.FindOpen(flow.Graph) == null)
        {
            SchematicGraphEditorWindow.ShowNodeInGraph(flow.Graph, flow.PausedNode);
            return;
        }

        foreach (var window in Resources.FindObjectsOfTypeAll<SchematicGraphEditorWindow>())
            window.RefreshDebugState(frame: newStop && flow.Graph == window.SchematicGraph);
    }
}
