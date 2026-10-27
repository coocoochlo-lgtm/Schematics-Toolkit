using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;

/// <summary>
/// Reads one controller's Input Actions and turns them into its input: each press, change and release of an input
/// sends that input's Signal through the controller (so only the objects it possesses hear it), and held inputs are
/// sent again every frame, as Remedy Input always has.
/// </summary>
internal sealed class InputReader
{
    private readonly Controller _controller;

    // The handlers attached to each Input Action, kept so they can be removed again.
    private readonly Dictionary<InputAction, (Action<InputAction.CallbackContext> started, Action<InputAction.CallbackContext> performed, Action<InputAction.CallbackContext> canceled)> _bound = new();
    // Inputs held right now, and the latest value of each, resent every frame by Update.
    private readonly Dictionary<InputAction, List<RemedyInput.InputActionEvent>> _held = new();
    private readonly Dictionary<SignalData, Union[]> _current = new();

    public InputReader(Controller controller) => _controller = controller;

    public bool IsBound(InputAction action) => action != null && _bound.ContainsKey(action);

    /// <summary>Reads an Input Action as an input: its Signal is sent when it's pressed, changes, and is released.</summary>
    public void Bind(InputAction action, RemedyInput.InputActionEvent input)
    {
        if (action == null || input == null || _bound.ContainsKey(action))
            return;

        Action<InputAction.CallbackContext> started = context =>
        {
            Send(input, context);
            if (!input.OneShot)
            {
                if (!_held.TryGetValue(action, out var held))
                    _held.Add(action, held = new());
                if (!held.Contains(input))
                    held.Add(input);
            }
        };
        Action<InputAction.CallbackContext> performed = context => Send(input, context);
        Action<InputAction.CallbackContext> canceled = context =>
        {
            _held.Remove(action);
            Release(input);
        };

        action.started += started;
        action.performed += performed;
        action.canceled += canceled;
        _bound[action] = (started, performed, canceled);
    }

    /// <summary>
    /// Detaches every Input Action and releases what's held (its Signals get defaults), so nothing stays "pressed".
    /// The actions are turned off too when <paramref name="disable"/> says so.
    /// </summary>
    public void Unbind(bool disable)
    {
        foreach (var entry in _bound)
        {
            var action = entry.Key;
            action.started -= entry.Value.started;
            action.performed -= entry.Value.performed;
            action.canceled -= entry.Value.canceled;
            if (disable && RemedyInput.InputActionEvent.IsEnabled(action)) action.Disable();
        }
        _bound.Clear();

        foreach (var held in _held.Values)
            foreach (var input in held)
                Release(input);
        _held.Clear();
        _current.Clear();
    }

    /// <summary>Sends every held input's latest value again; releases inputs whose actions stopped.</summary>
    public void Update()
    {
        if (_held.Count == 0)
            return;
        foreach (var entry in new List<KeyValuePair<InputAction, List<RemedyInput.InputActionEvent>>>(_held))
        {
            var action = entry.Key;
            if (action.phase != InputActionPhase.Started && action.phase != InputActionPhase.Performed)
            {
                _held.Remove(action);
                foreach (var input in entry.Value)
                    Release(input);
                continue;
            }
            foreach (var input in entry.Value)
            {
                var output = input.Output;
                if (output != null && _current.TryGetValue(output, out var args))
                    _controller.SendInput(output, args);
            }
        }
    }

    private void Release(RemedyInput.InputActionEvent input)
    {
        var output = input.Output;
        if (output == null)
            return;
        _current.Remove(output);
        // Released: every listener gets its parameter's default (false, 0, Vector2.zero...).
        _controller.ReleaseInput(output);
    }

    /// <summary>
    /// Sends an input's value through its Signal, read as the Signal's parameter type. Signals carry any value, so
    /// types without a case of their own are sent as whatever the Input System reads.
    /// </summary>
    private void Send(RemedyInput.InputActionEvent input, InputAction.CallbackContext context)
    {
        var output = input.Output;
        if (output == null)
            return;

        if (output.Parameters.Contents.Count == 0)
        {
            _controller.SendInput(output);
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
                Union.ValueType.Quaternion => context.ReadValue<UnityEngine.Quaternion>(),
                Union.ValueType.Vector2 => context.ReadValue<UnityEngine.Vector2>(),
                Union.ValueType.Vector3 => context.ReadValue<UnityEngine.Vector3>(),
                _ => context.ReadValueAsObject(),
            };
        }
        catch (InvalidOperationException)
        {
            // The binding gives another type than the Signal's parameter (e.g. a stick into a float): send what it
            // reads instead of throwing inside the Input System's callback, and listeners convert it.
            value = context.ReadValueAsObject();
        }

        var args = new[] { SignalArgs.ToUnion(value) };
        _current[output] = args;
        _controller.SendInput(output, args);
    }
}
