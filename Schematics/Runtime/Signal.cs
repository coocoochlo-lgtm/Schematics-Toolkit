using Remedy.Framework;
using Remedy.Schematics;
using System;
using UnityEngine;

//!!!----
// TODO: Signals and Variables should only be SchematicComponentID and SignalID, then refer to the VariableAndSignalManager for Subscription and Invokation, 
// as these values should be stored during edit-time within a LUT pairing them with their respective values.
// This simplified implementation, as they never need to be manually set in SchematicMonoBehaviors (will be automatically propagated)
// as well as enabling simple Type Conversions from generic variables (Variable<T>) to it's respective Types 
// Variable<T>.Set would always be used to set the value of the variable, because it needs to retain the ID's.
// When creating the SchematicComponent LUT, you can use GlobalObjectIDSlow with Update in Edit Mode only to ensure the 
// Component exists in Data. If it doesn't, that's how it can be sure that it needs to set the values of the Signals and Variables
// for SchematicMonobehaviors within it's children.

public interface ISignal
{
    public byte SchemaID { get; set; }
    public byte SignalID { get; set; }
    public void Unsubscribe(UnityEngine.Object component)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Unsubscribe(component);
    }
}

// ??? No args ?????????????????????????????????????????????????????????????????

public struct Signal : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component);
    }
}

// ??? 1 arg ???????????????????????????????????????????????????????????????????

public struct Signal<T1> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1);
    }
}

// ??? 2 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2);
    }
}

// ??? 3 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3);
    }
}

// ??? 4 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4);
    }
}

// ??? 5 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5);
    }
}

// ??? 6 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5, T6> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5, t6);
    }
}

// ??? 7 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5, T6, T7> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5, t6, t7);
    }
}

// ??? 8 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5, T6, T7, T8> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5, t6, t7, t8);
    }
}

// ??? 9 args ??????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5, T6, T7, T8, T9> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5, t6, t7, t8, t9);
    }
}

// ??? 10 args ?????????????????????????????????????????????????????????????????

public struct Signal<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : ISignal
{
    [SerializeField] private byte _schemaID;
    [SerializeField] private byte _signalID;
    public byte SchemaID { get => _schemaID; set => _schemaID = value; }
    public byte SignalID { get => _signalID; set => _signalID = value; }

    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 t1, T2 t2, T3 t3, T4 t4, T5 t5, T6 t6, T7 t7, T8 t8, T9 t9, T10 t10)
    {
        if (SchematicManagerData.TryGetSignal(SchemaID, SignalID, out SignalData data))
            data.Invoke(component, t1, t2, t3, t4, t5, t6, t7, t8, t9, t10);
    }
}