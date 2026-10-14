using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Build a basic window container for the BlueGraph canvas
    /// </summary>
    public class GraphEditorWindow : EditorWindow
    {
        public CanvasView Canvas { get; protected set; }

        public Graph Graph { get; protected set; }

        private int cacheDelay = 10;
        private int cacheTick = 0;

        /// <summary>
        /// Load a graph asset in this window for editing
        /// </summary>
        public virtual void Load(Graph graph)
        {
            Graph = graph;
            Graph.IsBeingEditted = true;
            //Graph.ReconstructPortConnections();

            Canvas = new CanvasView(this);
            Canvas.Load(graph);
            Canvas.StretchToParentSize();
            rootVisualElement.Add(Canvas);

            titleContent = new GUIContent(graph.name);
            Repaint();
        }

        private void OnDestroy()
        {
            OnClose();
        }

        protected virtual void OnFocus()
        {
            if (Graph != null)   
                Graph.IsBeingEditted = true;
        }

        protected virtual void OnLostFocus()
        {
            // A C key held for drawing comments releases elsewhere now, so stop treating it as held.
            Canvas?.ReleaseCommentKey();

            if (Graph != null)
                Graph.IsBeingEditted = false;
        }

        /// <summary>
        /// Override to add additional functional to the closing functionality of the Graph Editor.
        /// </summary>
        protected virtual void OnClose() 
        { }

        protected virtual void Update()
        {
            // Canvas can be invalidated when the Unity Editor
            // is closed and reopened with this editor window persisted.
            if (Canvas == null)
            {
                Close();
                return;
            }

            cacheTick++;

            if(cacheTick%cacheDelay == 0)
            {
                if (Application.isPlaying)
                {
                    Graph?.ReconstructPortConnections();
                }
                else
                {
                    Graph?.CachePortConnections();
                }
            }

            Canvas.Update();
        }

        /// <summary>
        /// Restore an already opened graph after a reload of assemblies
        /// </summary>
        protected virtual void OnEnable()
        {
            if (Graph)
            {
                Load(Graph);
            }
        }

        private Task NextEditorFrame()
        {
            var tcs = new TaskCompletionSource<bool>();
            EditorApplication.delayCall += () => tcs.SetResult(true);
            return tcs.Task;
        }
    }
}
