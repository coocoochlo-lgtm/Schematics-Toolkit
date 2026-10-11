using UnityEngine;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// A Manifold Neuron Network's shape and training settings, as an asset. Every network made from it starts from
    /// the same seed, so they're identical until they learn differently.
    /// </summary>
    [CreateAssetMenu(menuName = "Remedy/Manifold Network/Config", fileName = "Manifold Network Config")]
    public sealed class MnnConfig : ScriptableObject
    {
        public MnnSettings Settings = new();

        private void OnValidate() => Settings.Clamp();

        public static MnnConfig Create(MnnSettings settings)
        {
            var config = CreateInstance<MnnConfig>();
            config.Settings = settings.Clone();
            return config;
        }
    }
}
