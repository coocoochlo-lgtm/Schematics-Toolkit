using UnityEngine;

namespace Remedy.Audio
{
    [CreateAssetMenu(menuName = "Remedy Toolkit/Audio/Audio Clip Tag")]
    public class AudioClipTag : ScriptableObject
    {
        public string TagName = "New Tag";
        public AudioType Type = AudioType.SoundEffect;
        public Color Color = new(0.35f, 0.65f, 0.9f, 1f);
    }
}
