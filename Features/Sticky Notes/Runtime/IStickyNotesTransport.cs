using System;
using UnityEngine;

namespace Remedy.StickyNotes
{
    /// <summary>
    /// How notes reach the collaboration room. There are two: <see cref="CollabRuntimeTransport"/> (builds, and play mode
    /// when no editor transport is installed) and the editor's CollabSession transport (in the Sticky Notes editor
    /// assembly). Both talk to the same room, channel and shared keys, so a note made in a build shows up in editors and
    /// the other way round. Only one is used at a time (<see cref="StickyNoteStore.Transport"/>) so a note isn't sent twice
    /// from one process.
    /// </summary>
    public interface IStickyNotesTransport
    {
        bool IsConnected { get; }
        /// <summary>True in the editor: this side writes the notes file and commits it.</summary>
        bool CanSaveToRepository { get; }
        string LocalPeerId { get; }
        string LocalName { get; }
        Color LocalColor { get; }

        void Send(string channel, string json);
        void Subscribe(string channel, Action<string, string> handler);
        void Unsubscribe(string channel, Action<string, string> handler);

        void SetShared(string key, string json);
        string GetShared(string key);

        event Action<string> SharedChanged;
        event Action Connected;
        event Action Disconnected;
    }
}
