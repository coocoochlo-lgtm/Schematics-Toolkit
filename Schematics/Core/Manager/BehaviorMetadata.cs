using PlasticPipe.PlasticProtocol.Messages;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Metadata of a Behavior. Used as a lookup for BehaviorRuntime
/// </summary>
public class BehaviorMetadata : ScriptableObjectWithID<BehaviorMetadata>
{
    /// <summary>
    /// Gets the Behavior Metadata for the given Behavior Type
    /// </summary>
    /// <param name="type"></param>
    /// <returns></returns>
    public static BehaviorMetadata Get(Type behaviorType)
    {
        foreach(var kvp in Lookup)
        {
            if(kvp.Value.BehaviorType.Type == behaviorType)
            {
                return kvp.Value;
            }
        }
        return null;
    }

    [HideInInspector]
    public SerializableType BehaviorType; 
    public SerializableDictionary<string, Type> Variables = new();
    public SerializableDictionary<string, List<string>> Signals = new();

    /// <summary>
    /// Default values applied to Schematic Objects that implement this behavior, as defined within their
    /// inspector.
    /// </summary>
    public SerializableDictionary<GameObject, SerializableTuple<SerializableType, Union>> DefaultValues = new();
}