using BlueGraph;
using Remedy.Framework;
using System;
using UnityEngine;

namespace Remedy.Schematics.TimelineGraphs
{
    #region Events (in a Timeline Graph)

    [SchematicDoc(@"{""name"": ""On Timeline Started"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Timeline/Events"", ""description"": ""Starts a flow when this Timeline Graph's Timeline starts playing on an object."", ""use_for"": [""set up a cutscene (hide the HUD, lock the player)""], ""notes"": ""Only in Timeline Graphs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Timeline Started", Path = "Timeline/Events"), Tags("Timeline")]
    public class OnTimelineStarted : SchematicEventNode { }

    [SchematicDoc(@"{""name"": ""On Timeline Paused"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Timeline/Events"", ""description"": ""Starts a flow when this Timeline Graph's Timeline is paused."", ""use_for"": [""show a 'paused' prompt during a cutscene""], ""notes"": ""Only in Timeline Graphs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Timeline Paused", Path = "Timeline/Events"), Tags("Timeline")]
    public class OnTimelinePaused : SchematicEventNode { }

    [SchematicDoc(@"{""name"": ""On Timeline Resumed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Timeline/Events"", ""description"": ""Starts a flow when this Timeline Graph's Timeline carries on after a pause."", ""use_for"": [""hide a 'paused' prompt""], ""notes"": ""Only in Timeline Graphs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Timeline Resumed", Path = "Timeline/Events"), Tags("Timeline")]
    public class OnTimelineResumed : SchematicEventNode { }

    [SchematicDoc(@"{""name"": ""On Timeline Finished"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Timeline/Events"", ""description"": ""Starts a flow when this Timeline Graph's Timeline stops: Completed is true when it reached the end, false when something stopped it."", ""outputs"": [{""name"": ""Completed"", ""type"": ""bool""}], ""use_for"": [""end a cutscene (give control back, load the next level)""], ""notes"": ""Only in Timeline Graphs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Timeline Finished", Path = "Timeline/Events"), Tags("Timeline")]
    public class OnTimelineFinished : SchematicEventNode
    {
        [Tooltip("True if it reached the end; false if something stopped it early.")]
        [Output("Completed")] public bool Completed;
    }

    [SchematicDoc(@"{""name"": ""On Timeline Marker"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Timeline/Events"", ""description"": ""Starts a flow when the Timeline passes an Event marker: the ones named in Listen For, or any if it's empty."", ""outputs"": [{""name"": ""Marker"", ""type"": ""string""}], ""use_for"": [""do something at an exact moment of a cutscene (spawn, shake the camera, play a line)""], ""notes"": ""Put Event markers on a Schematic track (or the Timeline's marker row). Only in Timeline Graphs.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Timeline Marker", Path = "Timeline/Events"), Tags("Timeline")]
    public class OnTimelineMarker : SchematicEventNode
    {
        [Tooltip("The Event marker name this listens for. Empty: every marker.")]
        [Editable] public string ListenFor = "";

        [Output("Marker")] public string Marker;

        protected override void OnTrigger(GameObject instance, params Union[] arguments)
        {
            if (!string.IsNullOrEmpty(ListenFor) && (arguments.Length == 0 || arguments[0].GetValue<string>() != ListenFor))
                _blocked = true;
        }
    }

    #endregion

    #region Control (any graph)

    /// <summary>
    /// Base for nodes that control a Timeline Graph on an object: the one named in Timeline (or the graph the node is
    /// in, if that's a Timeline Graph), on the object wired into Of (or the one running the flow).
    /// </summary>
    [Serializable]
    public abstract class TimelineControlNode : SchematicActionNode
    {
        [Tooltip("The Timeline Graph to control. Empty: this graph (when it's a Timeline Graph).")]
        [Editable] public TimelineGraph Timeline;

        [Input("Of")] public GameObject Of;

        protected TimelineGraphPlayer Player(GameObject instance, bool create)
        {
            var graph = TimelineGraphPlayer.Resolve(Timeline, this);
            if (graph == null)
            {
                Debug.LogWarning($"[{Name}] Pick the Timeline Graph to control (or put this node in a Timeline Graph).", instance);
                return null;
            }
            var target = GetInputValue<GameObject>("Of", instance);
            if (target == null)
                target = instance;
            return TimelineGraphPlayer.For(target, graph, create);
        }
    }

    [SchematicDoc(@"{""name"": ""Play Timeline"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Plays a Timeline Graph's Timeline on an object (the one running the flow, or the one wired into Of), from the start or from where it is."", ""use_for"": [""start a cutscene"", ""play a door-opening sequence""], ""notes"": ""Pick the Timeline Graph in Timeline (empty: this graph, if it's a Timeline Graph).""}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Play Timeline", Path = "Timeline"), Tags("Timeline")]
    public class PlayTimeline : TimelineControlNode
    {
        [Tooltip("Start at the beginning (else carry on from the current time).")]
        [Editable] public bool FromStart = true;

        protected override void OnTrigger(GameObject instance, bool awaiting = false) => Player(instance, true)?.Play(FromStart);
    }

    [SchematicDoc(@"{""name"": ""Pause Timeline"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Pauses a Timeline Graph's Timeline on an object."", ""use_for"": [""pause a cutscene (menus, waiting for input)""]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Pause Timeline", Path = "Timeline"), Tags("Timeline")]
    public class PauseTimeline : TimelineControlNode
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false) => Player(instance, false)?.Pause();
    }

    [SchematicDoc(@"{""name"": ""Resume Timeline"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Carries on a paused Timeline Graph's Timeline on an object."", ""use_for"": [""continue a cutscene after a choice or a prompt""]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Resume Timeline", Path = "Timeline"), Tags("Timeline")]
    public class ResumeTimeline : TimelineControlNode
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false) => Player(instance, false)?.Resume();
    }

    [SchematicDoc(@"{""name"": ""Stop Timeline"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Stops a Timeline Graph's Timeline on an object (On Timeline Finished fires with Completed false)."", ""use_for"": [""skip a cutscene""]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Stop Timeline", Path = "Timeline"), Tags("Timeline")]
    public class StopTimeline : TimelineControlNode
    {
        protected override void OnTrigger(GameObject instance, bool awaiting = false) => Player(instance, false)?.Stop();
    }

    [SchematicDoc(@"{""name"": ""Seek Timeline"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Jumps a Timeline Graph's Timeline to a time (seconds), or to an Event marker by name."", ""inputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Marker"", ""type"": ""string""}], ""use_for"": [""skip to a part of a cutscene"", ""loop a section""], ""notes"": ""With a Marker name, it jumps there and ignores Time.""}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Seek Timeline", Path = "Timeline"), Tags("Timeline")]
    public class SeekTimeline : TimelineControlNode
    {
        [Input("Time", Editable = true)] public float Time;
        [Tooltip("The Event marker to jump to, by name (empty: jump to Time instead).")]
        [Input("Marker", Editable = true)] public string Marker = "";

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var player = Player(instance, true);
            if (player == null) return;
            string marker = GetInputValue("Marker", Marker);
            if (!string.IsNullOrEmpty(marker))
            {
                if (!player.SeekMarker(marker))
                    Debug.LogWarning($"[{Name}] {player.Graph.name} has no Event marker called \"{marker}\".", instance);
                return;
            }
            player.Seek(GetInputValue("Time", Time));
        }
    }

    [SchematicDoc(@"{""name"": ""Set Timeline Speed"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Sets how fast a Timeline Graph's Timeline plays (1 normal, 0.5 half speed, 2 double)."", ""inputs"": [{""name"": ""Speed"", ""type"": ""float""}], ""use_for"": [""slow motion"", ""fast-forward a cutscene""]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Set Timeline Speed", Path = "Timeline"), Tags("Timeline")]
    public class SetTimelineSpeed : TimelineControlNode
    {
        [Input("Speed", Editable = true)] public float Speed = 1f;

        protected override void OnTrigger(GameObject instance, bool awaiting = false) => Player(instance, true)?.SetSpeed(GetInputValue("Speed", Speed));
    }

    [SchematicDoc(@"{""name"": ""Bind Timeline Track"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Points one of a Timeline's tracks (by name) at an object, e.g. the player's character or the main camera, before playing it."", ""inputs"": [{""name"": ""Track"", ""type"": ""string""}, {""name"": ""Object"", ""type"": ""GameObject""}], ""use_for"": [""animate scene objects (the player, the camera) in a cutscene""], ""notes"": ""Tracks bind to the object playing the Timeline by default; this changes one.""}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Bind Timeline Track", Path = "Timeline"), Tags("Timeline")]
    public class BindTimelineTrack : TimelineControlNode
    {
        [Tooltip("The track's name in the Timeline.")]
        [Input("Track", Editable = true)] public string Track = "";
        [Input("Object")] public GameObject Object;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var player = Player(instance, true);
            string track = GetInputValue("Track", Track);
            var target = GetInputValue<GameObject>("Object", Object);
            if (player != null && !player.Bind(track, target))
                Debug.LogWarning($"[{Name}] {player.Graph.name} has no track called \"{track}\".", instance);
        }
    }

    [SchematicDoc(@"{""name"": ""Timeline Info"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Timeline"", ""description"": ""Where a Timeline Graph's Timeline is on an object: its time, length, progress (0 to 1) and whether it's playing."", ""outputs"": [{""name"": ""Time"", ""type"": ""float""}, {""name"": ""Duration"", ""type"": ""float""}, {""name"": ""Progress"", ""type"": ""float""}, {""name"": ""Playing"", ""type"": ""bool""}], ""use_for"": [""show a skip bar"", ""wait for part of a cutscene""]}")]
    [Serializable]
    [NodeColor(NodeColors.Time)]
    [Node(Name = "Timeline Info", Path = "Timeline"), Tags("Timeline")]
    public class TimelineInfo : TimelineControlNode
    {
        [Output("Time")] public float Time;
        [Output("Duration")] public float Duration;
        [Output("Progress")] public float Progress;
        [Tooltip("True while it's playing.")]
        [Output("Playing")] public bool Playing;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var player = Player(instance, false);
            float time = player != null ? (float)player.Time : 0f, duration = player != null ? (float)player.Duration : 0f;
            SetOutputValue("Time", new Union(time));
            SetOutputValue("Duration", new Union(duration));
            SetOutputValue("Progress", new Union(duration > 0 ? time / duration : 0f));
            SetOutputValue("Playing", new Union(player != null && player.IsPlaying));
        }
    }

    #endregion
}
