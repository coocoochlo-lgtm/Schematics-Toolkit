using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Remedy.Schematics.TimelineGraphs
{
    /// <summary>
    /// Plays a Timeline Graph on one object: a child GameObject with its own Playable Director (so an object can play
    /// several), made the first time it's needed. Tells the graph when the Timeline starts, pauses, resumes and finishes,
    /// and carries out the Schematic markers. The graph's flows run as the object (its "self").
    /// </summary>
    [AddComponentMenu("")]
    public class TimelineGraphPlayer : MonoBehaviour, INotificationReceiver
    {
        public TimelineGraph Graph { get; private set; }

        /// <summary>The object playing the Timeline (its Schematic Object, if it has one).</summary>
        public GameObject Host { get; private set; }

        public PlayableDirector Director { get; private set; }

        private SchematicObject _owner;
        private readonly List<(SignalData signal, SignalListener listener, int uid)> _subscriptions = new();
        private bool _stopping, _resuming;
        private float _speed = 1f;

        public bool IsPlaying => Director != null && Director.state == PlayState.Playing;
        public double Time => Director != null ? Director.time : 0;
        public double Duration => Director != null ? Director.duration : 0;

        #region Finding

        /// <summary>
        /// The player for a Timeline Graph on an object (its Schematic Object, if it's part of one). Made when
        /// <paramref name="create"/> is set and there isn't one yet.
        /// </summary>
        public static TimelineGraphPlayer For(GameObject target, TimelineGraph graph, bool create = true)
        {
            if (target == null || graph == null)
                return null;
            var owner = target.GetComponentInParent<SchematicObject>();
            var host = owner != null ? owner.gameObject : target;
            foreach (Transform child in host.transform)
                if (child.TryGetComponent(out TimelineGraphPlayer existing) && existing.Graph == graph)
                    return existing;
            if (!create)
                return null;

            var holder = new GameObject($"[Timeline] {graph.name}");
            holder.transform.SetParent(host.transform, false);
            var director = holder.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            var player = holder.AddComponent<TimelineGraphPlayer>();
            player.Setup(graph, host, owner, director);
            return player;
        }

        /// <summary>The Timeline Graph to act on: the given one, or the graph running the flow if it is a Timeline Graph.</summary>
        public static TimelineGraph Resolve(TimelineGraph given, SchematicGraphNode node) => given != null ? given : node?.Graph as TimelineGraph;

        #endregion

        #region Setup

        private void Setup(TimelineGraph graph, GameObject host, SchematicObject owner, PlayableDirector director)
        {
            Graph = graph;
            Host = host;
            _owner = owner;
            Director = director;
            director.playableAsset = graph.Timeline;
            director.extrapolationMode = graph.WrapMode;
            director.timeUpdateMode = graph.UpdateMode;
            director.played += _ => OnPlayed();
            director.paused += _ => OnPaused();
            director.stopped += _ => OnStopped();
            BindTracks();
            SubscribeSignals();
        }

        // Schematic tracks talk to this player; other tracks bind by the graph's list, a child's name, or the object.
        private void BindTracks()
        {
            if (Graph.Timeline == null)
                return;
            foreach (var track in Graph.Timeline.GetOutputTracks())
            {
                if (track is SchematicTrack)
                {
                    Director.SetGenericBinding(track, gameObject);
                    continue;
                }
                string path = Graph.PathFor(track.name);
                var target = path != null ? (string.IsNullOrEmpty(path) ? Host.transform : Host.transform.Find(path))
                                          : Host.transform.Find(track.name) ?? Host.transform;
                if (target == null)
                    continue;
                Director.SetGenericBinding(track, BindingFor(track, target.gameObject));
            }
        }

        // A track wants a particular component (an Animator, an AudioSource...) or the GameObject itself.
        private static UnityEngine.Object BindingFor(TrackAsset track, GameObject target)
        {
            foreach (var output in track.outputs)
            {
                var type = output.outputTargetType;
                if (type == null || type == typeof(GameObject))
                    return target;
                if (typeof(Component).IsAssignableFrom(type))
                    return target.GetComponentInChildren(type, true) ?? target.AddComponent(type);
                return target;
            }
            return target;
        }

        /// <summary>Binds a track (by name) to an object, e.g. a camera or character in the scene.</summary>
        public bool Bind(string trackName, UnityEngine.Object value)
        {
            if (Graph.Timeline == null)
                return false;
            foreach (var track in Graph.Timeline.GetOutputTracks())
                if (track.name == trackName)
                {
                    var binding = value is GameObject go ? BindingFor(track, go) : value is Component c ? BindingFor(track, c.gameObject) : value;
                    Director.SetGenericBinding(track, binding);
                    return true;
                }
            return false;
        }

        // "On Signal Received" nodes of a Timeline Graph that isn't the object's own graph still hear Signals sent to
        // the object, for as long as it's playing it.
        private void SubscribeSignals()
        {
            if (_owner == null || _owner.SchematicGraph == Graph || _owner.UID == 0)
                return;
            foreach (var node in Graph.FlowOnInvokeCache)
            {
                if (node == null || node.Signal == null)
                    continue;
                var receiver = node;
                bool invoking = false;
                var listener = new GraphSignalListener(args =>
                {
                    if (invoking) return;
                    try { invoking = true; receiver.Trigger(Host, args); }
                    finally { invoking = false; }
                });
                node.Signal.Subscribe(_owner.UID, listener);
                _subscriptions.Add((node.Signal, listener, _owner.UID));
            }
        }

        private void OnDestroy()
        {
            foreach (var (signal, listener, uid) in _subscriptions)
                signal.Unsubscribe(uid, listener);
            _subscriptions.Clear();
        }

        #endregion

        #region Control

        public void Play(bool fromStart = true)
        {
            if (Director == null || Graph.Timeline == null)
            {
                Debug.LogWarning($"{(Graph != null ? Graph.name : "This Timeline Graph")} has no Timeline to play.", Graph);
                return;
            }
            if (fromStart)
                Director.time = 0;
            Director.Play();
            ApplySpeed();
        }

        public void Pause()
        {
            if (Director != null && Director.state == PlayState.Playing)
                Director.Pause();
        }

        public void Resume()
        {
            if (Director == null) return;
            if (Director.state == PlayState.Paused)
            {
                _resuming = true;
                Director.Resume();
                _resuming = false;
                ApplySpeed();
                Trigger<OnTimelineResumed>();
            }
        }

        public void Stop()
        {
            if (Director == null) return;
            _stopping = true;
            Director.Stop();
            _stopping = false;
        }

        public void Seek(double time)
        {
            if (Director == null) return;
            Director.time = Math.Max(0, Math.Min(time, Duration));
            Director.Evaluate();
        }

        /// <summary>Jumps to an Event marker by name. False if the Timeline has none by that name.</summary>
        public bool SeekMarker(string name)
        {
            if (Graph.Timeline == null) return false;
            foreach (var marker in AllMarkers())
                if (marker is EventMarker evt && evt.EventName == name)
                {
                    Seek(marker.time);
                    return true;
                }
            return false;
        }

        public void SetSpeed(float speed)
        {
            _speed = speed;
            ApplySpeed();
        }

        private void ApplySpeed()
        {
            if (Director != null && Director.playableGraph.IsValid() && Director.playableGraph.GetRootPlayableCount() > 0)
                Director.playableGraph.GetRootPlayable(0).SetSpeed(_speed);
        }

        private IEnumerable<IMarker> AllMarkers()
        {
            if (Graph.Timeline.markerTrack != null)
                foreach (var marker in Graph.Timeline.markerTrack.GetMarkers())
                    yield return marker;
            foreach (var track in Graph.Timeline.GetOutputTracks())
                foreach (var marker in track.GetMarkers())
                    yield return marker;
        }

        #endregion

        #region Graph events

        private void OnPlayed()
        {
            if (!_resuming)
                Trigger<OnTimelineStarted>();
        }
        private void OnPaused() => Trigger<OnTimelinePaused>();

        // Stopped by reaching the end, or by Stop.
        private void OnStopped() => Trigger<OnTimelineFinished>(new Union(!_stopping));

        private void Trigger<T>(params Union[] args) where T : SchematicEventNode
        {
            if (Graph != null)
                Graph.TriggerEvent<T>(Host, args);
        }

        public void OnNotify(Playable origin, INotification notification, object context)
        {
            switch (notification)
            {
                case SignalMarker signal: Send(signal); break;
                case VariableMarker variable when variable.Target.ValueType != null:
                    variable.Target.Apply(Host, Graph, variable.Value.Get(variable.Target.ValueType));
                    break;
                case EventMarker evt:
                    Trigger<OnTimelineMarker>(new Union(evt.EventName ?? ""));
                    break;
            }
        }

        private void Send(SignalMarker marker)
        {
            if (marker.Signal == null)
                return;
            int uid = 0;
            if (!marker.Everyone)
            {
                var target = string.IsNullOrEmpty(marker.TargetPath) ? Host.transform : Host.transform.Find(marker.TargetPath);
                if (target == null || SignalData.UIDResolver == null)
                    return;
                uid = SignalData.UIDResolver(target.gameObject);
                if (uid == 0)
                    return;
            }
            var args = new Union[marker.Arguments.Count];
            for (int i = 0; i < args.Length; i++)
            {
                var argument = marker.Arguments[i];
                var type = ReferenceEquals(argument.Type, null) ? null : argument.Type.Type;
                args[i] = type != null ? SignalArgs.ToUnion<object>(argument.Value.Get(type)) : Union.Null;
            }
            marker.Signal.InvokeFromGraph(uid, args);
        }

        #endregion

        // Timeline Graphs that are an object's own graph and play on start.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install() => SchematicObject.Started += obj =>
        {
            if (obj.SchematicGraph is TimelineGraph timeline && timeline.PlayOnStart)
                For(obj.gameObject, timeline)?.Play();
        };
    }
}
