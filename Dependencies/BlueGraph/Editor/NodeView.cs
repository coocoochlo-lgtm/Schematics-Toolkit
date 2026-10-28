using System.Reflection;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;

using UnityEditor.Experimental.GraphView;
using GraphViewNode = UnityEditor.Experimental.GraphView.Node;
using static UnityEngine.UIElements.DropdownMenuAction;

namespace BlueGraph.Editor
{
    public class NodeView : GraphViewNode, ICanDirty
    {
        public Node Target { get; private set; }

        public List<PortView> Inputs { get; set; } = new List<PortView>();

        public List<PortView> Outputs { get; set; } = new List<PortView>();

        protected EdgeConnectorListener ConnectorListener { get; set; }

        protected SerializedProperty SerializedNode { get; set; }

        protected NodeReflectionData ReflectionData { get; set; }

        protected CanvasView Canvas { get; set; }

        private Label errorMessage;

        internal void Initialize(Node node, CanvasView canvas, EdgeConnectorListener connectorListener)
        {
            viewDataKey = node.ID;
            Target = node;
            Canvas = canvas;
            ReflectionData = NodeReflection.GetNodeType(node.GetType());
            ConnectorListener = connectorListener;

            styleSheets.Add(Resources.Load<StyleSheet>("BlueGraphEditor/NodeView"));
            AddToClassList("nodeView");

            // Add a class name matching the node's name (e.g. `.node-My-Branch`)
            var ussSafeName = Regex.Replace(Target.Name, @"[^a-zA-Z0-9]+", "-").Trim('-');
            AddToClassList($"node-{ussSafeName}");

            // Also a class for the node's type (e.g. `.node-On-Signal-Received`), so styles keep applying to nodes
            // whose title was changed (like "On JumpInput").
            if (ReflectionData != null && !string.IsNullOrEmpty(ReflectionData.Name))
            {
                var typeClass = Regex.Replace(ReflectionData.Name, @"[^a-zA-Z0-9]+", "-").Trim('-');
                if (typeClass != ussSafeName)
                    AddToClassList($"node-{typeClass}");
            }

            var errorContainer = new VisualElement { name = "error" };
            errorContainer.Add(new VisualElement { name = "error-icon" });

            errorMessage = new Label { name = "error-label" };
            errorContainer.Add(errorMessage);

            if (errorContainer != null)
            { 
                errorContainer.RegisterCallback<MouseEnterEvent>( (MouseEnterEvent evt) => errorMessage.name = "error-label-hover");
                errorContainer.RegisterCallback<MouseLeaveEvent>( (MouseLeaveEvent evt) => errorMessage.name = "error-label");
            }

            Insert(0, errorContainer);

            // A warning: a yellow wavy outline round the node, and a badge whose tooltip says what's wrong.
            warningOutline = new VisualElement { name = "warning", pickingMode = PickingMode.Ignore };
            warningOutline.style.position = Position.Absolute;
            warningOutline.style.left = warningOutline.style.top = warningOutline.style.right = warningOutline.style.bottom = -6;
            warningOutline.style.display = DisplayStyle.None;
            warningOutline.generateVisualContent += DrawWarningOutline;
            Insert(0, warningOutline);
            warningBadge = new Label("!") { name = "warning-badge" };
            warningBadge.style.position = Position.Absolute;
            warningBadge.style.left = -14;
            warningBadge.style.top = -14;
            warningBadge.style.width = 20;
            warningBadge.style.height = 20;
            warningBadge.style.borderTopLeftRadius = warningBadge.style.borderTopRightRadius =
                warningBadge.style.borderBottomLeftRadius = warningBadge.style.borderBottomRightRadius = 10;
            warningBadge.style.backgroundColor = new Color(0.16f, 0.14f, 0.05f);
            warningBadge.style.borderLeftWidth = warningBadge.style.borderRightWidth =
                warningBadge.style.borderTopWidth = warningBadge.style.borderBottomWidth = 2;
            warningBadge.style.borderLeftColor = warningBadge.style.borderRightColor =
                warningBadge.style.borderTopColor = warningBadge.style.borderBottomColor = WarningColor;
            warningBadge.style.color = WarningColor;
            warningBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            warningBadge.style.unityTextAlign = TextAnchor.MiddleCenter;
            warningBadge.style.fontSize = 13;
            warningBadge.style.paddingLeft = warningBadge.style.paddingRight = warningBadge.style.paddingTop = warningBadge.style.paddingBottom = 0;
            warningBadge.style.display = DisplayStyle.None;
            Add(warningBadge);

            SetPosition(new Rect(node.Position, Vector2.one));
            title = node.DisplayName;

            // Double-click the title to give the node a name of its own.
            var titleLabel = titleContainer.Q<Label>("title-label");
            titleLabel?.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount == 2)
                {
                    BeginRename();
                    evt.StopImmediatePropagation();
                }
            });

            // [NodeColor] colors the title bar by kind of node.
            var nodeColor = node.GetType().GetCustomAttribute<NodeColorAttribute>(true);
            if (nodeColor != null)
                titleContainer.style.backgroundColor = nodeColor.Color;

            if (!ReflectionData.Deletable)
            {
                capabilities &= ~Capabilities.Deletable;
            }
            if (!ReflectionData.Moveable)
            {
                capabilities &= ~Capabilities.Movable;
            }

            // Custom OnDestroy() handler via https://forum.unity.com/threads/request-for-visualelement-ondestroy-or-onremoved-event.718814/
            RegisterCallback<DetachFromPanelEvent>((e) => Destroy());
            RegisterCallback<TooltipEvent>(OnTooltip);

            node.OnErrorEvent += RefreshErrorState;
            node.OnWarningEvent += RefreshWarningState;
            node.OnValidateEvent += OnValidate;

            ReloadPorts();
            ReloadEditables();
            RefreshErrorState();
            RefreshWarningState();

            OnInitialize();
            Target.Graph.OnNodeViewCreated?.Invoke(this);
            Target.Graph.OnNodeCreated?.Invoke(Target);
        }


        /// <summary>
        /// Executed after receiving a node target and initial configuration
        /// but before being added to the graph.
        /// </summary>
        protected virtual void OnInitialize() { }

        internal void Destroy()
        {
            OnDestroy();
            Target.OnErrorEvent -= RefreshErrorState;
            Target.OnWarningEvent -= RefreshWarningState;
            Target.OnValidateEvent -= OnValidate;
        }

        /// <summary>
        /// Executed when we're about to detach this element from the graph.
        /// </summary>
        protected virtual void OnDestroy() { }

        protected void RefreshErrorState()
        {
            if (string.IsNullOrEmpty(Target.Error))
            {
                RemoveFromClassList("hasError");
                errorMessage.text = "";
            }
            else
            {
                AddToClassList("hasError");
                errorMessage.text = Target.Error;
            }

            OnError();
        }

        private static readonly Color WarningColor = new Color(0.98f, 0.78f, 0.18f);
        private VisualElement warningOutline;
        private Label warningBadge;

        protected void RefreshWarningState()
        {
            bool has = !string.IsNullOrEmpty(Target.Warning);
            EnableInClassList("hasWarning", has);
            warningOutline.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            warningBadge.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
            warningBadge.tooltip = has ? "Warning: " + Target.Warning : null;
            warningOutline.MarkDirtyRepaint();
        }

        // A wavy line round the node: small waves along each side, like a spell checker's underline.
        private void DrawWarningOutline(MeshGenerationContext context)
        {
            var rect = warningOutline.contentRect;
            if (rect.width < 4 || rect.height < 4)
                return;
            const float inset = 2f, amplitude = 1.6f, wavelength = 9f;
            var painter = context.painter2D;
            painter.strokeColor = WarningColor;
            painter.lineWidth = 1.6f;
            painter.lineJoin = LineJoin.Round;
            painter.BeginPath();
            var corners = new[]
            {
                new Vector2(rect.xMin + inset, rect.yMin + inset), new Vector2(rect.xMax - inset, rect.yMin + inset),
                new Vector2(rect.xMax - inset, rect.yMax - inset), new Vector2(rect.xMin + inset, rect.yMax - inset),
            };
            painter.MoveTo(corners[0]);
            for (int side = 0; side < 4; side++)
            {
                var from = corners[side];
                var to = corners[(side + 1) % 4];
                var along = to - from;
                float length = along.magnitude;
                var normal = new Vector2(-along.y, along.x) / Mathf.Max(0.001f, length);
                int steps = Mathf.Max(2, Mathf.CeilToInt(length / 2f));
                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    float wave = Mathf.Sin(t * length / wavelength * Mathf.PI * 2f) * amplitude;
                    painter.LineTo(from + along * t + normal * wave);
                }
            }
            painter.ClosePath();
            painter.Stroke();
        }

        /// <summary>
        /// Called after the target node's <c>OnError</c> property is executed.
        /// </summary>
        protected virtual void OnError() { }

        /// <summary>
        /// Called after the target node's <c>OnValidate</c> is executed.
        /// </summary>
        protected virtual void OnValidate() { }

        /// <summary>
        /// Make sure our list of PortViews and editable controls sync up with our NodePorts
        /// </summary>
        protected void ReloadPorts()
        {
            foreach (var port in Target.Ports.Values)
            {
                if (port.Direction == PortDirection.Input)
                {
                    AddInputPort(port);
                }
                else
                {
                    AddOutputPort(port);
                }
            }

            // Update state classes
            EnableInClassList("hasInputs", Inputs.Count > 0);
            EnableInClassList("hasOutputs", Outputs.Count > 0);
        }

        protected void ReloadEditables()
        {
            var reflectionData = NodeReflection.GetNodeType(Target.GetType());
            if (reflectionData != null)
            {
                foreach (var editable in reflectionData.Editables)
                {
                    AddEditableField(editable);
                }
            }

            RefreshExpandedState();
        }

        protected void AddEditableField(EditableReflectionData editable)
        {
            var field = editable.GetControlElement(this);
            string help = null;
            try { help = SettingHelp?.Invoke(Target, editable.Field) ?? editable.Field?.GetCustomAttribute<TooltipAttribute>(true)?.tooltip; }
            catch (System.Exception e) { Debug.LogException(e); }
            if (!string.IsNullOrEmpty(help) && field != null)
                field.tooltip = help;
            extensionContainer.Add(field);
        }

        protected virtual void AddInputPort(Port port)
        {
            var view = PortView.Create(port, ConnectorListener);

            // If we're exposing a control element via reflection: include it in the view
            var reflection = NodeReflection.GetNodeType(Target.GetType());
            var element = reflection.GetPortByName(port.Name)?.GetControlElement(this);

            if (element != null)
            {
                var container = new VisualElement();
                container.AddToClassList("property-field-container");
                container.Add(element);

                view.SetEditorField(container);
            }

            view.tooltip = HelpFor(port) ?? view.tooltip;
            Inputs.Add(view);
            inputContainer.Add(view);
        }

        protected virtual void AddOutputPort(Port port)
        {
            var view = PortView.Create(port, ConnectorListener);
            view.tooltip = HelpFor(port) ?? view.tooltip;

            Outputs.Add(view);
            outputContainer.Add(view);
        }

        public PortView GetInputPort(string name)
        {
            return Inputs.Find((port) => port.portName == name);
        }

        public PortView GetOutputPort(string name)
        {
            return Outputs.Find((port) => port.portName == name);
        }

        public PortView GetCompatibleInputPort(PortView output)
        {
            return Inputs.Find((port) => port.IsCompatibleWith(output));
        }

        public PortView GetCompatibleOutputPort(PortView input)
        {
            return Outputs.Find((port) => port.IsCompatibleWith(input));
        }

        /// <summary>
        /// A property has been updated, either by a port or a connection
        /// </summary>
        public virtual void OnPropertyChange()
        {
            Target.Validate();
            Canvas?.Dirty(this);
        }

        public void Dirty()
        {
            OnDirty();

            // Dirty all ports so they can refresh their state
            Inputs.ForEach(port => port.OnDirty());
            Outputs.ForEach(port => port.OnDirty());
        }

        public void Update()
        {
            OnUpdate();

            // Propagate update to all ports
            Inputs.ForEach(port => port.OnUpdate());
            Outputs.ForEach(port => port.OnUpdate());
        }

        /// <summary>
        /// Dirty this node in response to a change in connectivity or internal state.
        /// Invalidate any cache in prep for an OnUpdate() call.
        /// </summary>
        public virtual void OnDirty() { }

        /// <summary>
        /// Called when this node was dirtied and the UI is redrawing.
        /// </summary>
        public virtual void OnUpdate() { }

        public override Rect GetPosition()
        {
            // The default implementation doesn't give us back a valid position until layout is resolved.
            // See: https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/GraphViewEditor/Elements/Node.cs#L131
            Rect position = base.GetPosition();
            if (position.width > 0 && position.height > 0)
            {
                return position;
            }

            return new Rect(Target.Position, Vector2.one);
        }

        public override void SetPosition(Rect newPos)
        {
            // (Dragged: the comments it's in grow as it's pushed against their edges.)
            var from = selected ? GetPosition() : default;
            base.SetPosition(newPos);
            Target.Position = newPos.position;
            if (selected)
                GetFirstAncestorOfType<CanvasView>()?.NodeDragged(this, from, GetPosition());
        }

        private TextField renameField;
        private bool renameCancelled;

        /// <summary>
        /// Edits the node's custom name in place on its title (Enter or clicking away keeps it, Escape doesn't; an
        /// empty name goes back to the node's own).
        /// </summary>
        public void BeginRename()
        {
            var titleLabel = titleContainer.Q<Label>("title-label");
            if (titleLabel == null)
                return;
            if (renameField == null)
            {
                renameField = new TextField { name = "rename-field" };
                renameField.style.flexGrow = 1f;
                renameField.style.minWidth = 80f;
                renameField.RegisterCallback<KeyDownEvent>(evt =>
                {
                    if (evt.keyCode == KeyCode.Escape)
                    {
                        renameCancelled = true;
                        renameField.Blur();
                        evt.StopImmediatePropagation();
                    }
                    else if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        renameField.Blur();
                        evt.StopImmediatePropagation();
                    }
                }, TrickleDown.TrickleDown);
                renameField.RegisterCallback<FocusOutEvent>(evt => EndRename());
                titleContainer.Insert(titleContainer.IndexOf(titleLabel) + 1, renameField);
            }
            renameCancelled = false;
            renameField.SetValueWithoutNotify(Target.CustomName ?? "");
            renameField.style.display = DisplayStyle.Flex;
            titleLabel.style.display = DisplayStyle.None;
            renameField.schedule.Execute(() =>
            {
                var input = renameField.Q(TextField.textInputUssName);
                input?.Focus();
                renameField.SelectAll();
            });
        }

        private void EndRename()
        {
            if (renameField == null || renameField.style.display == DisplayStyle.None)
                return;
            renameField.style.display = DisplayStyle.None;
            var titleLabel = titleContainer.Q<Label>("title-label");
            if (titleLabel != null)
                titleLabel.style.display = DisplayStyle.Flex;
            if (!renameCancelled)
                SetCustomName(renameField.value);
            renameCancelled = false;
        }

        /// <summary>Gives the node a custom name (empty for none), with undo.</summary>
        public void SetCustomName(string customName)
        {
            customName = string.IsNullOrWhiteSpace(customName) ? "" : customName.Trim();
            if ((Target.CustomName ?? "") == customName)
                return;
            if (Target.Graph != null)
                Undo.RegisterCompleteObjectUndo(Target.Graph, "Rename Node");
            Target.CustomName = customName;
            title = Target.DisplayName;
            if (Target.Graph != null)
                EditorUtility.SetDirty(Target.Graph);
        }

        /// <summary>
        /// More items for a node's right-click menu: editor tools add theirs here (Collapse to Function, for one, acting
        /// on the selection).
        /// </summary>
        public static event System.Action<NodeView, ContextualMenuPopulateEvent> ExtendContextMenu;

        /// <summary>
        /// Where hover help comes from (editor tools plug in their docs): a node's (on its title), a pin's, and a
        /// setting's on the node. Null, or giving null: the [Tooltip] on the field, if any.
        /// </summary>
        public static System.Func<Node, string> NodeHelp;
        public static System.Func<Node, Port, string> PortHelp;
        public static System.Func<Node, FieldInfo, string> SettingHelp;

        /// <summary>A pin's hover help (see <see cref="PortHelp"/>).</summary>
        public string HelpFor(Port port)
        {
            string text = null;
            try { text = PortHelp?.Invoke(Target, port); }
            catch (System.Exception e) { Debug.LogException(e); }
            return text;
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            if (ExtendContextMenu != null)
            {
                foreach (System.Action<NodeView, ContextualMenuPopulateEvent> extend in ExtendContextMenu.GetInvocationList())
                {
                    try { extend(this, evt); }
                    catch (System.Exception e) { Debug.LogException(e); }
                }
                evt.menu.AppendSeparator();
            }
            evt.menu.AppendAction("Rename", (e) => BeginRename());
            evt.menu.AppendAction("Clear Custom Name", (e) => SetCustomName(""),
                string.IsNullOrEmpty(Target.CustomName) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

            //Add Edit Node Script and Edit Node View Script to context menu of the Node
            evt.menu.AppendSeparator("Edit");
            evt.menu.AppendAction("Edit/Node Script", (e) => EditNodeScript(), GetNodeScriptStatus);
            evt.menu.AppendAction("Edit/Node View Script", (e) => EditNodeViewScript(), GetNodeViewScriptStatus);

            //Add ContextMethods by Attributes from node
            var contextMethods = ReflectionData.ContextMethods;

            foreach (var attr in contextMethods.Keys)
            {
                string title = string.IsNullOrEmpty(attr.menuItem) ? contextMethods[attr].Name : attr.menuItem;
                var info = contextMethods[attr];

                evt.menu.AppendAction(title, (e) => OnContextMenuSelected(info));
            }
        }

        /// <summary>
        /// Open NodeScript at Script Editor
        /// </summary>
        public void EditNodeScript()
        {
            var script = ReflectionData.NodeScript;

            if (script != null)
                AssetDatabase.OpenAsset(script);
        }

        /// <summary>
        /// Open NodeViewScript at Script Editor
        /// </summary>
        public void EditNodeViewScript()
        {
            var script = ReflectionData.NodeViewScript;

            if (script != null)
                AssetDatabase.OpenAsset(script);
        }

        private Status GetNodeScriptStatus(DropdownMenuAction action)
        {
            if (ReflectionData.NodeScript != null)
                return Status.Normal;
            return Status.Disabled;
        }

        /// <summary>
        /// Utility method for <see cref="BuildContextualMenu(ContextualMenuPopulateEvent)"/> to toggle the context menu item
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        private Status GetNodeViewScriptStatus(DropdownMenuAction action)
        {
            if (ReflectionData.NodeViewScript)
                return Status.Normal;
            return Status.Disabled;
        }

        /// <summary>
        /// Event triggered when a contexxt method is clicked
        /// </summary>
        /// <param name="info"></param>
        private void OnContextMenuSelected(System.Reflection.MethodInfo info)
        {
            info.Invoke(Target, null);
        }

        protected void OnTooltip(TooltipEvent evt)
        {
            // TODO: Better implementation that can be styled
            if (evt.target == titleContainer.Q("title-label"))
            {
                var typeData = NodeReflection.GetNodeType(Target.GetType());
                string help = null;
                try { help = NodeHelp?.Invoke(Target); }
                catch (System.Exception e) { Debug.LogException(e); }
                evt.tooltip = string.IsNullOrEmpty(help) ? typeData?.Help : help;

                // Float the tooltip above the node title bar
                var bound = titleContainer.worldBound;
                bound.x = 0;
                bound.y = 0;
                bound.height *= -1;

                evt.rect = titleContainer.LocalToWorld(bound);
            }
        }
    }
}
