using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Remedy.Schematics;
using Remedy.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>Read-only structural view of UI Builder UXML, with Schematic Behaviors and nodes attached by element name.</summary>
public sealed class UIHierarchy : VisualElement
{
    private sealed class ElementNode
    {
        public VisualElement Element;
        public string Key;
        public readonly List<ElementNode> Children = new();
    }

    private readonly SchematicUIGraph _graph;
    private VisualTreeAsset _tree => _graph != null ? _graph.Layout : null;
    private readonly EditorWindow _window;
    private readonly Dictionary<string, int> _nameCounts = new();
    private static readonly Dictionary<string, Texture2D> _elementIcons = new();
    private string _assetPath;
    private Hash128 _dependencyHash;
    private IVisualElementScheduledItem _watch;

    public UIHierarchy(SchematicUIGraph graph, EditorWindow window)
    {
        _graph = graph;
        _window = window;
        _assetPath = AssetDatabase.GetAssetPath(_tree);
        _dependencyHash = DependencyHash();
        style.flexGrow = 1;
        var dockStyle = Resources.Load<StyleSheet>("IODock");
        if (dockStyle != null)
            styleSheets.Add(dockStyle);
        Rebuild();
        RegisterCallback<AttachToPanelEvent>(_ =>
        {
            _watch ??= schedule.Execute(WatchAsset).Every(500);
            _watch.Resume();
        });
        RegisterCallback<DetachFromPanelEvent>(_ => _watch?.Pause());
    }

    private Hash128 DependencyHash() => string.IsNullOrEmpty(_assetPath) ? default : AssetDatabase.GetAssetDependencyHash(_assetPath);

    private void WatchAsset()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        var path = AssetDatabase.GetAssetPath(_tree);
        if (path != _assetPath)
        {
            _assetPath = path;
            _dependencyHash = DependencyHash();
            Rebuild();
            return;
        }
        if (_tree == null)
            return;
        var hash = DependencyHash();
        if (hash == _dependencyHash)
            return;
        _dependencyHash = hash;
        Rebuild();
    }

    private void Rebuild()
    {
        Clear();
        Add(DockRows.SectionLabel("UI Builder Hierarchy"));
        if (_tree == null)
        {
            Add(new HelpBox("Assign a UI Builder UXML asset to this UI Graph.", HelpBoxMessageType.Info));
            return;
        }

        var preview = _tree.CloneTree();
        if (preview == null)
        {
            Add(new HelpBox("The UI Builder hierarchy could not be loaded.", HelpBoxMessageType.Warning));
            return;
        }
        _nameCounts.Clear();
        CountNames(preview);
        var roots = CollectElements(preview, "");
        Add(new HierarchyTreeView<ElementNode>(roots, node => node.Children, node => node.Key,
            CreateElementHeader, _assetPath + "|UI Tree"));
    }

    private void CountNames(VisualElement parent)
    {
        if (parent.visualTreeAssetSource != null && !string.IsNullOrEmpty(parent.name))
            _nameCounts[parent.name] = _nameCounts.TryGetValue(parent.name, out var count) ? count + 1 : 1;
        for (int i = 0; i < parent.hierarchy.childCount; i++)
            CountNames(parent.hierarchy.ElementAt(i));
    }

    private List<ElementNode> CollectElements(VisualElement parent, string parentKey)
    {
        var nodes = new List<ElementNode>();
        for (int i = 0; i < parent.hierarchy.childCount; i++)
        {
            var element = parent.hierarchy.ElementAt(i);
            // Composite controls create implementation children at runtime. Keep walking through them to find any
            // UXML-authored descendants, but only show elements that actually came from a UI Builder asset.
            if (element.visualTreeAssetSource == null)
            {
                nodes.AddRange(CollectElements(element, $"{parentKey}/@{element.GetType().Name}#{i}"));
                continue;
            }
            var node = new ElementNode
            {
                Element = element,
                Key = $"{parentKey}/{(string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name)}#{i}",
            };
            node.Children.AddRange(CollectElements(element, node.Key));
            nodes.Add(node);
        }
        return nodes;
    }

    private VisualElement CreateElementHeader(ElementNode node)
    {
            var element = node.Element;
            var name = element.name;
            bool duplicate = !string.IsNullOrEmpty(name) && _nameCounts.TryGetValue(name, out var nameCount) && nameCount > 1;
            var title = string.IsNullOrEmpty(name) ? "(unnamed)" : duplicate ? name + " (duplicate name)" : name;
            var graphWindow = _window as SchematicGraphEditorWindow;
            var itemContainer = new VisualElement();
            itemContainer.AddToClassList("ui-hierarchy-item");
            var row = new VisualElement();
            row.AddToClassList("ui-hierarchy-row");
            row.AddToClassList("dock-object-header");
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.paddingLeft = 4;
            row.style.minHeight = 22;

            var icon = new Image
            {
                image = ElementIcon(element),
                scaleMode = ScaleMode.ScaleToFit,
                tooltip = element.GetType().Name,
            };
            icon.AddToClassList("ui-hierarchy-icon");
            icon.style.width = icon.style.height = 16;
            icon.style.flexShrink = 0;
            icon.style.marginRight = 4;
            row.Add(icon);

            var label = new Label($"{title}  ·  {element.GetType().Name}");
            label.AddToClassList("ui-hierarchy-label");
            label.style.flexGrow = 1;
            row.Add(label);

            if (!string.IsNullOrEmpty(name) && !duplicate && graphWindow != null)
            {
                var item = new DockItem
                {
                    Kind = DockItem.ItemKind.UIElement,
                    ElementName = name,
                };
                DockRows.MakeDragSource(row, item);
                row.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
                {
                    evt.menu.AppendAction("Open UI Builder", _ => AssetDatabase.OpenAsset(_tree));
                    evt.menu.AppendAction("Create/UI Element Reference", _ => graphWindow.CreateUINode(item, typeof(UIElementReferenceNode)));
                    if (element is Button)
                        evt.menu.AppendAction("Create/On Button Clicked", _ => graphWindow.CreateUINode(item, typeof(OnUIButtonClicked)));
                    if (element is TextElement)
                        evt.menu.AppendAction("Create/Set Text", _ => graphWindow.CreateUINode(item, typeof(SetUIText)));
                    evt.menu.AppendAction("Create/Set Visibility", _ => graphWindow.CreateUINode(item, typeof(SetUIVisible)));
                    evt.menu.AppendAction("Create/Set Enabled", _ => graphWindow.CreateUINode(item, typeof(SetUIEnabled)));
                    evt.menu.AppendSeparator("Attach Behavior/");
                    foreach (var type in AvailableBehaviors(element))
                    {
                        var behaviorType = type;
                        evt.menu.AppendAction("Attach Behavior/" + BehaviorDropdown.GetMenuPath(type),
                            _ => graphWindow.AttachUIBehavior(name, behaviorType),
                            _graph.FindBehaviors(name)?.Items?.Any(a => a?.Metadata?.BehaviorType.Type == behaviorType) == true
                                ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
                    }
                    var attached = _graph.FindBehaviors(name);
                    if (attached?.Items != null)
                        foreach (var attachment in attached.Items.ToArray())
                        {
                            var behaviorType = attachment?.Metadata?.BehaviorType.Type;
                            if (behaviorType == null) continue;
                            evt.menu.AppendAction("Remove Behavior/" + ObjectNames.NicifyVariableName(behaviorType.Name),
                                _ => graphWindow.RemoveUIBehavior(name, attachment));
                        }
                });
                row.tooltip = $"Drag '{name}' into the graph to create a UI Element reference.";
            }
            else
            {
                row.tooltip = "Give this element a unique Name in UI Builder to reference it from Schematics.";
            }

            itemContainer.Add(row);
            if (graphWindow != null && !string.IsNullOrEmpty(name) && !duplicate)
                itemContainer.Add(CreateBehaviorTabs(name, graphWindow, element));
            return itemContainer;
    }

    private static Texture2D ElementIcon(VisualElement element)
    {
        var type = element.GetType();
        var name = type.Name;
        string iconName = element switch
        {
            Button => "button",
            Image => "image",
            Toggle => "toggle",
            ScrollView => "scroll-view",
            Foldout => "foldout",
            TextElement => "text",
            _ when name.Contains("Dropdown") || name.Contains("Popup") || name.Contains("EnumField") => "dropdown",
            _ when name.Contains("Toggle") || name.Contains("RadioButton") => "toggle",
            _ when name.Contains("ListView") || name.Contains("TreeView") => "list",
            _ when name.Contains("Slider") || name.Contains("ProgressBar") => "slider",
            _ when name.Contains("TextField") || name.Contains("Field") => "input",
            _ => "container",
        };

        if (!_elementIcons.TryGetValue(iconName, out var icon))
        {
            icon = LoadElementIconAsset(iconName);
            if (icon == null && iconName != "container")
                icon = LoadElementIconAsset("container");
            if (icon != null)
                _elementIcons[iconName] = icon;
        }
        return icon;
    }

    private static Texture2D LoadElementIconAsset(string iconName)
    {
        var assetName = "ui-" + iconName;
        var icon = Resources.Load<Texture2D>($"UIElementIcons/{assetName}");
        if (icon != null)
            return icon;

        foreach (var guid in AssetDatabase.FindAssets(assetName + " t:Texture2D"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!string.Equals(System.IO.Path.GetFileNameWithoutExtension(path), assetName, StringComparison.Ordinal))
                continue;
            icon = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (icon != null)
                return icon;
        }
        return null;
    }

    private static IEnumerable<System.Type> AvailableBehaviors(VisualElement element)
        => BehaviorMetadataGenerator.BehaviorTypes
            .Where(type => typeof(SchematicUIBehavior).IsAssignableFrom(type)
                && !type.IsAbstract && !type.ContainsGenericParameters
                && (type.GetCustomAttribute<SchematicUIElementBehaviorAttribute>()?.ElementType is not Type elementType
                    || elementType.IsAssignableFrom(element.GetType()))
                && type.GetCustomAttribute<SchematicBehaviorPathAttribute>()?.Hidden != true)
            .OrderBy(type => BehaviorDropdown.GetMenuPath(type), StringComparer.OrdinalIgnoreCase);

    private VisualElement CreateBehaviorTabs(string elementName, SchematicGraphEditorWindow graphWindow, VisualElement element)
    {
        var container = new VisualElement();
        container.AddToClassList("component-tabs");
        StopClicks(container);
        var tabs = new TabView("", "components", true, hideTabLabels: true, collapsible: true);
        var path = SchematicUI.PathForElement(elementName);
        var attachments = _graph.FindBehaviors(elementName)?.Items;
        if (attachments != null)
        {
            foreach (var attachment in attachments.ToArray())
            {
                var type = attachment?.Metadata?.BehaviorType.Type;
                if (type == null)
                    continue;
                string tabName = ObjectNames.NicifyVariableName(type.Name);
                while (tabs.HasTab(tabName))
                    tabName += " ";
                var (icon, ownIcon) = BehaviorIcon(type);
                var tab = tabs.AddTab(tabName, icon, showLabel: !ownIcon);
                tab.UseFactory(() => CreateBehaviorContent(elementName, path, attachment, type, graphWindow));
                var capturedAttachment = attachment;
                var capturedType = type;
                tab.Button.AddManipulator(new ContextualMenuManipulator(evt =>
                    evt.menu.AppendAction("Remove Behavior", _ => graphWindow.RemoveUIBehavior(elementName, capturedAttachment))));
                DockRows.MakeDragSource(tab.Button, new DockItem
                {
                    Kind = DockItem.ItemKind.Behavior,
                    Behavior = capturedType,
                    Path = path,
                    ElementName = elementName,
                });
                tab.Button.tooltip = $"{tabName}: attached to UI element '{elementName}'. Right-click to remove.";
            }
        }

        tabs.AddActionButton("+", "Add a UI Behavior to this element", () =>
        {
            var attached = new HashSet<Type>(_graph.FindBehaviors(elementName)?.Items
                ?.Select(a => a?.Metadata?.BehaviorType.Type).Where(t => t != null) ?? Enumerable.Empty<Type>());
            var menu = new GenericMenu();
            foreach (var type in AvailableBehaviors(element))
            {
                var behaviorType = type;
                menu.AddItem(new GUIContent(BehaviorDropdown.GetMenuPath(type)), attached.Contains(type), () =>
                {
                    if (!attached.Contains(behaviorType))
                        graphWindow.AttachUIBehavior(elementName, behaviorType);
                });
            }
            if (menu.GetItemCount() == 0)
                menu.AddDisabledItem(new GUIContent("No UI Behaviors available"));
            menu.ShowAsContext();
        }).AddToClassList("tab-add-component");

        container.Add(tabs);
        return container;
    }

    private static void StopClicks(VisualElement element)
    {
        element.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<MouseUpEvent>(evt => evt.StopPropagation());
        element.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
    }

    private VisualElement CreateBehaviorContent(string elementName, string path, BehaviorAttachment attachment,
        Type behaviorType, SchematicGraphEditorWindow graphWindow)
    {
        var content = new VisualElement();
        var title = new Label(ObjectNames.NicifyVariableName(behaviorType.Name));
        title.AddToClassList("dock-behavior-name");
        content.Add(title);
        var metadata = attachment?.Metadata;
        if (metadata == null)
            return content;

        foreach (var variable in metadata.Variables ?? new List<VariableDefinition>())
        {
            if (variable == null)
                continue;
            var variableItem = new DockItem
            {
                Kind = DockItem.ItemKind.Variable,
                Behavior = behaviorType,
                Path = path,
                Name = variable.Name,
                ValueType = variable.Type != null ? variable.Type.Type : null,
                ElementName = elementName,
            };
            var variableRow = DockRows.Build(variableItem, DockRows.VariableIcon,
                ObjectNames.NicifyVariableName(variable.Name), DockRows.TypeName(variableItem.ValueType),
                MemberHelp.Row(MemberHelp.OfBehavior(behaviorType, variable.Name), "Drag to create a Get or Set node for this UI Behavior Variable."),
                () => { graphWindow.FocusNodes(variableItem); ScopeItemInspector.Show(variableItem); });
            var definition = variable;
            var value = new IMGUIContainer(() => DrawVariable(attachment, definition));
            value.style.minWidth = 70;
            value.style.flexGrow = 1;
            variableRow.Add(value);
            content.Add(variableRow);
        }

        foreach (var signal in metadata.Signals ?? new List<SignalDefinition>())
        {
            if (signal?.Asset == null)
                continue;
            bool intoGraph = signal.Direction == SignalDirection.Output;
            var signalItem = new DockItem
            {
                Kind = DockItem.ItemKind.Signal,
                Signal = signal.Asset,
                Direction = signal.Direction,
            };
            var signalRow = DockRows.Build(signalItem, DockRows.SignalIconFor(intoGraph),
                ObjectNames.NicifyVariableName(signal.Name), "Signal",
                MemberHelp.Row(MemberHelp.OfBehavior(behaviorType, signal.Name), "Drag this Signal into the graph to send or receive it."),
                () => graphWindow.FocusNodes(signalItem));
            DockRows.SetSignalMark(signalRow, intoGraph);
            content.Add(signalRow);
        }
        return content;
    }

    private static readonly Dictionary<Type, (Texture icon, bool own)> _behaviorIcons = new();

    private static (Texture icon, bool own) BehaviorIcon(Type type)
    {
        if (_behaviorIcons.TryGetValue(type, out var found))
            return found;
        foreach (var guid in AssetDatabase.FindAssets(type.Name + " t:MonoScript"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));
            if (script == null || script.GetClass() != type)
                continue;
            var own = EditorGUIUtility.GetIconForObject(script);
            found = own != null ? (own, true) : (AssetPreview.GetMiniThumbnail(script), false);
            _behaviorIcons[type] = found;
            return found;
        }
        found = (EditorGUIUtility.IconContent("cs Script Icon").image, false);
        _behaviorIcons[type] = found;
        return found;
    }

    private void DrawVariable(BehaviorAttachment attachment, VariableDefinition definition)
    {
        var type = definition == null ? null : definition.Type.Type;
        if (attachment == null || type == null)
            return;
        var currentOverride = attachment.FindOverride(definition.Name);
        var current = currentOverride?.Value != null ? currentOverride.Value.Get(type) : definition.Default?.Get(type);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (SchematicValueField.Draw(GUIContent.none, type, current, out var value))
            {
                Undo.RecordObject(_graph, "Edit UI Behavior Variable");
                attachment.SetOverride(definition.Name, value, type);
                EditorUtility.SetDirty(_graph);
            }
            using (new EditorGUI.DisabledScope(currentOverride == null))
            {
                if (GUILayout.Button(new GUIContent("↺", "Use the Behavior's default value"), EditorStyles.miniButton, GUILayout.Width(20)))
                {
                    Undo.RecordObject(_graph, "Reset UI Behavior Variable");
                    attachment.Overrides.Remove(currentOverride);
                    EditorUtility.SetDirty(_graph);
                }
            }
        }
    }
}
