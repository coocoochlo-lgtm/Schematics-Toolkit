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

    [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 0, foldoutTitle: "Input Maps", itemLabel: "Map")]
    public InputActionMap[] InputMaps = new InputActionMap[0];
    [SerializeField]
    //[Dropdown("GetInputMaps")]
    private InputActionMap _currentActionMap;
    private Dictionary<InputAction, List<InputActionEvent>> _actionsToUpdate = new();


    public void InitializeInput()
    {
        if (InputMaps.Length > 0)
        {
            SetInputMap(InputMaps[0].Name);
        }
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
        _currentActionMap = map;

        foreach (var inputEventCollection in _currentActionMap.Inputs)
        {
            inputEventCollection.Input.Enable();

            inputEventCollection.Input.started += (InputAction.CallbackContext context) =>
            {
                Send(inputEventCollection, context);

                if (!inputEventCollection.OneShot)
                {
                    if (!_actionsToUpdate.ContainsKey(inputEventCollection.Input))
                        _actionsToUpdate.Add(inputEventCollection.Input, new());
                    _actionsToUpdate[inputEventCollection.Input].Add(inputEventCollection);
                }
            };

            inputEventCollection.Input.performed += (InputAction.CallbackContext context) => Send(inputEventCollection, context);

            inputEventCollection.Input.canceled += (InputAction.CallbackContext context) =>
            {
                _actionsToUpdate.Remove(inputEventCollection.Input);

                var output = inputEventCollection.Output;
                if (output == null) return;
                _currentInput.Remove(output);
                // Released: every listener gets its parameter's default (false, 0, Vector2.zero...).
                output.BroadcastDefault();
            };
        }
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

        object value = output.Parameters[0].Type switch
        {
            Union.ValueType.Bool => context.ReadValueAsButton(),
            Union.ValueType.Float => context.ReadValue<float>(),
            Union.ValueType.Vector2 => context.ReadValue<Vector2>(),
            Union.ValueType.Vector3 => context.ReadValue<Vector3>(),
            _ => context.ReadValueAsObject(),
        };

        _currentInput[output] = value;
        output.Broadcast(value);
    }

    public void UpdateInputs()
    {
        if (_currentActionMap == null) return;

        foreach(var inputEvents in _actionsToUpdate.Values)
        {
            foreach (var inputEvent in inputEvents)
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
        [IdentityListRenderer(identifierType: ListIdentifierType.Name, identifierField: "Name", depth: 1, foldoutTitle: "Inputs", itemLabel: "Input")]
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

        [SignalContainerRenderer(typeof(SignalData), "EventName", false)]
        public SignalData Output;

        [Tooltip("If true, the Output Event is not called again on update, although it will be called if the value for the Input has been updated (InputActionPhase.Performed).")]
        public bool OneShot = false;

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