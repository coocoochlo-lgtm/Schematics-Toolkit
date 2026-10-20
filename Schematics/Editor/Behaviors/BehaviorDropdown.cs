using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// The searchable "Add Schematic Behavior" menu, styled after Unity's own Add Component menu.
/// </summary>
public class BehaviorDropdown : AdvancedDropdown
{
    private class BehaviorItem : AdvancedDropdownItem
    {
        public readonly Type Type;
        public BehaviorItem(string name, Type type) : base(name) { Type = type; }
    }

    private readonly HashSet<Type> _exclude;
    private readonly Action<Type> _onPicked;

    public BehaviorDropdown(AdvancedDropdownState state, HashSet<Type> exclude, Action<Type> onPicked) : base(state)
    {
        _exclude = exclude;
        _onPicked = onPicked;
        minimumSize = new Vector2(230, 320);
    }

    public static string GetMenuPath(Type type)
    {
        var attribute = type.GetCustomAttribute<SchematicBehaviorPathAttribute>();
        if (attribute != null && !string.IsNullOrEmpty(attribute.Path))
            return attribute.Path;

        var name = ObjectNames.NicifyVariableName(type.Name);
        return string.IsNullOrEmpty(type.Namespace) ? name : $"{type.Namespace.Replace('.', '/')}/{name}";
    }

    protected override AdvancedDropdownItem BuildRoot()
    {
        var root = new AdvancedDropdownItem("Schematic Behaviors");

        var entries = BehaviorMetadataGenerator.BehaviorTypes
            .Where(type => !_exclude.Contains(type) && type.GetCustomAttribute<SchematicBehaviorPathAttribute>()?.Hidden != true)
            .Select(type => (type, path: GetMenuPath(type)))
            .OrderBy(entry => entry.path);

        foreach (var (type, path) in entries)
        {
            var parts = path.Split('/');
            var parent = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                var folder = parent.childList.FirstOrDefault(child => child.name == parts[i] && !(child is BehaviorItem));
                if (folder == null)
                {
                    folder = new AdvancedDropdownItem(parts[i]);
                    parent.AddChild(folder);
                }
                parent = folder;
            }
            parent.AddChild(new BehaviorItem(parts[parts.Length - 1], type));
        }

        if (!root.childList.Any())
            root.AddChild(new AdvancedDropdownItem("No Schematic Behaviors to add") { enabled = false });

        return root;
    }

    protected override void ItemSelected(AdvancedDropdownItem item)
    {
        if (item is BehaviorItem behaviorItem)
            _onPicked(behaviorItem.Type);
    }
}
