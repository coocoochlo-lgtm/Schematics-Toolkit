using BlueGraph;
using System;

namespace Remedy.Schematics
{
    /// <summary>In a state's graph: runs when the state is entered.</summary>
    [Serializable]
    [Node(Name = "On State Enter", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateEnter : SchematicEventNode { }

    /// <summary>In a state's graph: runs every frame while the state (or one of its children) is active.</summary>
    [Serializable]
    [Node(Name = "On State Update", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateUpdate : SchematicEventNode { }

    /// <summary>In a state's graph: runs when the state is left.</summary>
    [Serializable]
    [Node(Name = "On State Exit", Path = "Events/State Machine"), Tags("Object", "State")]
    public class OnStateExit : SchematicEventNode { }
}
