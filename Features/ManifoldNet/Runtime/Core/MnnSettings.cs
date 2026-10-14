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
    /// How the weights move once the nudges are scored. Momentum is the blueprint's plain step with momentum. Adam scales
    /// each weight's step by its own recent gradient size, so one wild window can't fling the weights.
    /// </summary>
    public enum UpdateRule { Momentum, Adam }

    /// <summary>Which shape the shared plan grid has, and which rulebooks are live (see the WFC section of the README).</summary>
    public enum WfcAxis
    {
        /// <summary>H × 1: heads coordinate what they do right now (Coexist only).</summary>
        Heads,
        /// <summary>H × F, rows independent: each head plans its own sequence (Follow only).</summary>
        Time,
        /// <summary>H × F: a full coordinated plan (Coexist, Follow and Handoff).</summary>
        Grid,
    }

    /// <summary>Which heads count as neighbours in the plan grid.</summary>
    public enum HeadNeighbors { Ring, All }

    /// <summary>Whether every instance shares one episodic memory bank, or each has its own.</summary>
    public enum MemoryScope { Shared, PerInstance }

    /// <summary>
    /// Every hyperparameter of a Manifold Neuron Network. Plain data (no Unity types), so the CPU reference can be
    /// built and tested anywhere; <see cref="MnnConfig"/> wraps it as an asset. Defaults are starting guesses.
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
        /// <summary>Sampling falloff σ, in space-radius units.</summary>
        public float dispersion = 0.15f;
        /// <summary>The most a head moves in one nominal tick.</summary>
        public float trajectorySpeed = 0.05f;
        /// <summary>How hard heads chase the master's target.</summary>
        public float pull = 0.5f;
        /// <summary>How hard heads are pulled toward nearby point clusters (the Hopfield attractor). Keep it small.</summary>
        public float settle = 0.1f;
        /// <summary>The fastest and slowest heads' base timescales τh, in seconds (heads are spread between them).</summary>
        public float timescaleMin = 0.05f;
        public float timescaleMax = 1f;
        /// <summary>Base timescale of the trail ("where I've been") average, in seconds.</summary>
        public float trailTimescale = 0.5f;
        /// <summary>Each head's MLP also outputs a gate that speeds it up when it should pay attention. Off: fixed timescales.</summary>
        public bool liquidGates = true;
        /// <summary>The nominal tick rate (Hz). Movement is scaled by real dt / (1 / tickRate), so uneven ticks behave the same.</summary>
        public float tickRate = 20f;
        public SamplingMode samplingMode = SamplingMode.Soft;

        // Training.
        public TrainingMode trainingMode = TrainingMode.Supervised;
        public PerturbMode perturbMode = PerturbMode.Antithetic;
        public int perturbPairs = 8;
        public float perturbSigma = 0.02f;
        public UpdateRule updateRule = UpdateRule.Adam;
        /// <summary>Step size η (Adam's step size with <see cref="UpdateRule.Adam"/>).</summary>
        public float learningRate = 0.001f;
        /// <summary>Momentum on the step (Adam's β1 with <see cref="UpdateRule.Adam"/>).</summary>
        public float momentum = 0.9f;
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
        /// <summary>Bucket points into a grid so a head only checks nearby cells (D ≤ 3; worth it above ~4,096 points).</summary>
        public bool spatialGrid = false;

        // Shared WFC commitment grid.
        public bool wfcEnabled = false;
        public WfcAxis wfcAxis = WfcAxis.Grid;
        /// <summary>Point types T (max 16).</summary>
        public int typeCount = 8;
        /// <summary>Now + future plan columns F (max 16). Heads axis uses 1.</summary>
        public int planColumns = 8;
        public int planTickSteps = 5;
        public int wfcPasses = 3;
        public int wfcCollapseBudget = 2;
        public HeadNeighbors headNeighbors = HeadNeighbors.Ring;
        public float wfcResetThreshold = 2f;
        public float wfcRuleLrScale = 0.25f;
        public bool masterPlanBias = true;

        // Episodic memory bank.
        public bool memoryEnabled = false;
        public int memorySlots = 64;
        public float memoryBeta = 8f;
        public float writeThreshold = 0.5f;
        public float mergeThreshold = 0.95f;
        /// <summary>Seconds for an unused slot's strength to halve.</summary>
        public float memoryHalfLife = 600f;
        public MemoryScope memoryScope = MemoryScope.Shared;

        public uint seed = 1234;

        public const int MaxDimensions = 8;
        public const int MaxOutputs = 64;
        public const int MaxHiddenWidth = 64;
        public const int MaxHeads = 256;
        public const int MaxTypes = 16;
        public const int MaxPlanColumns = 16;
        public const int MaxWfcHeads = 32;
        public const int MaxPlanValues = 4096;
        public const int MaxMemorySlots = 256;

        /// <summary>Points are spread over a torus from -SpaceRadius to +SpaceRadius on every axis.</summary>
        public const float SpaceRadius = 1f;

        /// <summary>The nominal seconds per tick.</summary>
        public float TickDt => 1f / Math.Max(1e-3f, tickRate);

        public int ClockCount => clockInputs && clockFrequencies != null ? clockFrequencies.Length : 0;

        /// <summary>The master's inputs: base inputs, last step's outputs, the error ratio, clocks, and changes.</summary>
        public int MasterInputs => inputCount + outputCount + 1 + 2 * ClockCount + (changeInputs ? inputCount : 0);

        /// <summary>Plan columns in use (0 with the WFC grid off, 1 on the Heads axis).</summary>
        public int PlanColumns => !wfcEnabled ? 0 : wfcAxis == WfcAxis.Heads ? 1 : planColumns;
        public int PlanCells => headCount * PlanColumns;

        /// <summary>The master's per-future-column type biases ((F - 1) × T), when on.</summary>
        public int PlanBiasCount => wfcEnabled && masterPlanBias && PlanColumns > 1 ? (PlanColumns - 1) * typeCount : 0;

        /// <summary>The master's outputs: one mask slice per head, then the plan biases.</summary>
        public int MasterOutputs => headCount * maskWidth + PlanBiasCount;

        /// <summary>Each head's MLP: a steering target (D), plus the liquid gate.</summary>
        public int SlaveOutputs => spaceDimensions + (liquidGates ? 1 : 0);

        /// <summary>The memory bank's recalled outputs and confidence, read by the translator.</summary>
        public int MemoryInputs => memoryEnabled ? outputCount + 1 : 0;

        /// <summary>The translator's inputs: each head's sample and trail (and velocity), the base inputs, and memory.</summary>
        public int TranslatorInputs => headCount * TranslatorHeadBlock + inputCount + MemoryInputs;

        public int TranslatorHeadBlock => 2 * outputCount + (velocityReadout ? spaceDimensions : 0);

        /// <summary>Trainable rulebook values (Coexist, Follow, Handoff; T × T each).</summary>
        public int RuleCount => wfcEnabled ? 3 * typeCount * typeCount : 0;

        /// <summary>Floats per memory slot: key (hiddenWidth), value (N), strength, age, time since recalled, used.</summary>
        public int MemoryStride => hiddenWidth + outputCount + 4;

        public int MemoryBanks(int instances) => memoryScope == MemoryScope.Shared ? 1 : Math.Max(1, instances);

        /// <summary>Copies of the network run per instance during a training window.</summary>
        public int CopiesPerWindow => perturbMode == PerturbMode.Antithetic ? 1 + 2 * perturbPairs : 1 + perturbPairs;

        /// <summary>True when sampling can use the spatial grid (it only pays off, and only fits, in low dimensions).</summary>
        public bool UsesSpatialGrid => spatialGrid && spaceDimensions <= 3 && SpatialGridSize >= 3;

        /// <summary>Spatial grid cells per axis: as many as fit with each cell at least 3σ wide.</summary>
        public int SpatialGridSize => (int)MathF.Floor(2f / (3f * Math.Max(1e-4f, dispersion)));

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
            Check(trajectorySpeed > 0f, "trajectorySpeed must be positive");
            Check(timescaleMin > 0f && timescaleMax >= timescaleMin, "timescales must be 0 < min <= max");
            Check(trailTimescale > 0f, "trailTimescale must be positive");
            Check(tickRate > 0f, "tickRate must be positive");
            Check(perturbPairs >= 1, "perturbPairs must be at least 1");
            Check(perturbSigma > 0f, "perturbSigma must be positive");
            Check(evalWindow >= 1, "evalWindow must be at least 1");
            if (wfcEnabled)
            {
                Check(typeCount >= 2 && typeCount <= MaxTypes, $"typeCount must be 2-{MaxTypes}");
                Check(planColumns >= 1 && planColumns <= MaxPlanColumns, $"planColumns must be 1-{MaxPlanColumns}");
                Check(headCount <= MaxWfcHeads, $"the WFC grid needs headCount <= {MaxWfcHeads}");
                Check(PlanCells * typeCount <= MaxPlanValues, $"the WFC grid needs heads × planColumns × typeCount <= {MaxPlanValues}");
                Check(headNeighbors != HeadNeighbors.All || headCount <= 8, "headNeighbors All needs headCount <= 8");
                Check(planTickSteps >= 1 && wfcPasses >= 0 && wfcCollapseBudget >= 0, "planTickSteps must be >= 1, passes and budget >= 0");
            }
            if (memoryEnabled)
            {
                Check(memorySlots >= 1 && memorySlots <= MaxMemorySlots, $"memorySlots must be 1-{MaxMemorySlots}");
                Check(hiddenLayers >= 1, "the memory bank's keys are the master's hidden activations, so it needs hiddenLayers >= 1");
                Check(memoryHalfLife > 0f, "memoryHalfLife must be positive");
            }
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
            trajectorySpeed = Math.Max(1e-6f, trajectorySpeed);
            timescaleMin = Math.Max(1e-3f, timescaleMin);
            timescaleMax = Math.Max(timescaleMin, timescaleMax);
            trailTimescale = Math.Max(1e-3f, trailTimescale);
            tickRate = Math.Max(0.1f, tickRate);
            perturbPairs = Math.Max(1, perturbPairs);
            perturbSigma = Math.Max(1e-6f, perturbSigma);
            evalWindow = Math.Max(1, evalWindow);
            typeCount = Math.Clamp(typeCount, 2, MaxTypes);
            planColumns = Math.Clamp(planColumns, 1, MaxPlanColumns);
            planTickSteps = Math.Max(1, planTickSteps);
            wfcPasses = Math.Clamp(wfcPasses, 0, 16);
            wfcCollapseBudget = Math.Clamp(wfcCollapseBudget, 0, 64);
            if (wfcEnabled)
            {
                headCount = Math.Min(headCount, MaxWfcHeads);
                while (PlanCells * typeCount > MaxPlanValues && planColumns > 1) planColumns--;
                if (headNeighbors == HeadNeighbors.All && headCount > 8) headNeighbors = HeadNeighbors.Ring;
            }
            memorySlots = Math.Clamp(memorySlots, 1, MaxMemorySlots);
            memoryHalfLife = Math.Max(1e-3f, memoryHalfLife);
            if (memoryEnabled) hiddenLayers = Math.Max(1, hiddenLayers);
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
        /// Identifies everything the weights depend on (shape, seed, toggles that add inputs or outputs). Brains with
        /// the same signature can swap and mix weights.
        /// </summary>
        public string LayoutSignature =>
            $"I{inputCount}N{outputCount}H{headCount}M{maskWidth}D{spaceDimensions}P{pointsPerSpace}W{hiddenWidth}L{hiddenLayers}" +
            $"C{ClockCount}X{(changeInputs ? 1 : 0)}V{(velocityReadout ? 1 : 0)}T{(trainableValueSpaces ? 1 : 0)}S{seed}" +
            (liquidGates ? "G" : "") +
            (wfcEnabled ? $"Wt{typeCount}f{PlanColumns}b{PlanBiasCount}" : "") +
            (memoryEnabled ? "Mem" : "");
    }
}
