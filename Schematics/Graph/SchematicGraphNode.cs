
using Remedy.Framework;
using BlueGraph;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Schematics
{
    [Serializable]
    public class SchematicGraphNode : BlueGraph.Node
    {
        public SchematicGraph Schematic => (SchematicGraph)Graph;
        public GameObject GameObject => Schematic.Prefab;
        public Transform Transform => GameObject.transform;

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

        protected Dictionary<string, (SchematicGraphNode node, string portName)[]> _cachedInputValueSources
            = new();

        protected Remedy.Framework.SerializableDictionary<string, Union> _cachedInputsByName = new();
        protected Remedy.Framework.SerializableDictionary<string, Union> _cachedOutputsByName = new();

        protected Remedy.Framework.SerializableDictionary<string, string[]> _cachedPortConnections = new();
        protected List<SchematicActionNode> _cachedChildren = new();

        private List<Union> _returnValList = new();

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

        #region Flow

        /// <summary>The action nodes this node leads to.</summary>
        internal List<SchematicActionNode> FlowChildren
        {
            get
            {
                if (IsDirty)
                    UpdateCaches();
                return _cachedChildren;
            }
        }

        /// <summary>
        /// Runs this node as a step of a flow. True if its children should run after it. Plain value nodes aren't steps.
        /// </summary>
        internal virtual bool ExecuteInFlow(Flow flow, FlowScope scope, Union[] arguments) => false;

        /// <summary>Copies this node's output values from one scope to another.</summary>
        internal void CopyOutputs(FlowScope from, FlowScope to)
        {
            foreach (var name in _cachedOutputsByName.Keys)
                if (from.TryGet(this, name, out var value))
                    to.Set(this, name, value);
        }

        /// <summary>The values a flow's running node reads and writes (null outside of a flow).</summary>
        private static FlowScope CurrentScope => Flow.Current?.ExecutingScope;

        /// <summary>An output's value as seen by a flow (for the debugger), or its last value.</summary>
        public Union PeekOutput(string name, FlowScope scope)
        {
            if (scope != null && scope.TryGet(this, name, out var value))
                return value;
            return _cachedOutputsByName.TryGetValue(name, out value) ? value : default;
        }

        /// <summary>The value an input would read in a flow (for the debugger). False if nothing is connected to it.</summary>
        public bool PeekInput(string name, FlowScope scope, out Union value)
        {
            if (IsDirty)
                UpdateCaches();
            value = default;
            if (scope != null && scope.TryGetInput(this, name, out value))
                return true;
            return _cachedInputValueSources.TryGetValue(name, out var sources) && TryMostRecent(sources, scope, out value);
        }

        #endregion

        public override void OnValidate()
        {
            UpdateCaches();
        }

        /// <summary>
        /// Called while editting the parent graph to cache information about the Node for efficient use during Runtime
        /// </summary>
        public void UpdateCaches()
        {
            //_cachedInputsByName.Clear();
            //_cachedOuputsByName.Clear();
            _cachedPortConnections.Clear();
            _cachedInputValueSources.Clear();
            //_cachedInputNames.Clear();
            //_cachedOutputNames.Clear();
            
            int inputIndex = 0;

            foreach (var kvp in Ports.Where(p => p.Value.Type != typeof(ActionPort)))
            {
                var port = kvp.Value;
                
                if (port.Direction == PortDirection.Input)
                {
                    _cachedInputsByName[port.Name] = inputIndex;
                    //_cachedInputNames.Add(port.Name);

                    // Build source mapping
                    var sources = new List<(SchematicGraphNode, string)>();
                    foreach (var connectedPort in port.ConnectedPorts)
                    {
                        if (connectedPort?.Node is SchematicGraphNode sourceNode)
                        {
                            sources.Add((sourceNode, connectedPort.Name));
                        }
                    }
                    _cachedInputValueSources[port.Name] = sources.ToArray();

                    inputIndex++;
                }
                else
                {
                    _cachedOutputsByName[port.Name] = default;
                    //_cachedOutputNames.Add(port.Name);

                    //outputIndex++;
                }

                _cachedPortConnections[port.Name] = port.ConnectedPorts.Where(p => p != null).Select(p => p.ID).ToArray();
            }

            RebuildChildrenCache();
            OnCacheUpdate();

            IsDirty = false;
        }

        protected virtual void OnCacheUpdate() { }

        /// <summary>
        /// Rebuilds the cache for the immediate childre of this Graph Node for quick access
        /// </summary>
        protected void RebuildChildrenCache()
        {
            _cachedChildren.Clear();

            foreach (var kvp in Ports)
            {
                var port = kvp.Value;
                if (port.Direction == PortDirection.Output && port.Type == typeof(ActionPort))
                {
                    _cachedChildren.Capacity = port.ConnectedPorts.Count();
                    // Iterate directly instead of ToList()
                    foreach (var connectedPort in port.ConnectedPorts)
                    {
                        if (connectedPort?.Node is SchematicActionNode child)
                            if(!_cachedChildren.Contains(child))
                                _cachedChildren.Add(child);
                    }
                }
            }
        }

        /// <summary>
        /// Sets the value of the Union for the port with the given name to the given value
        /// </summary>
        /// <param name="name"></param>
        /// <param name="value"></param>
        public void SetOutputValue(string name, Union value)
        {
            if (IsDirty)
                UpdateCaches();

            value.LastUpdateTick = StampTick();
            _cachedOutputsByName[name] = value;
            CurrentScope?.Set(this, name, value);
        }

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
        /// Returns the value of the Union for the Given Port cast to the given value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        public new T GetInputValue<T>(string name, T defaultValue = default)
        {
            if(IsDirty)
                UpdateCaches();

            try
            {
                PullMostRecentInputValue(name);
                var value = _cachedInputsByName[name].GetValue<T>();
                return value;
            }
            catch
            {
                return defaultValue;
            }
        }

        public int GetInputPort(string name)
        {
            return _cachedInputsByName[name];
        }




        /// <summary>
        /// Returns the value of the Union for the Given Port cast to the given value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        public new List<T> GetInputValues<T>(string name)
        {
            if (IsDirty)
                UpdateCaches();

            //UpdateCaches();
            PullAllInputValues(name);
            return _cachedInputsByName[name].GetValue<List<T>>();
        }

        /// <summary>
        /// Returns true if the input port with the given name has any connected sources.
        /// </summary>
        public bool HasInputConnections(string inputName)
        {
            if (IsDirty)
                UpdateCaches();

            //UpdateCaches(); // make sure the cache is up-to-date
            if (!_cachedInputValueSources.TryGetValue(inputName, out var sources))
                return false;

            return sources != null && sources.Length > 0;
        }


        /// <summary>
        /// Returns the value of the Union for the Given Port cast to the given value
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="name"></param>
        /// <returns></returns>
        public new T GetOutputValue<T>(string name)
        {
            if (IsDirty)
                UpdateCaches();

            return _cachedOutputsByName[name].GetValue<T>();
        }

        /// <summary>
        /// Chooses the most recently updated Union Value as the one to use for the given Port.
        /// Most common input pull, as the most recent is usually expected.
        /// </summary>
        /// <param name="inputName"></param>
        private void PullMostRecentInputValue(string inputName)
        {
            // Values handed to the flow from outside (a node replicated from another peer) win.
            var scope = CurrentScope;
            if (scope != null && scope.TryGetInput(this, inputName, out var given))
            {
                _cachedInputsByName[inputName] = given;
                return;
            }

            if (!_cachedInputValueSources.TryGetValue(inputName, out var sources))
                return;

            if (TryMostRecent(sources, CurrentScope, out var value))
                _cachedInputsByName[inputName] = value;
        }

        // The newest value among the outputs feeding an input. Values written in this flow win over a node's last value,
        // which may have come from another object running the same graph.
        private static bool TryMostRecent((SchematicGraphNode node, string portName)[] sources, FlowScope scope, out Union value)
        {
            if (scope != null && TryMostRecent(sources, scope, fromFlow: true, out value))
                return true;
            return TryMostRecent(sources, scope, fromFlow: false, out value);
        }

        private static bool TryMostRecent((SchematicGraphNode node, string portName)[] sources, FlowScope scope, bool fromFlow, out Union value)
        {
            value = default;
            bool found = false;
            byte mostRecentTick = 0;

            for (int i = 0; i < sources.Length; i++)
            {
                var (node, portName) = sources[i];
                Union output;
                bool has = fromFlow ? scope.TryGet(node, portName, out output) : node._cachedOutputsByName.TryGetValue(portName, out output);
                if (!has || !IsNewer(output.LastUpdateTick, mostRecentTick))
                    continue;

                value = output;
                mostRecentTick = output.LastUpdateTick;
                found = true;
            }
            return found;
        }

        /// <summary>
        /// Sets the value of the given Input to a Union List of all the combined values connected to the Port.
        /// Good when you need to get multiple values for a Node.
        /// </summary>
        /// <param name="inputName"></param>
        private void PullAllInputValues(string inputName)
        {
            _returnValList.Clear();

            var sources = _cachedInputValueSources[inputName];

            for (int i = 0; i < sources.Length; i++)
            {
                var (node, portName) = sources[i];

                var scope = CurrentScope;
                if (!(scope != null && scope.TryGet(node, portName, out var value)))
                    value = node._cachedOutputsByName[portName];
                _returnValList.Add(value);
            }

            _cachedInputsByName[inputName] = _returnValList;
        }

        public override object OnRequestValue(BlueGraph.Port port)
        {
            return null;
        }
    }
}