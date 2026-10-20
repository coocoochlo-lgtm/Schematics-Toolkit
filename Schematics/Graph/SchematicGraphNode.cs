using Remedy.Framework;
using BlueGraph;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// The base of every Schematic graph node. Besides being a BlueGraph node, it owns the graph's value plumbing: how an
    /// input finds its value, how outputs are stored, and what is cached to keep that fast. The model is written up in
    /// Schematics/Graph/VALUES.md; in short:
    /// <list type="bullet">
    /// <item>Connected to a query node (no flow arrows): the query's OnRequestValue runs, once per flow step at most.</item>
    /// <item>Connected to an action or event node: the value it last wrote with SetOutputValue, for the object running
    /// the graph (in this flow first, then that object's last value).</item>
    /// <item>Not connected: the inline value on the node, read fresh every time, so editing it works even in Play Mode.</item>
    /// </list>
    /// Only the wiring is cached (which output feeds which input, port lookups, flow children), never values.
    /// </summary>
    [Serializable]
    [BlueGraph.NodeColor(BlueGraph.NodeColors.Value)]
    public class SchematicGraphNode : BlueGraph.Node
    {
        public SchematicGraph Schematic => (SchematicGraph)Graph;
        public GameObject GameObject => Schematic.Prefab;
        public Transform Transform => GameObject.transform;

        /// <summary>
        /// The object running the graph right now (the spawned instance, not the prefab), or null outside of a flow.
        /// Query nodes use it: they have no OnTrigger to hand it to them.
        /// </summary>
        public GameObject RunningInstance => Flow.Current?.Instance;

        private Rigidbody _rigidBody;
        public Rigidbody RigidBody
        {
            get
            {
                if (_rigidBody == null)
                    _rigidBody = GameObject.GetComponent<Rigidbody>();
                return _rigidBody;
            }
        }

        [SerializeField, HideInInspector]
        private bool _breakpoint;

        [SerializeField, HideInInspector]
        private NodeNetworkMode _networkMode;

        /// <summary>In a networked graph: whether this node runs only where its flow runs, or on every peer.</summary>
        public NodeNetworkMode NetworkMode
        {
            get => _networkMode;
            set => _networkMode = value;
        }

        /// <summary>
        /// Stops a flow before this node runs (in the editor, during Play Mode). Toggled from the graph window.
        /// </summary>
        public bool Breakpoint
        {
            get => _breakpoint;
            set => _breakpoint = value;
        }

        #region Relations (cached wiring)

        /// <summary>One output feeding an input.</summary>
        private struct Source
        {
            public SchematicGraphNode Node;
            public Port Port;
        }

        private sealed class InputRelation
        {
            public int Index;
            public Source[] Sources;
        }

        /// <summary>
        /// Everything about this node's wiring the value lookups need, built once (and again when the node changes)
        /// so reading a value is a couple of dictionary hits rather than a walk over ports and connections.
        /// </summary>
        private sealed class Relations
        {
            public readonly Dictionary<string, InputRelation> Inputs = new();
            public readonly Dictionary<string, int> OutputIndexByName = new();
            public readonly Dictionary<Port, int> OutputIndexByPort = new();
            public string[] OutputNames = Array.Empty<string>();
            /// <summary>The nodes on the main flow output ("▶").</summary>
            public readonly List<SchematicActionNodeBase> Children = new();
            /// <summary>The nodes on each flow output, by its name (a branch's If/Else, Random's pins...).</summary>
            public readonly Dictionary<string, List<SchematicActionNodeBase>> ChildrenByPort = new();

            /// <summary>No flow arrows at all: a query (pure) node, evaluated on demand through OnRequestValue.</summary>
            public bool IsQuery;

            public int ConnectionSignature;
        }

        // Changes whenever a port or a connection is added or removed (editor-only staleness check).
        private int ConnectionSignature()
        {
            int signature = 0;
            if (Ports is Dictionary<string, Port> ports)
            {
                foreach (var port in ports.Values)
                    if (port != null)
                        signature = signature * 31 + port.ConnectionCount + 1;
            }
            return signature;
        }

        [NonSerialized] private Relations _relations;

        private Relations Wiring
        {
            get
            {
                if (_relations == null || IsDirty)
                    UpdateCaches();
#if UNITY_EDITOR
                // In the editor, wires can change while playing without this node being told; rebuild if they did.
                else if (_relations.ConnectionSignature != ConnectionSignature())
                    UpdateCaches();
#endif
                return _relations;
            }
        }

        public override void OnValidate()
        {
            UpdateCaches();
        }

        /// <summary>
        /// Rebuilds the cached wiring (inputs to their sources, output indices, flow children). Called when the node
        /// is validated or marked dirty, and lazily the first time a value is read (in builds nothing else calls it).
        /// </summary>
        public void UpdateCaches()
        {
            var relations = new Relations();
            var outputNames = new List<string>();
            bool hasFlowPort = false;
            int inputIndex = 0;

            foreach (var kvp in Ports)
            {
                var port = kvp.Value;
                if (port == null)
                    continue;

                if (port.Type == typeof(ActionPort))
                {
                    hasFlowPort = true;
                    if (port.Direction == PortDirection.Output)
                    {
                        var children = new List<SchematicActionNodeBase>();
                        foreach (var connectedPort in port.ConnectedPorts)
                            if (connectedPort?.Node is SchematicActionNodeBase child && !children.Contains(child))
                                children.Add(child);
                        relations.ChildrenByPort[port.Name] = children;
                        if (port.Name == MainFlowPort)
                            foreach (var child in children)
                                if (!relations.Children.Contains(child))
                                    relations.Children.Add(child);
                    }
                    continue;
                }

                if (port.Direction == PortDirection.Input)
                {
                    var sources = new List<Source>();
                    foreach (var connectedPort in port.ConnectedPorts)
                        if (connectedPort?.Node is SchematicGraphNode sourceNode)
                            sources.Add(new Source { Node = sourceNode, Port = connectedPort });
                    relations.Inputs[port.Name] = new InputRelation { Index = inputIndex++, Sources = sources.ToArray() };
                }
                else
                {
                    relations.OutputIndexByName[port.Name] = outputNames.Count;
                    relations.OutputIndexByPort[port] = outputNames.Count;
                    outputNames.Add(port.Name);
                }
            }

            relations.OutputNames = outputNames.ToArray();
            relations.IsQuery = !hasFlowPort;
            relations.ConnectionSignature = ConnectionSignature();
            _relations = relations;

            if (_memo == null || _memo.Length != outputNames.Count)
                _memo = new MemoSlot[outputNames.Count];

            IsDirty = false;
            OnCacheUpdate();
        }

        protected virtual void OnCacheUpdate() { }

        /// <summary>True when this node has no flow arrows: its outputs are computed on demand by OnRequestValue.</summary>
        public bool IsQueryNode => Wiring.IsQuery;

        /// <summary>The names of this node's data outputs (flow arrows excluded), in port order.</summary>
        protected internal IReadOnlyList<string> DataOutputNames => Wiring.OutputNames;

        /// <summary>The position of a data input among this node's data inputs (-1 if there's no such input).</summary>
        public int GetInputPort(string name)
            => Wiring.Inputs.TryGetValue(name, out var input) ? input.Index : -1;

        /// <summary>Returns true if the input port with the given name has anything connected to it.</summary>
        public bool HasInputConnections(string inputName)
            => Wiring.Inputs.TryGetValue(inputName, out var input) && input.Sources.Length > 0;

        #endregion

        #region Flow

        /// <summary>The name of the main flow output, which runs once a node is done.</summary>
        public const string MainFlowPort = "▶";

        private static readonly List<SchematicActionNodeBase> NoChildren = new();

        /// <summary>The action nodes this node leads to (on its main flow output).</summary>
        internal List<SchematicActionNodeBase> FlowChildren => Wiring.Children;

        /// <summary>The action nodes on one of this node's flow outputs (none if it has no such output).</summary>
        internal List<SchematicActionNodeBase> FlowChildrenOf(string port)
            => port == null ? Wiring.Children : Wiring.ChildrenByPort.TryGetValue(port, out var children) ? children : NoChildren;

        /// <summary>
        /// Runs this node as a step of a flow. True if its children should run after it. Plain value nodes aren't steps.
        /// </summary>
        internal virtual bool ExecuteInFlow(Flow flow, FlowScope scope, Union[] arguments) => false;

        /// <summary>Copies this node's output values from one scope to another.</summary>
        internal void CopyOutputs(FlowScope from, FlowScope to)
        {
            var names = Wiring.OutputNames;
            for (int i = 0; i < names.Length; i++)
                if (from.TryGet(this, names[i], out var value))
                    to.Set(this, names[i], value);
        }

        /// <summary>The values a flow's running node reads and writes (null outside of a flow).</summary>
        private static FlowScope CurrentScope => Flow.Current?.ExecutingScope;

        #endregion

        #region Outputs

        /// <summary>One output's last written value for one object.</summary>
        private struct OutputSlot
        {
            public Union Value;
            // The object the Union points at, held here too: a Union keeps references in raw memory the garbage
            // collector doesn't see, so this keeps a written object alive while the slot still refers to it.
            public object Reference;
            public bool Has;
        }

        /// <summary>A query output's result for one flow step.</summary>
        private struct MemoSlot
        {
            public int Stamp;
            public object Value;
            public bool HasValue;
            public bool Evaluating;
        }

        // Graph assets are shared by every object running them, so written values are kept per object.
        [NonSerialized] private Dictionary<GameObject, OutputSlot[]> _outputsByInstance;
        [NonSerialized] private OutputSlot[] _outputsWithoutInstance;
        // The last value written by anyone, for the debugger.
        [NonSerialized] private OutputSlot[] _lastOutputs;
        [NonSerialized] private MemoSlot[] _memo;

        private static readonly List<GameObject> _staleInstances = new();

        private OutputSlot[] SlotsFor(GameObject instance, bool hasInstance, bool create)
        {
            int count = Wiring.OutputNames.Length;
            OutputSlot[] slots;
            if (!hasInstance || ReferenceEquals(instance, null))
            {
                slots = _outputsWithoutInstance;
                if (slots == null || slots.Length < count)
                {
                    if (!create) return slots;
                    Array.Resize(ref _outputsWithoutInstance, count);
                    slots = _outputsWithoutInstance;
                }
                return slots;
            }

            _outputsByInstance ??= new Dictionary<GameObject, OutputSlot[]>();
            if (_outputsByInstance.TryGetValue(instance, out slots) && slots.Length >= count)
                return slots;
            if (!create)
                return slots;

            if (slots == null)
                PruneDestroyedInstances();
            Array.Resize(ref slots, count);
            _outputsByInstance[instance] = slots;
            return slots;
        }

        // Drops the values of destroyed objects once in a while (whenever the table has doubled), so a long session
        // spawning and destroying objects doesn't keep growing it.
        [NonSerialized] private int _pruneAt;
        private void PruneDestroyedInstances()
        {
            if (_outputsByInstance.Count < Mathf.Max(32, _pruneAt))
                return;
            _staleInstances.Clear();
            foreach (var key in _outputsByInstance.Keys)
                if (key == null)
                    _staleInstances.Add(key);
            for (int i = 0; i < _staleInstances.Count; i++)
                _outputsByInstance.Remove(_staleInstances[i]);
            _staleInstances.Clear();
            _pruneAt = Mathf.Max(32, _outputsByInstance.Count * 2);
        }

        /// <summary>
        /// Sets the value of the output port with the given name: for the flow running now (so the nodes after this one
        /// see it) and as the running object's latest value (for flows that read it later).
        /// </summary>
        public void SetOutputValue(string name, Union value)
        {
            var wiring = Wiring;
            value.LastUpdateTick = StampTick();
            CurrentScope?.Set(this, name, value);

            if (!wiring.OutputIndexByName.TryGetValue(name, out int index))
                return;

            var flow = Flow.Current;
            var slots = SlotsFor(flow?.Instance, flow != null, create: true);
            var slot = new OutputSlot { Value = value, Reference = PortValues.ReferenceOf(value), Has = true };
            slots[index] = slot;

            if (_lastOutputs == null || _lastOutputs.Length < wiring.OutputNames.Length)
                Array.Resize(ref _lastOutputs, wiring.OutputNames.Length);
            _lastOutputs[index] = slot;
        }

        /// <summary>Sets an output from any value (boxed values are stored as their natural Union kind).</summary>
        public void SetOutputObject(string name, object value) => SetOutputValue(name, PortValues.ToUnion(value));

        /// <summary>
        /// A fresh update tick for an output value, so inputs connected to several outputs can pick the newest.
        /// Never 0, which marks a value that was never set.
        /// </summary>
        public static byte StampTick()
        {
            byte tick = Union.NextTick();
            return tick != 0 ? tick : Union.NextTick();
        }

        // Ticks wrap around at 255, so "newer" is a short distance ahead rather than a bigger number.
        private static bool IsNewer(byte tick, byte than) => tick != 0 && (than == 0 || (sbyte)(tick - than) > 0);

        /// <summary>
        /// The value this (action or event) node wrote to an output for the object running the graph: this flow's
        /// value first, then that object's last one.
        /// </summary>
        private bool TryGetWritten(Port port, FlowScope scope, out Union value)
        {
            if (scope != null && scope.TryGet(this, port.Name, out value))
                return true;

            if (Wiring.OutputIndexByPort.TryGetValue(port, out int index))
            {
                var flow = Flow.Current;
                var slots = SlotsFor(flow?.Instance, flow != null, create: false);
                if (slots != null && index < slots.Length && slots[index].Has)
                {
                    value = slots[index].Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Runs this query node's OnRequestValue for a port, remembering the result for the rest of the flow step, so
        /// a query read by several inputs (or by several nodes in one step) is computed once. Outside of a flow it is
        /// always computed.
        /// </summary>
        private object Evaluate(Port port)
        {
            if (!Wiring.OutputIndexByPort.TryGetValue(port, out int index) || _memo == null || index >= _memo.Length)
                return OnRequestValue(port);

            int stamp = Flow.CurrentStamp;
            if (stamp != 0 && _memo[index].Stamp == stamp && _memo[index].HasValue)
                return _memo[index].Value;

            // Query nodes wired into a circle would otherwise recurse forever.
            if (_memo[index].Evaluating)
            {
                Debug.LogWarning($"[{Name}] Its output '{port.Name}' depends on itself (query nodes wired in a circle). Using its previous value.");
                return _memo[index].Value;
            }

            _memo[index].Evaluating = true;
            object result;
            try
            {
                result = OnRequestValue(port);
            }
            finally
            {
                if (_memo != null && index < _memo.Length)
                    _memo[index].Evaluating = false;
            }

            if (_memo != null && index < _memo.Length)
            {
                _memo[index].Stamp = stamp;
                _memo[index].Value = result;
                _memo[index].HasValue = true;
            }
            return result;
        }

        /// <summary>
        /// This node's own output value: computed now for a query node, otherwise the value it wrote for the object
        /// running the graph.
        /// </summary>
        public new T GetOutputValue<T>(string name)
        {
            var port = GetPort(name);
            if (port == null || port.Direction != PortDirection.Output)
                return default;
            return TryReadOutput(port, CurrentScope, out T value) ? value : default;
        }

        /// <summary>
        /// True for action nodes that can work out their outputs at any time (Get Variable, for example): inputs
        /// connected to them get a fresh value from OnRequestValue, like from a query node, rather than the value
        /// written the last time the node ran. Query nodes always work this way.
        /// </summary>
        protected virtual bool AnswersOnRequest => false;

        // Reads one output (of this node) as T, the way an input connected to it would.
        private bool TryReadOutput<T>(Port port, FlowScope scope, out T value)
        {
            if (Wiring.IsQuery || AnswersOnRequest)
            {
                object result = Evaluate(port);
                if (result != null)
                    return PortValues.TryFromObject(result, out value);
                // A query that wrote its value instead of returning it.
                if (TryGetWritten(port, scope, out var written))
                    return PortValues.TryFromUnion(written, out value);
                return PortValues.TryFromObject<T>(null, out value);
            }

            if (TryGetWritten(port, scope, out var union))
                return PortValues.TryFromUnion(union, out value);

            // Never written for this object: let the node answer for itself (null if it has nothing to say).
            object asked = OnRequestValue(port);
            if (asked != null)
                return PortValues.TryFromObject(asked, out value);

            value = default;
            return false;
        }

        #endregion

        #region Inputs

        /// <summary>
        /// Reads an input. Connected: the connected output's value (see the class summary). Not connected (or the
        /// connected node has no value yet): <paramref name="defaultValue"/>, which should be the node's field for the
        /// input so the inline value on the node is used, and edits to it take effect immediately.
        /// </summary>
        public new T GetInputValue<T>(string name, T defaultValue)
            => TryReadInput(name, out T value) ? value : defaultValue;

        /// <summary>
        /// Reads an input, falling back to the inline value of the node's field for it when nothing is connected.
        /// Prefer passing the field as a default (the other overload): reading the field this way boxes value types.
        /// </summary>
        public T GetInputValue<T>(string name)
            => TryReadInput(name, out T value) ? value : InlineValue<T>(name);

        /// <summary>
        /// Reads an input. True when something is connected and gave a value that converts to <typeparamref name="T"/>
        /// (or a value was handed to this flow from another peer).
        /// </summary>
        public bool TryGetInputValue<T>(string name, out T value) => TryReadInput(name, out value);

        private bool TryReadInput<T>(string name, out T value)
        {
            var scope = CurrentScope;

            // Values handed to the flow from outside (a node replicated from another peer) win.
            if (scope != null && scope.TryGetInput(this, name, out var given))
                return PortValues.TryFromUnion(given, out value);

            if (!Wiring.Inputs.TryGetValue(name, out var input) || input.Sources.Length == 0)
            {
                value = default;
                return false;
            }

            var sources = input.Sources;
            if (sources.Length == 1)
                return sources[0].Node.TryReadOutput(sources[0].Port, scope, out value);

            // Several outputs into one input: the most recently written action value, else the first query that answers.
            if (TryMostRecent(sources, scope, out var newest))
                return PortValues.TryFromUnion(newest, out value);
            for (int i = 0; i < sources.Length; i++)
                if (sources[i].Node.Wiring.IsQuery && sources[i].Node.TryReadOutput(sources[i].Port, scope, out value))
                    return true;

            value = default;
            return false;
        }

        private static bool TryMostRecent(Source[] sources, FlowScope scope, out Union value)
        {
            value = default;
            bool found = false;
            byte mostRecentTick = 0;
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                if (source.Node.Wiring.IsQuery || !source.Node.TryGetWritten(source.Port, scope, out var output))
                    continue;
                if (found && !IsNewer(output.LastUpdateTick, mostRecentTick))
                    continue;
                value = output;
                mostRecentTick = output.LastUpdateTick;
                found = true;
            }
            return found;
        }

        [NonSerialized] private object _valuesList;

        /// <summary>
        /// Every value connected to an input (one per connection, in connection order), as <typeparamref name="T"/>.
        /// The list is reused by the next call on this node: copy it if you keep it.
        /// </summary>
        public new List<T> GetInputValues<T>(string name)
        {
            var list = _valuesList as List<T>;
            if (list == null)
                _valuesList = list = new List<T>();
            list.Clear();

            if (!Wiring.Inputs.TryGetValue(name, out var input))
                return list;

            var scope = CurrentScope;
            var sources = input.Sources;
            for (int i = 0; i < sources.Length; i++)
                if (sources[i].Node.TryReadOutput(sources[i].Port, scope, out T value))
                    list.Add(value);
            return list;
        }

        // Fields backing inputs, by node type and port name, for GetInputValue without a default.
        private static readonly Dictionary<(Type, string), FieldInfo> _inputFields = new();

        private T InlineValue<T>(string portName)
        {
            var key = (GetType(), portName);
            FieldInfo field;
            lock (_inputFields)
            {
                if (!_inputFields.TryGetValue(key, out field))
                    _inputFields[key] = field = FindInputField(key.Item1, portName);
            }
            if (field == null)
                return default;
            return PortValues.TryFromObject(field.GetValue(this), out T value) ? value : default;
        }

        private static FieldInfo FindInputField(Type type, string portName)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            FieldInfo byName = null;
            for (var t = type; t != null && t != typeof(SchematicGraphNode); t = t.BaseType)
            {
                foreach (var field in t.GetFields(flags | BindingFlags.DeclaredOnly))
                {
                    foreach (var input in field.GetCustomAttributes<InputAttribute>(true))
                        if ((input.Name ?? field.Name) == portName)
                            return field;
                    if (byName == null && field.Name == portName)
                        byName = field;
                }
            }
            return byName;
        }

        #endregion

        #region Debugging

        /// <summary>An output's value as seen by a flow (for the debugger), or its last value.</summary>
        public Union PeekOutput(string name, FlowScope scope)
        {
            if (scope != null && scope.TryGet(this, name, out var value))
                return value;
            if (!Wiring.OutputIndexByName.TryGetValue(name, out int index))
                return default;
            if (_lastOutputs != null && index < _lastOutputs.Length && _lastOutputs[index].Has)
                return _lastOutputs[index].Value;
            if (_memo != null && index < _memo.Length && _memo[index].HasValue)
                return PortValues.ToUnion(_memo[index].Value);
            return default;
        }

        /// <summary>
        /// An output's latest value as written while running for one object (for the flow visualizer), or else its last
        /// value from anyone (see <see cref="PeekOutput"/>). False if nothing has written it yet. Doesn't run anything.
        /// </summary>
        public bool TryPeekOutputFor(GameObject instance, string name, out Union value)
        {
            value = default;
            if (!Wiring.OutputIndexByName.TryGetValue(name, out int index))
                return false;
            if (!ReferenceEquals(instance, null) && _outputsByInstance != null
                && _outputsByInstance.TryGetValue(instance, out var slots) && slots != null && index < slots.Length && slots[index].Has)
            {
                value = slots[index].Value;
                return true;
            }
            if (_lastOutputs != null && index < _lastOutputs.Length && _lastOutputs[index].Has)
            {
                value = _lastOutputs[index].Value;
                return true;
            }
            if (_memo != null && index < _memo.Length && _memo[index].HasValue)
            {
                value = PortValues.ToUnion(_memo[index].Value);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The value an input would read in a flow (for the debugger and for replicating a node). False if nothing is
        /// connected to it. While a flow step is running this reads it for real (computing connected queries);
        /// otherwise it shows the last known values without running anything.
        /// </summary>
        public bool PeekInput(string name, FlowScope scope, out Union value)
        {
            value = default;
            if (scope != null && scope.TryGetInput(this, name, out value))
                return true;
            if (!Wiring.Inputs.TryGetValue(name, out var input) || input.Sources.Length == 0)
                return false;

            // Inside the running step (replicating a node): read it for real.
            if (Flow.CurrentStamp != 0 && scope == CurrentScope)
                return TryReadInput(name, out value);

            // Otherwise (a paused flow in the debugger) show what's known, without running any query.
            var source = input.Sources[0];
            if (source.Node.Wiring.IsQuery || scope == null || !scope.TryGet(source.Node, source.Port.Name, out value))
                value = source.Node.PeekOutput(source.Port.Name, null);
            return true;
        }

        #endregion

        #region Upgrading old nodes

        /// <summary>
        /// Adds a data port if it's missing, or corrects its type, keeping its connections. For nodes whose ports
        /// changed after graphs were saved with them (ports are only made from the node's fields when it's created).
        /// Call from <see cref="OnValidate"/> (editor). True if anything changed.
        /// </summary>
        protected bool EnsureDataPort(string name, Type type, PortDirection direction, PortCapacity capacity = PortCapacity.Single)
        {
            var port = GetPort(name);
            if (port == null)
            {
                AddPort(new Port { Name = name, Type = type, Direction = direction, Capacity = capacity });
                IsDirty = true;
                return true;
            }
            if (port.Type != type || port.Direction != direction)
            {
                port.Type = type;
                port.Direction = direction;
                IsDirty = true;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Moves a Unity string tag saved by an older version of a node onto its Int Tag, by name (the built-in tags
        /// share Unity's names: Player, MainCamera, Respawn, Finish...). Clears the old string either way, so it runs
        /// once. True if there was something to migrate.
        /// </summary>
        protected bool MigrateLegacyTag(ref string legacyTag, ref IntTag tag)
        {
            if (string.IsNullOrEmpty(legacyTag))
                return false;
            if (IntTags.TryGetId(legacyTag, out int id))
                tag = id;
            else
                Debug.LogWarning($"[{Name}] The tag \"{legacyTag}\" has no Int Tag with that name yet. Add one (Add Tag... in the dropdown) and pick it on the node.");
            legacyTag = null;
            return true;
        }

        #endregion

        public override object OnRequestValue(BlueGraph.Port port)
        {
            return null;
        }
    }
}
