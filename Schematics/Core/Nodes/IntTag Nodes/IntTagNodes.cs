using BlueGraph;
using Remedy.Framework;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// Shared plumbing for the Int Tag action nodes: reading a tag or an object from an input that may or may not be
    /// connected. With nothing connected, the Tag input uses the dropdown on the node and the Object input means the
    /// object running the graph.
    /// </summary>
    [Serializable]
    public abstract class IntTagActionNode : SchematicActionNode
    {
        public const string TagPort = "Tag";
        public const string ObjectPort = "Object";

        /// <summary>The connected tag, or <paramref name="fallback"/> (the node's dropdown) when nothing is connected.</summary>
        protected IntTag ReadTag(string port, IntTag fallback)
        {
            // Ints (from other tag nodes), IntTags and boxed tags from Variables or Signals all convert; anything else
            // (or nothing connected) uses the dropdown.
            return GetInputValue(port, fallback);
        }

        /// <summary>
        /// The connected GameObject (a connected component counts as its GameObject), or <paramref name="fallback"/>
        /// when nothing is connected.
        /// </summary>
        protected GameObject ReadObject(string port, GameObject fallback)
        {
            if (!HasInputConnections(port))
                return fallback;

            // Read as a plain Unity Object so a Collider or Transform wired in isn't mistaken for a GameObject.
            var value = GetInputValue<UnityEngine.Object>(port, null);
            return value switch
            {
                GameObject go => go,
                Component component when component != null => component.gameObject,
                _ => null,
            };
        }
    }

    [SchematicDoc(@"{""name"": ""Find With Tag"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects/Int Tags"", ""description"": ""Every active object with an Int Tag, plus the first one and how many there are. A dictionary lookup, so it's cheap enough to use often."", ""inputs"": [{""name"": ""Tag"", ""type"": ""IntTag""}], ""outputs"": [{""name"": ""Objects"", ""type"": ""List<Union>""}, {""name"": ""First"", ""type"": ""GameObject""}, {""name"": ""Count"", ""type"": ""int""}], ""use_for"": [""finding the player"", ""finding every enemy, checkpoint or spawn point""], ""notes"": ""Runs when flow reaches it: connect an arrow into it, and from it to what runs next. Pick the tag from the dropdown, or choose Add Tag... to make one. First is empty when nothing has the tag.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Find With Tag", Path = "Objects/Int Tags"), Tags("Default")]
    public class FindWithIntTag : IntTagActionNode
    {
        [Input(TagPort, Editable = true)]
        public IntTag Tag = IntTags.Player;

        [Output("Objects")]
        public List<Union> Objects;
        [Output("First")]
        public GameObject First;
        [Output("Count")]
        public int Count;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var found = IntTagRegistry.Find(ReadTag(TagPort, Tag));

            // A new list each time: a flow that runs later (after a Wait) keeps the one it was given.
            var list = new List<Union>(found.Count);
            for (int i = 0; i < found.Count; i++)
                list.Add(new Union(found[i]));

            SetOutputValue("Objects", new Union(list));
            SetOutputValue("First", found.Count > 0 ? new Union(found[0]) : Union.Null);
            SetOutputValue("Count", new Union(found.Count));
        }
    }

    [SchematicDoc(@"{""name"": ""Has Tag"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects/Int Tags"", ""description"": ""Whether an object (or, with nothing connected, the object running the graph) has an Int Tag. The replacement for Compare Tag."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""outputs"": [{""name"": ""Has Tag"", ""type"": ""bool""}], ""use_for"": [""only reacting to the player in a trigger"", ""telling enemies from pickups""], ""notes"": ""Runs when flow reaches it. Connect Has Tag into an If, or turn on Stop If Missing to only continue when the object has the tag.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Has Tag", Path = "Objects/Int Tags"), Tags("Object")]
    public class HasIntTag : IntTagActionNode
    {
        [Input(ObjectPort, Editable = false)]
        public GameObject Target;
        [Input(TagPort, Editable = true)]
        public IntTag Tag = IntTags.Player;

        [Tooltip("Only run the nodes after this one when the object has the tag.")]
        [Editable("Stop If Missing")]
        public bool StopIfMissing;

        [Output("Has Tag")]
        public bool Result;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = ReadObject(ObjectPort, instance);
            bool has = target != null && target.HasIntTag(ReadTag(TagPort, Tag));
            SetOutputValue("Has Tag", new Union(has));
            if (StopIfMissing && !has)
                _processChildren = false;
        }
    }

    [SchematicDoc(@"{""name"": ""Add Tag"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects/Int Tags"", ""description"": ""Gives an object (or, with nothing connected, the object running the graph) an Int Tag, so Find With Tag finds it."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""use_for"": [""marking an enemy as defeated or a door as open"", ""tagging something spawned at runtime""], ""notes"": ""Runs when flow reaches it. The tag lasts until the object is disabled or despawned; to tag it permanently, add the tag to its Tags list in the Inspector.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Add Tag", Path = "Objects/Int Tags"), Tags("Object")]
    public class AddIntTag : IntTagActionNode
    {
        [Input(ObjectPort, Editable = false)]
        public GameObject Target;
        [Input(TagPort, Editable = true)]
        public IntTag Tag = IntTags.Untagged;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = ReadObject(ObjectPort, instance);
            if (target != null)
                IntTagRegistry.Add(target, ReadTag(TagPort, Tag));
        }
    }

    [SchematicDoc(@"{""name"": ""Remove Tag"", ""kind"": ""node"", ""flow"": ""action"", ""category"": ""Objects/Int Tags"", ""description"": ""Takes an Int Tag away from an object (or, with nothing connected, the object running the graph), so Find With Tag stops finding it."", ""inputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""use_for"": [""a pickup that's been collected"", ""an enemy that's no longer a target""], ""notes"": ""Runs when flow reaches it. Tags from the Inspector come back when the object is spawned again.""}")]
    [Serializable]
    [NodeColor(NodeColors.Object)]
    [Node(Name = "Remove Tag", Path = "Objects/Int Tags"), Tags("Object")]
    public class RemoveIntTag : IntTagActionNode
    {
        [Input(ObjectPort, Editable = false)]
        public GameObject Target;
        [Input(TagPort, Editable = true)]
        public IntTag Tag = IntTags.Untagged;

        protected override void OnTrigger(GameObject instance, bool awaiting = false)
        {
            var target = ReadObject(ObjectPort, instance);
            if (target != null)
                IntTagRegistry.Remove(target, ReadTag(TagPort, Tag));
        }
    }

    /// <summary>
    /// Shared filter for the tag-change events: fired by SchematicObject for every tag change in the world, with the
    /// object and the tag id as arguments, and blocked here when it isn't one this node cares about.
    /// </summary>
    [Serializable]
    public abstract class IntTagEventNode : SchematicEventNode
    {
        [Tooltip("Only this tag. Untagged means any tag.")]
        [Editable("Tag")]
        public IntTag Filter = IntTags.Untagged;

        [Tooltip("Only when the tag changes on the object running this graph.")]
        [Editable("Only This Object")]
        public bool OnlyThisObject;

        [Output("Object")]
        public GameObject Object;
        [Output("Tag")]
        public IntTag Tag;

        protected override void OnTrigger(GameObject instance, params Union[] arguments)
        {
            if (arguments == null || arguments.Length < 2)
            {
                _blocked = true;
                return;
            }

            var go = arguments[0].GetValue<GameObject>();
            int tag = arguments[1].GetValue<int>();

            if ((Filter.IsTagged && Filter.Value != tag) || (OnlyThisObject && go != instance))
                _blocked = true;
        }
    }

    [SchematicDoc(@"{""name"": ""On Tag Added"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Int Tags"", ""description"": ""Starts a flow when an object gains an Int Tag (spawned with it, enabled, or tagged by Add Tag). Outputs the object and the tag."", ""outputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""use_for"": [""reacting when the player spawns"", ""counting enemies as they appear""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Set Tag to listen for one tag (Untagged means any), and Only This Object to ignore other objects. Play Mode only. An object's own graph doesn't hear the tags it spawns with (use On Create for that).""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Tag Added", Path = "Events/Int Tags"), Tags("Object")]
    public class OnIntTagAdded : IntTagEventNode
    { }

    [SchematicDoc(@"{""name"": ""On Tag Removed"", ""kind"": ""node"", ""flow"": ""event"", ""category"": ""Events/Int Tags"", ""description"": ""Starts a flow when an object loses an Int Tag (disabled, despawned, or untagged by Remove Tag). Outputs the object and the tag."", ""outputs"": [{""name"": ""Object"", ""type"": ""GameObject""}, {""name"": ""Tag"", ""type"": ""IntTag""}], ""use_for"": [""noticing the last enemy is gone"", ""reacting when a pickup is collected""], ""notes"": ""Starts a flow: connect its arrow to the actions that should run. Set Tag to listen for one tag (Untagged means any), and Only This Object to ignore other objects. Play Mode only.""}")]
    [Serializable]
    [NodeColor(NodeColors.Event)]
    [Node(Name = "On Tag Removed", Path = "Events/Int Tags"), Tags("Object")]
    public class OnIntTagRemoved : IntTagEventNode
    { }
}
