using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;
using System.Data.SqlTypes;

public partial class SignalData : ScriptableObject
{
    public string Name;
    private static Dictionary<int, UnityEngine.Object> _allSubscribers;
    public static Dictionary<int, UnityEngine.Object> AllSubscribers
    {
        get
        {
            if (_allSubscribers == null) _allSubscribers = new();
            return _allSubscribers;
        }
    }

#if UNITY_EDITOR
    public SerializableProperty Property;
#endif

    /// <summary>
    /// If true, the Schematics system will use the original Scriptable Event Asset instead of instantiating one per instance that contains it. 
    /// </summary>
    public bool Global = false;

    /// <summary>
    /// All Active Subscription to this Event.
    /// </summary> private readonly List<Subscription> _subs0 = new();
    private readonly List<ISubscription> _subs = new();
    private int _subscribersCount;

    [SerializeField]
    private ParameterCollection _parameters = new();
    public ParameterCollection Parameters => _parameters;

    [SerializeField]
    public Union[] RuntimeArgs;

    [Serializable]
    public struct Parameter : INullable
    {
        public string Name;
        public Union Value;

        public Parameter(string name, Union val)
        {
            Name = name;
            Value = val;
        }

        public bool IsNull => string.IsNullOrEmpty(Name);
    }

    [Serializable]
    public class ParameterCollection 
    {
        public List<Parameter> Contents = new();

        public Union this[int i]
        {
            get
            {
                return Contents[i].Value;
            }
            set
            {
                Contents[i] = new Parameter(Contents[i].Name, value);
            }
        }

        public Union this[string name]
        {
            get
            {
                for (int i = 0; i < Contents.Count; i++)
                {
                    var param = Contents[i];

                    if (param.Name == name)
                    {
                        return param.Value;
                    }
                }
                return Union.Null;
            }
            set
            {
                for(int i = 0; i < Contents.Count; i++)
                {
                    var param = Contents[i];
                    
                    if(param.Name == name)
                    {
                        Contents[i] = new Parameter(name, value);
                    }
                }
            }
        }

        public void AddParameter(string name, Union value)
        {
            Contents.Add(new Parameter(name, value));
        }

        public void RemoveParameter(string name)
        {
            for (int i = Contents.Count - 1; i > 0; i--)
            {
                var param = Contents[i];

                if (param.Name == name)
                {
                    Contents.RemoveAt(i);
                }
            }
        }
    }

    // Unity creates ScriptableObjects through the parameterless constructor.
    public SignalData() { }

    public SignalData(string name)
    {
        Name = name;
    }

    /// <summary>
    /// Returns the ID of the MonoBehaviour instance within the Subscriber ecosystem. If it isn't already in the system, 
    /// it is added and it's new ID is returned.
    /// </summary>
    /// <param name="instance"></param>
    /// <returns></returns>
    protected void AddGlobalSubscriberID(UnityEngine.Object instance)
    {
        // Object.GetHashCode is the object's identity (GetInstanceID is obsolete in Unity 6).
        int instanceID = instance.GetHashCode();
        if (!AllSubscribers.ContainsKey(instanceID))
            AllSubscribers[instanceID] = instance;
    }

    /// <summary>
    /// Removes all of the MonoBehaviour's Subscriptions to this Event.
    /// </summary>
    /// <param name="monoBehaviour"></param>
    public void Unsubscribe(UnityEngine.Object monoBehaviour)
    {
        if (!Application.isPlaying) return;

        _subscribersCount -= _subs.RemoveAll(sub => ReferenceEquals(sub.Parent, monoBehaviour));
    }


    protected virtual void InternalInvoke(Union value)
    { }

    void OnValidate()
    {
        ValidateSubscribers();
    }

    protected virtual void ValidateSubscribers()
    { }

    [Serializable]
    public class EventToggler
    {
        public SignalData Event;
        public bool Value = false;
    }

    public void Invoke(UnityEngine.Object target)
    {
        FinalInvoke(target);
    }
    public void Invoke<T>(UnityEngine.Object target, T arg)
    {
        RuntimeArgs[0].SetValue(arg);
        FinalInvoke(target, RuntimeArgs);
    }
    public void Invoke<T1, T2>(UnityEngine.Object target, T1 arg1, T2 arg2)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        FinalInvoke(target, RuntimeArgs);
    }
    public void Invoke<T1, T2, T3>(UnityEngine.Object target, T1 arg1, T2 arg2, T3 arg3)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        FinalInvoke(target, RuntimeArgs);
    }
    public void Invoke<T1, T2, T3, T4>(UnityEngine.Object target, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        FinalInvoke(target, RuntimeArgs);
    }
    public void Invoke<T1, T2, T3, T4, T5>(UnityEngine.Object target, T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        FinalInvoke(target, RuntimeArgs);
    }
    public void Invoke<T1, T2, T3, T4, T5, T6>(UnityEngine.Object target,
    T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        RuntimeArgs[5].SetValue(arg6);
        FinalInvoke(target, RuntimeArgs);
    }

    public void Invoke<T1, T2, T3, T4, T5, T6, T7>(UnityEngine.Object target,
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        RuntimeArgs[5].SetValue(arg6);
        RuntimeArgs[6].SetValue(arg7);
        FinalInvoke(target, RuntimeArgs);
    }

    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8>(UnityEngine.Object target,
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        RuntimeArgs[5].SetValue(arg6);
        RuntimeArgs[6].SetValue(arg7);
        RuntimeArgs[7].SetValue(arg8);
        FinalInvoke(target, RuntimeArgs);
    }

    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9>(UnityEngine.Object target,
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5, T6 arg6, T7 arg7, T8 arg8, T9 arg9)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        RuntimeArgs[5].SetValue(arg6);
        RuntimeArgs[6].SetValue(arg7);
        RuntimeArgs[7].SetValue(arg8);
        RuntimeArgs[8].SetValue(arg9);
        FinalInvoke(target, RuntimeArgs);
    }

    public void Invoke<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(UnityEngine.Object target,
        T1 arg1, T2 arg2, T3 arg3, T4 arg4, T5 arg5,
        T6 arg6, T7 arg7, T8 arg8, T9 arg9, T10 arg10)
    {
        RuntimeArgs[0].SetValue(arg1);
        RuntimeArgs[1].SetValue(arg2);
        RuntimeArgs[2].SetValue(arg3);
        RuntimeArgs[3].SetValue(arg4);
        RuntimeArgs[4].SetValue(arg5);
        RuntimeArgs[5].SetValue(arg6);
        RuntimeArgs[6].SetValue(arg7);
        RuntimeArgs[7].SetValue(arg8);
        RuntimeArgs[8].SetValue(arg9);
        RuntimeArgs[9].SetValue(arg10);
        FinalInvoke(target, RuntimeArgs);
    }

    protected void FinalInvoke(UnityEngine.Object target, Union[] args = default)
    {
        // No target means a global invoke that reaches every subscriber. A destroyed target is not "no target".
        bool isGlobal = ReferenceEquals(target, null);

        // A disabled MonoBehaviour doesn't send.
        if (target is MonoBehaviour mb && !mb.enabled)
            return;

        // Indexed, so subscriptions added by a callback don't break the loop.
        for (int i = 0; i < _subs.Count; i++)
        {
            var sub = _subs[i];
            if (isGlobal || ReferenceEquals(sub.Parent, target))
                sub?.Invoke(args);
        }

        // Schematic Behaviors and Graphs listen by the UID of the object the target component belongs to.
        DeliverComponentInvokeToListeners(target, args);
    }

    public void Subscribe(UnityEngine.Object instance, Action action)
    {
        _subs.Add(new Subscription
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T>(UnityEngine.Object instance, Action<T> action)
    {
        _subs.Add(new Subscription<T>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2>(UnityEngine.Object instance, Action<T1, T2> action)
    {
        _subs.Add(new Subscription<T1, T2>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3>(UnityEngine.Object instance, Action<T1, T2, T3> action)
    {
        _subs.Add(new Subscription<T1, T2, T3>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4>(UnityEngine.Object instance, Action<T1, T2, T3, T4> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5>(UnityEngine.Object instance, Action<T1, T2, T3, T4, T5> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5, T6>(UnityEngine.Object instance, Action<T1, T2, T3, T4, T5, T6> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5, T6>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5, T6, T7>(UnityEngine.Object instance, Action<T1, T2, T3, T4, T5, T6, T7> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5, T6, T7>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5, T6, T7, T8>(UnityEngine.Object instance, Action<T1, T2, T3, T4, T5, T6, T7, T8> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5, T6, T7, T8, T9>(UnityEngine.Object instance, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            Parent = instance,
            action = action
        });
    }

    public void Subscribe<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
        UnityEngine.Object instance,
        Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action)
    {
        _subs.Add(new Subscription<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            Parent = instance,
            action = action
        });
    }

    public interface ISubscription
    {
        public UnityEngine.Object Parent { get; set; }
        public void Invoke(Union[] values);
    }
    public class Subscription : ISubscription
    {
        public Action action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if(Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke();
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke();
        }
    }
    public class Subscription<T1> : ISubscription
    {
        public Action<T1> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>()
                );
        }
    }
    public class Subscription<T1, T2> : ISubscription
    {
        public Action<T1, T2> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>()
                );
        }
    }
    public class Subscription<T1, T2, T3> : ISubscription
    {
        public Action<T1, T2, T3> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4> : ISubscription
    {
        public Action<T1, T2, T3, T4> action;
        public UnityEngine.Object Parent { get; set; }
        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5, T6> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5, T6> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>(),
                        values[5].GetValue<T6>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>(),
                    values[5].GetValue<T6>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5, T6, T7> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5, T6, T7> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>(),
                        values[5].GetValue<T6>(),
                        values[6].GetValue<T7>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>(),
                    values[5].GetValue<T6>(),
                    values[6].GetValue<T7>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5, T6, T7, T8> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5, T6, T7, T8> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>(),
                        values[5].GetValue<T6>(),
                        values[6].GetValue<T7>(),
                        values[7].GetValue<T8>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>(),
                    values[5].GetValue<T6>(),
                    values[6].GetValue<T7>(),
                    values[7].GetValue<T8>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5, T6, T7, T8, T9> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>(),
                        values[5].GetValue<T6>(),
                        values[6].GetValue<T7>(),
                        values[7].GetValue<T8>(),
                        values[8].GetValue<T9>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>(),
                    values[5].GetValue<T6>(),
                    values[6].GetValue<T7>(),
                    values[7].GetValue<T8>(),
                    values[8].GetValue<T9>()
                );
        }
    }
    public class Subscription<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : ISubscription
    {
        public Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> action;
        public UnityEngine.Object Parent { get; set; }

        public void Invoke(Union[] values)
        {
            if (Parent is MonoBehaviour mb)
            {
                if (mb.enabled)
                    action?.Invoke(
                        values[0].GetValue<T1>(),
                        values[1].GetValue<T2>(),
                        values[2].GetValue<T3>(),
                        values[3].GetValue<T4>(),
                        values[4].GetValue<T5>(),
                        values[5].GetValue<T6>(),
                        values[6].GetValue<T7>(),
                        values[7].GetValue<T8>(),
                        values[8].GetValue<T9>(),
                        values[9].GetValue<T10>()
                    );
                else
                    return; // disabled MonoBehaviours don't receive
            }
            else
                action?.Invoke(
                    values[0].GetValue<T1>(),
                    values[1].GetValue<T2>(),
                    values[2].GetValue<T3>(),
                    values[3].GetValue<T4>(),
                    values[4].GetValue<T5>(),
                    values[5].GetValue<T6>(),
                    values[6].GetValue<T7>(),
                    values[7].GetValue<T8>(),
                    values[8].GetValue<T9>(),
                    values[9].GetValue<T10>()
                );
        }
    }
}
