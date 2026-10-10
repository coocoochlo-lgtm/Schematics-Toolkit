using Remedy.Framework;
using Remedy.Schematics;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class SchematicsManager : Singleton<SchematicsManager>
{
    private readonly Dictionary<SchematicObject, SchematicPool> _pools = new();

    public static event UnityAction OnUpdate;

    [RuntimeInitializeOnLoadMethod]
    public static void Init()
    {
        // Objects that go through the Object Manager's own pool are kept in sync with the Execution Context.
        // Schematic prefabs should prefer SchematicsManager.Spawn, which gives each prefab its own Schematic Pool.
        ObjectManager.OnPrefabInstantiated += (GameObject prefab, GameObject instance) =>
        {
            if (instance.TryGetComponent(out SchematicObject schematicObject))
                ExecutionContext.Register(schematicObject);
        };

        var objectManager = ObjectManager.Instance;
        objectManager.OnDestroy ??= new UnityEvent<GameObject>();
        objectManager.OnDestroy.AddListener(instance =>
        {
            if (instance != null && instance.TryGetComponent(out SchematicObject schematicObject) && schematicObject.Pool == null)
                ExecutionContext.Unregister(schematicObject);
        });

        // Singleton Graphs are created by the SingletonGraphSpawner before the first scene loads.
        _ = Instance;
    }

    #region Spawning

    /// <summary>
    /// Gets the pool for a Schematic prefab, creating it on first use.
    /// </summary>
    public static SchematicPool GetPool(SchematicObject prefab)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));

        var pools = Instance._pools;
        if (!pools.TryGetValue(prefab, out var pool))
        {
            pool = new SchematicPool(prefab, Instance.transform);
            pools.Add(prefab, pool);
        }
        return pool;
    }

    public static SchematicPool GetPool(GameObject prefab) => GetPool(GetSchematicObject(prefab));

    /// <summary>
    /// Spawns an instance of a Schematic prefab from its pool, at the prefab's own position and rotation.
    /// </summary>
    public static SchematicObject Spawn(SchematicObject prefab) => GetPool(prefab).Spawn();

    public static SchematicObject Spawn(SchematicObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        => GetPool(prefab).Spawn(position, rotation, parent);

    public static SchematicObject Spawn(GameObject prefab) => Spawn(GetSchematicObject(prefab));

    public static SchematicObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null)
        => Spawn(GetSchematicObject(prefab), position, rotation, parent);

    /// <summary>
    /// Creates instances of a Schematic prefab ahead of time.
    /// </summary>
    public static void Prewarm(SchematicObject prefab, int count) => GetPool(prefab).Prewarm(count);

    /// <summary>
    /// Despawns a Schematic Object: back into its pool if it came from one, otherwise destroyed.
    /// </summary>
    public static void Destroy(SchematicObject instance)
    {
        if (instance == null)
            return;

        if (instance.Pool != null)
            instance.Pool.Despawn(instance);
        else
        {
            ExecutionContext.Unregister(instance);
            UnityEngine.Object.Destroy(instance.gameObject);
        }
    }

    private static SchematicObject GetSchematicObject(GameObject prefab)
    {
        if (prefab == null)
            throw new ArgumentNullException(nameof(prefab));
        if (!prefab.TryGetComponent(out SchematicObject schematicObject))
            throw new ArgumentException($"{prefab.name} has no {nameof(SchematicObject)} on its root.", nameof(prefab));
        return schematicObject;
    }

    #endregion

    private void Update()
    {
        OnUpdate?.Invoke();
        ExecutionContext.Update();
    }

    private void LateUpdate()
    {
        ExecutionContext.LateUpdate();
    }

    private void FixedUpdate()
    {
        ExecutionContext.FixedUpdate();
    }
}
