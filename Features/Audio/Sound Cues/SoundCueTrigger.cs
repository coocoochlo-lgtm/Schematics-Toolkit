using BlueGraph;
using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Threading;
using UnityEngine;

namespace Remedy.Audio
{
    [SchematicDoc(@"{""name"": ""Trigger"", ""kind"": ""node"", ""flow"": ""query"", ""category"": ""Sound Cue/Controls"", ""description"": ""Fires a sound part way through a playing cue: when its Signal is sent to the object the cue plays on (a graph Signal, like the cue's own Inputs, or any other; sent to every object reaches it too), or when Fire rises past 0.5 (wire a Parameter). Time is -1 (silent) until then, and runs from 0 when it fires: plug it into a Sound Wave's, Generator's or Impact's Time to play that sound then. Once: it fires only the first time; Retrigger starts it again each time. Triggered is 0 before and 1 after; Value is the Signal's first value (a hit's strength, say). A cue with a Trigger plays with as little delay as possible; with Auto length it plays until it's fired, then as long as its sound. Drop a Signal on a Sound Cue to make one. In the preview it fires Preview Delay seconds in."", ""inputs"": [{""name"": ""Fire"", ""type"": ""float""}], ""outputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Triggered"", ""type"": ""float""}, {""name"": ""Value"", ""type"": ""float""}], ""use_for"": [""a looping engine that backfires when a Signal says so"", ""a weapon's charge-up loop with the shot fired by gameplay"", ""a sound that waits for an event to finish""]}")]
    [Serializable]
    [Node(Name = "Trigger", Path = "Controls"), Tags(SoundNode.Tag)]
    [AssetNode(typeof(SignalData), Title = "Trigger: {0}")]
    public class SoundTriggerNode : SoundNode
    {
        [Tooltip("Fires when this rises past 0.5 (wire a Parameter in), as well as on its Signal.")]
        [Input("Fire", Editable = true)] public float Fire;
        [Editable, Tooltip("The Signal that fires it, sent to the object the cue plays on (or to every object).")]
        public SignalData Signal;
        [Editable, Tooltip("Off: it fires only the first time. On: each time starts it again from 0.")]
        public bool Retrigger;
        [Editable, Tooltip("The editor's preview fires it this many seconds in (below 0: never).")]
        public float PreviewDelay = 0.5f;
        [Output("Time")] public float Time;
        [Output("Triggered")] public float Triggered;
        [Output("Value")] public float Value;

        public override SoundProcessor CreateProcessor(SoundCueBuilder builder)
        {
            builder.Context.LowLatency = true;
            var probe = new Probe { Node = this };
            builder.Context.Probes.Add(probe);
            return new Processor { Fire = builder.Input(this, "Fire", Fire), Retrigger = Retrigger, Place = probe, PreviewDelay = PreviewDelay };
        }

        // The world side: listens for the Signal on the object the cue plays on, and says when it's been sent.
        private sealed class Probe : SoundProbe
        {
            public SoundTriggerNode Node;
            /// <summary>1 when the Signal has been sent since the processor last looked (and the value it came with).</summary>
            public int Pending;
            public float PendingValue;
            /// <summary>Whether the cue is playing in the editor's preview (then Preview Delay fires it).</summary>
            public int Previewing;

            private GameObject _self, _host;
            private SignalData _signal;
            private int _uid, _hostUid;
            private SignalListener _listener;

            /// <summary>Listens again from scratch on the next update (a new play: the player may have a new address).</summary>
            public void Forget()
            {
                Unsubscribe();
                _self = _host = null;
                _signal = null;
            }

            private void Unsubscribe()
            {
                if (_signal != null && _listener != null)
                {
                    _signal.Unsubscribe(_uid, _listener);
                    if (_hostUid != _uid)
                        _signal.Unsubscribe(_hostUid, _listener);
                }
                _listener = null;
            }

            public override void Update(in SoundProbeWorld world)
            {
                Volatile.Write(ref Previewing, world.Previewing ? 1 : 0);
                var signal = Node.Signal;
                if (world.Previewing || (world.Self == _self && world.Host == _host && signal == _signal))
                    return;
                // A new object (or Signal) to listen on: the old subscription goes.
                Unsubscribe();
                _self = world.Self;
                _host = world.Host;
                _signal = signal;
                if (signal == null || _self == null)
                    return;
                // Sent to the object it belongs to (Self), or to the playing cue's own object, reaches it; so does sent
                // to every object. (Neither a Schematic Object nor addressable: it hears it whoever it's sent to.)
                _uid = SignalData.UIDOf(_self);
                _hostUid = _host != null ? SignalData.UIDOf(_host) : _uid;
                _listener = new GraphSignalListener(Heard);
                signal.Subscribe(_uid, _listener);
                if (_hostUid != _uid && _hostUid != 0)
                    signal.Subscribe(_hostUid, _listener);
                else
                    _hostUid = _uid;
            }

            private void Heard(Union[] args)
            {
                float value = 1f;
                if (args != null && args.Length > 0)
                {
                    try
                    {
                        var converted = SignalArgs.FromUnion(args[0], typeof(float));
                        if (converted is float f) value = f;
                    }
                    catch { } // a value that isn't a number: 1
                }
                Volatile.Write(ref PendingValue, value);
                Volatile.Write(ref Pending, 1);
            }
        }

        private sealed class Processor : SoundProcessor
        {
            public float[] Fire;
            public bool Retrigger;
            public Probe Place;
            public float PreviewDelay;
            private long _firedAt;
            private bool _fired, _previewed;
            private float _lastFire, _value;

            public override void Reset(SoundContext context)
            {
                _fired = _previewed = false;
                _lastFire = 0f;
                _value = 0f;
                Interlocked.Exchange(ref Place.Pending, 0);
                Place.Forget();
            }

            public override void Process(SoundContext context, int count)
            {
                var time = Output("Time");
                var triggered = Output("Triggered");
                var value = Output("Value");
                long start = context.SamplePosition;
                // Sent since the last block: fires at the start of this one (rendered as it's heard, so that's now).
                int fireAt = -1;
                if (Interlocked.Exchange(ref Place.Pending, 0) != 0)
                {
                    fireAt = 0;
                    _value = Volatile.Read(ref Place.PendingValue);
                }
                // The preview: Preview Delay seconds in.
                if (!_previewed && Volatile.Read(ref Place.Previewing) != 0 && PreviewDelay >= 0f)
                {
                    long at = (long)(PreviewDelay * context.SampleRate);
                    if (at < start + count)
                    {
                        _previewed = true;
                        int i = (int)Math.Max(0L, at - start);
                        fireAt = fireAt < 0 ? i : Math.Min(fireAt, i);
                        _value = 1f;
                    }
                }
                double step = 1.0 / context.SampleRate;
                for (int i = 0; i < count; i++)
                {
                    // Fire rising past 0.5 fires it too.
                    float fire = Fire[i];
                    bool rising = fire >= 0.5f && _lastFire < 0.5f;
                    _lastFire = fire;
                    if ((i == fireAt || rising) && (!_fired || Retrigger))
                    {
                        if (rising && i != fireAt)
                            _value = fire;
                        _fired = true;
                        _firedAt = start + i;
                        context.Fired(_firedAt);
                    }
                    time[i] = _fired ? (float)((start + i - _firedAt) * step) : -1f;
                    triggered[i] = _fired ? 1f : 0f;
                    value[i] = _value;
                }
            }
        }
    }
}
