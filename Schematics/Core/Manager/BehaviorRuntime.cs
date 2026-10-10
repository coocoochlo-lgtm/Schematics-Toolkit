using Remedy.Framework;
using System.Collections.Generic;
using UnityEngine;

internal class BehaviorRuntime
{
    /// <summary>
    /// The Schematic Behavior this Runtime was created for. 
    /// </summary>
    public SchematicBehavior BehaviorType;
    private GameObject _currentInstance;
    private List<GameObject> _managedInstances = new();  
    
    //private Dictionary<int, Union[]> _instanceVariables = new();

    internal void OnUpdate()
    {
        foreach(var instance in _managedInstances)
        {
            _currentInstance = instance;
        }    
    }
}