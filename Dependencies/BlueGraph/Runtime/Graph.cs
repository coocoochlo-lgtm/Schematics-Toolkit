
using BlueGraph.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace BlueGraph
{
    public interface IGraph
    {
        T GetNode<T>() where T : Node;

        IEnumerable<T> GetNodes<T>() where T : Node;

        void AddNode(Node node);

        void RemoveNode(Node node);

        void AddEdge(Port output, Port input);

        void RemoveEdge(Port output, Port input);
    }

    public abstract class Graph : ScriptableObject, IGraph
    {
        /// <summary>
        /// Retrieve the title of the graph displayed in the editor.
        ///
        /// Override to provide identifiable information between
        /// different types of graphs (e.g. NPC AI versus Dialog Tree)
        /// </summary>
        public virtual string Title 
        {
            get { return "BLUEGRAPH"; }
        }

        public bool IsBeingEditted = false;

        /// <summary>
        /// Retrieve the min zoom value scale used by CanvasView
        /// 
        /// Override to provide other value for the min zoom scale of the CanvasView
        /// </summary>
        public virtual float ZoomMinScale 
        { 
            get {
                #if UNITY_EDITOR 
                return UnityEditor.Experimental.GraphView.ContentZoomer.DefaultMinScale; 
                #else
                return 1.0f;
                #endif
            } 
        }

        /// <summary>
        /// Retrieve the max zoom value scale used by CanvasView
        /// 
        /// Override to provide other value for the max zoom scale of the CanvasView
        /// </summary>
        public virtual float ZoomMaxScale 
        { 
            get {
                #if UNITY_EDITOR 
                return UnityEditor.Experimental.GraphView.ContentZoomer.DefaultMaxScale; 
                #else
                return 1.0f;
                #endif
            }
        }

        public Action<VisualElement> OnNodeViewCreated;
        public Action<Node> OnNodeCreated;

        [SerializeField]
        private SerializableDictionary<SerializableType, Node[]> nodeByTypeCache;
        /// <summary>
        /// The nodes Cached in the Graph, by their type so they can be easily queried. 
        /// </summary>
        protected SerializableDictionary<SerializableType, Node[]> NodesByTypeCache => nodeByTypeCache ??= new();
        /// <summary>
        /// The cached connections between nodes from the Schematics Editor in a Dictionary that allows them to be reconstructed if they're lost. 
        /// </summary>
        [SerializeField]
        [HideInInspector]
        internal SerializableDictionary<string, string[]> portConnectionCache = new();
        private Dictionary<string, List<string>> reconstructedPorts = new();

        /// <summary>
        /// A Cache pairing Port IDs to their Ports that is constructed at Runtime to improve Port Lookup time
        /// </summary>
        [SerializeField]
        [HideInInspector]
        private SerializableDictionary<string, Port> runtimePortCache = new();

        /// <summary>
        /// Retrieve all nodes on this graph
        /// </summary>
        public IReadOnlyList<Node> Nodes
        {
            get { return nodes.AsReadOnly(); }
        }

        [SerializeReference, HideInInspector] 
        private List<Node> nodes = new List<Node>();
        
        /// <summary>
        /// All comments to display in the editor for this Graph
        /// </summary>
        internal List<Comment> Comments
        {
            get { return comments; }
        }

        /// <summary>Read-only comments for editor graph overviews.</summary>
        public IReadOnlyList<Comment> EditorComments => comments;

        [SerializeField, HideInInspector]
        private List<Comment> comments = new List<Comment>();

        /// <summary>
        /// All notes to display in the editor for this Graph
        /// </summary>
        internal List<Note> Notes
        {
            get { return notes ??= new List<Note>(); }
        }

        /// <summary>Read-only notes for editor graph overviews.</summary>
        public IReadOnlyList<Note> EditorNotes => notes ??= new List<Note>();

        [SerializeField, HideInInspector]
        private List<Note> notes = new List<Note>();
        
        /// <summary>
        /// Graph serialization version for safely handling automatic upgrades.
        /// </summary>
        public int AssetVersion
        {
            get { return assetVersion; }
        }

        [SerializeField, HideInInspector] 
        private int assetVersion = 1;

        private bool reconstructed = false;

        /// <summary>
        /// Propagate OnDisable to all nodes.
        /// </summary>
        private void OnDisable()
        {
            OnGraphDisable();

            foreach (var node in Nodes)
            {
                if (node == null) continue;
                node.Disable();
            }
        }
        
        /// <summary>
        /// Propagate OnEnable to all nodes.
        /// </summary>
        private void OnEnable()
        {
            OnGraphEnable();

            foreach (var node in Nodes)
            {
                // TODO: Remove null nodes
                if (node == null) continue;
                node.Enable();
            }
        }
        
        /// <summary>
        /// Propagate OnValidate to all nodes.
        /// </summary>
        public virtual void OnValidate()
        {
            if (!Application.isPlaying)
                CacheNodesByType();

            reconstructed = false;

            OnGraphValidate();

            nodes = nodes.Where(item => item != null).ToList();

            foreach (var node in Nodes)
            {
                if (node == null) continue;
                node.Validate();
            }
        }
        
        /// <summary>
        /// Called during Unity's <c>OnDisable</c> event and before 
        /// <c>OnDisable</c> of all nodes on the graph.
        /// </summary>
        protected virtual void OnGraphDisable() { }

        /// <summary>
        /// Called during Unity's <c>OnEnable</c> event and before 
        /// <c>OnEnable</c> of all nodes on the graph.
        /// </summary>
        protected virtual void OnGraphEnable() { }
        

        /// <summary>
        /// Called during Unity's <c>OnValidate</c> event and before 
        /// <c>OnValidate</c> of all nodes on the graph.
        /// </summary>
        public virtual void OnGraphValidate() { }
        /// <summary>
        /// Find a node on the Graph by unique ID 
        /// </summary>
        public Node GetNodeById(string id)
        {
            return nodes.Find((node) => node != null && node.ID == id);
        }

        /// <summary>
        /// Find the first node on the Graph of, or inherited from, the given type. 
        /// </summary>
        public T GetNode<T>() where T : Node
        {
            return nodes.Find((node) => typeof(T).IsAssignableFrom(node.GetType())) as T;
        } 
        /// <summary>
          /// Find the first node on the Graph of, or inherited from, the given type. 
          /// </summary>
        public Node GetNode(Type type)
        {
            return nodes.Find((node) => type.IsAssignableFrom(node.GetType()));
        }

        /// <summary>
        /// Find all nodes on the Graph of, or inherited from, the given type. 
        /// </summary>
        public IEnumerable<T> GetNodes<T>() where T : Node
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (typeof(T).IsAssignableFrom(nodes[i].GetType()))
                {
                    yield return nodes[i] as T;
                }
            }
        }

        /// <summary>
        /// Find all nodes on the Graph of, or inherited from, the given type, from the <see cref="NodesByTypeCache"/>.
        /// </summary>
        public Node[] GetCachedNodes<TNode>() where TNode : Node
        {
            return NodesByTypeCache[typeof(TNode)];
        }

        /// <summary>
        /// Add a new node to the Graph.
        /// 
        /// <para>
        ///     <see cref="Node.OnAddedToGraph"/> and <see cref="Node.OnEnable"/> 
        ///     will be called <b>after</b> it has been added, in that order.
        /// </para>
        /// </summary>
        public void AddNode(Node node)
        {
            node.Graph = this;
            nodes.Add(node);

            node.OnAddedToGraph();
            node.Enable();
        }

        /// <summary>
        /// Add a set of new nodes to the Graph.
        /// 
        /// <para>
        ///     <see cref="Node.OnAddedToGraph"/> and <see cref="Node.OnEnable"/> 
        ///     will be called per-node <b>after</b> they've all been added, in that order.
        /// </para>
        /// </summary>
        /// <param name="nodes"></param>
        public void AddNodes(IEnumerable<Node> newNodes)
        {
            foreach (var node in newNodes)
            {
                node.Graph = this;
                nodes.Add(node);
            }

            foreach (var node in newNodes)
            {
                node.OnAddedToGraph();
            }

            foreach (var node in newNodes)
            {
                node.Enable();
            }
        }
        
        /// <summary>
        /// Remove a node from the Graph.
        /// 
        /// <para>
        ///     <see cref="Node.OnDisable"/> and <see cref="Node.OnRemovedFromGraph"/> 
        ///     will be called <b>before</b> it has been removed, in that order.
        /// </para>
        /// </summary>
        public void RemoveNode(Node node)
        {
            if (node == null)
            {
                nodes.RemoveAll((n) => n == null);
                return;
            }

            // Removed whatever its callbacks or broken links throw, so a damaged node can always be deleted
            // (a throw used to leave it disabled but still in the graph: a node that could neither run nor go).
            try
            {
                node.Disable();
                node.OnRemovedFromGraph();
                node.DisconnectAllPorts();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            nodes.Remove(node);
            node.Graph = null;
        }

        /// <summary>
        /// Caches the Connections between ports so they can be recreated if they are lost at Runtime.
        /// </summary>
        public void CachePortConnections()
        {
            portConnectionCache.Clear();

            foreach (var node in Nodes)
            {
                if (node == null) continue;
                foreach(var kvp in node.Ports)
                {
                    int connectionCount = kvp.Value.ConnectedPorts.Count();

                    portConnectionCache[kvp.Value.ID] = new string[connectionCount];

                    if (connectionCount == 0) continue;

                    for (int i = 0; i < connectionCount; i++)
                    {
                        try
                        {

                        }
                        catch(Exception e)
                        {
                            portConnectionCache[kvp.Value.ID][i] = kvp.Value.ConnectedPorts.ElementAt(i).ID;
                            Debug.LogError(e);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Caches all the Nodes in the Graph By Type in the <see cref="NodesByTypeCache"/> Dictionary
        /// </summary>
        public void CacheNodesByType()
        {
            NodesByTypeCache.Clear();
            ResetExtendedNodeCaches();

            foreach (var node in Nodes)
            {
                if (node == null) continue;
                var attr = node.GetType().GetCustomAttribute<CacheToAttribute>(false);
                CacheNodeByType(node, attr);
            }
        }

        /// <summary>
        /// Override this to handle the clearing of extended Node Caches
        /// </summary>
        protected virtual void ResetExtendedNodeCaches()
        { }

        /// <summary>
        /// Override this for cache handling. 
        /// </summary>
        /// <param name="node"></param>
        protected virtual void CacheNodeByType(Node node, CacheToAttribute attr)
        {
            if (attr != null)
            {
                if (!NodesByTypeCache.ContainsKey(attr.Type))
                    NodesByTypeCache.Add(attr.Type, new Node[0]);
                NodesByTypeCache[attr.Type] = NodesByTypeCache[attr.Type].Append((Node)node).ToArray();
            }
            if (attr == null || attr.CacheAsBoth)
            {
                var type = node.GetType();

                if (!NodesByTypeCache.ContainsKey(type) || NodesByTypeCache[type] == null)
                    NodesByTypeCache[type] = new Node[0];
                NodesByTypeCache[type] = NodesByTypeCache[type].Append((Node)node).ToArray();
            }
        }

        /// <summary>
        /// Uses the previously Cached Node Port information to Reconstruct the Graph during runtime.
        /// </summary>
        public void ReconstructPortConnections()
        {
            if (reconstructed) return;
            foreach (var node in Nodes)
            {
                reconstructedPorts.Clear();

                if (node == null) continue;
                foreach(var kvp in node.Ports)
                {
                    if (!portConnectionCache.ContainsKey(kvp.Value.ID) || kvp.Value == null) continue;
                    reconstructedPorts[kvp.Value.ID] = new();

                    foreach (var connectedPortID in portConnectionCache[kvp.Value.ID])
                    {
                        if (reconstructedPorts[kvp.Value.ID].Contains(connectedPortID)) continue;

                        var portToConnect = FindPortByID(connectedPortID);

                        if(kvp.Value != null && portToConnect != null)
                            kvp.Value.Connect(portToConnect, true);

                        reconstructedPorts[kvp.Value.ID].Add(connectedPortID);

                        kvp.Value.Node.IsDirty = true;

                        reconstructed = true;
                    }
                }
            }
        }

        /// <summary>
        /// Searches through all the ports of all the nodes in the graph to find a port with a matching ID.
        /// </summary>
        /// <param name="portID"></param>
        /// <returns></returns>
        public Port FindPortByID(string portID)
        {
            if (portID == null) return null;

            if (runtimePortCache.ContainsKey(portID))
                return runtimePortCache[portID];
            else
            {
                foreach(var node in Nodes)
                {
                    if (node == null) continue;
                    foreach(var kvp in node.Ports)
                    {
                        if (kvp.Value.ID == portID)
                            return kvp.Value;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Swaps a node for another in place: the new one takes its ID, position and links (on pins of the same name),
        /// so everything wired to it stays wired. For upgrading a node to a newer kind.
        /// </summary>
        public void ReplaceNode(Node old, Node replacement)
        {
            int index = nodes.IndexOf(old);
            if (index < 0 || replacement == null)
                return;
            replacement.ID = old.ID;
            replacement.Position = old.Position;
            replacement.Graph = this;
            foreach (var port in replacement.Ports.Values)
            {
                var was = old.GetPort(port.Name);
                if (was != null)
                    port.CopyConnectionsFrom(was);
            }
            try
            {
                old.Disable();
                old.OnRemovedFromGraph();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            nodes[index] = replacement;
            replacement.OnAddedToGraph();
            replacement.Enable();
            // The nodes at the other ends still hold the old node's pins: they look them up again.
            foreach (var port in replacement.Ports.Values)
                foreach (var edge in port.Connections)
                    GetNodeById(edge.NodeID)?.GetPort(edge.PortName)?.ReloadConnections();
            CacheNodesByType();
        }

        /// <summary>
        /// Add a new edge between two Ports.
        /// </summary>
        public void AddEdge(Port output, Port input)
        {
            output.Connect(input);
            output.Node.Validate();
            // The input's node caches where its values come from too, so it needs to hear about the new edge.
            input.Node.Validate();
        }

        /// <summary>
        /// Remove an edge between two Ports. 
        /// </summary>
        public void RemoveEdge(Port output, Port input)
        {
            output.Disconnect(input);
            output.Node.Validate();
            input.Node.Validate();
        }
    }
}
