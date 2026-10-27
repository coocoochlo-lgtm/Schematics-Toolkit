using UnityEngine;
using UnityEngine.UIElements;
using Remedy.Schematics;
using System;
using System.Collections.Generic;

namespace Remedy.UI
{
    /// <summary>A Schematic graph whose presentation is a UI Toolkit visual tree.</summary>
    [SchematicType("UI", Order = 40, ListsPrefabs = true, Icon = "SchematicTab:ui", NewAssetName = "New UI Schematic")]
    public sealed class SchematicUIGraph : SchematicGraph
    {
        public override string Title => "UI";

        [Tooltip("The UI Toolkit document displayed while this graph is running.")]
        public VisualTreeAsset Layout;

        // Controller Possession: a UI a controller possesses is driven by that controller's own input (its stick moves
        // the selection, its buttons press), so each player in split-screen works their own menu. Pick the Input
        // Signals here (from the Input Maps). A UI nobody possesses uses UI Toolkit's own input as usual.
        [Header("Player Input (when possessed)")]
        [Tooltip("A Vector2 Input Signal (a stick or the arrow keys) that moves the selection between buttons and fields.")]
        public SignalData Navigate;
        [Tooltip("A button Input Signal that presses the selected element (clicks a button, flips a toggle).")]
        public SignalData Submit;
        [Tooltip("A button Input Signal for going back: fires On UI Cancel.")]
        public SignalData Cancel;
        [Tooltip("The element selected first (its name in UI Builder). Empty: the first one that can be selected.")]
        public string FirstSelected;
        [Tooltip("The USS class the selected element gets, to style it in UI Builder.")]
        public string SelectedClass = "schematic-ui-selected";
        [Tooltip("Also outline the selected element (handy before it's styled).")]
        public bool OutlineSelected = true;

        [SerializeField] private List<UIElementBehaviorList> _uiBehaviors = new();
        public List<UIElementBehaviorList> UIBehaviors => _uiBehaviors ??= new();

        public UIElementBehaviorList FindBehaviors(string elementName)
            => UIBehaviors.Find(entry => entry != null && entry.ElementName == elementName);

        public UIElementBehaviorList GetOrCreateBehaviors(string elementName)
        {
            var entry = FindBehaviors(elementName);
            if (entry != null)
                return entry;
            entry = new UIElementBehaviorList { ElementName = elementName };
            UIBehaviors.Add(entry);
            return entry;
        }
    }

    /// <summary>Schematic Behaviors attached to one named element in the UI Builder hierarchy.</summary>
    [Serializable]
    public sealed class UIElementBehaviorList
    {
        public string ElementName;
        public List<BehaviorAttachment> Items = new();
    }

    /// <summary>Base class for Behaviors attached to a UI Builder element by the UI Tree dock.</summary>
    public abstract class SchematicUIBehavior : SchematicBehavior
    {
        public SchematicUI UI => Self?.GameObject != null ? Self.GameObject.GetComponentInParent<SchematicUI>() : null;
        public VisualElement Element => UI != null ? SchematicUI.ElementForBehavior(Self.GameObject) : null;
        public T Query<T>(string name) where T : VisualElement => UI != null ? UI.Query<T>(name) : null;
    }

    /// <summary>Limits a UI Behavior to one kind of UI Toolkit element; derived element types are included.</summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class SchematicUIElementBehaviorAttribute : Attribute
    {
        public Type ElementType { get; }

        public SchematicUIElementBehaviorAttribute(Type elementType)
            => ElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
    }

}
