// project armada

#pragma warning disable 0414

using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    // Function Graphs called like nodes: Call (in any logic graph) starts the function's On Called with its inputs,
    // on the calling object, and carries on once the function's Return hands back its outputs.

    /// <summary>
    /// One call of a Function Graph, from a Call node: the values going in, the values its Return gives back, and how to
    /// carry the caller on if the function waits before returning. The function's flows carry it on their scope, so
    /// Return finds it even after a Wait.
    /// </summary>
    public sealed class FunctionCall
    {
        public readonly FunctionGraph Graph;
        public readonly GameObject Instance;
        public readonly Dictionary<string, Union> Inputs = new(StringComparer.Ordinal);
        public readonly Dictionary<string, Union> Results = new(StringComparer.Ordinal);

        /// <summary>
        /// This call's own values for the function's Variables (a GraphVariableStore, made by the Variable nodes the
        /// first time they're used in the call). Every call starts fresh, and calls running at once (one waiting, or
        /// a function calling itself) each keep their own.
        /// </summary>
        public object Variables;

        /// <summary>The call of a Function Graph the running flow is in (the nearest, if it's nested), or null outside one.</summary>
        public static FunctionCall Current(SchematicGraph graph) => Flow.Current?.ExecutingScope?.FindCall(graph);

        /// <summary>Whether its Return has run (only the first one counts).</summary>
        public bool Returned { get; private set; }

        internal CallFunctionNode Caller;
        internal FlowScope ResumeScope;
        internal bool Waiting;
        internal bool HadInstance;

        public FunctionCall(FunctionGraph graph, GameObject instance)
        {
            Graph = graph;
            Instance = instance;
            HadInstance = instance != null;
        }

        /// <summary>Called by Return: the outputs are in; if the caller is waiting (the function waited), it carries on.</summary>
        internal void Complete()
        {
            if (Returned)
                return;
            Returned = true;
            if (!Waiting)
                return;
            Waiting = false;
            if (HadInstance && Instance == null)
                return;
            Caller?.Resume(this);
        }
    }

    /// <summary>Helpers shared by the function nodes.</summary>
    internal static class FunctionPins
    {
        /// <summary>
        /// A pin's value when nothing's wired in: what's set on the node, else the pin's default (as its type).
        /// </summary>
        public static Union Default(FunctionPin pin, List<FunctionArgument> arguments)
        {
            var value = Value(pin, arguments);
            return value != null ? PortValues.ToUnion(value) : Union.Null;
        }

        /// <summary>The value set on the node for a pin (of its type), else the pin's default.</summary>
        public static object Value(FunctionPin pin, List<FunctionArgument> arguments)
        {
            if (pin == null)
                return null;
            var type = pin.ValueType;
            if (pin.IsNumber)
            {
                float number = FunctionArguments.Get(arguments, pin.AsParameter());
                if (type == typeof(int))
                    return Mathf.RoundToInt(number);
                if (type == typeof(bool))
                    return number >= 0.5f;
                return number;
            }
            return FunctionArguments.TryGetObject(arguments, pin.Name, type, out var value) ? value : pin.DefaultValue;
        }

        /// <summary>Keeps a value set on the node for a pin.</summary>
        public static void SetValue(FunctionPin pin, List<FunctionArgument> arguments, object value)
        {
            if (pin == null || arguments == null)
                return;
            if (pin.IsNumber)
            {
                float number = value switch { float f => f, int i => i, bool b => b ? 1f : 0f, _ => 0f };
                FunctionArguments.Set(arguments, pin.AsParameter(), number);
            }
            else
                FunctionArguments.SetObject(arguments, pin.Name, pin.ValueType, value);
        }

        public static FunctionGraph GraphOf(Node node) => node.Graph as FunctionGraph;
    }

    /// <summary>
    /// A node with values set right on its pins (a function's inputs, Return's outputs): numbers drawn with each pin's
    /// control, other types (vectors, colours, strings, objects) with a value field.
    /// </summary>
    public interface IInlinePinValues
    {
        /// <summary>The function pin behind a pin's name, if it can be set on the node.</summary>
        bool InlinePin(string pin, out FunctionPin functionPin);
        object InlineValue(FunctionPin pin);
        void SetInlineValue(FunctionPin pin, object value);
    }

    /// <summary>
    /// Where a call starts in a Function Graph: its inputs come out of it, as the Call node calling it gave them.
    /// </summary>
    [SchematicDoc(@"{""name"": ""On Called"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Function"", ""description"": ""Starts a Function Graph's flow when a Call node in another graph calls it. Its pins are the function's inputs (set in the IO Dock's Graph tab, under Function), with the values the call gave. The function runs on the calling object (Self is the caller). Every Function Graph has one."", ""use_for"": [""the start of a reusable routine""], ""notes"": ""Starts a flow: connect its arrow to what the function does, ending in Return to hand values back.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Called", Path = "Function", Deletable = false), Tags(FunctionGraph.EntryOnlyTag)]
    public sealed class OnCalledNode : SchematicEventNode, IDynamicPorts
    {
        public bool SyncPorts()
        {
            var graph = FunctionPins.GraphOf(this);
            var pins = graph != null ? graph.Function.Inputs.Where(p => p != null).Select(p => (p.Name, p.ValueType ?? typeof(object))) : Enumerable.Empty<(string, Type)>();
            return DynamicPorts.SyncOutputs(this, pins, port => port.Type != typeof(ActionPort));
        }

        public override void OnValidate()
        {
            if (SyncPorts())
                DynamicPorts.Notify(this);
            base.OnValidate();
        }

        protected override void OnTrigger(GameObject instance, params Union[] arguments)
        {
            var graph = FunctionPins.GraphOf(this);
            if (graph == null)
                return;
            var call = Flow.Current?.ExecutingScope?.FindCall();
            foreach (var pin in graph.Function.Inputs)
                if (pin != null)
                    SetOutputValue(pin.Name, call != null && call.Inputs.TryGetValue(pin.Name, out var given) ? given : FunctionPins.Default(pin, null));
        }
    }

    /// <summary>
    /// Ends a call of a Function Graph: hands its outputs back to the Call node, which carries on from there.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Return"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Function"", ""description"": ""Ends a call of this Function Graph: its pins are the function's outputs (set in the IO Dock's Graph tab, under Function), and what's wired into them (or set on them) goes back to the Call node, which carries on. Only the first Return a call reaches counts. A function with a Return can wait (Wait, a delay) before it: the caller carries on when it's reached."", ""use_for"": [""handing a function's results back"", ""finishing a function that waits""], ""notes"": ""Runs when flow reaches it. Without a Return, a call carries straight on once the function has started.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "Return", Path = "Function"), Tags(FunctionGraph.Tag)]
    public sealed class ReturnNode : SchematicActionNodeBase, IDynamicPorts, IInlinePinValues
    {
        [SerializeField, HideInInspector] private List<FunctionArgument> _arguments = new();

        public bool SyncPorts()
        {
            var graph = FunctionPins.GraphOf(this);
            var pins = graph != null ? graph.Function.Outputs.Where(p => p != null).Select(p => (p.Name, p.ValueType ?? typeof(object))) : Enumerable.Empty<(string, Type)>();
            return DynamicPorts.SyncInputs(this, pins, port => port.Type != typeof(ActionPort));
        }

        public override void OnValidate()
        {
            if (SyncPorts())
                DynamicPorts.Notify(this);
            base.OnValidate();
        }

        public bool InlinePin(string pin, out FunctionPin functionPin)
        {
            functionPin = FunctionPins.GraphOf(this)?.Function.Outputs.FirstOrDefault(p => p != null && p.Name == pin && p.HasInlineValue);
            return functionPin != null;
        }

        public object InlineValue(FunctionPin pin) => FunctionPins.Value(pin, _arguments ??= new());

        public void SetInlineValue(FunctionPin pin, object value) => FunctionPins.SetValue(pin, _arguments ??= new(), value);

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var graph = FunctionPins.GraphOf(this);
            var call = Flow.Current?.ExecutingScope?.FindCall();
            if (graph == null || call == null || call.Returned)
                return;
            foreach (var pin in graph.Function.Outputs)
            {
                if (pin == null)
                    continue;
                call.Results[pin.Name] = TryGetInputValue<Union>(pin.Name, out var value) ? value : FunctionPins.Default(pin, _arguments);
            }
            call.Complete();
        }
    }

    /// <summary>
    /// Calls a Function Graph like a node: its inputs are this node's pins (values set right on them when nothing's
    /// wired in), it runs on this object, and its outputs come out here once it returns; the flow carries on then.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Call"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Function"", ""description"": ""Calls a Function Graph (Function) as a node. Its inputs are pins here: wire values in, or set them right on the node (numbers with the controls the function gave them: sliders, toggles, dropdowns; vectors, colours, text and objects with a value field). The function runs on this object, from its On Called, and its outputs come out here when it reaches Return; the flow carries on from this node then: straight away, or once the function's Waits are over. Function Graphs marked In Library are in the node search under Functions; dropping one into a graph makes a Call. Double-click to open it."", ""use_for"": [""reusing a routine across graphs (damage, spawning, a dialogue line)"", ""a library of gameplay building blocks with their own inputs and outputs"", ""splitting a big graph into named steps""], ""notes"": ""Runs when flow reaches it. A function calling itself is allowed (recursion) up to 64 calls deep.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Call", Path = "Functions"), Tags("Default")]
    [AssetNode(typeof(FunctionGraph), Title = "{0}", Field = nameof(Calls))]
    public sealed class CallFunctionNode : SchematicActionNode, IDynamicPorts, IInlinePinValues
    {
        /// <summary>How deep calls may nest (a function calling itself), so a runaway recursion stops with an error.</summary>
        public const int MaxDepth = 64;

        [ThreadStatic] private static int _depth;

        [Editable("Function")] public FunctionGraph Calls;
        [SerializeField, HideInInspector] private List<FunctionArgument> _arguments = new();

        /// <summary>The pin for one of the function's outputs: its name, unless an input has it.</summary>
        public string OutputPin(FunctionPin pin)
            => Calls != null && Calls.Function.Inputs.Any(i => i != null && i.Name == pin.Name) ? pin.Name + " (Result)" : pin.Name;

        public bool SyncPorts()
        {
            var inputs = Enumerable.Empty<(string, Type)>();
            var outputs = Enumerable.Empty<(string, Type)>();
            if (Calls != null)
            {
                inputs = Calls.Function.Inputs.Where(p => p != null).Select(p => (p.Name, p.ValueType ?? typeof(object))).ToList();
                outputs = Calls.Function.Outputs.Where(p => p != null).Select(p => (OutputPin(p), p.ValueType ?? typeof(object))).ToList();
            }
            bool changed = DynamicPorts.SyncInputs(this, inputs, port => port.Type != typeof(ActionPort));
            changed |= DynamicPorts.SyncOutputs(this, outputs, port => port.Type != typeof(ActionPort));
            return changed;
        }

        public override void OnValidate()
        {
            if (Calls != null && (string.IsNullOrEmpty(Name) || Name == "Call"))
                Name = Calls.name;
            if (SyncPorts())
                DynamicPorts.Notify(this);
            base.OnValidate();
            Warning = Calls == null ? "No Function Graph set: this does nothing."
                : Calls.Entry == null ? "Its Function Graph has no On Called: the call does nothing."
                : null;
        }

        public bool InlinePin(string pin, out FunctionPin functionPin)
        {
            functionPin = Calls?.Function.Inputs.FirstOrDefault(p => p != null && p.Name == pin && p.HasInlineValue);
            return functionPin != null;
        }

        public object InlineValue(FunctionPin pin) => FunctionPins.Value(pin, _arguments ??= new());

        public void SetInlineValue(FunctionPin pin, object value) => FunctionPins.SetValue(pin, _arguments ??= new(), value);

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            if (Calls == null)
                return;
            var entry = Calls.Entry;
            if (entry == null)
            {
                Debug.LogWarning($"[{Name}] \"{Calls.name}\" has no On Called to start from.", instance);
                return;
            }
            if (_depth >= MaxDepth)
            {
                Debug.LogError($"[{Name}] Calls of \"{Calls.name}\" nested more than {MaxDepth} deep: a function calling itself without stopping?", instance);
                _processChildren = false;
                return;
            }

            var call = new FunctionCall(Calls, instance) { Caller = this };
            foreach (var pin in Calls.Function.Inputs)
                if (pin != null)
                    call.Inputs[pin.Name] = TryGetInputValue<Union>(pin.Name, out var value) ? value : FunctionPins.Default(pin, _arguments);

            _depth++;
            try
            {
                Flow.Start(entry, instance, null, new FlowScope { Call = call });
            }
            finally
            {
                _depth--;
            }

            if (call.Returned)
            {
                WriteResults(call, null);
                return;
            }
            if (Calls.HasReturn)
            {
                // It waited (or stopped at a breakpoint) before its Return: the flow carries on from here when it's reached.
                _processChildren = false;
                call.ResumeScope = Flow.Current?.Capture();
                call.Waiting = true;
            }
        }

        // The function's outputs onto this node's pins: for this flow (or the one carrying on later) and as this object's
        // latest values.
        private void WriteResults(FunctionCall call, FlowScope scope)
        {
            foreach (var pin in call.Graph.Function.Outputs)
            {
                if (pin == null)
                    continue;
                var value = call.Results.TryGetValue(pin.Name, out var result) ? result : FunctionPins.Default(pin, null);
                string port = OutputPin(pin);
                SetOutputValue(port, value);
                scope?.Set(this, port, value);
            }
        }

        // The function returned after waiting: the nodes after this one run now, with its outputs.
        internal void Resume(FunctionCall call)
        {
            var scope = call.ResumeScope ?? new FlowScope();
            WriteResults(call, scope);
            Flow.StartChildren(this, call.Instance, scope);
        }
    }
}
