using UnityEngine;

namespace Remedy.Schematics
{
    /// <summary>
    /// A stand-in for a controller on another player's machine, so possession reads the same everywhere: Get Controller
    /// on an object another player possesses gives this, On Possessed fires with it, and local input never reaches what
    /// it possesses. It never gives input itself: that player's input happens on their machine and reaches everyone
    /// through the object's networked Signals and Variables.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("")]
    public sealed class RemoteController : Controller
    {
        private static Transform _root;

        private string _id, _peer, _displayName;

        /// <summary>Its name across the network ("3:p0": machine 3's Player 1).</summary>
        public string Key { get; private set; }

        /// <summary>The machine it's on (its network client id).</summary>
        public string Peer => _peer;

        public override bool IsLocal => false;

        public override bool IsPlayer => _id == "d" || (_id != null && _id.StartsWith("p"));

        public override string NetworkId => _id;

        public override string DisplayName => !string.IsNullOrEmpty(_displayName) ? _displayName : $"{(IsPlayer ? "Player" : "AI")} {Index + 1} (machine {_peer})";

        internal static RemoteController Create(string key, string peer, string id, string displayName)
        {
            if (_root == null)
            {
                var root = new GameObject("[Remote Controllers]");
                DontDestroyOnLoad(root);
                _root = root.transform;
            }
            var gameObject = new GameObject(key);
            gameObject.transform.SetParent(_root, false);
            var controller = gameObject.AddComponent<RemoteController>();
            controller.Key = key;
            controller._peer = peer;
            controller._id = id;
            controller._displayName = displayName;
            controller.Index = id != null && id.Length > 1 && int.TryParse(id.Substring(1), out int index) ? index : 0;
            Possession.Register(controller);
            return controller;
        }

        internal void Rename(string displayName) => _displayName = displayName;
    }
}
