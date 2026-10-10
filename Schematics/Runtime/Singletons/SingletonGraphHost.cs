using Remedy.Schematics;
using UnityEngine;

/// <summary>
/// Sits on a Singleton Graph's object and runs its game-wide events: On Start once, then On Runtime Update / Late
/// Update / Fixed Update every frame. (The object's Schematic Object already runs On Create, On Update and Signals.)
/// </summary>
[DisallowMultipleComponent]
[AddComponentMenu("")]
public sealed class SingletonGraphHost : MonoBehaviour
{
    private SchematicObject _schematic;

    private SchematicGraph Graph => (_schematic ??= GetComponent<SchematicObject>()) != null ? _schematic.SchematicGraph : null;

    private void Start() => Graph?.TriggerEvent<OnStart>(gameObject);
    private void Update() => Graph?.TriggerEvent<OnRuntimeUpdate>(gameObject);
    private void LateUpdate() => Graph?.TriggerEvent<OnRuntimeLateUpdate>(gameObject);
    private void FixedUpdate() => Graph?.TriggerEvent<OnRuntimeFixedUpdate>(gameObject);

    private void OnDestroy()
    {
        if (Graph is SingletonGraph singleton && singleton.Host == _schematic)
            SingletonGraph.SetHost(singleton, null);
    }
}
