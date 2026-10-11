using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>Marks an object (the player, a camera) whose position shows the chunks of the zones it's in.</summary>
    [AddComponentMenu("Level Streaming/Streaming Target")]
    public class StreamingTarget : MonoBehaviour
    {
        private void OnEnable() => LevelStreamer.Targets.Add(transform);
        private void OnDisable() => LevelStreamer.Targets.Remove(transform);
    }
}
