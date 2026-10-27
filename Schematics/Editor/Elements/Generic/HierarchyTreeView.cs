using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Renders any parent/child model as the IO Dock's nested, expandable hierarchy. Callers provide their own node model,
/// child lookup and header UI, so GameObjects and UI Toolkit elements share the same tree behavior.
/// </summary>
public sealed class HierarchyTreeView<TNode> : VisualElement where TNode : class
{
    private static readonly Dictionary<string, bool> ExpandedByKey = new();

    private readonly string _stateKey;
    private readonly Func<TNode, IEnumerable<TNode>> _children;
    private readonly Func<TNode, string> _nodeKey;
    private readonly Func<TNode, VisualElement> _makeHeader;

    public HierarchyTreeView(IEnumerable<TNode> roots, Func<TNode, IEnumerable<TNode>> children,
        Func<TNode, string> nodeKey, Func<TNode, VisualElement> makeHeader, string stateKey)
    {
        _children = children ?? throw new ArgumentNullException(nameof(children));
        _nodeKey = nodeKey ?? throw new ArgumentNullException(nameof(nodeKey));
        _makeHeader = makeHeader ?? throw new ArgumentNullException(nameof(makeHeader));
        _stateKey = stateKey ?? string.Empty;
        AddToClassList("hierarchy-tree");
        style.flexGrow = 1;
        if (roots != null)
        {
            foreach (var root in roots)
                if (root != null)
                    Add(BuildNode(root, isChild: false));
        }
        ApplyAlternatingShades();
    }

    private VisualElement BuildNode(TNode node, bool isChild)
    {
        var children = (_children(node) ?? Enumerable.Empty<TNode>()).Where(child => child != null).ToList();
        var header = _makeHeader(node) ?? new VisualElement();
        header.AddToClassList("hierarchy-tree-header");
        string key = $"{_stateKey}/{_nodeKey(node)}";
        bool expanded = !ExpandedByKey.TryGetValue(key, out var saved) || saved;
        var nodeContainer = new VisualElement();
        nodeContainer.AddToClassList("hierarchy-tree-node");
        nodeContainer.AddToClassList("dock-object");
        nodeContainer.style.flexDirection = FlexDirection.Column;
        VisualElement guide = null;
        if (children.Count > 0)
        {
            guide = new VisualElement();
            guide.AddToClassList("hierarchy-tree-guide");
            nodeContainer.Add(guide);
        }
        if (isChild)
        {
            var branch = new VisualElement();
            branch.AddToClassList("hierarchy-tree-branch");
            nodeContainer.Add(branch);
        }
        var row = new VisualElement();
        row.AddToClassList("hierarchy-tree-row");
        row.style.flexDirection = FlexDirection.Row;
        // Align the disclosure with the title row. The caller's header may also contain behavior tabs below it,
        // so centering against the entire header would leave the triangle halfway down the hierarchy item.
        row.style.alignItems = Align.FlexStart;
        VisualElement childContainer = null;
        var toggle = new Toggle { value = expanded };
        toggle.AddToClassList("hierarchy-tree-toggle");
        var toggleLabel = toggle.Q<Label>();
        if (toggleLabel != null)
            toggleLabel.style.display = DisplayStyle.None;
        // Keep the gutter reserved on leaf rows so their icons and labels line up with expandable siblings.
        if (children.Count == 0)
            toggle.style.visibility = Visibility.Hidden;
        toggle.RegisterValueChangedCallback(evt =>
        {
            ExpandedByKey[key] = evt.newValue;
            if (childContainer != null)
                childContainer.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None;
            SetArrow(toggle, evt.newValue);
            if (guide != null)
                childContainer?.schedule.Execute(() => UpdateGuide(nodeContainer, childContainer, guide, toggle)).ExecuteLater(0);
            ApplyAlternatingShades();
        });
        toggle.RegisterCallback<AttachToPanelEvent>(_ => SetArrow(toggle, toggle.value));
        row.Add(toggle);
        header.style.flexGrow = 1;
        header.style.minWidth = 0;
        row.Add(header);
        nodeContainer.Add(row);

        childContainer = new VisualElement();
        childContainer.AddToClassList("hierarchy-tree-children");
        foreach (var child in children)
            childContainer.Add(BuildNode(child, isChild: true));
        childContainer.style.display = children.Count > 0 && expanded ? DisplayStyle.Flex : DisplayStyle.None;
        nodeContainer.Add(childContainer);

        if (guide != null)
        {
            nodeContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateGuide(nodeContainer, childContainer, guide, toggle));
            childContainer.RegisterCallback<GeometryChangedEvent>(_ => UpdateGuide(nodeContainer, childContainer, guide, toggle));
            var lastTitle = childContainer.Children().LastOrDefault()?.Q<VisualElement>(className: "dock-object-header");
            lastTitle?.RegisterCallback<GeometryChangedEvent>(_ => UpdateGuide(nodeContainer, childContainer, guide, toggle));
            nodeContainer.schedule.Execute(() => UpdateGuide(nodeContainer, childContainer, guide, toggle)).ExecuteLater(0);
        }
        return nodeContainer;
    }

    private static void UpdateGuide(VisualElement node, VisualElement children, VisualElement guide, Toggle toggle)
    {
        if (node == null || children == null || guide == null || toggle == null || !toggle.value || children.childCount == 0)
        {
            if (guide != null)
                guide.style.display = DisplayStyle.None;
            return;
        }

        var lastChild = children.hierarchy.ElementAt(children.childCount - 1);
        var titleRow = lastChild.Q<VisualElement>(className: "dock-object-header");
        if (titleRow == null || titleRow.layout.height <= 0)
            return;

        var titleCenter = titleRow.LocalToWorld(new Vector2(0, titleRow.layout.height * 0.5f));
        var endY = node.WorldToLocal(titleCenter).y;
        guide.style.display = DisplayStyle.Flex;
        guide.style.height = Mathf.Max(0, endY - 11f);
    }

    private void ApplyAlternatingShades()
    {
        var visibleIndex = 0;
        foreach (var root in Children().ToList())
            ApplyShade(root, ref visibleIndex);
    }

    private static void ApplyShade(VisualElement node, ref int visibleIndex)
    {
        node.EnableInClassList("dock-object--odd", visibleIndex++ % 2 == 1);

        // A collapsed branch does not take a place in the visible row sequence. Reapply after toggles so
        // every visible row continues the same light/dark alternation through nested levels.
        var toggle = node.Q<Toggle>(className: "hierarchy-tree-toggle");
        if (toggle == null || !toggle.value)
            return;

        var childContainer = node.Q<VisualElement>(className: "hierarchy-tree-children");
        if (childContainer == null)
            return;
        foreach (var child in childContainer.Children().ToList())
            ApplyShade(child, ref visibleIndex);
    }

    private static void SetArrow(Toggle toggle, bool expanded)
    {
        var arrow = toggle?.Q(name: "unity-checkmark");
        if (arrow != null)
            arrow.style.rotate = new Rotate(new Angle(expanded ? 90f : 0f));
    }
}
