using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics.Collaboration
{
    // Wire formats for the collaboration feature channels. Everything is plain JsonUtility so it's readable in logs and
    // needs no third-party JSON library. JsonUtility has a nesting limit of 10, so hierarchies are flattened into lists.

    /// <summary>Channel and presence key names, in one place so the README and the code agree.</summary>
    public static class CollabChannels
    {
        /// <summary>Live scene edits (<see cref="SceneMsg"/>).</summary>
        public const string Scene = CollabLiveEdits.Channel;
        /// <summary>Schematic Graph edits (<see cref="GraphMsg"/>).</summary>
        public const string Graph = "graph";
        /// <summary>Chat (<see cref="ChatMsg"/>).</summary>
        public const string Chat = "chat";

        /// <summary>Presence: Scene view camera (<see cref="CameraPresence"/>).</summary>
        public const string PresenceCamera = "camera";
        /// <summary>Presence: scene selection (<see cref="SelectionPresence"/>).</summary>
        public const string PresenceSelection = "selection";
        /// <summary>Presence: where the person is working (<see cref="LocationPresence"/>).</summary>
        public const string PresenceLocation = "location";
        /// <summary>Presence: Schematic Graph cursor and selection (<see cref="GraphPresence"/>).</summary>
        public const string PresenceGraph = "graph";

        /// <summary>Shared store key holding a scene's diff log since its last commit (<see cref="SceneLog"/>).</summary>
        public static string SharedSceneKey(string scenePath) => "scene:" + scenePath;
        /// <summary>Shared store key holding the latest content of a graph edited this session (<see cref="GraphMsg"/>).</summary>
        public static string SharedGraphKey(string guid) => "graph:" + guid;
        /// <summary>Shared store key holding the last 200 chat messages (<see cref="ChatLog"/>).</summary>
        public const string SharedChat = "chat";
    }

    /// <summary>One serialized property value: its path and its value encoded as text (see <see cref="PropertyCodec"/>).</summary>
    [Serializable]
    public class PropValue
    {
        public string p;
        public string v;
        public PropValue() { }
        public PropValue(string path, string value) { p = path; v = value; }
    }

    /// <summary>A component's full data in a snapshot.</summary>
    [Serializable]
    public class ComponentData
    {
        /// <summary>Assembly-qualified type name.</summary>
        public string type;
        /// <summary>"TypeFullName:index" among same-typed components on the GameObject.</summary>
        public string key;
        /// <summary>EditorJsonUtility data. Only filled for components with [SerializeReference] data, which property values can't carry.</summary>
        public string json;
        /// <summary>Every supported serialized property (object references made portable). Applied after <see cref="json"/>.</summary>
        public List<PropValue> props = new();
    }

    /// <summary>One GameObject in a flattened hierarchy snapshot.</summary>
    [Serializable]
    public class SnapshotNode
    {
        public string id;
        /// <summary>Index of the parent node in the snapshot, or -1 for the snapshot root.</summary>
        public int parent = -1;
        public int sibling;
        public string name;
        public string tag;
        public int layer;
        public bool active = true;
        public int staticFlags;
        /// <summary>Asset GUID of the prefab this node is an instance root of (outermost or added), else empty.</summary>
        public string prefabGuid;
        /// <summary>For a GameObject that comes from its prefab-root ancestor's prefab: its transform path below that root.</summary>
        public string prefabPath;
        /// <summary>Renderer bounds in this node's local space, unscaled (for placeholders).</summary>
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        /// <summary>Components. For prefab instances only overridden properties are included.</summary>
        public List<ComponentData> comps = new();
    }

    /// <summary>An alias from one object id to another (an unsaved object's collab id and its id once the host saved it).</summary>
    [Serializable]
    public class IdAlias
    {
        public string from;
        public string to;
    }

    /// <summary>Message on the "scene" channel.</summary>
    [Serializable]
    public class SceneMsg
    {
        /// <summary>props | comp | create | destroy | parent | structure | committed</summary>
        public string op;
        /// <summary>Scene asset path the change belongs to.</summary>
        public string scene;
        /// <summary>Unique message id ("peerId:counter"), used to skip messages already applied (log replay).</summary>
        public string mid;
        /// <summary>Sender peer id (filled in by the sender, so the host's log keeps authorship).</summary>
        public string from;
        /// <summary>Target GameObject id.</summary>
        public string id;
        /// <summary>Component key ("" = the GameObject itself) for props/comp.</summary>
        public string comp;
        public List<PropValue> props = new();
        /// <summary>Full component data for "comp".</summary>
        public ComponentData data;
        /// <summary>Hierarchy for create/structure (index 0 is the root).</summary>
        public List<SnapshotNode> nodes = new();
        /// <summary>New parent id for create/parent ("" = scene root).</summary>
        public string parentId;
        public int sibling;
        public List<IdAlias> aliases = new();
    }

    /// <summary>The host's record of a scene since its last commit, kept in the shared store for late joiners.</summary>
    [Serializable]
    public class SceneLog
    {
        /// <summary>Changes every commit, so joiners can tell an old log from a new one.</summary>
        public string baseId;
        public string committedAt;
        public List<SceneMsg> entries = new();
        public List<IdAlias> aliases = new();
    }

    /// <summary>Message on the "graph" channel: the saved asset of an edited Schematic Graph.</summary>
    [Serializable]
    public class GraphMsg
    {
        /// <summary>changed</summary>
        public string op = "changed";
        public string guid;
        public string path;
        public string from;
        public int rev;
        /// <summary>"text" (force-text YAML) or "base64" (binary serialization).</summary>
        public string enc;
        public string data;
        /// <summary>EditorJsonUtility of the graph, informational only (object references in it are local instance ids).</summary>
        public string json;
    }

    /// <summary>Message on the "chat" channel.</summary>
    [Serializable]
    public class ChatMsg
    {
        public string id;
        public string from;
        public string name;
        /// <summary>Hex colour, e.g. "#FF8800".</summary>
        public string color;
        public string text;
        /// <summary>Unix time in milliseconds.</summary>
        public long t;
    }

    [Serializable]
    public class ChatLog
    {
        public List<ChatMsg> messages = new();
    }

    /// <summary>Presence "camera": the Scene view camera.</summary>
    [Serializable]
    public class CameraPresence
    {
        public string scene;
        public List<string> scenes = new();
        public Vector3 pivot;
        public Quaternion rotation = Quaternion.identity;
        public float size = 10f;
        public bool ortho;
        public bool in2D;
        /// <summary>The camera's world position (where the avatar goes).</summary>
        public Vector3 position;
    }

    /// <summary>Presence "selection": selected scene objects.</summary>
    [Serializable]
    public class SelectionPresence
    {
        public string scene;
        public List<string> ids = new();
        /// <summary>Unix ms when this selection was made: the earlier selector keeps a contested object.</summary>
        public long t;
    }

    /// <summary>Presence "location": what the person is working on (for the Team window and jumping).</summary>
    [Serializable]
    public class LocationPresence
    {
        /// <summary>scene | graph | none</summary>
        public string kind = "none";
        public string path;
        public string name;
        public List<string> scenes = new();
        /// <summary>Active | Idle | Play Mode</summary>
        public string status = "Active";
    }

    /// <summary>Presence "graph": the Schematic Graph being looked at.</summary>
    [Serializable]
    public class GraphPresence
    {
        public string path;
        public string guid;
        public Vector2 cursor;
        public bool cursorInside;
        public List<string> selected = new();
        public long t;
        public Vector3 viewPosition;
        public float viewScale = 1f;
    }
}
