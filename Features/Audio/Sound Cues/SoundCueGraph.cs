using BlueGraph;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Remedy.Audio
{
    /// <summary>
    /// A Sound Cue: a sound built like a shader. Every wire carries a value per sample, the way a shader's wires carry a
    /// value per pixel. A playback Time (seconds since the cue started) runs through math into the Time input of a
    /// Sound Wave (an audio file) or a Generator (sfxr-style synth), which read themselves at that time like a texture
    /// read at a UV. Their audio then runs through effects (filters, delay, reverb...) and mixers into the Output. Any
    /// input can be driven by a control (distance from the listener, a curve over time, a gameplay parameter, an
    /// oscillator...) or left as the number typed on the node.
    /// </summary>
    /// <remarks>
    /// Played by a <see cref="SoundCuePlayer"/> (the Play Sound Cue node makes one). Each play compiles the graph into a
    /// <see cref="SoundCueVoice"/>, which renders blocks of samples on the audio thread: per block, every node runs once
    /// over the whole block, so the graph costs a little per block rather than per sample.
    /// </remarks>
    [SchematicType("Audio", Order = 30, Icon = "AudioSource Icon", NewAssetName = "New Sound Cue", DefaultFolder = SoundCueGraph.Folder)]
    [IncludeTags(SoundNode.Tag)]
    [RequireNode(typeof(SoundCueOutput), "Output", 400, 0)]
    public partial class SoundCueGraph : SchematicGraph
    {
        /// <summary>Where new Sound Cues go: shown by the Audio page (Sound Cues) and the Schematics page (Audio).</summary>
        public const string Folder = "Assets/Content/Audio/Sound Cues";

        public override string Title => "SOUND CUE";

        /// <summary>How the cue is heard (switched by the MONO / STEREO button by the cue's title in its graph).</summary>
        public enum SoundCueChannels
        {
            /// <summary>Placed in the world: panned left and right and fading with distance (spatial).</summary>
            Stereo,
            /// <summary>Flat and centered wherever it plays (UI, music, the player's own voice).</summary>
            Mono,
        }

        [Tooltip("Stereo: placed in the world, panned and fading with distance. Mono: flat and centered wherever it plays.")]
        public SoundCueChannels Channels = SoundCueChannels.Stereo;

        /// <summary>Whether the cue is heard placed in the world (Stereo) rather than flat (Mono).</summary>
        public bool IsSpatial => Channels == SoundCueChannels.Stereo;

        /// <summary>Its Parameters, for Set Graph Parameters (and Play Sound Cue's pins).</summary>
        public override IReadOnlyList<GraphParameter> GraphParameters
            => ParameterList.Select(p => new GraphParameter(p.name, typeof(float), p.value)).ToList();

        /// <summary>The cue's Parameters (each name once), with their defaults: what a node that plays it can set.</summary>
        public List<(string name, float value)> ParameterList
        {
            get
            {
                var found = new List<(string, float)>();
                var names = new HashSet<string>();
                foreach (var node in Nodes)
                    if (node is SoundParameterNode parameter && !string.IsNullOrWhiteSpace(parameter.ParameterName) && names.Add(parameter.ParameterName.Trim()))
                        found.Add((parameter.ParameterName.Trim(), parameter.Default));
                return found;
            }
        }

        /// <summary>The cue's Output node (the first one, if there are several).</summary>
        public SoundCueOutput Output
        {
            get
            {
                foreach (var node in Nodes)
                    if (node is SoundCueOutput output)
                        return output;
                return null;
            }
        }
    }

    /// <summary>
    /// A node in a Sound Cue. It makes a <see cref="SoundProcessor"/> for each voice that plays the cue, which fills its
    /// outputs a block of samples at a time from its inputs' blocks.
    /// </summary>
    [Serializable]
    [NodeColor(NodeColors.Audio)]
    public abstract class SoundNode : SchematicGraphNode
    {
        /// <summary>The tag every Sound Cue node has: Sound Cue graphs only offer nodes with it.</summary>
        public const string Tag = "Sound Cue";

        /// <summary>Makes this node's processor for one voice, asking the builder for its inputs (main thread).</summary>
        public abstract SoundProcessor CreateProcessor(SoundCueBuilder builder);

        // Sound Cue nodes aren't run by Schematic flows.
        public override object OnRequestValue(Port port) => null;
    }

    /// <summary>One node's work for one voice: fills its output blocks from its input blocks.</summary>
    public abstract class SoundProcessor
    {
        internal readonly Dictionary<string, float[]> Outputs = new();

        /// <summary>The block this processor writes an output into (made the first time it's asked for).</summary>
        public float[] Output(string port)
        {
            if (!Outputs.TryGetValue(port, out var buffer))
                Outputs[port] = buffer = new float[SoundCueBuilder.BlockSize];
            return buffer;
        }

        /// <summary>Fills the first <paramref name="count"/> samples of the outputs (audio thread).</summary>
        public abstract void Process(SoundContext context, int count);

        /// <summary>
        /// Back to how it was when built, for a voice played again (filters' memory, delay lines, random picks...).
        /// Called before every play, on the main thread, while the voice isn't rendering.
        /// </summary>
        public virtual void Reset(SoundContext context) { }
    }

    /// <summary>A processor with nothing to do (its outputs are filled by someone else, or are constants).</summary>
    public sealed class StaticProcessor : SoundProcessor
    {
        public override void Process(SoundContext context, int count) { }
    }

    /// <summary>What a voice's processors share while rendering: the sample rate, where it is, and its controls.</summary>
    public sealed class SoundContext
    {
        public int SampleRate;
        /// <summary>The first sample of the block being rendered, counted from the start of the cue.</summary>
        public long SamplePosition;
        /// <summary>How long the cue plays, in seconds (0 when it plays until it's stopped).</summary>
        public float Duration;
        /// <summary>Distance from the voice to the Audio Listener, in meters (set by the player each frame).</summary>
        public volatile float Distance;
        /// <summary>Listener node: degrees between where the listener faces and the sound (0 ahead, 180 behind).</summary>
        public volatile float ListenerAngle;
        /// <summary>Listener node: -1 left of the listener to 1 right.</summary>
        public volatile float ListenerPan;
        /// <summary>Listener node: 1 ahead of the listener, -1 behind.</summary>
        public volatile float ListenerFront = 1f;
        /// <summary>Listener node: degrees above the listener (negative below).</summary>
        public volatile float ListenerElevation;
        /// <summary>Listener node: how fast the sound and listener are closing in (m/s; negative parting).</summary>
        public volatile float ApproachSpeed;
        /// <summary>Listener node: 1 when the sound and listener are in the same Sound Room (or neither is in one), else 0.</summary>
        public volatile float SameRoom = 1f;
        /// <summary>A Listener node reads the above: the player works them out each frame (only then).</summary>
        public bool FollowsListener;
        /// <summary>The cue's parameters' values, by index (see <see cref="SoundCueBuilder.ParameterIndex"/>).</summary>
        public float[] Parameters = Array.Empty<float>();
        /// <summary>Random numbers for this voice (seeded per play).</summary>
        public System.Random Random = new();
        /// <summary>
        /// True when a Reverb sends to the scene's shared reverb (Reverb Zones) rather than running its own; the player
        /// then sets its AudioSource's Reverb Zone Mix to <see cref="ReverbSend"/>.
        /// </summary>
        public bool UsesSharedReverb;
        /// <summary>How much goes to the shared reverb (0 to 1.1, Unity's Reverb Zone Mix), set by a shared Reverb.</summary>
        public volatile float ReverbSend;
        /// <summary>
        /// Questions about the world the voice's nodes ask (what's between it and the listener, what it's standing on),
        /// answered by the player on the main thread a few times a second (see <see cref="SoundProbe"/>).
        /// </summary>
        public readonly List<SoundProbe> Probes = new();
        /// <summary>
        /// When the cue's sample 0 is heard, on the audio clock (<see cref="AudioSettings.dspTime"/>), or below 0 when
        /// unknown (the editor's preview, a pitched source). The voice renders ahead of what's heard; this says when
        /// each sample really sounds, so sends to the world's rooms and echoes line up with it.
        /// </summary>
        public double HeardStart = -1;

        /// <summary>
        /// The Listener Chain's input: one channel of everything the player hears, for the Listener Input node (null
        /// when the cue isn't running as a Listener Chain). <see cref="BlockStart"/> is where the block being rendered
        /// starts in it.
        /// </summary>
        public float[] ListenerInput;
        public int BlockStart;

        /// <summary>
        /// The cue reacts as it plays (a Trigger): it's played with as little delay as possible (rendered as it's
        /// heard, not ahead), so what fires it is heard at once.
        /// </summary>
        public bool LowLatency;
        /// <summary>The cue has Parameters the game sets as it plays (a Parameter node feeds its sound).</summary>
        public bool HasParameters;
        /// <summary>
        /// Rendered as it's heard (a block at a time, from the audio thread) rather than ahead in a streamed clip: for
        /// cues that change as they play, so a change is heard at once, not a second or two later. Set when it's built
        /// (Output's Rendering; Auto: with a Trigger or Parameters).
        /// </summary>
        public bool Live;
        /// <summary>How much later than when they're heard sends to the world's spaces go (low latency: past what they're playing).</summary>
        public int SendLead;
        /// <summary>
        /// An Auto-length cue with a Trigger: how long it plays on after it's fired (its longest sound), in seconds;
        /// it plays until then. 0: not that kind of cue.
        /// </summary>
        public float AfterTrigger;
        /// <summary>The sample it ends at (set when a Trigger fires; long.MaxValue: by its length alone).</summary>
        public long EndAt = long.MaxValue;

        /// <summary>A Trigger fired at a sample: an Auto-length cue now ends once its sound has played from there.</summary>
        public void Fired(long sample)
        {
            if (AfterTrigger <= 0f)
                return;
            long end = sample + (long)Math.Ceiling(AfterTrigger * SampleRate);
            EndAt = EndAt == long.MaxValue ? end : Math.Max(EndAt, end);
        }

        /// <summary>The audio-clock sample a sample of the cue is heard at (meaningless when HeardStart is below 0).</summary>
        public long HeardSample(long cueSample) => (long)Math.Round(HeardStart * SampleRate) + cueSample;
    }

    /// <summary>Where a playing voice is, for <see cref="SoundProbe"/>s.</summary>
    public struct SoundProbeWorld
    {
        /// <summary>The player's object (its own body isn't hit by casts from it).</summary>
        public GameObject Self;
        public Vector3 Position;
        /// <summary>The player's orientation (for local directions).</summary>
        public Quaternion Rotation;
        /// <summary>The Audio Listener's object (null when there isn't one).</summary>
        public GameObject Listener;
        public Vector3 ListenerPosition;
        /// <summary>
        /// True in the editor's preview, where there's no world: probes give their node's preview values.
        /// </summary>
        public bool Previewing;
        /// <summary>The Sound Room the sound is in (null when none, or the player doesn't use rooms).</summary>
        public SoundRoom Room;
        /// <summary>The player's own object (Signals sent to it reach the cue's Triggers, as do those sent to Self).</summary>
        public GameObject Host;
        /// <summary>
        /// How loud the player's own source makes the sound at a distance (its volume and distance falloff), 0 to 1: for
        /// sound that reaches the listener some other way (round a corner) falling off as the sound itself would.
        /// </summary>
        public Func<float, float> Hearing;
    }

    /// <summary>
    /// A question a Sound Cue node asks about the world (physics, surfaces...), which the audio thread can't: the
    /// player calls <see cref="Update"/> on the main thread (when the cue starts, then about 10 times a second), and
    /// the node's processor reads the answers it left in volatile fields, smoothing between them.
    /// </summary>
    public abstract class SoundProbe
    {
        public abstract void Update(in SoundProbeWorld world);
    }

    /// <summary>
    /// Compiles a Sound Cue into processors for one voice: each node once, in an order where every node comes after
    /// the nodes it reads (main thread).
    /// </summary>
    public sealed class SoundCueBuilder
    {
        /// <summary>The most samples a block holds.</summary>
        public const int BlockSize = 1024;

        public readonly int SampleRate;
        public readonly SoundContext Context;
        internal readonly List<SoundProcessor> Order = new();

        /// <summary>The cue's playback time in seconds, per sample (what an unconnected Time input reads).</summary>
        public readonly float[] Time = new float[BlockSize];
        /// <summary>Time / Duration (0 to 1), per sample; 0 when the cue has no set length.</summary>
        public readonly float[] Progress = new float[BlockSize];

        /// <summary>The longest Sound Wave or Generator the cue plays (for Auto length), in seconds.</summary>
        public float LongestSource { get; private set; }

        /// <summary>Problems found while building (shown in the graph window).</summary>
        public readonly List<string> Warnings = new();

        private readonly Dictionary<SoundNode, SoundProcessor> _built = new();
        private readonly HashSet<SoundNode> _building = new();
        private readonly Dictionary<string, int> _parameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<float> _parameterDefaults = new();

        public SoundCueBuilder(int sampleRate, SoundContext context)
        {
            SampleRate = sampleRate;
            Context = context;
        }

        /// <summary>
        /// The block an input reads: the output it's connected to, or a block of its typed-in value.
        /// </summary>
        public float[] Input(SoundNode node, string port, float constant)
            => Connected(node, port) ?? Constant(constant);

        /// <summary>A Time input: the output it's connected to, or the cue's own playback time.</summary>
        public float[] TimeInput(SoundNode node, string port)
            => Connected(node, port) ?? Time;

        /// <summary>Whether something is connected to an input.</summary>
        public bool IsConnected(SoundNode node, string port)
        {
            var input = node.GetPort(port);
            if (input == null)
                return false;
            foreach (var source in input.ConnectedPorts)
                if (source?.Node is SoundNode || source?.Node is SchematicGraphNode)
                    return true;
            return false;
        }

        /// <summary>A block holding one value (for unconnected inputs).</summary>
        public float[] Constant(float value)
        {
            var buffer = new float[BlockSize];
            if (value != 0f)
                for (int i = 0; i < buffer.Length; i++)
                    buffer[i] = value;
            return buffer;
        }

        /// <summary>The slot a named parameter's value is kept in (made the first time it's asked for).</summary>
        public int ParameterIndex(string name, float defaultValue)
        {
            name = (name ?? "").Trim();
            if (_parameters.TryGetValue(name, out int index))
                return index;
            index = _parameterDefaults.Count;
            _parameters[name] = index;
            _parameterDefaults.Add(defaultValue);
            return index;
        }

        internal IReadOnlyDictionary<string, int> Parameters => _parameters;
        internal float[] ParameterDefaults => _parameterDefaults.ToArray();

        /// <summary>
        /// Adds a helper processor to the voice (one a node makes for itself, not another node's), run before the
        /// processor being built now: call it from <see cref="SoundNode.CreateProcessor"/> and read its outputs.
        /// </summary>
        public void AddHelper(SoundProcessor helper)
        {
            if (helper != null)
                Order.Add(helper);
        }

        /// <summary>
        /// How much longer than its length the cue goes on (seconds): sound that's still on its way when it ends (Travel
        /// Time). Its Progress still runs over its length.
        /// </summary>
        public float Tail { get; private set; }

        /// <summary>Notes that the cue's sound can arrive up to <paramref name="seconds"/> after it's made.</summary>
        public void NoteTail(float seconds)
        {
            if (seconds > Tail)
                Tail = seconds;
        }

        /// <summary>Notes a source's length (a Sound Wave's clip, a Generator's sound) for Auto length.</summary>
        public void NoteSourceLength(float seconds)
        {
            if (seconds > LongestSource)
                LongestSource = seconds;
        }

        private float[] Connected(SoundNode node, string port)
        {
            var input = node.GetPort(port);
            if (input == null)
                return null;
            foreach (var source in input.ConnectedPorts)
            {
                if (source?.Node is SoundNode sourceNode)
                    return Build(sourceNode)?.Output(source.Name);
                // A plain value node (Float Value...), which works in any graph: its value, read once as the cue is built.
                if (source?.Node is SchematicGraphNode valueNode)
                    return Constant(ValueOf(valueNode, source));
            }
            return null;
        }

        private float ValueOf(SchematicGraphNode node, Port port)
        {
            try
            {
                return node.OnRequestValue(port) switch
                {
                    float f => f,
                    int i => i,
                    bool b => b ? 1f : 0f,
                    double d => (float)d,
                    IConvertible convertible => Convert.ToSingle(convertible, System.Globalization.CultureInfo.InvariantCulture),
                    _ => 0f,
                };
            }
            catch (Exception exception)
            {
                Warnings.Add($"\"{node.Name}\" couldn't give a value: {exception.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// The processors of every node feeding an input, however far back (each built if it isn't yet). A Spatial
        /// effect finds the Trails before it this way.
        /// </summary>
        public List<SoundProcessor> Upstream(SoundNode node, string port)
        {
            var found = new List<SoundProcessor>();
            var seen = new HashSet<SoundNode> { node };
            var waiting = new Stack<Port>();
            var first = node.GetPort(port);
            if (first != null)
                waiting.Push(first);
            while (waiting.Count > 0)
            {
                foreach (var source in waiting.Pop().ConnectedPorts)
                {
                    if (!(source?.Node is SoundNode sourceNode) || !seen.Add(sourceNode))
                        continue;
                    var built = Build(sourceNode);
                    if (built != null)
                        found.Add(built);
                    foreach (var other in sourceNode.Ports.Values)
                        if (other.Direction == PortDirection.Input)
                            waiting.Push(other);
                }
            }
            return found;
        }

        /// <summary>A node's processor for this voice (built the first time it's asked for).</summary>
        public SoundProcessor Build(SoundNode node)
        {
            if (_built.TryGetValue(node, out var built))
                return built;
            if (!_building.Add(node))
            {
                Warnings.Add($"\"{node.Name}\" is wired into a loop (it ends up reading its own output): that wire reads silence.");
                return null;
            }
            SoundProcessor processor;
            try
            {
                processor = node.CreateProcessor(this) ?? new StaticProcessor();
            }
            catch (Exception exception)
            {
                Warnings.Add($"\"{node.Name}\" couldn't be built: {exception.Message}");
                Debug.LogException(exception);
                processor = new StaticProcessor();
            }
            _building.Remove(node);
            _built[node] = processor;
            Order.Add(processor);
            return processor;
        }
    }

    /// <summary>
    /// One playing instance of a Sound Cue: its compiled processors and where it is in the sound. Made on the main
    /// thread (<see cref="Create"/>), rendered on the audio thread (<see cref="Render"/>).
    /// </summary>
    public sealed class SoundCueVoice
    {
        public readonly SoundCueGraph Cue;
        public readonly SoundContext Context;
        /// <summary>How long it plays, in samples (0: until it's stopped).</summary>
        public readonly long LengthSamples;
        public readonly IReadOnlyList<string> Warnings;

        private readonly SoundProcessor[] _order;
        private readonly float[] _audio, _volume;
        private readonly Dictionary<string, int> _parameters;
        private readonly float[] _parameterDefaults;
        private volatile bool _finished;
        private bool _failed;

        /// <summary>
        /// Too far away (or too quiet) to be heard: the player sets it, and rendering is skipped while it's on, the
        /// cue's time still running so it carries on from the right place when it's heard again.
        /// </summary>
        public volatile bool Culled;

        /// <summary>The cue's state when this voice was built (editor: its edit count), so a pooled voice of an edited cue isn't reused.</summary>
        internal int BuiltVersion;
        /// <summary>The sample rate it was built for.</summary>
        public int SampleRate => Context.SampleRate;

        /// <summary>True once it has played its whole length.</summary>
        public bool Finished => _finished;

        /// <summary>Seconds played so far.</summary>
        public float Time => Context.SampleRate > 0 ? (float)(Context.SamplePosition / (double)Context.SampleRate) : 0f;

        private SoundCueVoice(SoundCueGraph cue, SoundContext context, SoundCueBuilder builder, float[] audio, float[] volume, long length)
        {
            Cue = cue;
            Context = context;
            _order = builder.Order.ToArray();
            _audio = audio;
            _volume = volume;
            LengthSamples = length;
            Warnings = builder.Warnings;
            _parameters = new Dictionary<string, int>(builder.Parameters, StringComparer.OrdinalIgnoreCase);
            _parameterDefaults = builder.ParameterDefaults;
            context.Parameters = (float[])_parameterDefaults.Clone();
            _time = builder.Time;
            _progress = builder.Progress;
        }

        /// <summary>Compiles a cue for playing at a sample rate (main thread). Null when there's no cue.</summary>
        public static SoundCueVoice Create(SoundCueGraph cue, int sampleRate, int seed = 0)
        {
            if (cue == null)
                return null;
            sampleRate = Mathf.Max(8000, sampleRate);
            var context = new SoundContext
            {
                SampleRate = sampleRate,
                Random = seed != 0 ? new System.Random(seed) : new System.Random(),
            };
            var builder = new SoundCueBuilder(sampleRate, context);

            var output = cue.Output;
            float[] audio, volume;
            long length = 0;
            if (output == null)
            {
                builder.Warnings.Add("The cue has no Output node, so it's silent.");
                audio = builder.Constant(0f);
                volume = builder.Constant(0f);
            }
            else
            {
                audio = builder.Input(output, SoundCueOutput.AudioPort, 0f);
                volume = builder.Input(output, SoundCueOutput.VolumePort, output.Volume);
                float seconds = output.Length switch
                {
                    SoundCueOutput.LengthMode.Fixed => output.Duration,
                    SoundCueOutput.LengthMode.Auto => builder.LongestSource,
                    _ => 0f,
                };
                if (output.Length == SoundCueOutput.LengthMode.Auto && seconds <= 0f)
                    builder.Warnings.Add("Length is Auto but nothing in the cue has a length (no Sound Wave or Generator), so it plays until it's stopped.");
                context.Duration = Mathf.Max(0f, seconds);
                length = context.Duration > 0f ? (long)Math.Ceiling((context.Duration + builder.Tail) * sampleRate) : 0;
                // Auto with a Trigger: it plays until it's fired, then for as long as its sound.
                if (output.Length == SoundCueOutput.LengthMode.Auto && context.LowLatency && length > 0)
                {
                    context.AfterTrigger = context.Duration + builder.Tail;
                    length = 0;
                }
                context.Live = context.LowLatency || output.Rendering switch
                {
                    SoundCueOutput.RenderMode.Live => true,
                    SoundCueOutput.RenderMode.Ahead => false,
                    _ => context.HasParameters || context.FollowsListener,
                };
            }
            var voice = new SoundCueVoice(cue, context, builder, audio, volume, length) { BuiltVersion = SoundCueVoicePool.Version(cue) };
            voice.Restart(seed);
            return voice;
        }

        /// <summary>
        /// Back to the start, to be played again without being built again (main thread, while it isn't rendering):
        /// time, parameters, random picks and every node's memory.
        /// </summary>
        public void Restart(int seed = 0)
        {
            Context.SamplePosition = 0;
            Context.Distance = 0f;
            Context.ListenerAngle = 0f;
            Context.ListenerPan = 0f;
            Context.ListenerFront = 1f;
            Context.ListenerElevation = 0f;
            Context.ApproachSpeed = 0f;
            Context.SameRoom = 1f;
            Context.ReverbSend = 0f;
            Context.HeardStart = -1;
            Context.EndAt = long.MaxValue;
            Context.Random = seed != 0 ? new System.Random(seed) : new System.Random();
            Array.Copy(_parameterDefaults, Context.Parameters, _parameterDefaults.Length);
            _finished = false;
            _failed = false;
            Culled = false;
            foreach (var processor in _order)
                processor.Reset(Context);
        }

        /// <summary>Sets a parameter (a Parameter node's value) by name. Unknown names are ignored.</summary>
        public void SetParameter(string name, float value)
        {
            if (name != null && _parameters.TryGetValue(name.Trim(), out int index) && index < Context.Parameters.Length)
                Context.Parameters[index] = value;
        }

        /// <summary>Whether the cue has a parameter with this name.</summary>
        public bool HasParameter(string name) => name != null && _parameters.ContainsKey(name.Trim());

        /// <summary>
        /// Writes the next <paramref name="count"/> mono samples into <paramref name="data"/> from
        /// <paramref name="offset"/> (audio thread). Silence past the cue's end.
        /// </summary>
        public void Render(float[] data, int offset, int count)
        {
            if (_failed)
            {
                Array.Clear(data, offset, count);
                return;
            }
            if (Culled)
            {
                // Not heard: silence, but time goes on (and the end comes when it should).
                Array.Clear(data, offset, count);
                Context.SamplePosition += count;
                if ((LengthSamples > 0 && Context.SamplePosition >= LengthSamples) || Context.SamplePosition >= Context.EndAt)
                    _finished = true;
                return;
            }
            int written = 0;
            try
            {
                while (written < count)
                {
                    int n = Math.Min(SoundCueBuilder.BlockSize, count - written);
                    if (LengthSamples > 0 || Context.EndAt != long.MaxValue)
                    {
                        long end = LengthSamples > 0 ? Math.Min(LengthSamples, Context.EndAt) : Context.EndAt;
                        long left = end - Context.SamplePosition;
                        if (left <= 0)
                        {
                            _finished = true;
                            break;
                        }
                        if (left < n)
                            n = (int)left;
                    }

                    FillTime(n);
                    Context.BlockStart = written;
                    for (int p = 0; p < _order.Length; p++)
                        _order[p].Process(Context, n);
                    for (int i = 0; i < n; i++)
                    {
                        float sample = _audio[i] * _volume[i];
                        // NaN or infinite (a divide by zero upstream) would break the whole mix: silence instead.
                        data[offset + written + i] = float.IsNaN(sample) || float.IsInfinity(sample) ? 0f : sample;
                    }
                    Context.SamplePosition += n;
                    written += n;
                }
            }
            catch (Exception exception)
            {
                _failed = true;
                Debug.LogException(exception);
            }
            if (written < count)
                Array.Clear(data, offset + written, count - written);
        }

        // The cue's time (and progress) for the block: the builder's shared blocks, which unconnected Time inputs and
        // the Time node read.
        private readonly float[] _time, _progress;
        private void FillTime(int count)
        {
            double rate = Context.SampleRate;
            float duration = Context.Duration;
            for (int i = 0; i < count; i++)
            {
                double t = (Context.SamplePosition + i) / rate;
                _time[i] = (float)t;
                _progress[i] = duration > 0f ? Mathf.Clamp01((float)(t / duration)) : 0f;
            }
        }
    }

    /// <summary>
    /// Audio files as samples: each clip read once (mixed down to mono) and kept, since clips can only be read on the
    /// main thread and voices read them on the audio thread.
    /// </summary>
    public static class SoundClipCache
    {
        public sealed class Entry
        {
            public float[] Samples;
            public int Frequency;
            public float Length => Frequency > 0 ? Samples.Length / (float)Frequency : 0f;
        }

        private static readonly Dictionary<AudioClip, Entry> _entries = new();

        /// <summary>A clip's samples (main thread). Null when it can't be read (see the warning it logs).</summary>
        public static Entry Get(AudioClip clip)
        {
            if (clip == null)
                return null;
            if (_entries.TryGetValue(clip, out var entry))
                return entry;

            if (clip.loadState != AudioDataLoadState.Loaded)
                clip.LoadAudioData();
            int channels = Mathf.Max(1, clip.channels);
            var interleaved = new float[clip.samples * channels];
            if (clip.samples == 0 || !clip.GetData(interleaved, 0))
            {
                Debug.LogWarning($"Sound Cue: couldn't read \"{clip.name}\". Set its Load Type to Decompress On Load (and untick Load In Background) in its import settings.", clip);
                return null;
            }
            var mono = new float[clip.samples];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0f;
                for (int c = 0; c < channels; c++)
                    sum += interleaved[i * channels + c];
                mono[i] = sum / channels;
            }
            return _entries[clip] = new Entry { Samples = mono, Frequency = clip.frequency };
        }

        /// <summary>Forgets a clip (after it's reimported).</summary>
        public static void Forget(AudioClip clip)
        {
            if (clip != null)
                _entries.Remove(clip);
        }

        public static void Clear() => _entries.Clear();
    }

    /// <summary>
    /// An AnimationCurve as a lookup table, so the audio thread can read it (curves can only be evaluated safely on the
    /// main thread). Linear between 512 points across the curve's keys.
    /// </summary>
    public sealed class CurveTable
    {
        private const int Points = 512;
        private readonly float[] _values = new float[Points];
        private readonly float _start, _end;

        public CurveTable(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0)
            {
                _start = 0f;
                _end = 1f;
                return;
            }
            _start = curve.keys[0].time;
            _end = curve.keys[curve.length - 1].time;
            if (_end <= _start)
                _end = _start + 1f;
            for (int i = 0; i < Points; i++)
                _values[i] = curve.Evaluate(Mathf.Lerp(_start, _end, i / (float)(Points - 1)));
        }

        /// <summary>The curve's value at x (held at its first and last values outside its keys).</summary>
        public float Evaluate(float x)
        {
            float u = (x - _start) / (_end - _start) * (Points - 1);
            if (!(u > 0f)) return _values[0]; // also NaN
            if (u >= Points - 1) return _values[Points - 1];
            int i = (int)u;
            float f = u - i;
            return _values[i] + (_values[i + 1] - _values[i]) * f;
        }
    }
}
