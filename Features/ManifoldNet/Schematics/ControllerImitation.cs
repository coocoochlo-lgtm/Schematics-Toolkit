using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.ManifoldNetwork.Schematics
{
    /// <summary>
    /// How an AI Controller learns to play by watching a player (its teacher). For every kind of object the teacher
    /// possesses (a character, a car), a Manifold Neuron Network learns what the teacher presses from what that object
    /// is going through: its Behaviors' Variables (speed, grounded, health...) and how it's moving. When the AI
    /// possesses an object of a kind it has learned, it presses what the teacher would have, through the same Input
    /// Signals a player's devices send, so the object can't tell the difference.
    /// <para>What it learns can be kept in a <see cref="LearnedControls"/> asset (saved when Play Mode ends, in the
    /// editor) or in a file (Save AI Skill), and loaded again: kinds of objects it learned are picked up the first
    /// time it sees (or controls) one.</para>
    /// </summary>
    public sealed class ControllerImitation : IControllerSkill
    {
        /// <summary>How often it watches and decides (per second).</summary>
        public const float TickRate = 20f;
        /// <summary>How many of the teacher's objects of one kind it learns from at once.</summary>
        public const int TeacherRows = 3;
        /// <summary>The most numbers it reads from one object.</summary>
        public const int MaxObservations = 128;

        private const int Rows = TeacherRows + 1;
        private const int StudentRow = TeacherRows;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => AIController.SkillFactory = ai => new ControllerImitation(ai);

        private readonly AIController _ai;
        private Controller _teacher;
        private readonly Dictionary<int, Kind> _kinds = new();
        private float _accumulator;
        private bool _warnedNothingToWatch;

        // What the teacher is pressing right now, and Signals without values (a Jump) pressed since the last tick.
        private readonly Dictionary<SignalData, Union[]> _held = new();
        private readonly HashSet<SignalData> _pulses = new();

        private static List<Slot> _layout;
        private static string _layoutSignature;

        // Kinds learned before (loaded from an asset or a file), built into networks when one turns up.
        private readonly Dictionary<int, LearnedKind> _saved = new();

        // Every one still running, so the editor can keep what they learned when Play Mode ends.
        private static readonly List<ControllerImitation> _live = new();

        public ControllerImitation(AIController ai)
        {
            _ai = ai;
            _live.Add(this);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void SaveWhenPlayModeEnds()
        {
            UnityEditor.EditorApplication.playModeStateChanged += state =>
            {
                if (state != UnityEditor.PlayModeStateChange.ExitingPlayMode)
                    return;
                foreach (var imitation in _live.ToArray())
                    imitation.AutoSave();
            };
        }
#endif

        // In the editor: into the AI's Learned Controls, if it has one that wants it.
        private void AutoSave()
        {
#if UNITY_EDITOR
            if (_ai != null && _ai.SkillData is LearnedControls asset && asset.SaveOnExitPlayMode && _kinds.Count > 0)
                SaveTo(asset);
#endif
        }

        public Controller Teacher
        {
            get => _teacher;
            set
            {
                if (_teacher == value)
                    return;
                if (_teacher != null)
                    _teacher.InputSent -= OnTeacherInput;
                _teacher = value;
                _held.Clear();
                _pulses.Clear();
                _warnedNothingToWatch = false;
                if (_teacher != null)
                    _teacher.InputSent += OnTeacherInput;
            }
        }

        public bool Acting { get; set; }

        #region What it can press

        private enum SlotKind { Pulse, Button, Axis }

        // One input it can press: an Input Signal, and where it sits in the network's outputs.
        private sealed class Slot
        {
            public SignalData Signal;
            public SlotKind Kind;
            public Union.ValueType Type;
            public int Offset, Width;
        }

        /// <summary>
        /// Every Input Signal of every Input Map, in a fixed order: what the network's outputs mean. (A layout made
        /// once, so a network learned on one map still means the same thing after a map switch.)
        /// </summary>
        private static List<Slot> Layout
        {
            get
            {
                if (_layout != null)
                    return _layout;
                _layout = new List<Slot>();
                var input = RemedyInput.Instance;
                var seen = new HashSet<SignalData>();
                int offset = 0;
                if (input != null && input.InputMaps != null)
                    foreach (var map in input.InputMaps)
                        if (map?.Inputs != null)
                            foreach (var entry in map.Inputs)
                            {
                                var signal = entry?.Output;
                                if (signal == null || !seen.Add(signal))
                                    continue;
                                var slot = SlotFor(signal);
                                if (slot == null || offset + slot.Width > MnnSettings.MaxOutputs)
                                    continue;
                                slot.Offset = offset;
                                offset += slot.Width;
                                _layout.Add(slot);
                            }
                return _layout;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLayout()
        {
            _layout = null;
            _layoutSignature = null;
            _live.Clear();
        }

        /// <summary>The Input Signals the outputs press, in order: learned controls only fit while this is the same.</summary>
        private static string LayoutSignature
        {
            get
            {
                if (_layoutSignature != null)
                    return _layoutSignature;
                var parts = new List<string>();
                foreach (var slot in Layout)
                    parts.Add(slot.Signal.Uid + "/" + slot.Width);
                return _layoutSignature = string.Join("|", parts);
            }
        }

        private static Slot SlotFor(SignalData signal)
        {
            var parameters = signal.Parameters.Contents;
            if (parameters.Count == 0)
                return new Slot { Signal = signal, Kind = SlotKind.Pulse, Width = 1 };
            var type = parameters[0].Value.Type;
            return type switch
            {
                Union.ValueType.Bool => new Slot { Signal = signal, Kind = SlotKind.Button, Type = type, Width = 1 },
                Union.ValueType.Float or Union.ValueType.Int => new Slot { Signal = signal, Kind = SlotKind.Axis, Type = type, Width = 1 },
                Union.ValueType.Vector2 => new Slot { Signal = signal, Kind = SlotKind.Axis, Type = type, Width = 2 },
                Union.ValueType.Vector3 => new Slot { Signal = signal, Kind = SlotKind.Axis, Type = type, Width = 3 },
                _ => null,
            };
        }

        private static int OutputWidth(List<Slot> layout)
        {
            int width = 0;
            foreach (var slot in layout)
                width = Math.Max(width, slot.Offset + slot.Width);
            return Math.Max(1, width);
        }

        private void OnTeacherInput(Controller teacher, SignalData signal, Union[] args)
        {
            if (signal.Parameters.Contents.Count == 0)
                _pulses.Add(signal);
            else
                _held[signal] = args != null ? (Union[])args.Clone() : Array.Empty<Union>();
        }

        // What the teacher is pressing, as the network's targets: buttons ±1, axes their values (-1 to 1).
        private void WriteTargets(List<Slot> layout, float[] targets, int at)
        {
            foreach (var slot in layout)
            {
                int o = at + slot.Offset;
                switch (slot.Kind)
                {
                    case SlotKind.Pulse:
                        targets[o] = _pulses.Contains(slot.Signal) ? 1f : -1f;
                        break;
                    case SlotKind.Button:
                        targets[o] = _held.TryGetValue(slot.Signal, out var pressed) && pressed.Length > 0 && SignalArgs.FromUnion<bool>(pressed[0]) ? 1f : -1f;
                        break;
                    default:
                        Vector3 value = default;
                        if (_held.TryGetValue(slot.Signal, out var args) && args.Length > 0)
                            value = slot.Type switch
                            {
                                Union.ValueType.Vector2 => SignalArgs.FromUnion<Vector2>(args[0]),
                                Union.ValueType.Vector3 => SignalArgs.FromUnion<Vector3>(args[0]),
                                _ => new Vector3(SignalArgs.FromUnion<float>(args[0]), 0f, 0f),
                            };
                        for (int k = 0; k < slot.Width; k++)
                            targets[o + k] = Mathf.Clamp(value[k], -1f, 1f);
                        break;
                }
            }
        }

        #endregion

        #region Watching and acting

        public void Tick(float deltaTime)
        {
            float interval = 1f / TickRate;
            _accumulator += deltaTime;
            if (_accumulator < interval)
                return;
            float step = Mathf.Min(_accumulator, interval * 3f);
            _accumulator = 0f;

            var layout = Layout;
            if (layout.Count == 0)
                return;

            // The teacher's objects, by kind (their prefab), and the AI's own.
            var byKind = new Dictionary<int, List<SchematicObject>>();
            if (_teacher != null)
            {
                foreach (var possessed in _teacher.Possessed)
                    if (possessed != null && possessed.UID != 0)
                    {
                        if (!byKind.TryGetValue(possessed.ObjectID, out var list))
                            byKind[possessed.ObjectID] = list = new List<SchematicObject>();
                        if (list.Count < TeacherRows)
                            list.Add(possessed);
                    }
                if (byKind.Count == 0 && !_warnedNothingToWatch)
                {
                    _warnedNothingToWatch = true;
                    Debug.LogWarning($"{_ai.DisplayName} is learning from {_teacher.DisplayName}, who doesn't possess anything yet, so there's nothing to watch. Possess the player's character with their controller (in a game that doesn't set up its players: Get Player Controller 0 → Possess).", _ai);
                }
            }
            var own = Acting ? _ai.Primary : null;

            foreach (var entry in byKind)
                KindFor(entry.Key, entry.Value[0], layout).Step(entry.Value, own != null && own.ObjectID == entry.Key ? own : null, this, layout, step);
            if (own != null && !byKind.ContainsKey(own.ObjectID))
            {
                // Something it learned before (loaded) is built the first time it controls one.
                if (!_kinds.TryGetValue(own.ObjectID, out var known) && _saved.ContainsKey(own.ObjectID))
                    known = KindFor(own.ObjectID, own, layout);
                known?.Step(null, own, this, layout, step);
            }

            _pulses.Clear();
        }

        private Kind KindFor(int objectId, SchematicObject example, List<Slot> layout)
        {
            if (!_kinds.TryGetValue(objectId, out var kind))
            {
                _saved.TryGetValue(objectId, out var saved);
                if (saved != null && saved.InputLayout != LayoutSignature)
                {
                    Debug.LogWarning($"{_ai.DisplayName}: what it learned about {saved.ObjectName} was for other inputs (the Input Maps changed), so it starts that over.", _ai);
                    saved = null;
                }
                _kinds[objectId] = kind = new Kind(_ai, example, layout, saved);
            }
            return kind;
        }


        // Pressing what the network decided, as the teacher would have.
        private void Act(Kind kind, List<Slot> layout, float[] outputs, int at)
        {
            for (int s = 0; s < layout.Count; s++)
            {
                var slot = layout[s];
                int o = at + slot.Offset;
                switch (slot.Kind)
                {
                    case SlotKind.Pulse:
                    {
                        bool on = kind.Pressed[s] ? outputs[o] > 0.2f : outputs[o] > 0.5f;
                        if (on && !kind.Pressed[s])
                            _ai.SendInput(slot.Signal);
                        kind.Pressed[s] = on;
                        break;
                    }
                    case SlotKind.Button:
                    {
                        bool on = kind.Pressed[s] ? outputs[o] > -0.2f : outputs[o] > 0.3f;
                        if (on)
                            _ai.SendInput(slot.Signal, new Union(true));
                        else if (kind.Pressed[s])
                            _ai.ReleaseInput(slot.Signal);
                        kind.Pressed[s] = on;
                        break;
                    }
                    default:
                    {
                        var value = new Vector3(outputs[o], slot.Width > 1 ? outputs[o + 1] : 0f, slot.Width > 2 ? outputs[o + 2] : 0f);
                        for (int k = 0; k < 3; k++)
                            value[k] = Mathf.Clamp(value[k], -1f, 1f);
                        bool on = value.sqrMagnitude > 0.08f * 0.08f;
                        if (on)
                        {
                            Union union = slot.Type switch
                            {
                                Union.ValueType.Vector2 => new Union(new Vector2(value.x, value.y)),
                                Union.ValueType.Vector3 => new Union(value),
                                Union.ValueType.Int => new Union(Mathf.RoundToInt(value.x)),
                                _ => new Union(value.x),
                            };
                            _ai.SendInput(slot.Signal, union);
                        }
                        else if (kind.Pressed[s])
                            _ai.ReleaseInput(slot.Signal);
                        kind.Pressed[s] = on;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// One kind of object (a prefab) and the network that learned how the teacher controls it. Rows 0-2 watch the
        /// teacher's objects of this kind (and learn), row 3 is the AI's own (and acts).
        /// </summary>
        private sealed class Kind : IDisposable
        {
            public readonly ManifoldNet Net;
            public readonly bool[] Pressed;
            private readonly int _inputs, _outputs;
            private readonly float[] _in, _targets, _weights, _out;
            private readonly float[] _mean, _variance;
            private readonly SchematicObject[] _rowObjects = new SchematicObject[Rows];
            private readonly Dictionary<SchematicObject, (Vector3 position, float yaw)> _lastPose = new();
            private readonly float[] _scratch = new float[MaxObservations];
            private readonly string _objectName;

            public Kind(AIController ai, SchematicObject example, List<Slot> layout, LearnedKind saved)
            {
                _objectName = example.name;
                _inputs = Mathf.Clamp(Observe(example, _scratch, 0f, record: false), 1, MaxObservations);
                _outputs = OutputWidth(layout);
                if (saved != null && (saved.Observations != _inputs || saved.Mean == null || saved.Mean.Length != _inputs))
                {
                    Debug.LogWarning($"{ai.DisplayName}: what it learned about {saved.ObjectName} doesn't fit it any more (its Behaviors' Variables changed), so it starts that over.", ai);
                    saved = null;
                }

                MnnSettings settings;
                MnnBrain brain = null;
                if (saved != null)
                {
                    settings = saved.Settings.Clone();
                    brain = MnnBrain.Create(settings, saved.Parameters, saved.Updates, saved.Memory, saved.Identities);
                    if (!brain.Fits(settings))
                    {
                        UnityEngine.Object.DestroyImmediate(brain);
                        brain = null;
                    }
                }
                else
                {
                    settings = new MnnSettings
                    {
                        inputCount = _inputs,
                        outputCount = _outputs,
                        hiddenWidth = 24,
                        headCount = 8,
                        tickRate = TickRate,
                        trainingMode = TrainingMode.Supervised,
                    };
                    settings.Clamp();
                }
                Net = new ManifoldNet(settings, Rows, brain) { Name = $"{ai.DisplayName} learning {example.name}" };
                if (brain != null)
                    UnityEngine.Object.DestroyImmediate(brain);

                Pressed = new bool[layout.Count];
                _in = new float[Rows * _inputs];
                _targets = new float[Rows * _outputs];
                _weights = new float[Rows * _outputs];
                _out = new float[Rows * _outputs];
                _mean = new float[_inputs];
                _variance = new float[_inputs];
                for (int i = 0; i < _inputs; i++) _variance[i] = 1f;
                if (saved != null)
                {
                    Array.Copy(saved.Mean, _mean, _inputs);
                    if (saved.Variance != null && saved.Variance.Length == _inputs)
                        Array.Copy(saved.Variance, _variance, _inputs);
                }
            }

            /// <summary>What it has learned, to keep (null if its network is already gone).</summary>
            public LearnedKind Snapshot(int objectId, string layoutSignature)
            {
                if (Net == null || Net.Disposed)
                    return null;
                var brain = Net.SaveBrain();
                var kind = new LearnedKind
                {
                    ObjectId = objectId,
                    ObjectName = _objectName,
                    InputLayout = layoutSignature,
                    Observations = _inputs,
                    Mean = (float[])_mean.Clone(),
                    Variance = (float[])_variance.Clone(),
                    Settings = brain.Settings.Clone(),
                    Parameters = brain.Parameters,
                    Memory = brain.MemorySlots,
                    Identities = brain.IdentityMasks,
                    Updates = brain.Updates,
                };
                UnityEngine.Object.DestroyImmediate(brain);
                return kind;
            }

            public void Step(List<SchematicObject> teacherObjects, SchematicObject own, ControllerImitation owner, List<Slot> layout, float deltaTime)
            {
                Array.Clear(_in, 0, _in.Length);
                Array.Clear(_weights, 0, _weights.Length);
                bool teaching = false;
                for (int row = 0; row < Rows; row++)
                {
                    var target = row == StudentRow ? own : teacherObjects != null && row < teacherObjects.Count ? teacherObjects[row] : null;
                    if (_rowObjects[row] != target)
                    {
                        // A different object: its memory starts afresh.
                        _rowObjects[row] = target;
                        Net.ResetState(row);
                    }
                    if (target == null)
                        continue;
                    Array.Clear(_scratch, 0, _scratch.Length);
                    Observe(target, _scratch, deltaTime, record: true);
                    for (int i = 0; i < _inputs; i++)
                    {
                        float x = _scratch[i];
                        if (row != StudentRow)
                        {
                            // What's usual is learned from the teacher's objects.
                            float d = x - _mean[i];
                            _mean[i] += d * 0.01f;
                            _variance[i] += (d * d - _variance[i]) * 0.01f;
                        }
                        _in[row * _inputs + i] = Mathf.Clamp((x - _mean[i]) / Mathf.Sqrt(_variance[i] + 1e-4f), -3f, 3f) / 3f;
                    }
                    if (row != StudentRow)
                    {
                        owner.WriteTargets(layout, _targets, row * _outputs);
                        for (int k = 0; k < _outputs; k++)
                            _weights[row * _outputs + k] = 1f;
                        teaching = true;
                    }
                }

                Net.Training = teaching;
                Net.DeltaTime = deltaTime;
                if (teaching)
                    Net.Step(_in, _targets, _weights);
                else
                    Net.Step(_in);

                if (own != null && Net.TryGetOutputs(_out))
                    owner.Act(this, layout, _out, StudentRow * _outputs);
            }

            /// <summary>
            /// What it sees of an object: every number its Behaviors' Variables hold (in a fixed order), then how it's
            /// moving in its own frame (velocity, turn rate) and which way is up for it. Returns how many numbers.
            /// </summary>
            private int Observe(SchematicObject schematic, float[] into, float deltaTime, bool record)
            {
                int n = 0;
                Span<float> numbers = stackalloc float[3];
                foreach (var instance in schematic.BehaviorInstances)
                {
                    if (instance == null || !instance.IsSpawned)
                        continue;
                    var runtime = instance.Runtime;
                    for (int v = 0; v < runtime.VariableCount && n < MaxObservations - 7; v++)
                    {
                        int count = runtime.ReadNumbers(instance, v, numbers);
                        for (int k = 0; k < count && n < MaxObservations - 7; k++)
                            into[n++] = numbers[k];
                    }
                }

                var transform = schematic.transform;
                Vector3 position = transform.position;
                float yaw = transform.eulerAngles.y;
                Vector3 velocity = default;
                float turn = 0f;
                if (record && deltaTime > 0f && _lastPose.TryGetValue(schematic, out var last))
                {
                    velocity = transform.InverseTransformDirection((position - last.position) / deltaTime);
                    turn = Mathf.DeltaAngle(last.yaw, yaw) / deltaTime;
                }
                if (record)
                    _lastPose[schematic] = (position, yaw);
                into[n++] = velocity.x;
                into[n++] = velocity.y;
                into[n++] = velocity.z;
                into[n++] = turn;
                Vector3 up = transform.up;
                into[n++] = up.x;
                into[n++] = up.y;
                into[n++] = up.z;
                return n;
            }

            public void Dispose() => Net?.Dispose();
        }

        #endregion

        #region Keeping what it learned

        public bool LoadFrom(ControllerSkillData data)
        {
            if (data is not LearnedControls asset)
                return false;
            return Take(asset.Kinds);
        }

        public void SaveTo(ControllerSkillData data)
        {
            if (data is not LearnedControls asset)
            {
                Debug.LogWarning($"{_ai.DisplayName} can only keep what it learned in a Learned Controls asset.", _ai);
                return;
            }
            foreach (var kind in Snapshot())
                asset.Put(kind);
#if UNITY_EDITOR
            if (UnityEditor.EditorUtility.IsPersistent(asset))
            {
                UnityEditor.EditorUtility.SetDirty(asset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
            }
#endif
        }

        public string SaveToFile(string fileName)
        {
            string path = FilePath(fileName);
            try
            {
                var file = new LearnedControlsFile();
                var kinds = Snapshot();
                file.Kinds.AddRange(kinds);
                // Kinds it was given but hasn't met this time are kept too.
                foreach (var saved in _saved.Values)
                    if (!kinds.Exists(kind => kind.ObjectId == saved.ObjectId))
                        file.Kinds.Add(saved);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, JsonUtility.ToJson(file));
                return path;
            }
            catch (Exception exception)
            {
                Debug.LogError($"{_ai.DisplayName} couldn't save what it learned to {path}: {exception.Message}", _ai);
                return null;
            }
        }

        public bool LoadFromFile(string fileName)
        {
            string path = FilePath(fileName);
            if (!System.IO.File.Exists(path))
                return false;
            try
            {
                var file = JsonUtility.FromJson<LearnedControlsFile>(System.IO.File.ReadAllText(path));
                return file != null && Take(file.Kinds);
            }
            catch (Exception exception)
            {
                Debug.LogError($"{_ai.DisplayName} couldn't read {path}: {exception.Message}", _ai);
                return false;
            }
        }

        /// <summary>Where a skill file lives: the player's save folder, under "AI Skills".</summary>
        public static string FilePath(string fileName)
        {
            string name = string.IsNullOrWhiteSpace(fileName) ? "AI Skill" : fileName.Trim();
            foreach (char bad in System.IO.Path.GetInvalidFileNameChars())
                name = name.Replace(bad, '_');
            return System.IO.Path.Combine(Application.persistentDataPath, "AI Skills", name + ".json");
        }

        // Takes kinds learned before; any that are running now start again from them.
        private bool Take(List<LearnedKind> kinds)
        {
            bool any = false;
            if (kinds == null)
                return false;
            foreach (var kind in kinds)
            {
                if (kind == null || kind.Parameters == null || kind.Parameters.Length == 0 || kind.Settings == null)
                    continue;
                _saved[kind.ObjectId] = kind;
                if (_kinds.TryGetValue(kind.ObjectId, out var running))
                {
                    running.Dispose();
                    _kinds.Remove(kind.ObjectId);
                }
                any = true;
            }
            return any;
        }

        private List<LearnedKind> Snapshot()
        {
            var kinds = new List<LearnedKind>();
            foreach (var entry in _kinds)
            {
                var kind = entry.Value.Snapshot(entry.Key, LayoutSignature);
                if (kind != null)
                {
                    kinds.Add(kind);
                    _saved[entry.Key] = kind;
                }
            }
            return kinds;
        }

        #endregion

        public void Dispose()
        {
            AutoSave();
            _live.Remove(this);
            Teacher = null;
            foreach (var kind in _kinds.Values)
                kind.Dispose();
            _kinds.Clear();
        }
    }
}
