using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>A Parameter of a graph: a value whatever runs the graph gives it (a Sound Cue's Parameters, say).</summary>
    public readonly struct GraphParameter
    {
        public readonly string Name;
        public readonly Type Type;
        public readonly object Default;

        public GraphParameter(string name, Type type, object value)
        {
            Name = name;
            Type = type;
            Default = value;
        }
    }

    /// <summary>
    /// Something running a Schematic Graph, of any kind: a Schematic Object running its graph, a Sound Cue Player playing
    /// its cue, the Listener Chain. A graph Variable of type Graph holds one, and Set Graph Parameters sets its graph's
    /// Parameters while it runs; Signals sent To its object reach it.
    /// </summary>
    public abstract class SchematicGraphRunner : MonoBehaviour
    {
        /// <summary>The graph it's running (null: none right now).</summary>
        public abstract SchematicGraph RunningGraph { get; }

        /// <summary>The object it belongs to, when that isn't its own (a pooled one-shot sound following an object).</summary>
        public virtual GameObject BelongsTo => null;

        /// <summary>Sets one of its graph's Parameters while it runs. False if it has no such Parameter.</summary>
        public virtual bool SetGraphParameter(string name, object value) => false;

        /// <summary>Runners kept outside the object they belong to (pooled players), for <see cref="RunnersOn"/>.</summary>
        public static event Func<IEnumerable<SchematicGraphRunner>> Elsewhere;

        /// <summary>
        /// What's running a graph on a target: the target itself when it's a runner; otherwise the runners on its
        /// object (and its children), and those that belong to it from elsewhere. Only those running
        /// <paramref name="graph"/> when one is given.
        /// </summary>
        public static List<SchematicGraphRunner> RunnersOn(UnityEngine.Object target, SchematicGraph graph = null)
        {
            var found = new List<SchematicGraphRunner>();
            if (target is SchematicGraphRunner runner && runner != null)
            {
                if (graph == null || runner.RunningGraph == graph)
                    found.Add(runner);
                return found;
            }
            var gameObject = target as GameObject ?? (target as Component)?.gameObject;
            if (gameObject == null)
                return found;
            foreach (var child in gameObject.GetComponentsInChildren<SchematicGraphRunner>())
                if (graph == null || child.RunningGraph == graph)
                    found.Add(child);
            if (Elsewhere != null)
                foreach (Func<IEnumerable<SchematicGraphRunner>> source in Elsewhere.GetInvocationList())
                    foreach (var other in source())
                        if (other != null && !found.Contains(other) && other.BelongsTo != null
                            && other.BelongsTo.transform.IsChildOf(gameObject.transform)
                            && (graph == null || other.RunningGraph == graph))
                            found.Add(other);
            return found;
        }
    }

    /// <summary>A node that hands out a running graph from an output (Play Sound Cue's Player): which graph that is.</summary>
    public interface IGraphSource
    {
        SchematicGraph GraphBehind(string outputPort);
    }
}
