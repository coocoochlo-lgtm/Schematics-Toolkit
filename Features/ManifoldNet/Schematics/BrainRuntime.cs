using Cysharp.Threading.Tasks;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>
    /// One instance's side of a brain: what the graph told it this tick (inputs, answers, rewards), what it decided,
    /// and a short history of both, so a reward can credit the decisions that led to it.
    /// </summary>
    public sealed class BrainAgent
    {
        public readonly BehaviorInstance Instance;
        public readonly BrainDefinition Definition;
        public BrainNetwork Network { get; internal set; }
        public int Row { get; internal set; } = -1;

        public GameObject GameObject => Instance?.GameObject;

        // This tick.
        internal readonly float[] Inputs;
        internal readonly float[] TeachTargets, TeachWeights;
        internal bool Taught;
        internal float Reward;
        internal bool Rewarded;

        // What it decided: raw network outputs, and what it actually did (after exploration), as targets would be
        // written: axes as values, buttons as ±1, choices one-hot ±1.
        internal readonly float[] Raw, Acted;
        private readonly bool[] _buttons;
        private readonly int[] _choices;

        // History, by tick (a ring).
        private readonly int _capacity;
        private readonly long[] _tick;
        private readonly float[] _hInputs, _hActed, _hTeachTargets, _hTeachWeights, _credit;
        private readonly bool[] _hasActed;
        private readonly List<(long tick, MnnState state)> _snapshots = new();
        internal long LatestActed = -1;

        internal BrainAgent(BehaviorInstance instance, BrainDefinition definition)
        {
            Instance = instance;
            Definition = definition;
            int i = definition.InputWidth, n = Mathf.Min(MnnSettings.MaxOutputs, definition.OutputWidth);
            Inputs = new float[i];
            TeachTargets = new float[n];
            TeachWeights = new float[n];
            Raw = new float[n];
            Acted = new float[n];
            _buttons = new bool[definition.Outputs.Count];
            _choices = new int[definition.Outputs.Count];

            _capacity = Mathf.Max(4, Mathf.CeilToInt(definition.HistorySeconds * definition.TickRate) + 2);
            _tick = new long[_capacity];
            for (int k = 0; k < _capacity; k++) _tick[k] = -1;
            _hInputs = new float[_capacity * i];
            _hActed = new float[_capacity * n];
            _hTeachTargets = new float[_capacity * n];
            _hTeachWeights = new float[_capacity * n];
            _credit = new float[_capacity];
            _hasActed = new bool[_capacity];
        }

        #region What the graph does

        /// <summary>Sets an input slot (values in the slot's Min-Max range; a slot of Size n takes n values).</summary>
        public void SetInput(string slot, ReadOnlySpan<float> values)
        {
            int index = Definition.FindInput(slot);
            if (index < 0)
                throw new ArgumentException($"The brain has no input \"{slot}\".");
            var definition = Definition.Inputs[index];
            int offset = Definition.InputOffset(index), size = Mathf.Max(1, definition.Size);
            for (int k = 0; k < size; k++)
                Inputs[offset + k] = definition.Normalize(k < values.Length ? values[k] : values.Length > 0 ? values[values.Length - 1] : 0f);
        }

        public void SetInput(string slot, float value) => SetInput(slot, stackalloc float[] { value });

        public float GetAxis(string slot) => Acted[OutputOffset(slot, BrainOutputKind.Axis)];

        public bool GetButton(string slot)
        {
            int index = Definition.FindOutput(slot);
            Check(index, slot, BrainOutputKind.Button);
            return _buttons[index];
        }

        /// <summary>The chosen option of a Choice slot (its index), and its name.</summary>
        public int GetChoice(string slot, out string option)
        {
            int index = Definition.FindOutput(slot);
            Check(index, slot, BrainOutputKind.Choice);
            int choice = _choices[index];
            var options = Definition.Outputs[index].Options;
            option = choice >= 0 && choice < options.Length ? options[choice] : "";
            return choice;
        }

        /// <summary>A slot's value as a plain number: an axis's value, 1/0 for a button, the option index for a choice.</summary>
        public float GetValue(string slot)
        {
            int index = Definition.FindOutput(slot);
            if (index < 0)
                throw new ArgumentException($"The brain has no output \"{slot}\".");
            return Definition.Outputs[index].Kind switch
            {
                BrainOutputKind.Axis => Acted[Definition.OutputOffset(index)],
                BrainOutputKind.Button => _buttons[index] ? 1f : 0f,
                _ => _choices[index],
            };
        }

        /// <summary>
        /// "The right answer here was ...": teaches one output slot this tick (an axis value, a button as 1/0, or a
        /// choice's option index).
        /// </summary>
        public void Teach(string slot, float value)
        {
            int index = Definition.FindOutput(slot);
            if (index < 0)
                throw new ArgumentException($"The brain has no output \"{slot}\".");
            var definition = Definition.Outputs[index];
            int offset = Definition.OutputOffset(index);
            if (offset >= TeachTargets.Length)
                return;
            switch (definition.Kind)
            {
                case BrainOutputKind.Axis:
                    TeachTargets[offset] = Mathf.Clamp(value, -1f, 1f);
                    TeachWeights[offset] = 1f;
                    break;
                case BrainOutputKind.Button:
                    TeachTargets[offset] = value > 0.5f ? 1f : -1f;
                    TeachWeights[offset] = 1f;
                    break;
                case BrainOutputKind.Choice:
                    int choice = Mathf.RoundToInt(value);
                    for (int k = 0; k < definition.Width && offset + k < TeachTargets.Length; k++)
                    {
                        TeachTargets[offset + k] = k == choice ? 1f : -1f;
                        TeachWeights[offset + k] = 1f;
                    }
                    break;
            }
            Taught = true;
        }

        /// <summary>Praise (positive) or punishment (negative). Adds up over the tick.</summary>
        public void AddReward(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return;
            Reward += value;
            Rewarded = true;
        }

        private int OutputOffset(string slot, BrainOutputKind kind)
        {
            int index = Definition.FindOutput(slot);
            Check(index, slot, kind);
            return Definition.OutputOffset(index);
        }

        private void Check(int index, string slot, BrainOutputKind kind)
        {
            if (index < 0)
                throw new ArgumentException($"The brain has no output \"{slot}\".");
            if (Definition.Outputs[index].Kind != kind)
                throw new ArgumentException($"\"{slot}\" is a {Definition.Outputs[index].Kind}, not a {kind}.");
        }

        #endregion

        #region Deciding and remembering

        /// <summary>How far the brain's own raw answer was from what it was taught this tick (weighted mean squared).</summary>
        internal float TeachingSurprise()
        {
            float sum = 0f, weight = 0f;
            for (int k = 0; k < TeachTargets.Length && k < Raw.Length; k++)
            {
                float e = Raw[k] - TeachTargets[k];
                sum += TeachWeights[k] * e * e;
                weight += TeachWeights[k];
            }
            return weight > 1e-6f ? sum / weight : 0f;
        }

        // Turns the network's raw outputs into decisions, with a little exploration while learning.
        internal void Decide(ReadOnlySpan<float> raw, float exploration, ref uint noise)
        {
            raw.Slice(0, Raw.Length).CopyTo(Raw);
            for (int s = 0; s < Definition.Outputs.Count; s++)
            {
                var slot = Definition.Outputs[s];
                int offset = Definition.OutputOffset(s);
                if (offset + slot.Width > Raw.Length)
                    break;
                switch (slot.Kind)
                {
                    case BrainOutputKind.Axis:
                        Acted[offset] = Mathf.Clamp(Raw[offset] + exploration * Gaussian(ref noise), -1f, 1f);
                        break;

                    case BrainOutputKind.Button:
                        float value = Raw[offset] + exploration * Gaussian(ref noise);
                        _buttons[s] = _buttons[s] ? value > 0.4f : value > 0.6f;
                        Acted[offset] = _buttons[s] ? 1f : -1f;
                        break;

                    case BrainOutputKind.Choice:
                        int best = 0;
                        for (int k = 1; k < slot.Width; k++)
                            if (Raw[offset + k] > Raw[offset + best]) best = k;
                        if (exploration > 0f && Unit(ref noise) < exploration * 0.5f)
                            best = Mathf.Min(slot.Width - 1, (int)(Unit(ref noise) * slot.Width));
                        _choices[s] = best;
                        for (int k = 0; k < slot.Width; k++)
                            Acted[offset + k] = k == best ? 1f : -1f;
                        break;
                }
            }
        }

        private static float Unit(ref uint state)
        {
            state = MnnHash.Pcg(state);
            return MnnHash.Unit(state);
        }

        private static float Gaussian(ref uint state)
        {
            float u1 = Mathf.Max(1e-6f, Unit(ref state)), u2 = Unit(ref state);
            return Mathf.Sqrt(-2f * Mathf.Log(u1)) * Mathf.Cos(2f * Mathf.PI * u2);
        }

        private int Slot(long tick) => (int)(tick % _capacity);

        /// <summary>This tick's inputs and answers go into history.</summary>
        internal void RecordInputs(long tick)
        {
            int at = Slot(tick), i = Inputs.Length, n = TeachTargets.Length;
            _tick[at] = tick;
            _hasActed[at] = false;
            _credit[at] = 0f;
            Array.Copy(Inputs, 0, _hInputs, at * i, i);
            Array.Copy(TeachTargets, 0, _hTeachTargets, at * n, n);
            Array.Copy(TeachWeights, 0, _hTeachWeights, at * n, n);
        }

        /// <summary>What it did, for the tick whose inputs produced it (a tick behind on the GPU).</summary>
        internal void RecordActed(long tick)
        {
            if (tick < 0) return;
            int at = Slot(tick);
            if (_tick[at] != tick) return;
            Array.Copy(Acted, 0, _hActed, at * Acted.Length, Acted.Length);
            _hasActed[at] = true;
            LatestActed = tick;
        }

        internal void ClearTick()
        {
            Array.Clear(TeachWeights, 0, TeachWeights.Length);
            Taught = false;
            Reward = 0f;
            Rewarded = false;
        }

        /// <summary>A reward (already normalized) credits recent decisions, fading by age.</summary>
        internal void Credit(float advantage, float halfLifeTicks)
        {
            if (LatestActed < 0) return;
            float fade = Mathf.Pow(0.5f, 1f / Mathf.Max(0.01f, halfLifeTicks));
            float weight = advantage;
            for (long tick = LatestActed; tick > LatestActed - _capacity && tick >= 0; tick--)
            {
                int at = Slot(tick);
                if (_tick[at] != tick) break;
                if (_hasActed[at]) _credit[at] += weight;
                weight *= fade;
                if (Mathf.Abs(weight) < 1e-4f) break;
            }
        }

        internal void AddSnapshot(long tick, MnnState state)
        {
            _snapshots.Add((tick, state));
            long oldest = LatestActed - _capacity;
            _snapshots.RemoveAll(s => s.tick < oldest);
            if (_snapshots.Count > 64) _snapshots.RemoveAt(0);
        }

        /// <summary>How much there is to practice: credit and answers waiting in history.</summary>
        internal float LessonStrength()
        {
            float sum = 0f;
            int n = TeachTargets.Length;
            for (int at = 0; at < _capacity; at++)
            {
                if (_tick[at] < 0 || !_hasActed[at]) continue;
                sum += Mathf.Abs(_credit[at]);
                for (int k = 0; k < n; k++) sum += _hTeachWeights[at * n + k];
            }
            return sum;
        }

        /// <summary>
        /// Builds a lesson from history: replay from the oldest memory snapshot still in range, with targets from
        /// answers (taught) and credit (praised decisions again, punished ones avoided). False if there's nothing.
        /// </summary>
        internal bool BuildLesson(float teachingWeight, float rewardWeight, float[] inputs, float[] targets, float[] weights, out MnnState start, out int steps)
        {
            start = null;
            steps = 0;
            if (_snapshots.Count == 0 || LatestActed < 0)
                return false;

            // The oldest snapshot whose ticks are all still in history.
            (long tick, MnnState state) from = default;
            bool found = false;
            foreach (var snapshot in _snapshots)
                if (snapshot.tick > LatestActed - _capacity + 1 && snapshot.tick <= LatestActed && _tick[Slot(snapshot.tick)] == snapshot.tick)
                {
                    from = snapshot;
                    found = true;
                    break;
                }
            if (!found)
                return false;

            int i = Inputs.Length, n = Acted.Length;
            int maxSteps = inputs.Length / i;
            double total = 0;
            for (long tick = from.tick; tick <= LatestActed && steps < maxSteps; tick++, steps++)
            {
                int at = Slot(tick);
                if (_tick[at] != tick)
                    break;
                Array.Copy(_hInputs, at * i, inputs, steps * i, i);
                float credit = _hasActed[at] ? Mathf.Clamp(_credit[at], -3f, 3f) * rewardWeight : 0f;
                for (int s = 0; s < Definition.Outputs.Count; s++)
                {
                    var slot = Definition.Outputs[s];
                    int offset = Definition.OutputOffset(s);
                    for (int k = 0; k < slot.Width && offset + k < n; k++)
                    {
                        int o = offset + k;
                        float acted = _hActed[at * n + o];
                        float teach = _hTeachWeights[at * n + o] * teachingWeight;
                        float target = 0f, weight = 0f;
                        if (teach > 0f)
                        {
                            target = _hTeachTargets[at * n + o];
                            weight = teach;
                        }
                        else if (credit > 0f)
                        {
                            target = acted; // do that again
                            weight = credit;
                        }
                        else if (credit < 0f)
                        {
                            // Not that: buttons and choices that were on get turned off; axes get nudged away, gently.
                            if (slot.Kind == BrainOutputKind.Axis)
                            {
                                if (Mathf.Abs(acted) > 0.05f)
                                {
                                    target = acted - Mathf.Sign(acted) * Mathf.Min(0.5f, Mathf.Abs(acted));
                                    weight = -credit;
                                }
                            }
                            else if (acted > 0f)
                            {
                                target = -1f;
                                weight = -credit;
                            }
                        }
                        targets[steps * n + o] = target;
                        weights[steps * n + o] = weight;
                        total += weight;
                    }
                }
            }
            if (steps == 0 || total <= 1e-6)
                return false;

            start = from.state;

            // Practiced: it counts for less next time.
            for (long tick = from.tick; tick < from.tick + steps; tick++)
            {
                int at = Slot(tick);
                _credit[at] *= 0.5f;
                for (int k = 0; k < n; k++) _hTeachWeights[at * n + k] *= 0.5f;
            }
            return true;
        }

        #endregion
    }

    /// <summary>
    /// One network and the instances it thinks for: every instance of a Schematic Object when shared, or just one.
    /// It ticks at its definition's rate: asks each instance's graph for inputs (On Think), steps, decides, tells the
    /// graph (On Decided), then learns from what it was taught and rewarded.
    /// </summary>
    public sealed class BrainNetwork : IDisposable
    {
        /// <summary>A teaching sample is "notable" (worth a memory) when the brain's own answer was this far off (mean squared).</summary>
        public const float TeachingMemoryThreshold = 0.05f;

        public readonly BrainDefinition Definition;
        public readonly int ObjectId, NetworkId;
        public ManifoldNet Net { get; private set; }
        public readonly MnnSettings Settings;
        public long Tick { get; private set; }

        private BrainAgent[] _rows;
        private float[] _inputs, _targets, _weights, _outputs;
        private readonly List<BrainAgent> _agents = new();
        private float _accumulator, _practiceBudget;
        private uint _noise;
        private int _practiceCursor;
        private float _rewardMean, _rewardSquare;
        private bool _anyReward;
        private readonly float[] _lessonInputs, _lessonTargets, _lessonWeights;

        public IReadOnlyList<BrainAgent> Agents => _agents;
        public int Capacity => _rows.Length;
        public BrainAgent AgentAtRow(int row) => row >= 0 && row < _rows.Length ? _rows[row] : null;

        /// <summary>Raised after each tick (for the visualizer).</summary>
        public event Action<BrainNetwork> Ticked;

        internal BrainNetwork(BrainDefinition definition, int objectId, int networkId, int capacity)
        {
            Definition = definition;
            ObjectId = objectId;
            NetworkId = networkId;
            Settings = definition.BuildSettings();
            _noise = Settings.seed ^ (uint)(networkId * 7919);
            int lessonSteps = Mathf.CeilToInt(definition.HistorySeconds * definition.TickRate) + 2;
            _lessonInputs = new float[lessonSteps * Settings.inputCount];
            _lessonTargets = new float[lessonSteps * Settings.outputCount];
            _lessonWeights = new float[lessonSteps * Settings.outputCount];
            Build(Mathf.Max(1, capacity), null);
        }

        private void Build(int capacity, float[] parameters)
        {
            var old = Net;
            Net = new ManifoldNet(Settings, capacity, preferCpu: Definition.PreferCpu)
            {
                Name = $"{Definition.name} ({ObjectId}:{NetworkId})",
                InputLabels = Definition.InputLabels(),
                OutputLabels = Definition.OutputLabels(),
            };
            if (parameters != null)
                Net.SetParameters(parameters);
            else if (Definition.SavedBrainFits)
                Net.LoadBrain(Definition.SavedBrain);
            else if (Definition.SavedBrain != null && !Definition.SavedBrain.IsEmpty)
                Debug.LogWarning($"[Brain] {Definition.name}'s saved brain doesn't fit its slots or network settings anymore, so it starts fresh. Reset it in the IO Dock to clear this warning.", Definition);
            old?.Dispose();

            var rows = new BrainAgent[capacity];
            if (_rows != null)
                Array.Copy(_rows, rows, Mathf.Min(_rows.Length, capacity));
            _rows = rows;
            _inputs = new float[capacity * Settings.inputCount];
            _targets = new float[capacity * Settings.outputCount];
            _weights = new float[capacity * Settings.outputCount];
            _outputs = new float[capacity * Settings.outputCount];
        }

        internal void Add(BrainAgent agent)
        {
            int row = Array.IndexOf(_rows, null);
            if (row < 0)
            {
                // Full: a bigger network with the same weights. Memories restart (the new rows start at rest).
                row = _rows.Length;
                Build(_rows.Length * 2, Net.GetParameters());
            }
            _rows[row] = agent;
            agent.Row = row;
            agent.Network = this;
            _agents.Add(agent);
            Net.ResetState(row);
        }

        internal void Remove(BrainAgent agent)
        {
            if (agent.Row >= 0 && agent.Row < _rows.Length && _rows[agent.Row] == agent)
                _rows[agent.Row] = null;
            _agents.Remove(agent);
            agent.Row = -1;
            agent.Network = null;
        }

        public bool Empty => _agents.Count == 0;

        internal void Advance(float deltaTime)
        {
            float interval = 1f / Mathf.Max(1f, Definition.TickRate);
            _accumulator = Mathf.Min(_accumulator + deltaTime, interval * 3f);
            while (_accumulator >= interval)
            {
                _accumulator -= interval;
                Step();
            }
        }

        private void Step()
        {
            var brain = BrainHub.BrainFor(this);
            int i = Settings.inputCount, n = Settings.outputCount;
            bool learning = Definition.Learning;
            bool population = Settings.trainingMode == TrainingMode.Population;
            long tick = Tick;

            // 1. Each instance's graph says what it senses (and may teach or reward).
            foreach (var agent in _agents.ToArray())
            {
                if (agent.Instance == null || !agent.Instance.IsSpawned) continue;
                brain?.Think(agent);
            }

            // 2. Step every row at once.
            bool taught = false;
            Array.Clear(_weights, 0, _weights.Length);
            for (int row = 0; row < _rows.Length; row++)
            {
                var agent = _rows[row];
                if (agent == null)
                {
                    Array.Clear(_inputs, row * i, i);
                    continue;
                }
                Array.Copy(agent.Inputs, 0, _inputs, row * i, i);
                if (agent.Taught && learning)
                {
                    taught = true;
                    for (int k = 0; k < n; k++)
                    {
                        _targets[row * n + k] = agent.TeachTargets[k];
                        _weights[row * n + k] = agent.TeachWeights[k] * Definition.TeachingWeight;
                    }
                }
                agent.RecordInputs(tick);
            }
            Net.Training = learning && (population || taught);
            Net.DeltaTime = 1f / Mathf.Max(1f, Definition.TickRate);
            if (taught && !population) Net.Step(_inputs, _targets, _weights);
            else Net.Step(_inputs);

            // Episodic memory: a teaching moment the brain didn't already know is remembered at once, so the bot can
            // repeat it next time while training slowly makes it permanent.
            if (learning && Settings.memoryEnabled)
                foreach (var agent in _agents)
                    if (agent.Taught && agent.TeachingSurprise() > TeachingMemoryThreshold)
                        Net.Remember(agent.Row, agent.TeachTargets, Definition.TeachingWeight);

            // 3. Decide. On the GPU the outputs are a tick behind (from the previous inputs).
            int latency = Net.UsingGpu ? 1 : 0;
            if (Net.TryGetOutputs(_outputs))
                foreach (var agent in _agents)
                {
                    agent.Decide(new ReadOnlySpan<float>(_outputs, agent.Row * n, n), learning ? Definition.Exploration : 0f, ref _noise);
                    agent.RecordActed(tick - latency);
                }

            // Memory snapshots about once a second, for replaying lessons from.
            int snapshotEvery = Mathf.Max(1, Mathf.RoundToInt(Definition.TickRate));
            if (learning && tick % snapshotEvery == 0)
                foreach (var agent in _agents)
                {
                    var target = agent;
                    long at = tick + 1;
                    Net.CaptureState(agent.Row, state => target.AddSnapshot(at, state));
                }

            foreach (var agent in _agents.ToArray())
                if (agent.Instance != null && agent.Instance.IsSpawned)
                    brain?.Decided(agent);

            // 4. Rewards credit recent decisions (and, in a population, score each bot's nudge).
            if (learning)
                Learn();

            foreach (var agent in _agents)
                agent.ClearTick();
            Tick++;
            Ticked?.Invoke(this);
        }

        private void Learn()
        {
            float halfLifeTicks = Definition.CreditHalfLife * Definition.TickRate;
            float sum = 0f;
            foreach (var agent in _agents) sum += agent.Reward;
            if (_agents.Count > 0 && (_anyReward || sum != 0f))
            {
                _anyReward = true;
                // Rewards are judged against the usual reward per tick, and scaled by their typical size, so "+1"
                // and "+500" train the same and a steady penalty says nothing on its own.
                float mean = sum / _agents.Count;
                _rewardMean += (mean - _rewardMean) * 0.01f;
                foreach (var agent in _agents)
                {
                    float advantage = agent.Reward - _rewardMean;
                    _rewardSquare += (advantage * advantage - _rewardSquare) * 0.01f / _agents.Count;
                }
                float scale = 1f / Mathf.Sqrt(Mathf.Max(_rewardSquare, 1e-6f));
                foreach (var agent in _agents)
                {
                    float advantage = Mathf.Clamp((agent.Reward - _rewardMean) * scale, -5f, 5f);
                    if (Mathf.Abs(advantage) > 1e-3f)
                        agent.Credit(advantage, halfLifeTicks);
                    if (Settings.memoryEnabled && agent.Rewarded)
                    {
                        // A big reward (in typical-reward units) remembers what the bot did, signed: bad moments are
                        // remembered as "avoid". Whatever it just recalled is reinforced or weakened by the outcome.
                        if (Mathf.Abs(advantage) > Settings.writeThreshold)
                            Net.Remember(agent.Row, agent.Acted, advantage);
                        Net.Reinforce(agent.Row, 0.1f * advantage);
                    }
                    if (Settings.trainingMode == TrainingMode.Population)
                        Net.ReportError(agent.Row, -advantage);
                }
            }

            // Replay lessons from history.
            _practiceBudget += Definition.PracticePerSecond / Mathf.Max(1f, Definition.TickRate);
            if (_practiceBudget < 1f || _agents.Count == 0)
                return;
            _practiceBudget -= 1f;
            for (int tries = 0; tries < _agents.Count; tries++)
            {
                var agent = _agents[_practiceCursor++ % _agents.Count];
                if (agent.LessonStrength() <= 1e-3f)
                    continue;
                Array.Clear(_lessonWeights, 0, _lessonWeights.Length);
                if (!agent.BuildLesson(Definition.TeachingWeight, Definition.RewardWeight, _lessonInputs, _lessonTargets, _lessonWeights, out var start, out int steps))
                    continue;
                Net.Training = true;
                Net.Practice(start, _lessonInputs, _lessonTargets, _lessonWeights, steps);
                break;
            }
        }

        public void Dispose() => Net?.Dispose();
    }

    /// <summary>
    /// Every running brain: <c>Networks[objectID][networkID]</c>, where objectID is the Schematic Object's
    /// <see cref="SchematicObject.ObjectID"/> (its prefab). A shared brain is one network per definition on that object;
    /// per-instance brains add a network each. Ticks them all from Unity's frame loop.
    /// </summary>
    public static class BrainHub
    {
        public static readonly Dictionary<int, List<BrainNetwork>> Networks = new();
        private static readonly Dictionary<BrainNetwork, Brain> _brains = new();
        private static bool _running;

        public static IEnumerable<BrainNetwork> All
        {
            get
            {
                foreach (var list in Networks.Values)
                    foreach (var network in list)
                        if (network != null) yield return network;
            }
        }

        /// <summary>Raised when a network is created (the visualizer listens).</summary>
        public static event Action<BrainNetwork> NetworkAdded;
        public static event Action<BrainNetwork> NetworkRemoved;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (var network in new List<BrainNetwork>(All))
            {
                NetworkRemoved?.Invoke(network);
                network.Dispose();
            }
            Networks.Clear();
            _brains.Clear();
            _running = false;
        }

        internal static Brain BrainFor(BrainNetwork network) => _brains.TryGetValue(network, out var brain) ? brain : null;

        internal static void Join(Brain brain, BrainAgent agent, int objectId)
        {
            if (!Networks.TryGetValue(objectId, out var list))
                Networks[objectId] = list = new List<BrainNetwork>();

            BrainNetwork network;
            if (agent.Definition.Sharing == BrainSharing.Shared)
            {
                // Everyone's: the object's network for this definition (an object can have more than one Brain).
                network = list.Find(n => n != null && n.Definition == agent.Definition);
                if (network == null)
                {
                    network = Create(agent.Definition, objectId, list.Count, 4);
                    list.Add(network);
                }
            }
            else
            {
                network = Create(agent.Definition, objectId, list.Count, 1);
                list.Add(network);
            }
            _brains[network] = brain;
            network.Add(agent);
            if (!_running)
                Run().Forget();
        }

        private static BrainNetwork Create(BrainDefinition definition, int objectId, int networkId, int capacity)
        {
            var network = new BrainNetwork(definition, objectId, networkId, capacity);
            NetworkAdded?.Invoke(network);
            return network;
        }

        internal static void Leave(BrainAgent agent)
        {
            var network = agent.Network;
            if (network == null)
                return;
            network.Remove(agent);
            if (!network.Empty)
                return;

            // The last instance left: keep what was learned on the definition's network list, then let it go.
            if (Networks.TryGetValue(network.ObjectId, out var list))
            {
                int index = list.IndexOf(network);
                if (index >= 0) list[index] = null;
                if (list.TrueForAll(n => n == null)) Networks.Remove(network.ObjectId);
            }
            _brains.Remove(network);
            NetworkRemoved?.Invoke(network);
            BeforeDispose?.Invoke(network);
            network.Dispose();
        }

        /// <summary>Raised just before a network goes away (the editor saves what it learned).</summary>
        public static event Action<BrainNetwork> BeforeDispose;

        private static async UniTaskVoid Run()
        {
            _running = true;
            try
            {
                while (Networks.Count > 0)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                    float delta = Time.deltaTime;
                    foreach (var list in new List<List<BrainNetwork>>(Networks.Values))
                        foreach (var network in list.ToArray())
                        {
                            if (network == null || network.Empty) continue;
                            try { network.Advance(delta); }
                            catch (Exception exception) { Debug.LogException(exception); }
                        }
                    try { BrainClouds.Draw(); }
                    catch (Exception exception) { Debug.LogException(exception); }
                }
            }
            finally
            {
                _running = false;
            }
        }
    }
}
