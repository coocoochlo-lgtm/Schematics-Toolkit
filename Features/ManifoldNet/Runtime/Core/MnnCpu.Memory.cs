using System;

namespace Remedy.ManifoldNetwork
{
    /// <summary>
    /// The episodic memory bank (optional, off by default): a small Hopfield-style memory that stores key moments
    /// instantly, so a bot can act on something it was shown once while perturbation training slowly makes the lesson
    /// permanent. A slot holds a key (the master's last hidden activations that step), a value (N outputs), a signed
    /// strength, its age, and how long since it was last recalled. Every step each copy reads it
    /// (a = softmax(β·cos(k, key)), r = Σ a·strength·value, c = max a) and the translator gets r and c. Only the real
    /// network writes, between steps, in instance order, so perturbed copies never fill it with fake memories.
    /// </summary>
    public sealed partial class MnnCpu
    {
        /// <summary>Strengths are kept within ±this, so one moment can't drown out everything else.</summary>
        public const float MemoryMaxStrength = 4f;

        private float[] _bank;
        private int _banks, _stride, _slots, _keyWidth;
        private float[] _queueValue, _queueStrength, _queueReinforce;
        private bool[] _queueWrite;
        private bool _anyQueued;
        private int _memoryWrites;

        /// <summary>The memory slots, bank after bank: S × (key, value, strength, age, since recalled, used). Saved with the brain.</summary>
        public float[] MemoryBank => _bank;

        public int MemoryBankCount => _banks;

        private void InitializeMemory()
        {
            var s = Settings;
            if (!s.memoryEnabled)
            {
                _bank = Array.Empty<float>();
                return;
            }
            _banks = s.MemoryBanks(_instances);
            _stride = s.MemoryStride;
            _slots = s.memorySlots;
            _keyWidth = s.hiddenWidth;
            _bank = new float[_banks * _slots * _stride];
            _queueValue = new float[_instances * _n];
            _queueStrength = new float[_instances];
            _queueReinforce = new float[_instances];
            _queueWrite = new bool[_instances];
        }

        private int BankOf(int row) => Settings.memoryScope == MemoryScope.Shared || row >= _instances ? 0 : row;

        private int SlotAt(int bank, int slot) => (bank * _slots + slot) * _stride;
        private int StrengthAt(int at) => at + _keyWidth + _n;

        /// <summary>
        /// Asks for a moment to be remembered: this instance's latest key (from the step just taken) with
        /// <paramref name="value"/> (N outputs) at <paramref name="strength"/> (positive: "do this"; negative: "avoid
        /// this"). Written at the start of the next step, in instance order. Ignored with the memory off.
        /// </summary>
        public void Remember(int instance, ReadOnlySpan<float> value, float strength)
        {
            if (!Settings.memoryEnabled || instance < 0 || instance >= _instances || value.Length < _n || float.IsNaN(strength))
                return;
            value.Slice(0, _n).CopyTo(new Span<float>(_queueValue, instance * _n, _n));
            _queueStrength[instance] = Math.Clamp(strength, -MemoryMaxStrength, MemoryMaxStrength);
            _queueWrite[instance] = true;
            _anyQueued = true;
        }

        /// <summary>
        /// An outcome for the slots this instance just recalled: each moves by amount × how much it was recalled, so a
        /// memory recalled right before a good outcome is reinforced and one before a bad outcome weakens.
        /// </summary>
        public void Reinforce(int instance, float amount)
        {
            if (!Settings.memoryEnabled || instance < 0 || instance >= _instances || float.IsNaN(amount))
                return;
            _queueReinforce[instance] += amount;
            _anyQueued = true;
        }

        /// <summary>Forgets everything (do this when the sensor layout changes: every old key is meaningless).</summary>
        public void ClearMemory()
        {
            if (_bank.Length > 0) Array.Clear(_bank, 0, _bank.Length);
        }

        /// <summary>Replaces the bank (a loaded brain). Ignored if it's the wrong size.</summary>
        public void SetMemoryBank(ReadOnlySpan<float> bank)
        {
            if (bank.Length == _bank.Length)
                bank.CopyTo(_bank);
        }

        /// <summary>
        /// MemoryWrite: queued writes and reinforcements in instance order (merging into a near-identical slot, else
        /// replacing the weakest × least recent, ties to the lowest index), then fading by the half-life, then marking
        /// what each instance just recalled.
        /// </summary>
        private void ProcessMemory(float dt)
        {
            var s = Settings;
            if (!s.memoryEnabled)
                return;

            if (_anyQueued)
            {
                for (int row = 0; row < _instances; row++)
                {
                    var state = State(row, 0);
                    int bank = BankOf(row);
                    if (_queueWrite[row])
                    {
                        WriteSlot(bank, state.MemoryKey, row);
                        _queueWrite[row] = false;
                    }
                    float amount = _queueReinforce[row];
                    if (amount != 0f)
                    {
                        for (int slot = 0; slot < _slots; slot++)
                        {
                            int at = SlotAt(bank, slot);
                            if (_bank[at + _stride - 1] < 0.5f) continue;
                            int strength = StrengthAt(at);
                            _bank[strength] = Math.Clamp(_bank[strength] + amount * state.MemoryAttention[slot], -MemoryMaxStrength, MemoryMaxStrength);
                        }
                        _queueReinforce[row] = 0f;
                    }
                }
                _anyQueued = false;
            }

            // Fading: unused strength halves every memoryHalfLife seconds.
            float fade = MathF.Exp(-dt / s.memoryHalfLife * 0.6931472f);
            for (int bank = 0; bank < _banks; bank++)
                for (int slot = 0; slot < _slots; slot++)
                {
                    int at = SlotAt(bank, slot);
                    if (_bank[at + _stride - 1] < 0.5f) continue;
                    int strength = StrengthAt(at);
                    _bank[strength] *= fade;
                    _bank[strength + 1] += dt;
                    _bank[strength + 2] += dt;
                }

            // Recalled: the slot each instance leaned on most (more than half its attention) was just used.
            for (int row = 0; row < _instances; row++)
            {
                var attention = State(row, 0).MemoryAttention;
                int best = -1;
                float max = 0.5f;
                for (int slot = 0; slot < _slots; slot++)
                    if (attention[slot] > max) { max = attention[slot]; best = slot; }
                if (best >= 0)
                    _bank[StrengthAt(SlotAt(BankOf(row), best)) + 2] = 0f;
            }
        }

        private void WriteSlot(int bank, float[] key, int row)
        {
            var s = Settings;
            float keyNorm = Norm(key, 0, _keyWidth);
            float strength = _queueStrength[row];

            // A near-identical moment blends into its slot.
            int merge = -1;
            float bestSimilarity = s.mergeThreshold;
            for (int slot = 0; slot < _slots; slot++)
            {
                int at = SlotAt(bank, slot);
                if (_bank[at + _stride - 1] < 0.5f) continue;
                float similarity = Cosine(key, keyNorm, at);
                if (similarity > bestSimilarity) { bestSimilarity = similarity; merge = slot; }
            }
            if (merge >= 0)
            {
                int at = SlotAt(bank, merge);
                int st = StrengthAt(at);
                float old = _bank[st];
                float w = MathF.Abs(strength) / (MathF.Abs(old) + MathF.Abs(strength) + 1e-6f);
                for (int k = 0; k < _keyWidth; k++) _bank[at + k] += (key[k] - _bank[at + k]) * w;
                for (int n = 0; n < _n; n++) _bank[at + _keyWidth + n] += (_queueValue[row * _n + n] - _bank[at + _keyWidth + n]) * w;
                _bank[st] = Math.Clamp(old + strength, -MemoryMaxStrength, MemoryMaxStrength);
                _bank[st + 1] = 0f;
                _memoryWrites++;
                return;
            }

            // Otherwise an empty slot, or the weakest × least recently recalled one.
            int target = -1;
            float lowest = float.MaxValue;
            for (int slot = 0; slot < _slots; slot++)
            {
                int at = SlotAt(bank, slot);
                if (_bank[at + _stride - 1] < 0.5f) { target = slot; break; }
                int st = StrengthAt(at);
                float score = MathF.Abs(_bank[st]) * MathF.Exp(-_bank[st + 2] / s.memoryHalfLife * 0.6931472f);
                if (score < lowest) { lowest = score; target = slot; }
            }
            int to = SlotAt(bank, target);
            for (int k = 0; k < _keyWidth; k++) _bank[to + k] = key[k];
            for (int n = 0; n < _n; n++) _bank[to + _keyWidth + n] = _queueValue[row * _n + n];
            int strengthAt = StrengthAt(to);
            _bank[strengthAt] = strength;
            _bank[strengthAt + 1] = 0f;
            _bank[strengthAt + 2] = 0f;
            _bank[strengthAt + 3] = 1f;
            _memoryWrites++;
        }

        private static float Norm(float[] a, int at, int count)
        {
            float sum = 0f;
            for (int k = 0; k < count; k++) sum += a[at + k] * a[at + k];
            return MathF.Sqrt(sum);
        }

        private float Cosine(float[] key, float keyNorm, int slotAt)
        {
            float dot = 0f;
            for (int k = 0; k < _keyWidth; k++) dot += key[k] * _bank[slotAt + k];
            return dot / (keyNorm * Norm(_bank, slotAt, _keyWidth) + 1e-6f);
        }

        /// <summary>
        /// MemoryRead for one copy: compare its key with every used slot, sharpen with β (a trainable parameter), and
        /// blend the values. Writes r (N) and c into the translator's memory inputs and keeps the attention per slot.
        /// </summary>
        private void MemoryRead(MnnState state, float[] parameters, int row, float[] destination, int at)
        {
            int bank = BankOf(row);
            float beta = parameters[Layout.BetaOffset];
            float keyNorm = Norm(state.MemoryKey, 0, _keyWidth);
            var attention = state.MemoryAttention;
            for (int n = 0; n <= _n; n++) destination[at + n] = 0f;

            float max = float.MinValue;
            bool any = false;
            for (int slot = 0; slot < _slots; slot++)
            {
                int slotAt = SlotAt(bank, slot);
                if (_bank[slotAt + _stride - 1] < 0.5f) { attention[slot] = 0f; continue; }
                float logit = beta * Cosine(state.MemoryKey, keyNorm, slotAt);
                attention[slot] = logit;
                if (logit > max) max = logit;
                any = true;
            }
            if (!any)
                return;

            float sum = 0f;
            for (int slot = 0; slot < _slots; slot++)
            {
                int slotAt = SlotAt(bank, slot);
                if (_bank[slotAt + _stride - 1] < 0.5f) continue;
                float e = MathF.Exp(attention[slot] - max);
                attention[slot] = e;
                sum += e;
            }
            float inv = 1f / sum, confidence = 0f;
            for (int slot = 0; slot < _slots; slot++)
            {
                int slotAt = SlotAt(bank, slot);
                if (_bank[slotAt + _stride - 1] < 0.5f) continue;
                float a = attention[slot] * inv;
                attention[slot] = a;
                if (a > confidence) confidence = a;
                float weight = a * _bank[StrengthAt(slotAt)];
                for (int n = 0; n < _n; n++)
                    destination[at + n] += weight * _bank[slotAt + _keyWidth + n];
            }
            destination[at + _n] = confidence;
        }
    }
}
