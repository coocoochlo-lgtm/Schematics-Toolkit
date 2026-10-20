using Remedy.Schematics;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The pool of one Schematic prefab. Spawning reuses an inactive instance if one exists; despawning runs the
/// Behaviors' OnDisable/OnDestroy and parks the object back here instead of destroying it.
/// Get one through <see cref="SchematicsManager.GetPool"/>.
/// </summary>
public sealed class SchematicPool
{
    public SchematicObject Prefab { get; }

    private readonly Stack<SchematicObject> _inactive = new();
    private readonly HashSet<SchematicObject> _active = new();
    private readonly Transform _container;

    public int ActiveCount => _active.Count;
    public int InactiveCount => _inactive.Count;

    internal SchematicPool(SchematicObject prefab, Transform root)
    {
        Prefab = prefab;

        // Instances are created under an inactive container, so their Awake waits until they are actually spawned.
        var container = new GameObject($"{prefab.name} Pool");
        container.SetActive(false);
        _container = container.transform;
        _container.SetParent(root, false);
    }

    /// <summary>
    /// Creates instances ahead of time so spawning them later doesn't need to instantiate.
    /// </summary>
    public void Prewarm(int count)
    {
        for (int i = _active.Count + _inactive.Count; i < count; i++)
            _inactive.Push(CreateInstance());
    }

    public SchematicObject Spawn() => Spawn(Prefab.transform.position, Prefab.transform.rotation, null);

    public SchematicObject Spawn(Vector3 position, Quaternion rotation, Transform parent = null)
    {
        SchematicObject instance = null;
        while (instance == null && _inactive.Count > 0)
            instance = _inactive.Pop(); // skips any that were destroyed while parked
        if (instance == null)
            instance = CreateInstance();

        var transform = instance.transform;
        transform.SetParent(parent, false);
        if (parent == null)
            SceneManager.MoveGameObjectToScene(instance.gameObject, SceneManager.GetActiveScene());
        transform.SetPositionAndRotation(position, rotation);

        _active.Add(instance);
        instance.gameObject.SetActive(true);
        ExecutionContext.Register(instance);

        return instance;
    }

    /// <summary>
    /// Runs the despawn lifecycle and returns the instance to the pool.
    /// </summary>
    public void Despawn(SchematicObject instance)
    {
        if (instance == null || !_active.Remove(instance))
            return;

        ExecutionContext.Unregister(instance);

        // A Behavior may have destroyed it during OnDestroy.
        if (instance == null)
            return;

        instance.gameObject.SetActive(false);
        instance.transform.SetParent(_container, false);
        _inactive.Push(instance);
    }

    /// <summary>
    /// Destroys every parked instance.
    /// </summary>
    public void Clear()
    {
        while (_inactive.Count > 0)
        {
            var instance = _inactive.Pop();
            if (instance != null)
                Object.Destroy(instance.gameObject);
        }
    }

    /// <summary>
    /// Called when a pooled instance is destroyed for real (e.g. its scene unloaded).
    /// </summary>
    internal void Forget(SchematicObject instance)
    {
        _active.Remove(instance);
    }

    private SchematicObject CreateInstance()
    {
        var instance = Object.Instantiate(Prefab, _container);
        instance.name = Prefab.name;
        instance.Pool = this;
        return instance;
    }
}
