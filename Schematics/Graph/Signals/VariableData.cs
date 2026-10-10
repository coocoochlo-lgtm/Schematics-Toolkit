using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using Remedy.Schematics.Utils;
using System.Data.SqlTypes;

public class VariableData : ScriptableObject
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

    public VariableData(string name)
    {
        Name = name;
    }

    
}
