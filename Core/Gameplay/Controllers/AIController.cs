using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Something an AI Controller has learned to do (made by the learning system, when it's installed): given a player to
/// watch, it learns how they control the objects they possess, and can then control objects of the same kind itself.
/// </summary>
public interface IControllerSkill : IDisposable
{
    /// <summary>The controller it watches and learns from (null: it stops learning, and keeps what it knows).</summary>
    Controller Teacher { get; set; }

    /// <summary>True: the AI controls what it possesses with what it has learned.</summary>
    bool Acting { get; set; }

    /// <summary>Called every frame by its AI Controller.</summary>
    void Tick(float deltaTime);
}

/// <summary>
/// An AI that controls Schematic Objects the way a player does: through their input. It's a <see cref="Controller"/>
/// like a player's, so it possesses objects with the same nodes, and everything they do with a player's input they do
/// with its input.
/// <para>Where its input comes from: an <see cref="AIControllerGraph"/> it was given (Send Input), or what it has
/// learned by watching a player (a teacher): it follows the objects the player possesses, learns how the player
/// controls them from what's going on around them (their Behaviors' Variables, how they're moving), and controls
/// objects of the same kind the same way.</para>
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class AIController : Controller
{
    private static readonly List<AIController> _all = new();

    private IControllerSkill _skill;

    /// <summary>
    /// Makes the learning part of an AI Controller (set by the learning system, Remedy ManifoldNet, when it's in the
    /// project). Null: AI Controllers can't learn from players.
    /// </summary>
    public static Func<AIController, IControllerSkill> SkillFactory;

    /// <summary>Every AI Controller, in the order they were made.</summary>
    public static IReadOnlyList<AIController> All => _all;

    public override bool IsPlayer => false;

    public override string DisplayName => $"AI {Index + 1}";

    /// <summary>The graph it runs (null if it only acts on what it has learned).</summary>
    public AIControllerGraph Graph { get; private set; }

    /// <summary>The controller it learns from, or null.</summary>
    public Controller Teacher => _skill?.Teacher;

    /// <summary>What it has learned (null until it's given a teacher).</summary>
    public IControllerSkill Skill => _skill;

    /// <summary>
    /// Whether it controls what it possesses with what it has learned. On by default when it has no graph to run;
    /// with a graph, the graph decides (Set AI Learning).
    /// </summary>
    public bool UseLearnedControls
    {
        get => _skill != null && _skill.Acting;
        set
        {
            if (_skill != null)
                _skill.Acting = value;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _all.Clear();

    /// <summary>
    /// Makes an AI Controller: running <paramref name="graph"/> if given, and learning from <paramref name="teacher"/> if
    /// given (with no graph, it acts on what it learns). Possess something with it to let it play.
    /// </summary>
    public static AIController Create(AIControllerGraph graph = null, Controller teacher = null)
    {
        int index = _all.Count;
        // Built inactive, so its graph's object registers only once everything is set up.
        var gameObject = new GameObject(graph != null ? $"AI {index + 1} ({graph.name})" : $"AI {index + 1}");
        gameObject.SetActive(false);
        gameObject.transform.SetParent(PlayerController.Root, false);
        var controller = gameObject.AddComponent<AIController>();
        controller.Index = index;
        controller.Graph = graph;
        _all.Add(controller);
        Possession.Register(controller);

        if (graph != null)
        {
            var schematic = gameObject.AddComponent<SchematicObject>();
            schematic.SchematicGraph = graph;
            // No prefab: identified by its graph, as Singletons are.
            schematic.AssignObjectID(SchematicObject.ObjectIDFor("ai-controller:" + graph.name));
        }
        gameObject.SetActive(true);

        if (teacher != null)
            controller.LearnFrom(teacher, act: graph == null);
        return controller;
    }

    /// <summary>
    /// Starts watching a controller (usually a player) to learn how it controls what it possesses. With
    /// <paramref name="act"/>, it also controls what it possesses itself with what it learns.
    /// </summary>
    public void LearnFrom(Controller teacher, bool act = true)
    {
        if (teacher == this)
            return;
        if (_skill == null)
        {
            if (SkillFactory == null)
            {
                Debug.LogWarning($"{DisplayName} can't learn from {(teacher != null ? teacher.DisplayName : "nobody")}: learning needs Remedy ManifoldNet in the project.", this);
                return;
            }
            _skill = SkillFactory(this);
            if (_skill == null)
                return;
        }
        _skill.Teacher = teacher;
        _skill.Acting = act;
    }

    /// <summary>Stops learning (it keeps what it knows, and keeps acting on it if it was).</summary>
    public void StopLearning()
    {
        if (_skill != null)
            _skill.Teacher = null;
    }

    private void Update() => _skill?.Tick(Time.deltaTime);

    protected override void OnDestroy()
    {
        _skill?.Dispose();
        _skill = null;
        _all.Remove(this);
        base.OnDestroy();
    }
}
