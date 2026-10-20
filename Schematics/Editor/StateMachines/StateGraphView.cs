using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// The State Graph: one level of the state hierarchy at a time, states as nodes and transitions as arrows. Drag from
/// a state's right edge to another to add a transition; delete an arrow to remove it. Double-click a state to go
/// inside it (its child states); the breadcrumb at the top goes back up.
/// </summary>
public class StateGraphView : GraphView
{
    private readonly StateMachineWindow _window;
    private bool _rebuilding;

    public StateGraphView(StateMachineWindow window)
    {
        _window = window;
        SetupZoom(0.25f, 2f);
        this.AddManipulator(new ContentDragger());
        this.AddManipulator(new SelectionDragger());
        this.AddManipulator(new RectangleSelector());

        var grid = new GridBackground();
        Insert(0, grid);
        grid.StretchToParentSize();

        graphViewChanged = OnGraphViewChanged;
        Add(new IMGUIContainer(DrawBreadcrumb) { style = { position = Position.Absolute, left = 4, top = 4 } });

        Rebuild();
    }

    private StateMachineDefinition Definition => _window.Definition;

    public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
    {
        var position = contentViewContainer.WorldToLocal(evt.mousePosition);
        evt.menu.AppendAction("Add State", _ => _window.AddState(_window.Scope, position));
        if (!string.IsNullOrEmpty(_window.Scope))
            evt.menu.AppendAction("Go Up", _ => _window.SetScope(Definition.Find(_window.Scope)?.Parent));
        evt.menu.AppendSeparator();
        base.BuildContextualMenu(evt);
    }

    public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        => ports.ToList().Where(p => p.direction != startPort.direction && p.node != startPort.node).ToList();

    private void DrawBreadcrumb()
    {
        if (Definition == null)
            return;

        var chain = new List<StateDefinition>();
        for (var state = Definition.Find(_window.Scope); state != null && chain.Count < 64; state = Definition.Find(state.Parent))
            chain.Insert(0, state);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Top", EditorStyles.miniButtonLeft))
                _window.SetScope("");
            foreach (var state in chain)
                if (GUILayout.Button("▸ " + state.Name, EditorStyles.miniButtonMid))
                    _window.SetScope(state.Id);
        }
    }

    public void Rebuild()
    {
        if (Definition == null)
            return;
        _rebuilding = true;
        try
        {
            DeleteElements(graphElements.ToList().Where(e => e is StateNode || e is Edge));

            var nodes = new Dictionary<string, StateNode>();
            string scope = _window.Scope ?? "";
            int placed = 0;
            foreach (var state in Definition.States)
            {
                string parent = Definition.Find(state.Parent) != null ? state.Parent : "";
                if (parent != scope)
                    continue;
                if (state.Position == Vector2.zero)
                    state.Position = new Vector2(80 + 240 * (placed % 4), 80 + 140 * (placed / 4));
                placed++;

                var node = new StateNode(this, Definition, state);
                AddElement(node);
                nodes[state.Id] = node;
            }

            foreach (var node in nodes.Values)
                foreach (var transition in node.State.Transitions)
                {
                    // Arrows go to states on this level; a target inside a sibling is drawn to that sibling.
                    var targetNode = NodeOnThisLevel(nodes, transition.Target);
                    if (targetNode == null)
                        continue;
                    var edge = node.Output.ConnectTo(targetNode.Input);
                    edge.userData = transition;
                    edge.tooltip = node.Describe(transition);
                    AddElement(edge);
                }

            SyncSelection();
        }
        finally
        {
            _rebuilding = false;
        }
    }

    private StateNode NodeOnThisLevel(Dictionary<string, StateNode> nodes, string stateId)
    {
        for (var state = Definition.Find(stateId); state != null; state = Definition.Find(state.Parent))
            if (nodes.TryGetValue(state.Id, out var node))
                return node;
        return null;
    }

    public void SyncSelection()
    {
        ClearSelection();
        foreach (var node in nodes.ToList().OfType<StateNode>())
            if (node.State.Id == _window.Selected)
                AddToSelection(node);
    }

    internal void Selected(StateNode node) => _window.Select(node.State.Id);

    internal void Enter(StateNode node) => _window.SetScope(node.State.Id);

    private GraphViewChange OnGraphViewChanged(GraphViewChange change)
    {
        if (_rebuilding || Definition == null)
            return change;

        bool changed = false;
        if (change.edgesToCreate != null)
        {
            Undo.RecordObject(Definition, "Add Transition");
            foreach (var edge in change.edgesToCreate)
                if (edge.output.node is StateNode from && edge.input.node is StateNode to)
                {
                    var transition = new StateTransition { Target = to.State.Id };
                    from.State.Transitions.Add(transition);
                    edge.userData = transition;
                    _window.Select(from.State.Id);
                    changed = true;
                }
        }

        if (change.elementsToRemove != null)
        {
            Undo.RecordObject(Definition, "Remove From State Graph");
            foreach (var element in change.elementsToRemove)
            {
                if (element is Edge edge && edge.userData is StateTransition transition && edge.output.node is StateNode from)
                {
                    from.State.Transitions.Remove(transition);
                    changed = true;
                }
                else if (element is StateNode node)
                {
                    // Deleting asks first; the view is rebuilt either way.
                    EditorApplication.delayCall += () => _window.Delete(node.State);
                }
            }
        }

        if (change.movedElements != null)
        {
            Undo.RecordObject(Definition, "Move State");
            foreach (var element in change.movedElements)
                if (element is StateNode node)
                {
                    node.State.Position = node.GetPosition().position;
                    changed = true;
                }
        }

        if (changed)
        {
            Definition.MarkChanged();
            EditorUtility.SetDirty(Definition);
            EditorApplication.delayCall += () => _window.Refresh();
        }
        return change;
    }
}

/// <summary>A state in the State Graph.</summary>
public class StateNode : Node
{
    public StateDefinition State { get; }
    public Port Input { get; }
    public Port Output { get; }
    private readonly StateGraphView _view;
    private readonly StateMachineDefinition _definition;

    public StateNode(StateGraphView view, StateMachineDefinition definition, StateDefinition state)
    {
        _view = view;
        _definition = definition;
        State = state;
        title = state.Name;
        SetPosition(new Rect(state.Position, new Vector2(200, 100)));

        titleContainer.style.backgroundColor = state.Color * new Color(1, 1, 1, 0.9f);

        Input = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
        Input.portName = "";
        inputContainer.Add(Input);
        Output = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
        Output.portName = "";
        outputContainer.Add(Output);

        int index = definition.IndexOf(state.Id);
        int parent = definition.ParentOf(index);
        bool isStart = parent < 0 ? definition.InitialIndex == index : definition.DefaultChildOf(parent) == index;
        if (isStart)
            title = "▶ " + title;

        int children = definition.ChildrenOf(index).Length;
        if (children > 0)
            extensionContainer.Add(new Label($"▸ {children} child state{(children == 1 ? "" : "s")} (double-click)") { style = { color = new Color(0.8f, 0.8f, 0.8f), marginLeft = 6 } });

        int toggles = state.Behaviors.Count(b => b.Mode != ToggleMode.Inherit);
        if (toggles > 0)
            extensionContainer.Add(new Label(string.Join(", ", state.Behaviors.Where(b => b.Mode != ToggleMode.Inherit)
                .Select(b => (b.Mode == ToggleMode.Enable ? "+" : "−") + b.Behavior.Type?.Name))) { style = { fontSize = 10, marginLeft = 6, whiteSpace = WhiteSpace.Normal } });

        foreach (var transition in state.Transitions)
            if (definition.Find(transition.Target) == null || !IsOnLevel(definition, transition.Target, state.Parent))
                extensionContainer.Add(new Label("→ " + (definition.Find(transition.Target)?.Name ?? "(none)") + " (other level)") { style = { fontSize = 10, marginLeft = 6 } });

        if (state.Graph != null)
        {
            var open = new Button(() => AssetDatabase.OpenAsset(state.Graph)) { text = "Open Graph" };
            extensionContainer.Add(open);
        }

        RegisterCallback<MouseDownEvent>(evt =>
        {
            if (evt.clickCount == 2 && evt.button == 0)
            {
                _view.Enter(this);
                evt.StopPropagation();
            }
        });

        RefreshExpandedState();
        RefreshPorts();
    }

    private static bool IsOnLevel(StateMachineDefinition definition, string stateId, string parent)
    {
        for (var state = definition.Find(stateId); state != null; state = definition.Find(state.Parent))
            if ((state.Parent ?? "") == (parent ?? ""))
                return true;
        return false;
    }

    public string Describe(StateTransition transition)
        => transition.Trigger == TransitionTrigger.Signal
            ? "On " + (transition.Signal != null ? transition.Signal.name : "?")
            : transition.Conditions.Count == 0 ? (transition.MinTimeInState > 0 ? $"after {transition.MinTimeInState}s" : "no conditions yet")
            : $"{transition.Conditions.Count} condition(s)";

    public override void OnSelected()
    {
        base.OnSelected();
        _view.Selected(this);
    }
}
