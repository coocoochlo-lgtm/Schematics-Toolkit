using Remedy.Framework;
using Remedy.Schematics;
using System;

public class VariableBase
{
    public string _name;
    protected VariableData _data;
    protected bool _failed = false;
    protected bool _loaded = false;

    public bool TryGetData(UnityEngine.Object component, out VariableData data)
    {
        if (_failed)
        {
            data = null;
            return false;
        }
        if (_loaded)
        {
            data = _data;
            return true;
        }

        var controller = component.GetCachedComponentInParents<SchematicObject>();
        _data = controller.SchematicGraph.EditorVariableCache[_name];
        if (_data == null)
        {
            _failed = true;
            data = null;
            return false;
        }
        else
        {
            _loaded = true;
            data = _data;
            return true;
        }
    }
}

public class Variable<T> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }

    public static implicit operator T(Variable<T> v)
    {
        if(v.TryGetData(v.compon out VariableData data))
        {

        }
        return default;
    }

    public static implicit operator Variable<T>(T value)
    {
        return new Variable<T>(value);
    }
}
public class Variable<T1, T2> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }


}
public class Variable<T1, T2, T3> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }

}
public class Variable<T1, T2, T3, T4> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }

}
public class Variable<T1, T2, T3, T4, T5> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }

}
public class Variable<T1, T2, T3, T4, T5, T6> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }
    
}
public class Variable<T1, T2, T3, T4, T5, T6, T7> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }
    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7> method)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Invoke(val1, val2, val3, val4, val5, val6, val7);
    }
}
public class Variable<T1, T2, T3, T4, T5, T6, T7, T8> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }
    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8> method)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Invoke(val1, val2, val3, val4, val5, val6, val7, val8);
    }
}
public class Variable<T1, T2, T3, T4, T5, T6, T7, T8, T9> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }
    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> method)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Invoke(val1, val2, val3, val4, val5, val6, val7, val8, val9);
    }
}
public class Variable<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : VariableBase
{
    public Variable(string name)
    {
        _name = name;
    }
    public void Subscribe(UnityEngine.Object component, Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> method)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Subscribe(component, method);
    }
    public void Invoke(UnityEngine.Object component, T1 val1, T2 val2, T3 val3, T4 val4, T5 val5, T6 val6, T7 val7, T8 val8, T9 val9, T10 val10)
    {
        TryGetData(component);

        if (_failed) return;

        _data.Invoke(val1, val2, val3, val4, val5, val6, val7, val8, val9, val10);
    }
}