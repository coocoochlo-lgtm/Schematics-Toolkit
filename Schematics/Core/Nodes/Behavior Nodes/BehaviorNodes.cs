using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Which Behavior a graph node acts on: a Behavior type, attached to the Schematic Object's root or to one of its
    /// children (by path from the root). Signals don't need this (they reach the whole object by its UID), but
    /// Variables and methods live on one particular instance.
    /// </summary>
    [Serializable]
    public class BehaviorTarget
    {
        public SerializableType Behavior;
        [Tooltip("Path of the GameObject from the Schematic Object's root. Empty for the root itself.")]
        public string Path = "";

        // Graph assets are shared by every spawned object, so resolved instances are cached per object.
        [NonSerialized] private Dictionary<GameObject, BehaviorInstance> _resolved;

        public Type Type => Behavior.Type;

        public BehaviorTarget() { }

        public BehaviorTarget(Type behavior, string path)
        {
            Behavior = behavior;
            Path = path ?? "";
        }

        /// <summary>
        /// The instance on the spawned object running the graph. Null if that object doesn't have the Behavior there.
        /// </summary>
        public BehaviorInstance Resolve(GameObject root)
        {
            var type = Type;
            if (root == null || type == null)
                return null;

            _resolved ??= new Dictionary<GameObject, BehaviorInstance>();
            if (_resolved.TryGetValue(root, out var cached) && cached != null && cached.IsSpawned && cached.Owner != null && cached.Owner.gameObject == root)
                return cached;

            var target = string.IsNullOrEmpty(Path) ? root.transform : root.transform.Find(Path);
            var instance = target != null ? ExecutionContext.FindInstance(target.gameObject, type) : null;
            if (instance != null)
                _resolved[root] = instance;
            else
                _resolved.Remove(root);
            return instance;
        }

        public string Describe()
        {
            var type = Type;
            string name = type != null ? type.Name : "(missing Behavior)";
            return string.IsNullOrEmpty(Path) ? name : $"{name} on {Path}";
        }
    }

    /// <summary>
    /// Base for nodes that act on one Behavior instance of the object running the graph.
    /// </summary>
    [Serializable]
    public abstract class BehaviorNodeBase : SchematicActionNode
    {
        [HideInInspector]
        public BehaviorTarget Target = new();

        protected BehaviorInstance ResolveInstance(GameObject instance)
        {
            var resolved = Target.Resolve(instance);
            if (resolved == null)
                Debug.LogWarning($"[{Name}] {Target.Describe()} isn't on {(instance != null ? instance.name : "this object")}.", instance);
            return resolved;
        }

        /// <summary>Adds a data port if it's missing or has the wrong type. Used by the editor when building the node.</summary>
        public void EnsurePort(string name, Type type, PortDirection direction)
        {
            var existing = GetPort(name);
            if (existing != null)
            {
                if (existing.Type == type && existing.Direction == direction)
                    return;
                RemovePort(existing);
            }
            AddPort(new Port
            {
                Name = name,
                Type = type ?? typeof(object),
                Direction = direction,
                Capacity = direction == PortDirection.Input ? PortCapacity.Single : PortCapacity.Multiple,
            });
        }

        /// <summary>Removes every data port not in the given set (flow ports are kept).</summary>
        public void RemovePortsExcept(ICollection<string> keep)
        {
            var remove = new List<Port>();
            foreach (var port in Ports.Values)
                if (port.Type != typeof(ActionPort) && !keep.Contains(port.Name))
                    remove.Add(port);
            foreach (var port in remove)
                RemovePort(port);
        }
    }

    /// <summary>
    /// Reads a Behavior Variable of the object running the graph. Created by dragging a Variable from the IO Dock.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Get Variable"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Behaviors"", ""description"": ""Reads a Behavior Variable of the object running the graph. Created by dragging a Variable from the IO Dock."", ""use_for"": [""read a Behavior's state (health, speed, is grounded)"", ""feed values into conditions and math""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Made by dragging a Variable from the IO Dock and choosing Get. Pure value node: no flow needed.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Get Variable", Path = "Schematic/Behaviors"), Tags("Object")]
    public class GetBehaviorVariableNode : BehaviorNodeBase
    {
        public const string ValuePort = "Value";

        [HideInInspector]
        public string Variable;

        [NonSerialized] private BehaviorRuntime _runtime;
        [NonSerialized] private int _index = -1;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var behavior = ResolveInstance(instance);
            if (behavior == null)
                return;

            if (_runtime != behavior.Runtime)
            {
                _runtime = behavior.Runtime;
                _index = _runtime.IndexOfVariable(Variable);
            }
            if (_index < 0)
                return;

            SetOutputValue(ValuePort, SignalArgs.ToUnion<object>(_runtime.GetVariableBoxed(behavior, _index)));
        }

        // A Variable can be read any time, so connected inputs get its current value even when flow never passed here.
        protected override bool AnswersOnRequest => true;

        public override object OnRequestValue(Port port)
        {
            var instance = RunningInstance;
            var behavior = instance != null ? Target.Resolve(instance) : null;
            if (behavior == null)
                return null;

            if (_runtime != behavior.Runtime)
            {
                _runtime = behavior.Runtime;
                _index = _runtime.IndexOfVariable(Variable);
            }
            return _index >= 0 ? _runtime.GetVariableBoxed(behavior, _index) : null;
        }
    }

    /// <summary>
    /// Writes a Behavior Variable of the object running the graph. Created by dragging a Variable from the IO Dock.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Set Variable"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Behaviors"", ""description"": ""Writes a Behavior Variable of the object running the graph. Created by dragging a Variable from the IO Dock."", ""use_for"": [""change a Behavior's settings or state at runtime (speed boosts, toggles)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Made by dragging a Variable from the IO Dock and choosing Set. An action: wire flow into it.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Set Variable", Path = "Schematic/Behaviors"), Tags("Object")]
    public class SetBehaviorVariableNode : BehaviorNodeBase
    {
        public const string ValuePort = "Value";

        [HideInInspector]
        public string Variable;

        [NonSerialized] private BehaviorRuntime _runtime;
        [NonSerialized] private int _index = -1;
        [NonSerialized] private Type _type;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var behavior = ResolveInstance(instance);
            if (behavior == null)
                return;

            if (_runtime != behavior.Runtime)
            {
                _runtime = behavior.Runtime;
                _index = _runtime.IndexOfVariable(Variable);
                _type = BehaviorReflection.Get(Target.Type)?.FindVariableType(Variable);
            }
            if (_index < 0 || _type == null)
                return;

            var value = SignalArgs.FromUnion(GetInputValue<Union>(ValuePort, Union.Null), _type);
            _runtime.SetVariableBoxed(behavior, _index, value);
        }
    }

    /// <summary>
    /// A reference to one of the object's Behaviors, pinned out: every Variable as an output, its Enabled state,
    /// and optionally one of its public methods to call (with a pin per parameter and one for the result).
    /// Created by dragging a Behavior from the IO Dock. Reflection is done once per Behavior type and cached.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Behavior"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic/Behaviors"", ""description"": ""A reference to one of the object's Behaviors, pinned out: every Variable as an output, its Enabled state, and optionally one of its public methods to call (with a pin per parameter and one for the result). Created by dragging a Behavior from the IO Dock. Reflection is done once per Behavior type and cached."", ""use_for"": [""read several Variables of one Behavior at once"", ""enable or disable a Behavior""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Behavior", Path = "Schematic/Behaviors"), Tags("Object")]
    public class BehaviorReferenceNode : BehaviorNodeBase
    {
        public const string EnabledPort = "Enabled";
        public const string SetEnabledPort = "Set Enabled";
        public const string ResultPort = "Result";

        [Tooltip("The public method called when this node runs, as Name(ParamType,...). Empty to only read Variables.")]
        [HideInInspector]
        public string Method;

        [Tooltip("When on, the 'Set Enabled' input enables or disables the Behavior each time this node runs.")]
        [HideInInspector]
        public bool ControlsEnabled;

        [NonSerialized] private BehaviorReflection _reflection;
        [NonSerialized] private BehaviorReflection.MethodEntry _method;
        [NonSerialized] private object[] _arguments;
        [NonSerialized] private int[] _variableIndices;
        [NonSerialized] private BehaviorRuntime _runtime;

        public BehaviorReflection Reflection => _reflection ??= BehaviorReflection.Get(Target.Type);

        /// <summary>
        /// Rebuilds the data ports from the Behavior's Variables and the chosen method. Called by the editor.
        /// </summary>
        public void RebuildPorts()
        {
            _reflection = null;
            _method = null;
            var reflection = Reflection;
            var keep = new HashSet<string>();
            if (reflection == null)
            {
                RemovePortsExcept(keep);
                return;
            }

            EnsurePort(EnabledPort, typeof(bool), PortDirection.Output);
            keep.Add(EnabledPort);
            if (ControlsEnabled)
            {
                EnsurePort(SetEnabledPort, typeof(bool), PortDirection.Input);
                keep.Add(SetEnabledPort);
            }

            foreach (var variable in reflection.Variables)
            {
                EnsurePort(variable.Name, variable.Type, PortDirection.Output);
                keep.Add(variable.Name);
            }

            var method = reflection.FindMethod(Method);
            if (method != null)
            {
                foreach (var parameter in method.Inputs)
                {
                    string name = "(" + parameter.Name + ")";
                    EnsurePort(name, parameter.ParameterType, PortDirection.Input);
                    keep.Add(name);
                }
                if (method.HasResult)
                {
                    EnsurePort(ResultPort, method.ReturnType, PortDirection.Output);
                    keep.Add(ResultPort);
                }
            }

            RemovePortsExcept(keep);
            IsDirty = true;
        }

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var behavior = ResolveInstance(instance);
            var reflection = Reflection;
            if (behavior == null || reflection == null)
                return;

            if (ControlsEnabled && HasInputConnections(SetEnabledPort))
                behavior.SetEnabled(SignalArgs.FromUnion<bool>(GetInputValue<Union>(SetEnabledPort, Union.Null)));

            if (!string.IsNullOrEmpty(Method))
            {
                _method ??= reflection.FindMethod(Method);
                if (_method != null)
                    Call(behavior, _method);
                else
                    Debug.LogWarning($"[{Name}] {Target.Describe()} has no method {Method} anymore.", instance);
            }

            // Variables are read after the call, so the pins show its effect.
            if (_runtime != behavior.Runtime || _variableIndices == null)
            {
                _runtime = behavior.Runtime;
                _variableIndices = new int[reflection.Variables.Count];
                for (int i = 0; i < _variableIndices.Length; i++)
                    _variableIndices[i] = _runtime.IndexOfVariable(reflection.Variables[i].Name);
            }

            SetOutputValue(EnabledPort, new Union(behavior.Enabled));
            for (int i = 0; i < _variableIndices.Length; i++)
                if (_variableIndices[i] >= 0)
                    SetOutputValue(reflection.Variables[i].Name, SignalArgs.ToUnion<object>(_runtime.GetVariableBoxed(behavior, _variableIndices[i])));
        }

        private void Call(BehaviorInstance behavior, BehaviorReflection.MethodEntry method)
        {
            int offset = method.TakesInstance ? 1 : 0;
            int count = method.Inputs.Length + offset;
            if (_arguments == null || _arguments.Length != count)
                _arguments = new object[count];

            if (method.TakesInstance)
                _arguments[0] = behavior;
            for (int i = 0; i < method.Inputs.Length; i++)
            {
                var parameter = method.Inputs[i];
                string port = "(" + parameter.Name + ")";
                _arguments[i + offset] = HasInputConnections(port)
                    ? SignalArgs.FromUnion(GetInputValue<Union>(port, Union.Null), parameter.ParameterType)
                    : (parameter.HasDefaultValue ? parameter.DefaultValue : (parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null));
            }

            // Methods act on the selected instance; restore whatever was selected before.
            var schematicBehavior = behavior.Behavior;
            var previous = schematicBehavior.Selected;
            schematicBehavior.Select(behavior);
            try
            {
                var result = method.Method.Invoke(schematicBehavior, _arguments);
                if (method.HasResult)
                    SetOutputValue(ResultPort, SignalArgs.ToUnion<object>(result));
            }
            catch (System.Reflection.TargetInvocationException exception)
            {
                Debug.LogException(exception.InnerException ?? exception, behavior.GameObject);
            }
            finally
            {
                schematicBehavior.Select(previous);
            }
        }
    }

    /// <summary>
    /// A GameObject from the Schematic Object's hierarchy, found on the spawned object running the graph.
    /// Created by dragging a GameObject from the IO Dock's prefab hierarchy.
    /// </summary>
    [SchematicDoc(@"{""name"": ""Prefab Object"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Schematic"", ""description"": ""A GameObject from the Schematic Object's hierarchy, found on the spawned object running the graph. Created by dragging a GameObject from the IO Dock's prefab hierarchy."", ""outputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Transform_"", ""type"": ""Transform""}], ""use_for"": [""refer to a child object of this prefab (a muzzle, a hitbox, a model)""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next.""}")]
    [Serializable]
    [NodeColor(NodeColors.Schematic)]
    [Node(Name = "Prefab Object", Path = "Schematic"), Tags("Object")]
    public class PrefabObjectNode : SchematicActionNode
    {
        public const string ObjectPort = "Object";
        public const string TransformPort = "Transform";

        [Tooltip("Path of the GameObject from the Schematic Object's root. Empty for the root itself.")]
        [HideInInspector]
        public string Path = "";

        [Output(ObjectPort)] public GameObject Object;
        [Tooltip("Its Transform (position, rotation, scale).")]
        [Output(TransformPort)] public Transform Transform_;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = instance == null ? null : string.IsNullOrEmpty(Path) ? instance.transform : instance.transform.Find(Path);
            SetOutputValue(ObjectPort, target != null ? new Union(target.gameObject) : Union.Null);
            SetOutputValue(TransformPort, target != null ? new Union((Component)target) : Union.Null);
        }
    }
}
