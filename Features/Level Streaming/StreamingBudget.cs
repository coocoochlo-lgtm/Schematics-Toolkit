using System.Diagnostics;
using UnityEngine;

namespace Remedy.LevelStreaming
{
    /// <summary>
    /// The one time budget every kind of streaming work shares each frame: chunks being shown or hidden, additive
    /// levels waking up, levels being taken apart. Each piece of work spends from the same pot, so two big levels and a
    /// chunk changing in the same frame still only cost <see cref="MillisecondsPerFrame"/> between them, instead of each
    /// taking its own slice.
    /// </summary>
    /// <remarks>
    /// Work measures itself between <see cref="Begin"/> and <see cref="End"/>, and checks <see cref="Exhausted"/> after
    /// every object. Only time spent inside those brackets counts, so the rest of the game's frame never eats into it.
    /// </remarks>
    public static class StreamingBudget
    {
        /// <summary>
        /// Time per frame all streaming work may spend between them, in milliseconds. 0 or less removes the time
        /// limit (the object cap still applies).
        /// </summary>
        public static float MillisecondsPerFrame = 1.5f;

        /// <summary>
        /// A safety cap on how many objects all streaming work may wake (or take apart) per frame, however fast they
        /// are. Waking an object is quick to call but some of its cost (Start, first render) lands later in the frame
        /// where the clock can't see it. 0 or less removes the cap.
        /// </summary>
        public static int MaxObjectsPerFrame = 200;

        private static readonly Stopwatch _clock = new Stopwatch();
        private static int _frame = -1;
        private static long _spentTicks;
        private static int _objects;
        private static int _depth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // The settings above are kept on purpose: they are the game's choice, not per-session state.
            _clock.Reset();
            _frame = -1;
            _spentTicks = 0;
            _objects = 0;
            _depth = 0;
        }

        /// <summary>Time spent on streaming so far this frame, in milliseconds.</summary>
        public static float SpentMilliseconds
        {
            get
            {
                Roll();
                return SpentTicks * 1000f / Stopwatch.Frequency;
            }
        }

        /// <summary>Objects woken (or taken apart) so far this frame.</summary>
        public static int SpentObjects
        {
            get
            {
                Roll();
                return _objects;
            }
        }

        /// <summary>Whether this frame's budget is used up, so streaming work should wait for the next frame.</summary>
        public static bool Exhausted
        {
            get
            {
                Roll();
                if (MaxObjectsPerFrame > 0 && _objects >= MaxObjectsPerFrame)
                    return true;
                if (MillisecondsPerFrame <= 0f)
                    return false;
                return SpentTicks >= (long)(MillisecondsPerFrame * Stopwatch.Frequency / 1000f);
            }
        }

        /// <summary>Starts timing a stretch of streaming work. Always pair with <see cref="End"/> (try/finally).</summary>
        public static void Begin()
        {
            Roll();
            if (_depth++ == 0)
                _clock.Restart();
        }

        /// <summary>Stops timing and adds the time to this frame's spending.</summary>
        public static void End()
        {
            if (_depth == 0)
                return;
            if (--_depth == 0)
            {
                Roll();
                _spentTicks += _clock.ElapsedTicks;
                _clock.Reset();
            }
        }

        /// <summary>Counts objects woken (or taken apart) against <see cref="MaxObjectsPerFrame"/>.</summary>
        public static void Spend(int objects = 1)
        {
            Roll();
            _objects += objects;
        }

        private static long SpentTicks => _spentTicks + (_depth > 0 ? _clock.ElapsedTicks : 0);

        // A new frame starts with a full budget. Work that is mid-measurement when the frame changes (it shouldn't be,
        // nothing awaits inside Begin/End) starts counting again from now.
        private static void Roll()
        {
            int frame = Time.frameCount;
            if (frame == _frame)
                return;
            _frame = frame;
            _spentTicks = 0;
            _objects = 0;
            if (_depth > 0)
                _clock.Restart();
        }
    }
}
