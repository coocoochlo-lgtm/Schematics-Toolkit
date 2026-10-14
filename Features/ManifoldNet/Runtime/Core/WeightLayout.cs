using System;
using System.Collections.Generic;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// Where every weight lives in the one flat parameter array: the master MLP, one small MLP per head, then the
    /// translator (each layer's weights row by row, then its biases); then the WFC rulebooks (Coexist, Follow, Handoff,
    /// T × T each), the memory bank's recall sharpness β and the heads' identity projections (H × K × K) when those
    /// modules are on; and finally the value spaces'
    /// point values when they're trainable. The GPU uses the same offsets.
    /// </summary>
    public sealed class WeightLayout
    {
        public readonly struct Layer
        {
            public readonly int In, Out, Weights, Biases;
            public Layer(int @in, int @out, int weights) { In = @in; Out = @out; Weights = weights; Biases = weights + @in * @out; }
            public int End => Biases + Out;
        }

        public readonly struct Block
        {
            public readonly int FirstLayer, LayerCount, Start, End;
            public Block(int firstLayer, int layerCount, int start, int end) { FirstLayer = firstLayer; LayerCount = layerCount; Start = start; End = end; }
        }

        public readonly Layer[] Layers;
        public readonly Block Master;
        public readonly Block[] Slaves;
        public readonly Block Translator;

        /// <summary>MLP parameters (everything before the value spaces).</summary>
        public readonly int MlpCount;

        /// <summary>The three rulebooks (T × T each, row = from-type, column = to-type), when the WFC grid is on.</summary>
        public readonly int RulesOffset, RulesCount;

        /// <summary>The memory bank's β (one value), when the memory is on.</summary>
        public readonly int BetaOffset, BetaCount;

        /// <summary>The heads' identity projections P_h (H × K × K, row = sample value written, column = bit read), when identity masks are on.</summary>
        public readonly int IdentityOffset, IdentityCount, IdentityWidth;

        /// <summary>Where the trainable point values start, and how many there are (0 when fixed).</summary>
        public readonly int ValuesOffset, ValuesCount;

        private readonly float _ruleLrScale, _valueRate, _beta;

        /// <summary>Every trainable parameter.</summary>
        public readonly int Count;

        public WeightLayout(MnnSettings s)
        {
            var layers = new List<Layer>();
            int offset = 0;

            Block Add(int inputs, int outputs)
            {
                int first = layers.Count, start = offset;
                int width = inputs;
                for (int l = 0; l < s.hiddenLayers; l++)
                {
                    var layer = new Layer(width, s.hiddenWidth, offset);
                    layers.Add(layer);
                    offset = layer.End;
                    width = s.hiddenWidth;
                }
                var last = new Layer(width, outputs, offset);
                layers.Add(last);
                offset = last.End;
                return new Block(first, layers.Count - first, start, offset);
            }

            Master = Add(s.MasterInputs, s.MasterOutputs);
            Slaves = new Block[s.headCount];
            for (int h = 0; h < s.headCount; h++)
                Slaves[h] = Add(s.maskWidth, s.SlaveOutputs);
            Translator = Add(s.TranslatorInputs, s.outputCount);

            Layers = layers.ToArray();
            MlpCount = offset;
            RulesOffset = offset;
            RulesCount = s.RuleCount;
            BetaOffset = RulesOffset + RulesCount;
            BetaCount = s.memoryEnabled ? 1 : 0;
            IdentityOffset = BetaOffset + BetaCount;
            IdentityCount = s.IdentityCount;
            IdentityWidth = s.IdentityWidth;
            ValuesOffset = IdentityOffset + IdentityCount;
            ValuesCount = s.trainableValueSpaces ? s.headCount * s.pointsPerSpace * s.outputCount : 0;
            Count = ValuesOffset + ValuesCount;
            _ruleLrScale = s.wfcRuleLrScale;
            _valueRate = s.valueSpaceRate;
            _beta = s.memoryBeta;
        }

        /// <summary>How big a nudge this parameter gets, relative to σ (point values move much less).</summary>
        public float SigmaScale(int index) => index < ValuesOffset ? 1f : _valueRate;

        /// <summary>How big a step this parameter takes, relative to the learning rate.</summary>
        public float RateScale(int index)
            => index >= ValuesOffset ? _valueRate : index >= RulesOffset && index < RulesOffset + RulesCount ? _ruleLrScale : 1f;

        /// <summary>The largest layer width, for scratch buffers.</summary>
        public int MaxWidth
        {
            get
            {
                int max = 0;
                foreach (var layer in Layers)
                    max = Math.Max(max, Math.Max(layer.In, layer.Out));
                return max;
            }
        }

        /// <summary>Fresh weights: Xavier-uniform from the seed, zero biases, neutral rulebooks (all zero), β from the settings, small identity projections.</summary>
        public void Initialize(float[] parameters, uint seed)
        {
            Array.Clear(parameters, 0, parameters.Length);
            for (int l = 0; l < Layers.Length; l++)
                InitializeLayer(parameters, l, seed, 0);
            if (BetaCount > 0)
                parameters[BetaOffset] = _beta;
            for (int i = 0; i < IdentityCount; i++)
                parameters[IdentityOffset + i] = 0.1f * MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamIdentity, 0xFFFFFFFEu, (uint)i, 0));
        }

        /// <summary>Re-randomizes one block (a head being revived), from a different stream so it gets new weights.</summary>
        public void Reinitialize(float[] parameters, Block block, uint seed, uint generation)
        {
            for (int l = block.FirstLayer; l < block.FirstLayer + block.LayerCount; l++)
                InitializeLayer(parameters, l, seed, generation);
        }

        private void InitializeLayer(float[] parameters, int index, uint seed, uint generation)
        {
            var layer = Layers[index];
            float limit = MathF.Sqrt(6f / (layer.In + layer.Out));
            for (int i = 0; i < layer.In * layer.Out; i++)
                parameters[layer.Weights + i] = MnnHash.Signed(MnnHash.Hash(seed, MnnHash.StreamWeights, (uint)index, (uint)i, generation)) * limit;
            for (int o = 0; o < layer.Out; o++)
                parameters[layer.Biases + o] = 0f;
        }
    }
}
