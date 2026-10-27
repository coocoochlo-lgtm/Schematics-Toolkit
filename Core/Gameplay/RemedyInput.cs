using Remedy.Framework;
using Remedy.Schematics;
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

    [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 0, foldoutTitle: "Input Maps", itemLabel: "Map", renamable: true, AsTabs = true)]
    public InputActionMap[] InputMaps = new InputActionMap[0];
    // The map in use while the game runs: one of InputMaps itself, so never saved. Saved, it was a second copy of that
    // map's data in the asset pointing at the same object, and applying any edit wrote the map from both: the second
    // (unedited) one last, putting every change to its Inputs straight back (only after Play Mode had set it; a
    // recompile split them into separate copies, which is why that fixed it).
    [NonSerialized]
    private InputActionMap _currentActionMap;

    // The default controller's input (every device; see Controller Possession): read from the Input Actions themselves.
    // Once the game sets up its own players, each Player Controller reads its own copy of the map instead.
    [NonSerialized]
    private InputReader _defaultReader;

    /// <summary>The name of the Input Map in use (each player can use another: Set Input Map).</summary>
    public string CurrentMapName => _currentActionMap?.Name;

    /// <summary>The Input Map with this name, or null.</summary>
    public InputActionMap FindMap(string mapName)
    {
        if (InputMaps == null)
            return null;
        foreach (var candidate in InputMaps)
            if (candidate != null && candidate.Name == mapName)
                return candidate;
        return null;
    }

    public void InitializeInput()
    {
        // The default controller: everything answers to it until the game sets up its own players.
        var defaultController = PlayerController.EnsureDefault();
        _defaultReader = new InputReader(defaultController);
        Possession.SeparationStarted -= OnPlayersSeparated;
        Possession.SeparationStarted += OnPlayersSeparated;

        if (InputMaps != null && InputMaps.Length > 0 && InputMaps[0] != null)
        {
            SetInputMap(InputMaps[0].Name);
        }
        else
            Debug.LogWarning("Remedy Input: there are no Input Maps, so no input is read. Add one in the Schematic window's Global tab (Input System).");

        // A Singleton reacting to On Device Connected sets the players up itself: told about every device now.
        PlayerDevices.Start();
    }

    // The game set up its own players: the default controller stops reading (each player reads their own devices).
    private void OnPlayersSeparated()
    {
        UnbindCurrentMap();
        if (_currentActionMap != null)
            PlayerController.RebuildAll();
    }

    /// <summary>
    /// Switches every player to an Input Map (one that follows the game's map; see <see cref="PlayerController.SetInputMap"/>
    /// for one player alone).
    /// </summary>
    public void SetInputMap(string mapName)
    {
        var map = FindMap(mapName);
        if (map == null)
        {
            Debug.LogError("The Requested Input Map does not exist: " + mapName);
            return;
        }
        UnbindCurrentMap();
        _currentActionMap = map;

        foreach (var inputEventCollection in _currentActionMap.Inputs)
        {
            var action = inputEventCollection?.Input;
            if (action == null)
                continue;

            InputActionEvent.ForgetStaleBindings(action);
            if (action.bindings.Count == 0)
                Debug.LogWarning($"Remedy Input: the input \"{inputEventCollection.Name}\" of map \"{map.Name}\" has no bindings, so it can't be pressed. Add one in the Schematic window's Global tab (Input System).");

            WarnIfDigital(inputEventCollection, map);

            if (inputEventCollection.Output == null)
                Debug.LogWarning($"Remedy Input: the input \"{inputEventCollection.Name}\" of map \"{map.Name}\" has no Signal, so nothing hears it. Open it in the Schematic window's Global tab (Input System) to create one.");

            // Separated players read their own copies of the map (below), not these actions.
            if (Possession.Separated || _defaultReader == null || _defaultReader.IsBound(action))
                continue;
            _defaultReader.Bind(action, inputEventCollection);
            action.Enable();
        }

        if (Possession.Separated)
            PlayerController.RebuildAll();
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
        // And in Edit Mode, an action something turned on again is turned off (and said so, once): while it's on it
        // can't be edited.
        double next = 0;
        UnityEditor.EditorApplication.update += () =>
        {
            if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || UnityEditor.EditorApplication.timeSinceStartup < next)
                return;
            next = UnityEditor.EditorApplication.timeSinceStartup + 0.5;
            foreach (var input in Resources.FindObjectsOfTypeAll<RemedyInput>())
                if (input.AnyActionEnabled())
                {
                    if (!_warnedEnabledInEditMode)
                    {
                        _warnedEnabledInEditMode = true;
                        Debug.LogWarning("Remedy Input: an Input Action was on in Edit Mode (something enabled it outside Play Mode), so it couldn't be edited. It's been turned off.");
                    }
                    input.ReleaseAllActions();
                }
        };
    }

    private static bool _warnedEnabledInEditMode;

    private bool AnyActionEnabled()
    {
        if (InputMaps == null)
            return false;
        foreach (var map in InputMaps)
            if (map?.Inputs != null)
                foreach (var input in map.Inputs)
                    if (InputActionEvent.IsEnabled(input?.Input))
                        return true;
        return false;
    }

    private void ReleaseAllActions()
    {
        UnbindCurrentMap();
        if (InputMaps == null)
            return;
        foreach (var map in InputMaps)
            if (map?.Inputs != null)
                foreach (var input in map.Inputs)
                {
                    var action = input?.Input;
                    if (action == null)
                        continue;
                    if (InputActionEvent.IsEnabled(action))
                        action.Disable();
                    InputActionEvent.ForgetRuntimeState(action);
                }
    }
#endif

    private void UnbindCurrentMap()
    {
        // Detaches the actions, turns them off and releases anything held (Disable asks enabled, which throws on a map
        // with no state, so the reader checks first).
        _defaultReader?.Unbind(disable: true);
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

        // Held inputs are sent again every frame, by every controller reading them.
        _defaultReader?.Update();
        PlayerController.UpdateAll();

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
        [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 1, foldoutTitle: "Inputs", itemLabel: "Input", renamable: true, Flat = true)]
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

        /// <summary>An action's bindings as saved (for building each player's own copy of it).</summary>
        internal static InputBinding[] SavedBindings(InputAction action) => SavedBindingsOf(action);

        private static readonly System.Reflection.FieldInfo _hiddenMap = typeof(InputAction).GetField("m_ActionMap",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        /// <summary>
        /// Drops a standalone action's hidden action map when it holds other bindings than the saved ones (it was made
        /// before they were edited, e.g. in an earlier Play session with domain reload off), so the action is rebuilt
        /// from what's saved when it's enabled. Only for disabled actions.
        /// </summary>
        /// <summary>
        /// Drops a disabled standalone action's hidden action map whatever it holds (the copy of its bindings made when
        /// it was enabled, and its state), so it's made again from the saved bindings the next time it's needed: after
        /// Play Mode, as a recompile would.
        /// </summary>
        /// <summary>
        /// Whether an action is on, safely: a standalone action without its hidden action map (never used, or dropped by
        /// <see cref="ForgetRuntimeState"/>) is off, and asking InputAction.enabled then would throw.
        /// </summary>
        internal static bool IsEnabled(InputAction action)
        {
            if (action == null)
                return false;
            // Asked of its (hidden) action map, whose enabled is a plain count: InputAction.enabled asserts and throws
            // when the map has no state yet ("Action map must have state at this point"), as one made just to read
            // its bindings doesn't.
            if (_hiddenMap != null)
                return _hiddenMap.GetValue(action) is UnityEngine.InputSystem.InputActionMap map && map.enabled;
            return action.actionMap != null && action.actionMap.enabled;
        }

        internal static void ForgetRuntimeState(InputAction action)
        {
            if (action == null || _hiddenMap == null || IsEnabled(action))
                return;
            _hiddenMap.SetValue(action, null);
            // Its place in the old map's state too, back to "none", as a freshly loaded action has: left pointing into
            // a state that's gone, the next Enable (the next Play Mode) asserted "Action map must have state".
            foreach (var (field, fresh) in _runtimeFields)
                field?.SetValue(action, fresh);
        }

        // InputAction's [NonSerialized] runtime fields and the values a freshly loaded action has.
        private static readonly (System.Reflection.FieldInfo field, object fresh)[] _runtimeFields =
        {
            (RuntimeField("m_ActionIndexInState"), -1), // (InputActionState.kInvalidIndex)
            (RuntimeField("m_BindingsStartIndex"), 0),
            (RuntimeField("m_BindingsCount"), 0),
            (RuntimeField("m_ControlStartIndex"), 0),
            (RuntimeField("m_ControlCount"), 0),
        };

        private static System.Reflection.FieldInfo RuntimeField(string name)
            => typeof(InputAction).GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        internal static void ForgetStaleBindings(InputAction action)
        {
            if (action == null || _hiddenMap == null || _hiddenMap.GetValue(action) == null || IsEnabled(action))
                return;
            var saved = SavedBindingsOf(action);
            var current = action.bindings;
            bool same = current.Count == saved.Length;
            for (int i = 0; same && i < saved.Length; i++)
                same = current[i].path == saved[i].path && current[i].isComposite == saved[i].isComposite
                    && current[i].isPartOfComposite == saved[i].isPartOfComposite;
            if (!same)
                ForgetRuntimeState(action);
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