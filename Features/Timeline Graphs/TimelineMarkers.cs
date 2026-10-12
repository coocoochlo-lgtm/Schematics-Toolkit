using BlueGraph.Utils;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using DisplayName = System.ComponentModel.DisplayNameAttribute;

namespace Remedy.Schematics.TimelineGraphs
{
    /// <summary>
    /// The Schematic track: put Signal, Variable and Event markers on it, and Variable clips that blend a Variable over
    /// time. It's bound to the Timeline's player automatically, so there's nothing to bind.
    /// </summary>
    [Serializable]
    [TrackColor(0.93f, 0.52f, 0.78f)]
    [TrackBindingType(typeof(GameObject))]
    [TrackClipType(typeof(VariableClip))]
    public class SchematicTrack : TrackAsset { }

    /// <summary>Which Variable a marker or clip sets: a graph Variable, or a Behavior Variable of the object playing it.</summary>
    [Serializable]
    public class VariableTarget
    {
        public enum TargetKind { GraphVariable, BehaviorVariable }

        public TargetKind Kind;

        [Tooltip("The graph that declares it. Empty: the Timeline Graph.")]
        public SchematicGraph Graph;
        public string VariableId;

        public SerializableType Behavior;
        [Tooltip("Path of the GameObject the Behavior is on, from the object playing the Timeline. Empty for the object itself.")]
        public string Path = "";
        public string Variable;

        [Tooltip("The Variable's type (filled in when you pick it).")]
        public SerializableType Type;

        public Type ValueType => ReferenceEquals(Type, null) ? null : Type.Type;

        /// <summary>A short description for the Inspector and the Timeline.</summary>
        public string Describe(SchematicGraph timeline)
        {
            if (Kind == TargetKind.GraphVariable)
            {
                var graph = Graph != null ? Graph : timeline;
                return graph != null && graph.FindVariable(VariableId) is { } variable ? variable.Name : "(pick a Variable)";
            }
            var behavior = ReferenceEquals(Behavior, null) ? null : Behavior.Type;
            return behavior != null && !string.IsNullOrEmpty(Variable) ? $"{behavior.Name}.{Variable}" : "(pick a Variable)";
        }

        /// <summary>Sets it on the object playing the Timeline.</summary>
        public void Apply(GameObject host, TimelineGraph timeline, object value)
        {
            if (host == null)
                return;
            if (Kind == TargetKind.GraphVariable)
            {
                var owner = host.GetComponentInParent<SchematicObject>();
                var store = owner != null ? owner.GraphVariables(Graph != null ? Graph : timeline) : null;
                store?.Set(store.IndexOf(VariableId), value);
                return;
            }

            var behavior = ReferenceEquals(Behavior, null) ? null : Behavior.Type;
            var target = string.IsNullOrEmpty(Path) ? host.transform : host.transform.Find(Path);
            var instance = behavior != null && target != null ? ExecutionContext.FindInstance(target.gameObject, behavior) : null;
            if (instance == null)
                return;
            int index = instance.Runtime.IndexOfVariable(Variable);
            if (index >= 0)
                instance.Runtime.SetVariableBoxed(instance, index, value);
        }
    }

    /// <summary>One argument of a Signal marker: a parameter's name, type and value.</summary>
    [Serializable]
    public class MarkerArgument
    {
        public string Name;
        public SerializableType Type;
        public SchematicValue Value = new();
    }

    /// <summary>Base of the Schematic markers: they fire once when the Timeline passes them.</summary>
    [Serializable]
    public abstract class SchematicMarker : Marker, INotification, INotificationOptionProvider
    {
        [Tooltip("Also fire when the Timeline starts (or jumps) past this marker.")]
        public bool Retroactive;
        [Tooltip("Fire only the first time, not again when the Timeline loops or is played again.")]
        public bool OnlyOnce;

        public PropertyName id => new(GetType().Name);

        NotificationFlags INotificationOptionProvider.flags =>
            (Retroactive ? NotificationFlags.Retroactive : 0) | (OnlyOnce ? NotificationFlags.TriggerOnce : 0);
    }

    /// <summary>
    /// Sends a Signal at its moment: to the object playing the Timeline (or one of its children, or everyone), with the
    /// Signal's parameters filled in. Use the Timeline Graph's own Signals, or any Behavior's.
    /// </summary>
    [Serializable]
    [DisplayName("Schematics/Signal Marker")]
    public class SignalMarker : SchematicMarker
    {
        public SignalData Signal;
        [Tooltip("Path of the child to send it to, from the object playing the Timeline. Empty for the object itself.")]
        public string TargetPath = "";
        [Tooltip("Send it to every object instead.")]
        public bool Everyone;
        public List<MarkerArgument> Arguments = new();
    }

    /// <summary>Sets a Variable at its moment, on the object playing the Timeline.</summary>
    [Serializable]
    [DisplayName("Schematics/Variable Marker")]
    public class VariableMarker : SchematicMarker
    {
        public VariableTarget Target = new();
        public SchematicValue Value = new();
    }

    /// <summary>Fires the Timeline Graph's "On Timeline Marker" events (those listening for this name, or for any).</summary>
    [Serializable]
    [DisplayName("Schematics/Event Marker")]
    public class EventMarker : SchematicMarker
    {
        [Tooltip("What \"On Timeline Marker\" nodes listen for. Also where Seek Timeline can jump to.")]
        public string EventName = "Event";
    }

    /// <summary>
    /// Blends a Variable from one value to another over the clip's length (numbers, vectors, colours, rotations;
    /// anything else switches to the end value halfway).
    /// </summary>
    [Serializable]
    [DisplayName("Schematics/Variable Clip")]
    public class VariableClip : PlayableAsset, ITimelineClipAsset
    {
        public VariableTarget Target = new();
        public SchematicValue From = new();
        public SchematicValue To = new();
        [Tooltip("How it moves from From to To over the clip (0 to 1 across, 0 to 1 up).")]
        public AnimationCurve Curve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<VariableClipBehaviour>.Create(graph);
            playable.GetBehaviour().Clip = this;
            return playable;
        }

        public object Evaluate(float t)
        {
            var type = Target.ValueType;
            if (type == null) return null;
            t = Curve != null ? Curve.Evaluate(Mathf.Clamp01(t)) : Mathf.Clamp01(t);
            object a = From.Get(type), b = To.Get(type);
            return a switch
            {
                float fa when b is float fb => Mathf.LerpUnclamped(fa, fb, t),
                int ia when b is int ib => Mathf.RoundToInt(Mathf.LerpUnclamped(ia, ib, t)),
                Vector2 va when b is Vector2 vb => Vector2.LerpUnclamped(va, vb, t),
                Vector3 va when b is Vector3 vb => Vector3.LerpUnclamped(va, vb, t),
                Vector4 va when b is Vector4 vb => Vector4.LerpUnclamped(va, vb, t),
                Color ca when b is Color cb => Color.LerpUnclamped(ca, cb, t),
                Quaternion qa when b is Quaternion qb => Quaternion.SlerpUnclamped(qa, qb, t),
                _ => t < 0.5f ? a : b,
            };
        }
    }

    /// <summary>Plays a <see cref="VariableClip"/>: sets its Variable every frame the clip is active.</summary>
    public class VariableClipBehaviour : PlayableBehaviour
    {
        public VariableClip Clip;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (Clip == null || !(playerData is GameObject bound) || !bound.TryGetComponent(out TimelineGraphPlayer player))
                return;
            double duration = playable.GetDuration();
            float t = duration > 0 ? (float)(playable.GetTime() / duration) : 1f;
            Clip.Target.Apply(player.Host, player.Graph, Clip.Evaluate(t));
        }
    }
}
