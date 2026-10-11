using System;

namespace Remedy.ManifoldNetwork
{
    public enum SamplingMode { Soft, Nearest }
    public enum PerturbMode { Antithetic, OneSided }

    /// <summary>
    /// How the network learns. Supervised: every step comes with the right answers (teaching). Population: many
    /// instances share the brain and each tries a different nudge, scored by the errors they report. Sequential: one
    /// instance tries the nudges one window at a time.
    /// </summary>
    public enum TrainingMode { Supervised, Population, Sequential }

    /// <summary>
    /// Every hyperparameter of a Manifold Neuron Network. Plain data (no Unity types), so the CPU reference can be
    /// built and tested anywhere; <see cref="MnnConfig"/> wraps it as an asset.
    /// </summary>
    [Serializable]
    public sealed class MnnSettings
    {
        // Shape.
        public int inputCount = 8;
        public int outputCount = 4;
        public int headCount = 8;
        public int maskWidth = 8;
        public int spaceDimensions = 3;
        public int pointsPerSpace = 256;
        public int hiddenWidth = 16;
        public int hiddenLayers = 1;

        // Movement through the value spaces.
        public float dispersion = 0.15f;
        public float trajectorySpeed = 0.05f;
        public float pull = 0.5f;
        public float inertiaMin = 0.5f;
        public float inertiaMax = 0.98f;
        public float trailRate = 0.1f;
        public SamplingMode samplingMode = SamplingMode.Soft;

        // Training.
        public TrainingMode trainingMode = TrainingMode.Supervised;
        public PerturbMode perturbMode = PerturbMode.Antithetic;
        public int perturbPairs = 8;
        public float perturbSigma = 0.02f;
        public float learningRate = 0.001f;   // Adam step size
        public float momentum = 0.9f;          // Adam β1
        public int evalWindow = 1;
        public float errorFalloff = 0.9f;
        public float baselineFalloff = 0.999f;

        // Adaptive sine activation.
        public float sineOmega = 3f;
        public float sineGain = 1f;
        public float sineAlphaMax = 0.5f;
        public bool alphaScalesSigma = false;

        // Keeping every head in use.
        public float balanceLambda = 0.01f;
        public float reviveThreshold = 0.05f;
        public int reviveSteps = 500;

        // Improvements.
        public bool multiTimescaleHeads = true;
        public bool bestWeightsRollback = true;
        public float rollbackMargin = 1f;      // baseline twice the best
        public int rollbackSteps = 1000;
        public bool clockInputs = false;
        public float[] clockFrequencies = { 0.01f, 0.05f, 0.2f };
        public bool changeInputs = false;
        public bool velocityReadout = false;
        public bool surpriseReset = false;
        public float resetThreshold = 2.5f;
        public float resetAmount = 0.5f;
        public bool trainableValueSpaces = false;
        public float valueSpaceRate = 0.1f;

        public uint seed = 1234;

        public const int MaxDimensions = 8;
        public const int MaxOutputs = 64;
        public const int MaxHiddenWidth = 64;
        public const int MaxHeads = 256;

        /// <summary>Points are spread over a torus from -SpaceRadius to +SpaceRadius on every axis.</summary>
        public const float SpaceRadius = 1f;

        public int ClockCount => clockInputs && clockFrequencies != null ? clockFrequencies.Length : 0;

        /// <summary>The master's inputs: base inputs, last step's outputs, the error ratio, clocks, and changes.</summary>
        public int MasterInputs => inputCount + outputCount + 1 + 2 * ClockCount + (changeInputs ? inputCount : 0);

        /// <summary>The translator's inputs: each head's sample and trail (and velocity), plus the base inputs.</summary>
        public int TranslatorInputs => headCount * (2 * outputCount + (velocityReadout ? spaceDimensions : 0)) + inputCount;

        public int TranslatorHeadBlock => 2 * outputCount + (velocityReadout ? spaceDimensions : 0);

        /// <summary>Copies of the network run per instance during a training window.</summary>
        public int CopiesPerWindow => perturbMode == PerturbMode.Antithetic ? 1 + 2 * perturbPairs : 1 + perturbPairs;

        /// <summary>Throws if a value is out of range; the network can't run with it.</summary>
        public void Validate()
        {
            Check(inputCount >= 1, "inputCount must be at least 1");
            Check(outputCount >= 1 && outputCount <= MaxOutputs, $"outputCount must be 1-{MaxOutputs}");
            Check(headCount >= 1 && headCount <= MaxHeads, $"headCount must be 1-{MaxHeads}");
            Check(maskWidth >= 1, "maskWidth must be at least 1");
            Check(spaceDimensions >= 1 && spaceDimensions <= MaxDimensions, $"spaceDimensions must be 1-{MaxDimensions}");
            Check(pointsPerSpace >= 1, "pointsPerSpace must be at least 1");
            Check(hiddenWidth >= 1 && hiddenWidth <= MaxHiddenWidth, $"hiddenWidth must be 1-{MaxHiddenWidth}");
            Check(hiddenLayers >= 0 && hiddenLayers <= 4, "hiddenLayers must be 0-4");
            Check(dispersion > 0f, "dispersion must be positive");
            Check(perturbPairs >= 1, "perturbPairs must be at least 1");
            Check(perturbSigma > 0f, "perturbSigma must be positive");
            Check(evalWindow >= 1, "evalWindow must be at least 1");
            Check(inertiaMin >= 0f && inertiaMax < 1f && inertiaMin <= inertiaMax, "inertia must be 0 <= min <= max < 1");
        }

        /// <summary>Clamps values into range (for OnValidate).</summary>
        public void Clamp()
        {
            inputCount = Math.Max(1, inputCount);
            outputCount = Math.Clamp(outputCount, 1, MaxOutputs);
            headCount = Math.Clamp(headCount, 1, MaxHeads);
            maskWidth = Math.Max(1, maskWidth);
            spaceDimensions = Math.Clamp(spaceDimensions, 1, MaxDimensions);
            pointsPerSpace = Math.Max(1, pointsPerSpace);
            hiddenWidth = Math.Clamp(hiddenWidth, 1, MaxHiddenWidth);
            hiddenLayers = Math.Clamp(hiddenLayers, 0, 4);
            dispersion = Math.Max(1e-4f, dispersion);
            perturbPairs = Math.Max(1, perturbPairs);
            perturbSigma = Math.Max(1e-6f, perturbSigma);
            evalWindow = Math.Max(1, evalWindow);
            inertiaMin = Math.Clamp(inertiaMin, 0f, 0.999f);
            inertiaMax = Math.Clamp(inertiaMax, inertiaMin, 0.999f);
        }

        private static void Check(bool ok, string message)
        {
            if (!ok) throw new ArgumentException("Manifold network: " + message);
        }

        public MnnSettings Clone()
        {
            var copy = (MnnSettings)MemberwiseClone();
            copy.clockFrequencies = (float[])clockFrequencies?.Clone();
            return copy;
        }

        /// <summary>
        /// Identifies everything the weights depend on (shape, seed, toggles that add inputs). Brains with the same
        /// signature can swap and mix weights.
        /// </summary>
        public string LayoutSignature =>
            $"I{inputCount}N{outputCount}H{headCount}M{maskWidth}D{spaceDimensions}P{pointsPerSpace}W{hiddenWidth}L{hiddenLayers}" +
            $"C{ClockCount}X{(changeInputs ? 1 : 0)}V{(velocityReadout ? 1 : 0)}T{(trainableValueSpaces ? 1 : 0)}S{seed}";
    }
}
