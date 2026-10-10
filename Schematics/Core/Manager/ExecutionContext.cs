using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

public class ExecutionContext : Singleton<ExecutionContext>
{
    static private Dictionary<int, GameObject> _instanceLookup = new();
    static private List<BehaviorRuntime> _behaviorRuntimes = new();
    static private int _varIDCoutner = -1;
    /// <summary>
    /// All of the active variables in the game
    /// </summary>
    static private List<Union> _variables = new();

    static public Union GetVariable(int variableID)
    {
        return _variables[variableID];
    }

    static public void SetVariable<T>(int variableID, T value)
    {
        _variables[variableID].SetValue<T>(value);
    }

    /// <summary>
    /// 
    /// </summary>
    static public void AddInstanceVariable(GameObject instance, )
    {

    }

    static public int GetCacheID()
    {
        return -1;
    }

    static public GameObject GetObject(int instanceID)
    {
        return _instanceLookup[instanceID];
    }
}