using System.Collections.Generic;
using System.Text;
using Remedy.Schematics;
using UnityEngine;
using UnityEngine.UIElements;

namespace Remedy.UI
{
    /// <summary>A Schematic Object that displays its UI graph through a UI Toolkit UIDocument.</summary>
    [AddComponentMenu("Schematics/Schematic UI")]
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(UIDocument))]
    public sealed class SchematicUI : SchematicObject, ISchematicUIRunner
    {
        private readonly Dictionary<GameObject, string> _elementByProxy = new();
        private readonly List<(Button button, EventCallback<ClickEvent> callback)> _clickBindings = new();
        private SchematicUIPlayerInput _playerInput;

        public SchematicUIGraph UIGraph => SchematicGraph as SchematicUIGraph;

        public static string PathForElement(string elementName)
        {
            var bytes = Encoding.UTF8.GetBytes(elementName ?? string.Empty);
            return "__SchematicUI_" + System.Convert.ToBase64String(bytes).TrimEnd('=').Replace('/', '_').Replace('+', '-');
        }

        protected override void Awake()
        {
            PrepareBehaviorProxies();
            base.Awake();
        }

        protected override void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            if (document != null && UIGraph != null && UIGraph.Layout != null)
                document.visualTreeAsset = UIGraph.Layout;
            base.OnEnable();
            BindUIEvents();
            Possession.Possessed += OnPossessed;
            Possession.Unpossessed += OnUnpossessed;
            if (Possession.PossessorOf(this) is { IsLocal: true })
                PlayerInput.Begin();
        }

        protected override void OnDisable()
        {
            Possession.Possessed -= OnPossessed;
            Possession.Unpossessed -= OnUnpossessed;
            _playerInput?.End();
            UnbindUIEvents();
            base.OnDisable();
        }

        protected override void OnDestroy()
        {
            Possession.Possessed -= OnPossessed;
            Possession.Unpossessed -= OnUnpossessed;
            _playerInput?.End();
            UnbindUIEvents();
            base.OnDestroy();
        }

        #region Possession

        private SchematicUIPlayerInput PlayerInput => _playerInput ??= new SchematicUIPlayerInput(this);

        /// <summary>
        /// The element a possessing player has selected (with the graph's Navigate input), or null when no player drives
        /// this UI.
        /// </summary>
        public VisualElement SelectedElement => _playerInput?.Selected;

        // A controller on this machine took this UI: its input (routed to this object alone) drives the selection. One
        // on another machine drives it there.
        private void OnPossessed(SchematicObject schematic, Controller controller)
        {
            if (schematic != this)
                return;
            if (controller != null && controller.IsLocal)
                PlayerInput.Begin();
            else
                _playerInput?.End();
        }

        private void OnUnpossessed(SchematicObject schematic, Controller controller)
        {
            if (schematic == this)
                _playerInput?.End();
        }

        /// <summary>A possessing player pressed a button: the graph's On UI Button Clicked for it fire.</summary>
        internal void ButtonPressed(string elementName)
        {
            if (UIGraph == null || string.IsNullOrEmpty(elementName))
                return;
            foreach (var node in UIGraph.Nodes)
                if (node is OnUIButtonClicked onClick && onClick.ElementName == elementName)
                    onClick.Trigger(gameObject, new Remedy.Framework.Union(elementName));
        }

        #endregion

        /// <summary>Gets the UI Builder element name associated with one of this UI's Behavior proxy objects.</summary>
        public string ElementNameFor(GameObject behaviorObject)
            => behaviorObject != null && _elementByProxy.TryGetValue(behaviorObject, out var name) ? name : null;

        /// <summary>Finds the UI Builder element associated with a Behavior's proxy GameObject.</summary>
        public static VisualElement ElementForBehavior(GameObject behaviorObject)
        {
            var ui = behaviorObject != null ? behaviorObject.GetComponentInParent<SchematicUI>() : null;
            var name = ui != null ? ui.ElementNameFor(behaviorObject) : null;
            return !string.IsNullOrEmpty(name) ? ui.Query<VisualElement>(name) : null;
        }

        private void PrepareBehaviorProxies()
        {
            if (UIGraph == null)
                return;
            Behaviors ??= new Remedy.Framework.SerializableDictionary<GameObject, BehaviorAttachmentList>();
            foreach (var entry in UIGraph.UIBehaviors)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ElementName) || entry.Items == null || entry.Items.Count == 0)
                    continue;
                var proxyName = PathForElement(entry.ElementName);
                var existing = transform.Find(proxyName);
                var proxy = existing != null ? existing.gameObject : new GameObject(proxyName);
                if (existing == null)
                    proxy.transform.SetParent(transform, false);
                proxy.hideFlags = HideFlags.HideInHierarchy;
                Behaviors[proxy] = new BehaviorAttachmentList { Items = entry.Items };
                _elementByProxy[proxy] = entry.ElementName;
            }
        }

        private void BindUIEvents()
        {
            UnbindUIEvents();
            var root = GetComponent<UIDocument>()?.rootVisualElement;
            if (root == null || UIGraph == null)
                return;
            foreach (var node in UIGraph.Nodes)
            {
                if (node is not OnUIButtonClicked onClick || string.IsNullOrEmpty(onClick.ElementName))
                    continue;
                var button = root.Q<Button>(onClick.ElementName);
                if (button == null)
                    continue;
                EventCallback<ClickEvent> callback = _ => onClick.Trigger(gameObject, new Remedy.Framework.Union(onClick.ElementName));
                button.RegisterCallback(callback);
                _clickBindings.Add((button, callback));
            }
        }

        private void UnbindUIEvents()
        {
            foreach (var (button, callback) in _clickBindings)
                if (button != null)
                    button.UnregisterCallback(callback);
            _clickBindings.Clear();
        }

        /// <summary>Finds a named element in this runner's active document, if one is present.</summary>
        public T Query<T>(string name) where T : VisualElement
        {
            var document = GetComponent<UIDocument>();
            return document != null ? document.rootVisualElement?.Q<T>(name) : null;
        }

        object ISchematicUIRunner.QueryElement(string name) => Query<VisualElement>(name);
    }
}
