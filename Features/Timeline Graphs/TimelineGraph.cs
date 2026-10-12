using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Remedy.Schematics.TimelineGraphs
{
    /// <summary>
    /// A Schematic Graph with a Timeline: cutscenes and other timed sequences as their own graph. Its Timeline (a Unity
    /// Timeline asset) holds animation, audio, camera tracks and so on, plus Schematic markers that send Signals, set
    /// Variables and fire the graph's "On Timeline Marker" events at their moment. The graph itself reacts to the
    /// Timeline (started, paused, finished, markers) and can control it, as can any other graph (Play Timeline, Pause,
    /// Seek...). Every object playing it has its own player, so many objects can play one Timeline Graph at once.
    /// Made with Assets/Create/Schematics/Timeline Graph (which makes its Timeline too).
    /// </summary>
    public class TimelineGraph : SchematicGraph
    {
        public override string Title => "TIMELINE";

        [Tooltip("The Timeline this graph plays. Edit it in the Timeline window (the graph window's Timeline bar opens it).")]
        public TimelineAsset Timeline;

        [Tooltip("Play as soon as an object whose own graph this is spawns.")]
        public bool PlayOnStart;

        [Tooltip("What happens at the end: stop (Hold keeps the last frame, Loop starts over).")]
        public DirectorWrapMode WrapMode = DirectorWrapMode.None;

        [Tooltip("Game time follows Time.timeScale (pauses with the game); Unscaled keeps running when the game is paused.")]
        public DirectorUpdateMode UpdateMode = DirectorUpdateMode.GameTime;

        [Tooltip("What each Timeline track animates on the object playing it, by track name. Tracks not listed bind by " +
                 "name to a child with that name, or else to the object itself. Use the Bind Timeline Track node for scene objects.")]
        public List<TrackBinding> Bindings = new();

        /// <summary>Where a track's binding is found on the object playing the Timeline.</summary>
        [Serializable]
        public class TrackBinding
        {
            public string Track;
            [Tooltip("Path from the object playing the Timeline. Empty for the object itself.")]
            public string Path = "";
        }

        public string PathFor(string track)
        {
            foreach (var binding in Bindings)
                if (binding != null && binding.Track == track)
                    return binding.Path;
            return null;
        }
    }
}
