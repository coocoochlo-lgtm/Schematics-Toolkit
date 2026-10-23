using Remedy.Framework;
//using SaintsField;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

[SchematicGlobalObject("Input System")]
public class RemedyInput : SingletonData<RemedyInput>
{
    public bool ShowCursor = false;
    public enum ControlSchemeType
    {
        Keyboard,
        Gamepad
    }
    private ControlSchemeType _currentControlScheme = ControlSchemeType.Keyboard;
    public static ControlSchemeType ControlScheme => Instance._currentControlScheme;

    private UnityEvent<InputAction.CallbackContext> _onInputDown = new();
    /// <summary>
    /// An Event that is called when an Input is pressed. The CallbackContext is passed with the Event.
    /// </summary>
    public static UnityEvent<InputAction.CallbackContext> OnInputDown => Instance._onInputDown;
    private UnityEvent<InputAction.CallbackContext> _onInputHeld = new();
    /// <summary>
    /// An Event that is called when an Input is held. The CallbackContext is passed with the Event.
    /// </summary>
    public static UnityEvent<InputAction.CallbackContext> OnInputHeld => Instance._onInputHeld;
    private UnityEvent<InputAction.CallbackContext> _onInputUp = new();
    /// <summary>
    /// An Event that is called when an Input is released. The CallbackContext is passed with the Event.
    /// </summary>
    public static UnityEvent<InputAction.CallbackContext> OnInputUp => Instance._onInputUp;

    // The latest value of each held input, resent every frame by UpdateInputs.
    private static readonly Dictionary<SignalData, object> _currentInput = new();

    [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 0, foldoutTitle: "Input Maps", itemLabel: "Map", renamable: true)]
    public InputActionMap[] InputMaps = new InputActionMap[0];
    [SerializeField]
    //[Dropdown("GetInputMaps")]
    private InputActionMap _currentActionMap;
    private Dictionary<InputAction, List<InputActionEvent>> _actionsToUpdate = new();

    // The handlers attached to each Input Action of the current map, kept so they can be removed again. Without this,
    // every call to SetInputMap stacked another set of handlers and inputs fired once more each time.
    private readonly Dictionary<InputAction, (Action<InputAction.CallbackContext> started, Action<InputAction.CallbackContext> performed, Action<InputAction.CallbackContext> canceled)> _boundHandlers = new();


    public void InitializeInput()
    {
        if (InputMaps != null && InputMaps.Length > 0 && InputMaps[0] != null)
        {
            SetInputMap(InputMaps[0].Name);
        }
        else
            Debug.LogWarning("Remedy Input: there are no Input Maps, so no input is read. Add one in the Schematic window's Global tab (Input System).");
    }

    public void SetInputMap(string mapName)
    {
        InputActionMap map = null;
        foreach (var candidate in InputMaps)
            if (candidate.Name == mapName)
                map = candidate;

        if (map == null)
        {
            Debug.LogError("The Requested Input Map does not exist: " + mapName);
            return;
        }
        UnbindCurrentMap();
        _currentActionMap = map;

        foreach (var inputEventCollection in _currentActionMap.Inputs)
        {
            var action = inputEventCollection.Input;
            if (action == null || _boundHandlers.ContainsKey(action))
                continue;

            InputActionEvent.ForgetStaleBindings(action);
            if (action.bindings.Count == 0)
                Debug.LogWarning($"Remedy Input: the input \"{inputEventCollection.Name}\" of map \"{map.Name}\" has no bindings, so it can't be pressed. Add one in the Schematic window's Global tab (Input System).");

            WarnIfDigital(inputEventCollection, map);

            if (inputEventCollection.Output == null)
                Debug.LogWarning($"Remedy Input: the input \"{inputEventCollection.Name}\" of map \"{map.Name}\" has no Signal, so nothing hears it. Open it in the Schematic window's Global tab (Input System) to create one.");

            Action<InputAction.CallbackContext> started = context =>
            {
                Send(inputEventCollection, context);

                if (!inputEventCollection.OneShot)
                {
                    if (!_actionsToUpdate.TryGetValue(action, out var held))
                        _actionsToUpdate.Add(action, held = new());
                    if (!held.Contains(inputEventCollection))
                        held.Add(inputEventCollection);
                }
            };

            Action<InputAction.CallbackContext> performed = context => Send(inputEventCollection, context);

            Action<InputAction.CallbackContext> canceled = context =>
            {
                _actionsToUpdate.Remove(action);
                Release(inputEventCollection);
            };

            action.started += started;
            action.performed += performed;
            action.canceled += canceled;
            _boundHandlers[action] = (started, performed, canceled);
            action.Enable();
        }
    }

    /// <summary>
    /// Detaches the current map's inputs: removes their handlers, disables them, and releases any that are held
    /// (their Signals get defaults), so nothing stays "pressed" after switching maps.
    /// </summary>
#if UNITY_EDITOR
    // Leaving Play Mode: every Input Action is turned off again and its handlers taken off. They're on the asset, so
    // they stayed enabled after Play Mode, and Unity's Input Action editor can't change an enabled action: new inputs
    // drew blank and existing ones wouldn't change, until a recompile reset them.
    [UnityEditor.InitializeOnLoadMethod]
    private static void ReleaseActionsAfterPlayMode()
    {
        UnityEditor.EditorApplication.playModeStateChanged += change =>
        {
            if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode && change != UnityEditor.PlayModeStateChange.EnteredEditMode)
                return;
            // (Only the ones loaded: Instance would make the asset if there were none.)
            foreach (var input in Resources.FindObjectsOfTypeAll<RemedyInput>())
                input.ReleaseAllActions();
        };
    }

    private void ReleaseAllActions()
    {
        UnbindCurrentMap();
        _currentInput.Clear();
        if (InputMaps == null)
            return;
        foreach (var map in InputMaps)
            if (map?.Inputs != null)
                foreach (var input in map.Inputs)
                {
                    var action = input?.Input;
                    if (action == null)
                        continue;
                    if (action.enabled)
                        action.Disable();
                    InputActionEvent.ForgetStaleBindings(action);
                }
    }
#endif

    private void UnbindCurrentMap()
    {
        foreach (var entry in _boundHandlers)
        {
            var action = entry.Key;
            action.started -= entry.Value.started;
            action.performed -= entry.Value.performed;
            action.canceled -= entry.Value.canceled;
            action.Disable();
        }
        _boundHandlers.Clear();

        foreach (var held in _actionsToUpdate.Values)
            foreach (var inputEvent in held)
                Release(inputEvent);
        _actionsToUpdate.Clear();
    }

    // Analog input that arrives as all-or-nothing (a stick at 0.2 read as 1) is always the Input Action's own setup,
    // so it's pointed out when the map is bound: a 2D Vector composite in a digital mode (its default) turns each of
    // its parts into a button, and a Button action only sends when it's pressed and released, not in between.
    private static void WarnIfDigital(InputActionEvent input, InputActionMap map)
    {
        var action = input.Input;
        var output = input.Output;
        // A bool Signal on an analog input (a stick, a trigger, a 2D Vector): it sends "pressed" (true), which anything
        // reading it as a number or vector gets as 1, or (1, 0): a stick pushed a little reads as fully pushed right.
        var analogType = InputActionEvent.ValueTypeOf(action);
        bool boolSignal = output != null && output.Parameters.Contents.Count > 0 && output.Parameters[0].Type == Union.ValueType.Bool;
        bool analogBindings = false;
        foreach (var binding in action.bindings)
        {
            string path = (binding.path ?? "").ToLowerInvariant();
            if (binding.isComposite ? path.StartsWith("2dvector") || path.StartsWith("1daxis") || path.StartsWith("3dvector")
                    : path.Contains("stick") || path.Contains("trigger") || path.Contains("delta"))
                analogBindings = true;
        }
        if (boolSignal && (analogBindings || (analogType != null && analogType != typeof(bool))))
            Debug.LogWarning($"Remedy Input: \"{input.Name}\" (map \"{map.Name}\") reads an analog control but its Signal carries a bool, so it only sends pressed or not: read as a number or vector that's 1, or (1, 0). Set its Action Type to Value and its Control Type to Vector2 (or Axis) so the Signal carries the real value.");

        bool analogSignal = output != null && output.Parameters.Contents.Count > 0
            && output.Parameters[0].Type is Union.ValueType.Float or Union.ValueType.Vector2 or Union.ValueType.Vector3;
        if (!analogSignal)
            return;
        if (action.type == InputActionType.Button)
            Debug.LogWarning($"Remedy Input: \"{input.Name}\" (map \"{map.Name}\") is a Button action, so it only sends when it's pressed and released, all or nothing. For an axis or stick, set its Action Type to Value.");
        foreach (var binding in action.bindings)
        {
            if (!binding.isComposite)
                continue;
            string path = (binding.path ?? "").ToLowerInvariant().Replace(" ", "");
            if (path.StartsWith("2dvector") && !path.Contains("mode=2"))
            {
                Debug.LogWarning($"Remedy Input: \"{input.Name}\" (map \"{map.Name}\") uses a 2D Vector composite in a digital mode (the default), which reads each direction as a button: a stick pushed a little comes out as 1. For analog parts (a stick's directions), set the composite's Mode to Analog, or bind the whole stick (<Gamepad>/leftStick) instead.");
                break;
            }
        }
    }

    private static void Release(InputActionEvent inputEvent)
    {
        var output = inputEvent.Output;
        if (output == null) return;
        _currentInput.Remove(output);
        // A Signal with no value has nothing to say about a release (sent, it's just another press: a second Boost).
        if (output.Parameters.Contents.Count == 0)
            return;
        // Released: every listener gets its parameter's default (false, 0, Vector2.zero...).
        output.BroadcastDefault();
    }

    /// <summary>
    /// Sends an input's value through its Signal, read as the Signal's parameter type. Signals carry any value, so
    /// types without a case of their own are sent as whatever the Input System reads.
    /// </summary>
    private static void Send(InputActionEvent inputEvent, InputAction.CallbackContext context)
    {
        var output = inputEvent.Output;
        if (output == null)
            return;

        if (output.Parameters.Contents.Count == 0)
        {
            output.Broadcast();
            return;
        }

        object value;
        try
        {
            value = output.Parameters[0].Type switch
            {
                Union.ValueType.Bool => context.ReadValueAsButton(),
                Union.ValueType.Float => context.ReadValue<float>(),
                Union.ValueType.Int => context.ReadValue<int>(),
                Union.ValueType.Quaternion => context.ReadValue<Quaternion>(),
                Union.ValueType.Vector2 => context.ReadValue<Vector2>(),
                Union.ValueType.Vector3 => context.ReadValue<Vector3>(),
                _ => context.ReadValueAsObject(),
            };
        }
        catch (InvalidOperationException)
        {
            // The binding gives another type than the Signal's parameter (e.g. a stick into a float): send what it
            // reads instead of throwing inside the Input System's callback, and listeners convert it.
            value = context.ReadValueAsObject();
        }

        _currentInput[output] = value;
        output.Broadcast(value);
    }

#if UNITY_EDITOR
    // Editing an input (its Input Action's control type, say) brings its Signal up to date. After the edit is applied:
    // renaming a Signal's file can't happen in the middle of OnValidate.
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= SyncSignals;
        UnityEditor.EditorApplication.delayCall += SyncSignals;
    }

    /// <summary>
    /// Editor only: names every input's Signal after the input, and gives it the parameter its Input Action's control
    /// type calls for (see <see cref="InputActionEvent.ValueType"/>).
    /// </summary>
    public void SyncSignals()
    {
        if (this == null || InputMaps == null)
            return;
        foreach (var map in InputMaps)
            if (map?.Inputs != null)
                foreach (var input in map.Inputs)
                    input?.SyncSignal();
    }
#endif

    public void UpdateInputs()
    {
        if (_currentActionMap == null) return;

        foreach (var entry in _actionsToUpdate.ToArray())
        {
            var action = entry.Key;
            if (action.phase != InputActionPhase.Started && action.phase != InputActionPhase.Performed)
            {
                _actionsToUpdate.Remove(action);
                foreach (var inputEvent in entry.Value)
                    Release(inputEvent);
                continue;
            }

            foreach (var inputEvent in entry.Value)
            {
                var output = inputEvent.Output;
                if (output != null && _currentInput.TryGetValue(output, out var value))
                    output.Broadcast(value);
            }
        }

        if (ShowCursor)
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
        else
        {
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
        }
    }

    [Serializable]
    public class InputActionMap 
    {
        public string Name;
        [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 1, foldoutTitle: "Inputs", itemLabel: "Input", renamable: true)]
        public InputActionEvent[] Inputs = new InputActionEvent[0];

        public InputActionMap() { }

        public InputActionMap(string name)
        {
            Name = name;
        }

        public InputActionEvent this[int i]
        {
            get { return Inputs[i]; }
        }

        public void AddInput(string inputName)
        {
            Inputs = Inputs.Append(new(inputName)).ToArray();
        }

        public void RemoveInput(InputActionEvent inputEvent)
        {
            Inputs = Inputs.Where(item => item != inputEvent).ToArray();
        }
    }

    [Serializable]
    public class InputActionEvent
    {
        public string Name;
        public string EventName => $"Input {Name}";  
        [IMGUIContainerRenderer]
        [Tooltip("The Input Action, with it's binds.")]
        public InputAction Input= new();

        /// <summary>
        /// The Signal this input sends. Its name and its parameter (the value read from the input: pick bool for
        /// buttons, float for triggers and axes, Vector2 for sticks and WASD...) are edited in the Schematic window's
        /// Global tab. Graphs react to it with On Received.
        /// </summary>
        [SignalContainerRenderer(typeof(SignalData), nameof(EventName), parametersEditable: true, paramTypeModifiable: true, paramNameModifiable: true,
            Outgoing = true, MaxParameters = 1, NameFrom = nameof(Name), ParameterTypeFrom = nameof(ValueType))]
        public SignalData Output;

        [IMGUIContainerRenderer]
        [Tooltip("If true, the Output Event is not called again on update, although it will be called if the value for the Input has been updated (InputActionPhase.Performed).")]
        public bool OneShot = false;

        /// <summary>
        /// The type of value this input gives, from its Input Action: bool for buttons, float for axes and triggers,
        /// Vector2 for sticks, WASD and the mouse, and so on. Its Signal carries one of these. Null when the action
        /// doesn't say (a pass-through with no control type or bindings to go by): then the Signal's type is picked by
        /// hand.
        /// </summary>
        public Type ValueType => ValueTypeOf(Input);

        public static Type ValueTypeOf(InputAction action)
        {
            if (action == null)
                return null;

            // The control type it expects, when it says ("Button", "Axis", "Vector2", "Stick", "Dpad"...).
            var fromControl = ValueTypeOfControl(action.expectedControlType);
            if (fromControl != null)
                return fromControl;
            if (action.type == InputActionType.Button)
                return typeof(bool);

            // Otherwise from its bindings: a WASD composite is a Vector2, a 1D axis a float...
            var bindings = SavedBindingsOf(action);
            foreach (var binding in bindings)
            {
                if (!binding.isComposite)
                    continue;
                switch (NameOf(binding.path))
                {
                    case "2dvector": case "dpad": return typeof(Vector2);
                    case "3dvector": return typeof(Vector3);
                    case "1daxis": case "axis": return typeof(float);
                    case "buttonwithonemodifier": case "buttonwithtwomodifiers":
                    case "onemodifier": case "twomodifiers": return typeof(bool);
                }
            }
            foreach (var binding in bindings)
            {
                if (binding.isComposite || binding.isPartOfComposite || string.IsNullOrEmpty(binding.path))
                    continue;
                // A single control: what it is, by its path ("<Gamepad>/leftStick", "<Mouse>/delta", "<Keyboard>/space").
                var control = InputSystem.FindControl(binding.path);
                // (A Value action reads a key or trigger as a float, so this goes by the value, not the control type.)
                var valueType = control?.valueType;
                if (valueType == typeof(double))
                    return typeof(float);
                if (valueType == typeof(float) || valueType == typeof(int) || valueType == typeof(Vector2)
                    || valueType == typeof(Vector3) || valueType == typeof(Quaternion))
                    return valueType;
            }
            return null;
        }

        private static readonly System.Reflection.FieldInfo _savedBindings = typeof(InputAction).GetField("m_SingletonActionBindings",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>
        /// The action's bindings as saved, read without side effects. Not InputAction.bindings: on a standalone action
        /// that builds a hidden action map holding the bindings as they are at that moment, and it isn't rebuilt when
        /// bindings are added later, so an input read before it had any could never be pressed ("has no bindings").
        /// </summary>
        private static InputBinding[] SavedBindingsOf(InputAction action)
            => _savedBindings?.GetValue(action) as InputBinding[] ?? Array.Empty<InputBinding>();

        private static readonly System.Reflection.FieldInfo _hiddenMap = typeof(InputAction).GetField("m_ActionMap",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>
        /// Drops a standalone action's hidden action map when it holds other bindings than the saved ones (it was made
        /// before they were edited, e.g. in an earlier Play session with domain reload off), so the action is rebuilt
        /// from what's saved when it's enabled. Only for disabled actions.
        /// </summary>
        internal static void ForgetStaleBindings(InputAction action)
        {
            if (action == null || action.enabled || _hiddenMap == null || _hiddenMap.GetValue(action) == null)
                return;
            var saved = SavedBindingsOf(action);
            var current = action.bindings;
            bool same = current.Count == saved.Length;
            for (int i = 0; same && i < saved.Length; i++)
                same = current[i].path == saved[i].path && current[i].isComposite == saved[i].isComposite
                    && current[i].isPartOfComposite == saved[i].isPartOfComposite;
            if (!same)
                _hiddenMap.SetValue(action, null);
        }

        private static Type ValueTypeOfControl(string controlType)
        {
            switch ((controlType ?? "").Trim().ToLowerInvariant())
            {
                case "button": case "key": case "digital": case "touchpress": return typeof(bool);
                case "axis": case "analog": case "double": return typeof(float);
                case "integer": return typeof(int);
                case "vector2": case "stick": case "dpad": case "delta": return typeof(Vector2);
                case "vector3": return typeof(Vector3);
                case "quaternion": return typeof(Quaternion);
                default: return null;
            }
        }

        // "2DVector(mode=2)" -> "2dvector"
        private static string NameOf(string compositePath)
        {
            if (string.IsNullOrEmpty(compositePath))
                return "";
            int paren = compositePath.IndexOf('(');
            return (paren >= 0 ? compositePath.Substring(0, paren) : compositePath).Trim().ToLowerInvariant();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor only: names the Signal after this input and gives it the parameter <see cref="ValueType"/> calls for.
        /// </summary>
        public void SyncSignal()
        {
            if (Output == null)
                return;
            if (!string.IsNullOrWhiteSpace(Name))
                Output.EditorRename(Name);
            if (Output.SetSingleParameter(ValueType))
                UnityEditor.EditorUtility.SetDirty(Output);
        }
#endif

        public InputActionEvent() 
        {
            //if (Input == null) Input = new();
        }
        public InputActionEvent(string name)
        {
            Name = name;
        }
    }
}