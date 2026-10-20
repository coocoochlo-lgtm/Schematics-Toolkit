using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using UnityEditor.Experimental.GraphView;
using GraphViewPort = UnityEditor.Experimental.GraphView.Port;
using GraphViewEdge = UnityEditor.Experimental.GraphView.Edge;
using GraphViewSearchWindow = UnityEditor.Experimental.GraphView.SearchWindow;

namespace BlueGraph.Editor
{
    /// <summary>
    /// Graph view that contains the nodes, edges, etc. 
    /// </summary>
    public class CanvasView : GraphView
    {
        public GraphEditorWindow EditorWindow { get; private set; }

        public Graph Graph { get; private set; }

        private Label title;
        private readonly List<CommentView> commentViews = new List<CommentView>();
        private readonly SearchWindow searchWindow;
        private readonly EdgeConnectorListener edgeConnectorListener;
        private readonly HashSet<ICanDirty> dirtyElements = new HashSet<ICanDirty>();

        private SerializedObject serializedGraph;

        private Vector2 lastMousePosition;

        private readonly CommentKeyTool commentKeyTool;

        /// <summary>True while C is held over the canvas (comments are only made then).</summary>
        public bool IsCommentKeyHeld => commentKeyTool != null && commentKeyTool.IsHeld;

        /// <summary>
        /// Forgets a held C key: the window calls this when it loses focus, since the key-up then goes elsewhere.
        /// </summary>
        public void ReleaseCommentKey() => commentKeyTool?.Release();

        public CanvasView(GraphEditorWindow window)
        {
            EditorWindow = window;
            name = "bluegraph-canvas";

            styleSheets.Add(Resources.Load<StyleSheet>("BlueGraphEditor/Variables"));
            styleSheets.Add(Resources.Load<StyleSheet>("BlueGraphEditor/CanvasView"));
            AddToClassList("canvasView");

            edgeConnectorListener = new EdgeConnectorListener(this);
            searchWindow = ScriptableObject.CreateInstance<SearchWindow>();
            searchWindow.Target = this;

            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            // Comments are made only while C is held (C+drag draws one, C+click wraps the selection).
            commentKeyTool = new CommentKeyTool(this);
            this.AddManipulator(commentKeyTool);
            this.AddManipulator(new ClickSelector());

            // Add event handlers for shortcuts and changes
            RegisterCallback<KeyUpEvent>(OnGraphKeyUp);
            RegisterCallback<MouseMoveEvent>(OnGraphMouseMove);

            graphViewChanged = OnGraphViewChanged;

            RegisterCallback<AttachToPanelEvent>(c => { Undo.undoRedoPerformed += OnUndoRedo; });
            RegisterCallback<DetachFromPanelEvent>(c => { Undo.undoRedoPerformed -= OnUndoRedo; });

            nodeCreationRequest = (ctx) => OpenSearch(ctx.screenMousePosition);

            // Add handlers for (de)serialization
            serializeGraphElements = OnSerializeGraphElements;
            canPasteSerializedData = OnTryPasteSerializedData;
            unserializeAndPaste = OnUnserializeAndPaste;

            RegisterCallback<GeometryChangedEvent>(OnFirstResize);

            title = new Label("BlueGraph");
            title.AddToClassList("canvasViewTitle");
            Add(title);

            // Add a grid renderer *behind* content containers
            Insert(0, new GridBackground());
        }

        private void OnUndoRedo()
        {
            Reload();
        }

        private void OnGraphMouseMove(MouseMoveEvent evt)
        {
            lastMousePosition = evt.mousePosition;
        }

        /// <summary>
        /// Event handler to frame the graph view on initial layout: where this graph's view was left, else its nodes
        /// at a readable size (Frame All zoomed out until everything fit, which left spread-out graphs tiny).
        /// </summary>
        private void OnFirstResize(GeometryChangedEvent evt)
        {
            UnregisterCallback<GeometryChangedEvent>(OnFirstResize);
            if (!RestoreView())
                FrameNodesReadable();
            viewTransformChanged += _ => SaveView();
        }

        private const float ReadableScale = 1f, SmallestStartScale = 0.75f, TooSmallScale = 0.6f;
        private static readonly Vector2 AssumedNodeSize = new Vector2(220f, 120f);

        // A node's rectangle in the graph (its laid-out size, or a typical size before it's laid out).
        private static Rect RectOf(GraphElement element)
        {
            var rect = element.GetPosition();
            if (rect.width <= 1f || rect.height <= 1f)
                rect.size = AssumedNodeSize;
            return rect;
        }

        /// <summary>
        /// Centres the view on the graph's nodes at a readable size: between 75% and 100%, zoomed out only as far as
        /// it takes to fit them (a big graph stays at 75%, centred on its nodes).
        /// </summary>
        public void FrameNodesReadable()
        {
            var size = layout.size;
            if (size.x <= 0f || size.y <= 0f)
                return;
            var rects = nodes.ToList().Select(RectOf).ToList();
            if (rects.Count == 0)
            {
                UpdateViewTransform(new Vector3(size.x * 0.5f, size.y * 0.5f, 0f), Vector3.one);
                return;
            }
            var bounds = rects[0];
            foreach (var rect in rects)
                bounds = Rect.MinMaxRect(Mathf.Min(bounds.xMin, rect.xMin), Mathf.Min(bounds.yMin, rect.yMin),
                    Mathf.Max(bounds.xMax, rect.xMax), Mathf.Max(bounds.yMax, rect.yMax));
            float fit = Mathf.Min((size.x - 80f) / Mathf.Max(1f, bounds.width), (size.y - 80f) / Mathf.Max(1f, bounds.height));
            float scale = Mathf.Clamp(Mathf.Clamp(fit, SmallestStartScale, ReadableScale), minScale, maxScale);
            var position = size * 0.5f - bounds.center * scale;
            UpdateViewTransform(new Vector3(position.x, position.y, 0f), new Vector3(scale, scale, 1f));
        }

        /// <summary>
        /// Brings a node into view: pans to it when it's off screen, and zooms back to a readable size when the view is
        /// zoomed far out. A node already on screen at a readable size leaves the view alone.
        /// </summary>
        public void RevealNode(GraphElement element)
        {
            if (element == null)
                return;
            schedule.Execute(() =>
            {
                if (element.panel == null)
                    return;
                var size = layout.size;
                if (size.x <= 0f || size.y <= 0f)
                    return;
                var rect = RectOf(element);
                float scale = viewTransform.scale.x;
                bool tooSmall = scale < TooSmallScale;
                if (tooSmall)
                    scale = Mathf.Clamp(ReadableScale, minScale, maxScale);
                Vector2 position = viewTransform.position;
                Vector2 min = position + rect.min * scale, max = position + rect.max * scale;
                bool onScreen = min.x >= 0f && min.y >= 0f && max.x <= size.x && max.y <= size.y;
                if (onScreen && !tooSmall)
                    return;
                var centred = size * 0.5f - rect.center * scale;
                UpdateViewTransform(new Vector3(centred.x, centred.y, 0f), new Vector3(scale, scale, 1f));
            }).ExecuteLater(30);
        }

        // The view (pan and zoom) is remembered per graph, so a graph opens where it was left.
        private string ViewKey
        {
            get
            {
                string path = Graph != null ? AssetDatabase.GetAssetPath(Graph) : null;
                return string.IsNullOrEmpty(path) ? null : "BlueGraph.View." + AssetDatabase.AssetPathToGUID(path);
            }
        }

        private void SaveView()
        {
            string key = ViewKey;
            if (key == null) return;
            var p = viewTransform.position;
            EditorPrefs.SetString(key, string.Join(";",
                p.x.ToString(System.Globalization.CultureInfo.InvariantCulture),
                p.y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                viewTransform.scale.x.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        private bool RestoreView()
        {
            string key = ViewKey;
            if (key == null || !EditorPrefs.HasKey(key)) return false;
            var parts = EditorPrefs.GetString(key, "").Split(';');
            var style = System.Globalization.NumberStyles.Float;
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            if (parts.Length != 3
                || !float.TryParse(parts[0], style, culture, out float x)
                || !float.TryParse(parts[1], style, culture, out float y)
                || !float.TryParse(parts[2], style, culture, out float scale)
                || scale <= 0f)
                return false;
            scale = Mathf.Clamp(scale, minScale, maxScale);
            UpdateViewTransform(new Vector3(x, y, 0f), new Vector3(scale, scale, 1f));
            return true;
        }

        private GraphViewChange OnGraphViewChanged(GraphViewChange change)
        {
            if (serializedGraph == null)
            {
                return change;
            }

            if (change.movedElements != null)
            {
                // Moved nodes will update their underlying models automatically.
                EditorUtility.SetDirty(Graph);
            }

            if (change.movedElements != null || change.elementsToRemove != null)
                ScheduleCommentRefit();

            if (change.elementsToRemove != null)
            {
                foreach (var element in change.elementsToRemove)
                {
                    if (element is NodeView node)
                    {
                        RemoveNode(node);
                    }
                    else if (element is GraphViewEdge edge)
                    {
                        RemoveEdge(edge, true);
                    }
                    else if (element is CommentView comment)
                    {
                        RemoveComment(comment);
                    }

                    if (element is ICanDirty canDirty)
                    {
                        dirtyElements.Remove(canDirty);
                    }
                }
            }

            return change;
        }

        private void OnGraphKeyUp(KeyUpEvent evt)
        {
            if (evt.target != this)
            {
                return;
            }

            // (C isn't a one-press shortcut any more: comments are made while it's held, see CommentKeyTool.)

            // H: Horizontally align selected nodes
            if (evt.keyCode == KeyCode.H && !evt.ctrlKey && !evt.commandKey)
            {
                HorizontallyAlignSelectedNodes();
            }

            if (evt.keyCode == KeyCode.V && !evt.ctrlKey && !evt.commandKey)
            {
                VerticallyAlignSelectedNodes();
            }
        }

        protected void HorizontallyAlignSelectedNodes()
        {
            float sum = 0;
            int count = 0;

            foreach (var selectable in selection)
            {
                if (selectable is NodeView node)
                {
                    sum += node.GetPosition().xMin;
                    count++;
                }
            }

            float xAvg = sum / count;
            foreach (var selectable in selection)
            {
                if (selectable is NodeView node)
                {
                    var pos = node.GetPosition();
                    pos.xMin = xAvg;
                    node.SetPosition(pos);
                }
            }
        }

        protected void VerticallyAlignSelectedNodes()
        {
            float sum = 0;
            int count = 0;

            foreach (var selectable in selection)
            {
                if (selectable is NodeView node)
                {
                    sum += node.GetPosition().yMin;
                    count++;
                }
            }

            float yAvg = sum / count;
            foreach (var selectable in selection)
            {
                if (selectable is NodeView node)
                {
                    var pos = node.GetPosition();
                    pos.yMin = yAvg;
                    node.SetPosition(pos);
                }
            }
        }

        /// <summary>
        /// Add a new provider to populate the search window
        /// </summary>
        public void AddSearchProvider(ISearchProvider provider)
        {
            searchWindow.AddSearchProvider(provider);
        }

        public void Load(Graph graph)
        {
            if (graph == null || !graph)
                return;

            Graph = graph;
            graph.ReconstructPortConnections();

            serializedGraph?.Dispose();
            serializedGraph = new SerializedObject(graph);

            title.text = graph.Title;
            SetupZoom(graph.ZoomMinScale, graph.ZoomMaxScale);

            AddNodeViews(graph.Nodes);
            AddCommentViews(graph.Comments);

            // Reset the search to a new set of tags and providers
            searchWindow.ClearSearchProviders();
            searchWindow.ClearTags();

            foreach (var provider in NodeReflection.SearchProviders)
            {
                if (provider.IsSupported(graph))
                {
                    searchWindow.AddSearchProvider(provider);
                }
            }

            // TODO: Move into reflection
            var attrs = graph.GetType().GetCustomAttributes(true);


            foreach (var attr in attrs)
            {
                //Add Tags for search provider
                if (attr is IncludeTagsAttribute include)
                {
                    searchWindow.IncludeTags.AddRange(include.Tags);
                }

                //Add Required nodes from GraphAttributes
                if (attr is RequireNodeAttribute required)
                {

                    Node node = graph.GetNode(required.type);
                    if (node == null)
                    {
                        node = NodeReflection.Instantiate(required.type);
                        node.Graph = graph;
                        node.Name = required.nodeName;
                        node.Position = required.position;
                        AddNodeFromSearch(node, node.Position, null, false);
                    }

                }
            }
        }

        /// <summary>
        /// Create a new node from reflection data and insert into the Graph.
        /// </summary>
        public void AddNodeFromSearch(Node node, Vector2 screenPosition, PortView connectedPort = null, bool registerUndo = true)
        {
            // Calculate where to place this node on the graph
            var windowRoot = EditorWindow.rootVisualElement;
            var windowMousePosition = EditorWindow.rootVisualElement.ChangeCoordinatesTo(
                windowRoot.parent,
                screenPosition - EditorWindow.position.position
            );

            var graphMousePosition = contentViewContainer.WorldToLocal(windowMousePosition);

            // Track undo and add to the graph
            if (registerUndo)
            {
                Undo.RegisterCompleteObjectUndo(Graph, $"Add Node {node.Name}");
            }

            node.Position = graphMousePosition;

            Graph.AddNode(node);
            serializedGraph.Update();
            EditorUtility.SetDirty(Graph);

            ScheduleCommentRefit();

            // Add a node to the visual graph
            var editorType = NodeReflection.GetNodeEditorType(node.GetType());
            var element = Activator.CreateInstance(editorType) as NodeView;
            element.Initialize(node, this, edgeConnectorListener);

            AddElement(element);

            // If there was a provided existing port to connect to, find the best 
            // candidate port on the new node and connect. 
            if (connectedPort != null)
            {
                var edge = new GraphViewEdge();

                if (connectedPort.direction == Direction.Input)
                {
                    edge.input = connectedPort;
                    edge.output = element.GetCompatibleOutputPort(connectedPort);
                }
                else
                {
                    edge.output = connectedPort;
                    edge.input = element.GetCompatibleInputPort(connectedPort);
                }

                AddEdge(edge, false);
            }

            Dirty(element);

            // A node made by the user (not one a graph requires, added while loading) is brought into view.
            if (registerUndo)
                RevealNode(element);
        }

        /// <summary>
        /// Remove a node from both the canvas view and the graph model
        /// </summary>
        public void RemoveNode(NodeView node)
        {
            Undo.RegisterCompleteObjectUndo(Graph, $"Delete Node {node.name}");

            Graph.RemoveNode(node.Target);
            serializedGraph.Update();
            EditorUtility.SetDirty(Graph);

            RemoveElement(node);
        }

        /// <summary>
        /// Add a new edge to both the canvas view and the underlying graph model
        /// </summary>
        public void AddEdge(GraphViewEdge edge, bool registerAsNewUndo)
        {
            try
            {
                if (edge.input == null || edge.output == null)
                {
                    return;
                }

                if (registerAsNewUndo)
                {
                    Undo.RegisterCompleteObjectUndo(Graph, "Add Edge");
                }

                // Handle single connection ports on either end. 
                var edgesToRemove = new List<GraphViewEdge>();
                if (edge.input.capacity == GraphViewPort.Capacity.Single)
                {
                    foreach (var conn in edge.input.connections)
                    {
                        edgesToRemove.Add(conn);
                    }
                }

                if (edge.output.capacity == GraphViewPort.Capacity.Single)
                {
                    foreach (var conn in edge.output.connections)
                    {
                        edgesToRemove.Add(conn);
                    }
                }

                foreach (var edgeToRemove in edgesToRemove)
                {
                    RemoveEdge(edgeToRemove, false);
                }

                var input = edge.input as PortView;
                var output = edge.output as PortView;

                // Connect the ports in the model
                Graph.AddEdge(input.Target, output.Target);
                serializedGraph.Update();
                EditorUtility.SetDirty(Graph);

                // Add a matching edge view onto the canvas
                var newEdge = input.ConnectTo(output);
                AddElement(newEdge);

                // Dirty the affected node views
                Dirty(input.node as NodeView);
                Dirty(output.node as NodeView);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        /// <summary>
        /// Remove an edge from both the canvas view and the underlying graph model
        /// </summary>
        public void RemoveEdge(GraphViewEdge edge, bool registerAsNewUndo)
        {
            var input = edge.input as PortView;
            var output = edge.output as PortView;

            if (registerAsNewUndo)
            {
                Undo.RegisterCompleteObjectUndo(Graph, "Remove Edge");
            }

            // Disconnect the ports in the model
            Graph.RemoveEdge(input.Target, output.Target);
            serializedGraph.Update();
            EditorUtility.SetDirty(Graph);

            // Remove the edge view
            edge.input.Disconnect(edge);
            edge.output.Disconnect(edge);
            edge.input = null;
            edge.output = null;
            RemoveElement(edge);

            // Dirty the affected node views
            Dirty(input.node as NodeView);
            Dirty(output.node as NodeView);
        }

        /// <summary>
        /// Reload a fresh serialized copy of the graph.
        /// </summary>
        public void Reload()
        {
            serializedGraph = null;

            DeleteElements(graphElements.ToList());
            commentViews.Clear();
            Load(Graph);
        }

        /// <summary>
        /// Mark a node and all dependents as dirty for the next refresh. 
        /// </summary>
        public void Dirty(ICanDirty element)
        {
            dirtyElements.Add(element);

            // TODO: Not the best place for this.
            EditorUtility.SetDirty(Graph);

            element.Dirty();

            // Also dirty outputs if a NodeView
            if (element is NodeView node)
            {
                foreach (var port in node.Outputs)
                {
                    foreach (var conn in port.connections)
                    {
                        if (!dirtyElements.Contains(conn.input.node as NodeView))
                        {
                            Dirty(conn.input.node as NodeView);
                        }

                    }
                }
            }
        }


        /// <summary>
        /// Dirty all nodes on the canvas for a complete refresh
        /// </summary>
        public void DirtyAll()
        {
            graphElements.ForEach((element) =>
            {
                if (element is ICanDirty cd)
                {
                    cd.Dirty();
                    dirtyElements.Add(cd);
                }
            });
        }

        public void Update()
        {
            // Propagate update on dirty elements
            foreach (var element in dirtyElements)
            {
                element.Update();
            }

            dirtyElements.Clear();
        }

        public void OpenSearch(Vector2 screenPosition, PortView connectedPort = null)
        {
            searchWindow.SourcePort = connectedPort;
            GraphViewSearchWindow.Open(new SearchWindowContext(screenPosition), searchWindow);
        }

        /// <summary>
        /// Append views for a set of nodes
        /// </summary>
        private void AddNodeViews(IEnumerable<Node> nodes, bool selectOnceAdded = false, bool centerOnMouse = false)
        {
            // Add views of each node from the graph
            var nodeMap = new Dictionary<Node, NodeView>();

            foreach (var node in nodes)
            {
                if (node == null) continue;
                if (!Graph.Nodes.Contains(node))
                {
                    Debug.LogError("Cannot add NodeView: Node is not indexed on the graph");
                }
                else
                {
                    var editorType = NodeReflection.GetNodeEditorType(node.GetType());
                    var element = Activator.CreateInstance(editorType) as NodeView;

                    element.Initialize(node, this, edgeConnectorListener);
                    AddElement(element);

                    nodeMap.Add(node, element);
                    Dirty(element);

                    if (selectOnceAdded)
                    {
                        AddToSelection(element);
                    }
                }
            }

            if (centerOnMouse)
            {
                var bounds = GetBounds(nodeMap.Values);
                var worldPosition = contentViewContainer.WorldToLocal(lastMousePosition);
                var delta = worldPosition - bounds.center;

                foreach (var node in nodeMap)
                {
                    node.Value.SetPosition(new Rect(node.Key.Position + delta, Vector2.one));
                }
            }

            // Sync edges on the graph with our graph's connections 
            // TODO: Deal with trash connections from bad imports
            // and try to just refactor this whole thing tbh
            foreach (var node in nodeMap)
            {
                foreach (var port in node.Key.Ports.Values)
                {
                    if (port.Direction == PortDirection.Output)
                    {
                        continue;
                    }

                    foreach (var conn in port.ConnectedPorts)
                    {
                        var connectedNode = conn?.Node;
                        if (connectedNode == null)
                        {
/*                            Debug.LogError(
                                 $"Could not connect `{node.Value.title}:{port.Name}`: " +
                                 $"Connected node no longer exists."
                            );*/
                            continue;
                        }

                        // Only add if the linked node is in the collection
                        // TODO: This shouldn't be a problem
                        if (!nodeMap.ContainsKey(connectedNode))
                        {
                            Debug.LogError(
                                 $"Could not connect `{node.Value.title}:{port.Name}` -> `{connectedNode.Name}:{conn.Name}`. " +
                                 $"Target node does not exist in the NodeView map"
                            );
                            continue;
                        }

                        var inPort = node.Value.GetInputPort(port.Name);
                        var outPort = nodeMap[connectedNode].GetOutputPort(conn.Name);

                        if (inPort == null)
                        {
                            Debug.LogError(
                                $"Could not connect `{node.Value.title}:{port.Name}` -> `{connectedNode.Name}:{conn.Name}`. " +
                                $"Input port `{port.Name}` no longer exists."
                            );
                        }
                        else if (outPort == null)
                        {
                            Debug.LogError(
                                $"Could not connect `{connectedNode.Name}:{conn.Name}` to `{node.Value.name}:{port.Name}`. " +
                                $"Output port `{conn.Name}` no longer exists."
                            );
                        }
                        else
                        {
                            var edge = inPort.ConnectTo(outPort);
                            AddElement(edge);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Append views for comments from a Graph
        /// </summary>
        private void AddCommentViews(IEnumerable<Comment> comments)
        {
            foreach (var comment in comments)
            {
                var commentView = new CommentView(comment);
                commentViews.Add(commentView);
                AddElement(commentView);
                Dirty(commentView);
            }
        }

        /// <summary>
        /// Calculate the bounding box for a set of nodes
        /// </summary>
        private Rect GetBounds(IEnumerable<ISelectable> items)
        {
            var contentRect = Rect.zero;

            foreach (var item in items)
            {
                if (item is NodeView ele)
                {
                    var boundingRect = ele.GetPosition();
                    boundingRect.width = Mathf.Max(boundingRect.width, 1);
                    boundingRect.height = Mathf.Max(boundingRect.height, 1);

                    boundingRect = ele.parent.ChangeCoordinatesTo(contentViewContainer, boundingRect);

                    if (contentRect.width < 1 || contentRect.height < 1)
                    {
                        contentRect = boundingRect;
                    }
                    else
                    {
                        contentRect = RectUtils.Encompass(contentRect, boundingRect);
                    }
                }
            }

            return contentRect;
        }

        private bool refitScheduled;

        /// <summary>
        /// Refits every auto-sizing comment around its nodes, once layout has caught up (node sizes are only known then).
        /// </summary>
        public void ScheduleCommentRefit()
        {
            if (refitScheduled)
                return;
            refitScheduled = true;
            schedule.Execute(() =>
            {
                refitScheduled = false;
                bool changed = false;
                foreach (var comment in commentViews)
                    changed |= comment.FitToMembers();
                if (changed && Graph != null)
                    EditorUtility.SetDirty(Graph);
            }).StartingIn(20);
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (evt.target == this)
            {
                // Comments are only made while C is held; say how, where people look for it.
                evt.menu.AppendAction("Add Comment: hold C and drag (or click to wrap the selection)", _ => { }, DropdownMenuAction.Status.Disabled);
                evt.menu.AppendSeparator();
            }
            base.BuildContextualMenu(evt);
        }

        /// <summary>
        /// Add a new comment to the canvas and the associated Graph
        /// 
        /// If there are selected nodes, this'll encapsulate the selection with
        /// the comment box. Otherwise, it'll add at the last mouse position.
        /// </summary>
        public void AddComment()
        {
            AddCommentAt(lastMousePosition);
        }

        /// <summary>
        /// Wraps the selected nodes in a new comment or, with no nodes selected, drops a small comment at a point
        /// (in window coordinates, like a mouse event's position).
        /// </summary>
        public void AddCommentAt(Vector2 worldPosition)
        {
            // Pad out the bounding box a bit more on the selection
            var padding = 30f; // TODO: Remove hardcoding

            var bounds = GetBounds(selection);

            if (bounds.width < 1 || bounds.height < 1)
            {
                Vector2 graphPosition = contentViewContainer.WorldToLocal(worldPosition);
                bounds.x = graphPosition.x;
                bounds.y = graphPosition.y;

                // TODO: For some reason CSS minWidth/minHeight isn't being respected. 
                // Maybe I need to wait for CSS to load before setting bounds?
                bounds.width = 150 - (padding * 2);
                bounds.height = 100 - (padding * 3);
            }

            bounds.x -= padding;
            bounds.y -= padding * 2;
            bounds.width += padding * 2;
            bounds.height += padding * 3;

            CreateComment(bounds, false);
        }

        /// <summary>
        /// Adds a comment covering a region of the graph (in graph coordinates), as drawn with C+drag. Drawn around
        /// nodes, it hugs them as usual; drawn over empty space, it keeps the size it was drawn at.
        /// </summary>
        public void AddComment(Rect graphRegion)
        {
            graphRegion.width = Mathf.Max(graphRegion.width, 150);
            graphRegion.height = Mathf.Max(graphRegion.height, 50);

            bool hasNodes = false;
            foreach (var node in nodes.ToList())
                if (node is NodeView view && graphRegion.Contains(view.GetPosition().center))
                {
                    hasNodes = true;
                    break;
                }

            CreateComment(graphRegion, !hasNodes);
        }

        private void CreateComment(Rect region, bool fixedSize)
        {
            if (Graph == null)
                return;

            Undo.RegisterCompleteObjectUndo(Graph, "Add Comment");

            // Add the model
            var comment = new Comment();
            comment.Text = "New Comment";
            comment.Region = region;
            comment.Color = Comment.DefaultColor;
            comment.FixedSize = fixedSize;

            Graph.Comments.Add(comment);
            serializedGraph?.Update();
            EditorUtility.SetDirty(Graph);

            // Add the view
            var commentView = new CommentView(comment);
            commentViews.Add(commentView);
            AddElement(commentView);

            Dirty(commentView);
            ScheduleCommentRefit();

            // Focus the title editor on first load (once C is up, so its key-repeat doesn't type into it)
            if (commentKeyTool != null)
                commentKeyTool.RunAfterRelease(() => { if (commentView.panel != null) commentView.EditTitle(); });
            else
                commentView.EditTitle();
        }

        /// <summary>
        /// Remove a comment from both the canvas view and the graph model
        /// </summary>
        public void RemoveComment(CommentView comment)
        {
            Undo.RegisterCompleteObjectUndo(Graph, "Delete Comment");

            // Remove the model
            Graph.Comments.Remove(comment.Target);
            serializedGraph.Update();
            EditorUtility.SetDirty(Graph);

            // Remove the view
            RemoveElement(comment);
            commentViews.Remove(comment);
        }

        /// <summary>
        /// Handler to deserialize a string back into a CopyPasteGraph
        /// </summary>
        private void OnUnserializeAndPaste(string operationName, string data)
        {
            Undo.RegisterCompleteObjectUndo(Graph, "Paste Subgraph");

            var cpg = CopyPasteGraph.Deserialize(data, searchWindow.IncludeTags);
            Graph.AddNodes(cpg.Nodes);

            foreach (var comment in cpg.Comments)
            {
                Graph.Comments.Add(comment);
            }

            serializedGraph.Update();
            EditorUtility.SetDirty(Graph);

            // Add views for all the new elements
            ClearSelection();
            AddNodeViews(cpg.Nodes, true, true);
            AddCommentViews(cpg.Comments);

            ScriptableObject.DestroyImmediate(cpg);
        }

        private bool OnTryPasteSerializedData(string data)
        {
            return CopyPasteGraph.CanDeserialize(data);
        }

        /// <summary>
        /// Serialize a selection to support cut/copy/duplicate
        /// </summary>
        private string OnSerializeGraphElements(IEnumerable<GraphElement> elements)
        {
            return CopyPasteGraph.Serialize(elements);
        }

        /// <summary>
        /// Replacement of the base AddElement() to undo the hardcoded border 
        /// style that's overriding USS files. Should probably report this as dumb. 
        /// </summary>
        public new void AddElement(GraphElement graphElement)
        {
            // See: https://github.com/Unity-Technologies/UnityCsReference/blob/02d565cf3dd0f6b15069ba976064c75dc2705b08/Modules/GraphViewEditor/Views/GraphView.cs#L1222

            var borderBottomWidth = graphElement.style.borderBottomWidth;
            base.AddElement(graphElement);

            if (graphElement.IsResizable())
            {
                graphElement.style.borderBottomWidth = borderBottomWidth;
            }
        }

        public override List<GraphViewPort> GetCompatiblePorts(GraphViewPort startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<GraphViewPort>();
            var startPortView = startPort as PortView;

            ports.ForEach((port) =>
            {
                var portView = port as PortView;
                if (portView.IsCompatibleWith(startPortView))
                {
                    compatiblePorts.Add(portView);
                }
            });

            return compatiblePorts;
        }
    }
}
