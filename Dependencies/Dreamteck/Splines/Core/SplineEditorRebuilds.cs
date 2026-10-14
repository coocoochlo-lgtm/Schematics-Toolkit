#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace Dreamteck.Splines
{
    /// <summary>
    /// Schematics addition. Outside Play Mode, Dreamteck rebuilt a spline and every mesh on it synchronously on every
    /// Rebuild() call, which happens several times per mouse event while dragging points (and twice per spline user
    /// when a scene opens). Requests are now collected and run once per editor update instead; spline users marked
    /// multithreaded build their mesh on a worker thread (see <see cref="SplineUser"/>).
    /// RebuildImmediate() is unchanged and still synchronous.
    /// </summary>
    internal static class SplineEditorRebuilds
    {
        private static readonly HashSet<SplineComputer> _computers = new HashSet<SplineComputer>();
        private static readonly HashSet<SplineUser> _users = new HashSet<SplineUser>();
        private static bool _scheduled;

        /// <summary>True while queued computers are rebuilding: their users are queued rather than built on the spot.</summary>
        internal static bool Flushing { get; private set; }

        public static void Request(SplineComputer computer)
        {
            if (computer == null) return;
            _computers.Add(computer);
            Schedule();
        }

        public static void Request(SplineUser user)
        {
            if (user == null) return;
            _users.Add(user);
            Schedule();
        }

        private static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.update += Flush;
        }

        private static readonly List<SplineComputer> _computerBuffer = new List<SplineComputer>();
        private static readonly List<SplineUser> _userBuffer = new List<SplineUser>();

        private static void Flush()
        {
            EditorApplication.update -= Flush;
            _scheduled = false;

            // Splines first: resampling them queues their users, which then build once below.
            _computerBuffer.Clear();
            _computerBuffer.AddRange(_computers);
            _computers.Clear();
            Flushing = true;
            try
            {
                foreach (var computer in _computerBuffer)
                    if (computer != null)
                        computer.RebuildImmediate(true, false);
            }
            finally
            {
                Flushing = false;
            }

            _userBuffer.Clear();
            _userBuffer.AddRange(_users);
            _users.Clear();
            foreach (var user in _userBuffer)
                if (user != null)
                    user.EditorBuildQueued();
        }
    }
}
#endif
