using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives one <see cref="SchematicBehavior"/> for every instance it is attached to. Instances are kept in a dense list,
/// and each Variable is a typed array indexed by the same row, so the update loops stay tight and allocation free.
/// Created on demand by the <see cref="ExecutionContext"/>.
/// </summary>
public sealed class BehaviorRuntime
{
    private enum Callback : byte
    {
        Create, Start, Enable, Update, LateUpdate, FixedUpdate, Disable, Destroy
    }

    internal const int InitialCapacity = 16;

    /// <summary>The Schematic Behavior this Runtime was created for.</summary>
    public SchematicBehavior Behavior { get; }
    /// <summary>The Behavior's Metadata. May be null for Behaviors that were never given one.</summary>
    public BehaviorMetadata Metadata { get; }
    public Type BehaviorType => Behavior.GetType();

    /// <summary>Number of rows, including instances despawned during the current loop that haven't been removed yet.</summary>
    public int Count => _count;

    /// <summary>The instance at the given row. Check <see cref="BehaviorInstance.IsSpawned"/> if you call this mid-update.</summary>
    public BehaviorInstance this[int row] => _instances[row];

    internal IVariableColumn[] Columns { get; }

    private BehaviorInstance[] _instances = new BehaviorInstance[InitialCapacity];
    private int _count;
    private readonly Dictionary<string, int> _variableIndices = new();

    // Despawns that happen while a loop is running are applied once it finishes, so rows don't shift underneath it.
    private int _iterating;
    private readonly List<BehaviorInstance> _pendingRemovals = new();

    private readonly bool _hasUpdate;
    private readonly bool _hasLateUpdate;
    private readonly bool _hasFixedUpdate;

    /// <summary>True if the Behavior overrides any collision or trigger callback.</summary>
    public bool HasPhysicsCallbacks { get; }

    internal BehaviorRuntime(SchematicBehavior behavior, BehaviorMetadata metadata)
    {
        Behavior = behavior;
        Metadata = metadata;
        behavior.Runtime = this;

        var variables = behavior.Variables;
        behavior.Cursor.Runtime = this;

        // The columns already exist: the Behavior's handles were bound to them when its field initializers ran.
        Columns = new IVariableColumn[variables.Count];
        for (int i = 0; i < variables.Count; i++)
        {
            Columns[i] = variables[i].Column;
            _variableIndices[variables[i].Name] = i;
        }

        if (metadata != null)
        {
            foreach (var definition in metadata.Variables)
            {
                if (!definition.OverrideDefault || definition.Default == null)
                    continue;
                int index = IndexOfVariable(definition.Name);
                if (index >= 0 && Columns[index].ValueType == definition.Type.Type)
                    Columns[index].SetDefault(definition.Default);
            }
        }

        _hasUpdate = behavior.Overrides(nameof(SchematicBehavior.OnUpdate));
        _hasLateUpdate = behavior.Overrides(nameof(SchematicBehavior.OnLateUpdate));
        _hasFixedUpdate = behavior.Overrides(nameof(SchematicBehavior.OnFixedUpdate));
        HasPhysicsCallbacks = behavior.Overrides(nameof(SchematicBehavior.OnCollisionEnter))
                           || behavior.Overrides(nameof(SchematicBehavior.OnCollisionExit))
                           || behavior.Overrides(nameof(SchematicBehavior.OnTriggerEnter))
                           || behavior.Overrides(nameof(SchematicBehavior.OnTriggerExit));
    }

    public int IndexOfVariable(string name)
        => name != null && _variableIndices.TryGetValue(name, out int index) ? index : -1;

    /// <summary>
    /// Boxed read of a Variable for a given instance. Meant for tools and debugging, not gameplay code.
    /// </summary>
    public object GetVariableBoxed(BehaviorInstance instance, int variableIndex)
        => instance.Row >= 0 ? Columns[variableIndex].GetBoxed(instance.Row) : null;

    /// <summary>
    /// Boxed write of a Variable for a given instance. Meant for tools and debugging, not gameplay code.
    /// </summary>
    public void SetVariableBoxed(BehaviorInstance instance, int variableIndex, object value)
    {
        if (instance.Row >= 0)
            Columns[variableIndex].SetBoxed(instance.Row, value);
    }

    /// <summary>
    /// Copies every spawned instance into the given list.
    /// </summary>
    public void GetInstances(List<BehaviorInstance> results)
    {
        results.Clear();
        for (int i = 0; i < _count; i++)
            if (!_instances[i].Removing)
                results.Add(_instances[i]);
    }

    #region Spawning

    /// <summary>
    /// Spawn step 1: gives the instance a row and resets its Variables. No callbacks yet, so every Behavior on the
    /// object has its data in place before any of them run.
    /// </summary>
    internal void AddRow(BehaviorInstance instance)
    {
        int row;
        if (instance.Row >= 0)
        {
            if (!instance.Removing)
                return;

            // Despawned and respawned within the same loop: keep the row its pending removal would have freed.
            row = instance.Row;
        }
        else
        {
            if (_count == _instances.Length)
                Grow();

            row = _count++;
            _instances[row] = instance;
            instance.Row = row;
        }

        instance.Removing = false;
        instance.Started = false;
        instance.Enabled = instance.Attachment == null || instance.Attachment.EnabledByDefault;

        for (int i = 0; i < Columns.Length; i++)
            Columns[i].Reset(row);
        instance.ApplyOverrides(Columns);

        if (Behavior.Selected == instance)
            Behavior.Cursor.Row = row;
    }

    /// <summary>Spawn step 2: OnCreate, only the first time this instance's object exists.</summary>
    internal void RunCreate(BehaviorInstance instance)
    {
        if (instance.Created || !instance.IsSpawned)
            return;
        instance.Created = true;
        Invoke(Callback.Create, instance);
    }

    /// <summary>Spawn step 3: OnStart then OnEnable, if the instance spawned enabled.</summary>
    internal void RunStart(BehaviorInstance instance)
    {
        if (instance.Enabled && instance.IsSpawned)
            StartAndEnable(instance);
    }

    /// <summary>
    /// Despawns the instance: OnDisable (if enabled), OnDestroy (if started), then frees its row.
    /// </summary>
    internal void Remove(BehaviorInstance instance)
    {
        if (instance.Row < 0 || instance.Removing)
            return;

        // Marked first, so a callback despawning the same object again is a no-op.
        instance.Removing = true;

        if (instance.Started)
        {
            if (instance.Enabled)
                Invoke(Callback.Disable, instance);
            Invoke(Callback.Destroy, instance);
            instance.Started = false;
        }

        if (_iterating > 0)
            _pendingRemovals.Add(instance);
        else
            RemoveRow(instance);
    }

    private void RemoveRow(BehaviorInstance instance)
    {
        int row = instance.Row;
        int last = _count - 1;

        if (row != last)
        {
            var moved = _instances[last];
            _instances[row] = moved;
            moved.Row = row;
            for (int i = 0; i < Columns.Length; i++)
                Columns[i].Move(last, row);
        }

        for (int i = 0; i < Columns.Length; i++)
            Columns[i].Clear(last);

        _instances[last] = null;
        _count--;

        instance.Row = -1;
        instance.Removing = false;

        // Keep the cursor pointing at the selected instance's (possibly new) row.
        var cursor = Behavior.Cursor;
        if (cursor.Instance != null && (cursor.Instance == instance || cursor.Row != cursor.Instance.Row))
            cursor.Row = cursor.Instance.Row;
    }

    private void Grow()
    {
        int capacity = _instances.Length * 2;
        Array.Resize(ref _instances, capacity);
        for (int i = 0; i < Columns.Length; i++)
            Columns[i].Resize(capacity);
    }

    #endregion

    #region Enabling

    /// <summary>
    /// Sets the instance's Enabled flag and calls OnEnable/OnDisable if it changed.
    /// The first time a spawned instance is enabled, OnStart runs before OnEnable.
    /// </summary>
    public void SetEnabled(BehaviorInstance instance, bool enabled)
    {
        if (instance == null || instance.Runtime != this || instance.Enabled == enabled)
            return;

        instance.Enabled = enabled;

        if (!instance.IsSpawned)
            return;

        if (enabled)
            StartAndEnable(instance);
        else if (instance.Started)
            Invoke(Callback.Disable, instance);
    }

    private void StartAndEnable(BehaviorInstance instance)
    {
        if (!instance.Started)
        {
            instance.Started = true;
            Invoke(Callback.Start, instance);
        }

        // OnStart may have disabled or despawned it.
        if (instance.Enabled && instance.IsSpawned)
            Invoke(Callback.Enable, instance);
    }

    #endregion

    #region Updating

    internal void Update()
    {
        if (_hasUpdate) RunLoop(Callback.Update);
    }

    internal void LateUpdate()
    {
        if (_hasLateUpdate) RunLoop(Callback.LateUpdate);
    }

    internal void FixedUpdate()
    {
        if (_hasFixedUpdate) RunLoop(Callback.FixedUpdate);
    }

    private void RunLoop(Callback callback)
    {
        var cursor = Behavior.Cursor;
        var previousSelection = cursor.Instance;
        var previousCurrent = ExecutionContext.CurrentInstance;

        _iterating++;
        // Instances spawned during the loop land past this count and start updating next frame.
        int count = _count;
        int i = 0;
        try
        {
            // The try sits outside the loop, so it costs nothing per instance; an exception logs and resumes at the next one.
            while (i < count)
            {
                try
                {
                    for (; i < count; i++)
                    {
                        var instance = _instances[i];
                        if (!instance.Enabled || !instance.Started || instance.Removing)
                            continue;

                        // Removals are deferred while iterating, so the instance's row is i.
                        cursor.Instance = instance;
                        cursor.Row = i;
                        ExecutionContext.CurrentInstance = instance;

                        switch (callback)
                        {
                            case Callback.Update: Behavior.OnUpdate(instance); break;
                            case Callback.LateUpdate: Behavior.OnLateUpdate(instance); break;
                            case Callback.FixedUpdate: Behavior.OnFixedUpdate(instance); break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    // One faulty instance shouldn't stop the rest of the Runtime.
                    Debug.LogException(exception, _instances[i] != null ? _instances[i].GameObject : null);
                    i++;
                }
            }
        }
        finally
        {
            _iterating--;
            if (_iterating == 0 && _pendingRemovals.Count > 0)
                FlushRemovals();

            cursor.Select(previousSelection);
            ExecutionContext.CurrentInstance = previousCurrent;
        }
    }

    private void FlushRemovals()
    {
        for (int i = 0; i < _pendingRemovals.Count; i++)
        {
            var instance = _pendingRemovals[i];
            if (instance.Removing && instance.Row >= 0)
                RemoveRow(instance);
        }
        _pendingRemovals.Clear();
    }

    #endregion

    #region Physics

    internal enum PhysicsMessage : byte
    {
        CollisionEnter, CollisionExit, TriggerEnter, TriggerExit
    }

    /// <summary>
    /// Forwards a Unity physics message to an enabled instance. Sent by the Schematic Object (root) or a
    /// <see cref="Remedy.Schematics.SchematicPhysicsRelay"/> (children).
    /// </summary>
    internal void DispatchPhysics(BehaviorInstance instance, PhysicsMessage message, Collision collision, Collider other)
    {
        if (!HasPhysicsCallbacks || !instance.Enabled || !instance.Started || !instance.IsSpawned)
            return;

        var previous = ExecutionContext.CurrentInstance;
        var previousSelection = Behavior.Selected;
        ExecutionContext.CurrentInstance = instance;
        Behavior.Selected = instance;
        try
        {
            switch (message)
            {
                case PhysicsMessage.CollisionEnter: Behavior.OnCollisionEnter(instance, collision); break;
                case PhysicsMessage.CollisionExit: Behavior.OnCollisionExit(instance, collision); break;
                case PhysicsMessage.TriggerEnter: Behavior.OnTriggerEnter(instance, other); break;
                case PhysicsMessage.TriggerExit: Behavior.OnTriggerExit(instance, other); break;
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, instance.GameObject);
        }
        finally
        {
            ExecutionContext.CurrentInstance = previous;
            Behavior.Selected = previousSelection;
        }
    }

    #endregion

    private void Invoke(Callback callback, BehaviorInstance instance)
    {
        // Saved and restored so a Behavior that enables, spawns or selects another instance mid-callback
        // gets its own selection back afterwards.
        var previous = ExecutionContext.CurrentInstance;
        var previousSelection = Behavior.Selected;
        ExecutionContext.CurrentInstance = instance;
        Behavior.Selected = instance;
        try
        {
            switch (callback)
            {
                case Callback.Create: Behavior.OnCreate(instance); break;
                case Callback.Start: Behavior.OnStart(instance); break;
                case Callback.Enable: Behavior.OnEnable(instance); break;
                case Callback.Update: Behavior.OnUpdate(instance); break;
                case Callback.LateUpdate: Behavior.OnLateUpdate(instance); break;
                case Callback.FixedUpdate: Behavior.OnFixedUpdate(instance); break;
                case Callback.Disable: Behavior.OnDisable(instance); break;
                case Callback.Destroy: Behavior.OnDestroy(instance); break;
            }
        }
        catch (Exception exception)
        {
            // One faulty instance shouldn't stop the rest of the Runtime.
            Debug.LogException(exception, instance.GameObject);
        }
        finally
        {
            ExecutionContext.CurrentInstance = previous;
            Behavior.Selected = previousSelection;
        }
    }
}
