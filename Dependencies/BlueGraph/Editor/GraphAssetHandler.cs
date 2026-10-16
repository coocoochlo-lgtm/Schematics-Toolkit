using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using System.Reflection;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Custom asset handler callback to react to "open" events in the Unity Editor
    /// </summary>
    public class GraphAssetHandler
    {
        [OnOpenAsset]
        public static bool OnOpenAsset(UnityEngine.EntityId instanceId, int line)
        {
            if (UnityEditor.EditorUtility.EntityIdToObject(instanceId) is Graph graph)
            {
                OnOpenGraph(graph);
                return true;
            }

            return false;
        }

        private static MethodInfo _instanceIdToObject;
        private static readonly object[] _idArgument = new object[1];

        /// <summary>
        /// EditorUtility.InstanceIDToObject is a compile error in newer Unity 6 versions (replaced by EntityId) but
        /// still exists, so it's reached by reflection: this compiles and works in every version.
        /// </summary>
        internal static Object InstanceIdToObject(int instanceId)
        {
            _instanceIdToObject ??= typeof(EditorUtility).GetMethod("InstanceIDToObject", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(int) }, null);
            if (_instanceIdToObject == null)
                return null;
            _idArgument[0] = instanceId;
            return _instanceIdToObject.Invoke(null, _idArgument) as Object;
        }

        /// <summary>
        /// Open the appropriate GraphEditor for the Graph asset
        /// </summary>
        public static GraphEditor OnOpenGraph(Graph graph)
        {
            var editor = UnityEditor.Editor.CreateEditor(graph) as GraphEditor;
            if (!editor)
            {
                Debug.LogWarning("No editor found for graph asset");
            } 
            else
            {
                editor.CreateOrFocusEditorWindow();
                return editor;
            }
            return null;
        }
    }
}
