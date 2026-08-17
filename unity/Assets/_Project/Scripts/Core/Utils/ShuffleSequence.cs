#nullable enable
using System;

namespace Pose.Core
{
    /// <summary>
    /// Drives the opening shuffle through its phases. Pure C# with no Unity
    /// dependency and no internal clock — the caller feeds it elapsed time, the
    /// same shape as <see cref="TurnTimer"/>, which keeps the sequencing
    /// testable away from the renderer.
    ///
    /// The shuffle exists to cover the wait for the server-issued deal, so it
    /// must never be the thing the deal waits on. <see cref="Swirl"/> loops
    /// indefinitely until <see cref="RequestFinish"/> says the deal has landed,
    /// and then leaves at the end of its current cycle rather than cutting mid
    /// motion. If the deal is already there before the shuffle starts, the
    /// swirl still runs one full cycle so it reads as a shuffle rather than a
    /// flicker.
    ///
    /// The randomness this drives is cosmetic only — the actual hands come from
    /// the server seed, so nothing here may influence or reveal them.
    /// </summary>
    public sealed class ShuffleSequence
    {
        /// <summary>Tiles sweep in from off board and cluster.</summary>
        public const float GatherSeconds = 0.55f;

        /// <summary>One loop of the tiles sliding over each other.</summary>
        public const float SwirlCycleSeconds = 0.9f;

        /// <summary>The cluster collapses into a neat face-down stack.</summary>
        public const float StackSeconds = 0.35f;

        /// <summary>Tiles fly out to the seats.</summary>
        public const float DealSeconds = 0.7f;

        /// <summary>
        /// Hard ceiling on swirling. If the deal never arrives — a dropped
        /// call to <see cref="RequestFinish"/>, a wedged request — the shuffle
        /// still ends rather than spinning forever in front of the player.
        /// </summary>
        public const float MaxSwirlSeconds = 12f;

        private readonly float _gather;
        private readonly float _swirlCycle;
        private readonly float _stack;
        private readonly float _deal;
        private readonly float _maxSwirl;

        private float _phaseElapsed;
        private float _swirlTotal;
        private bool _finishRequested;

        /// <param name="gatherSeconds">Override for <see cref="GatherSeconds"/>.</param>
        /// <param name="swirlCycleSeconds">Override for <see cref="SwirlCycleSeconds"/>.</param>
        /// <param name="stackSeconds">Override for <see cref="StackSeconds"/>.</param>
        /// <param name="dealSeconds">Override for <see cref="DealSeconds"/>.</param>
        /// <param name="maxSwirlSeconds">Override for <see cref="MaxSwirlSeconds"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when any duration is not positive.
        /// </exception>
        public ShuffleSequence(
            float gatherSeconds = GatherSeconds,
            float swirlCycleSeconds = SwirlCycleSeconds,
            float stackSeconds = StackSeconds,
            float dealSeconds = DealSeconds,
            float maxSwirlSeconds = MaxSwirlSeconds)
        {
            Require(gatherSeconds, nameof(gatherSeconds));
            Require(swirlCycleSeconds, nameof(swirlCycleSeconds));
            Require(stackSeconds, nameof(stackSeconds));
            Require(dealSeconds, nameof(dealSeconds));
            Require(maxSwirlSeconds, nameof(maxSwirlSeconds));

            _gather = gatherSeconds;
            _swirlCycle = swirlCycleSeconds;
            _stack = stackSeconds;
            _deal = dealSeconds;
            _maxSwirl = maxSwirlSeconds;
        }

        private static void Require(float value, string name)
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name, "Duration must be positive.");
            }
        }

        /// <summary>The phase currently being drawn.</summary>
        public ShufflePhase Phase { get; private set; } = ShufflePhase.Gather;

        /// <summary>
        /// How far through the current phase, 0..1. For <see cref="Swirl"/> this
        /// is progress through the current cycle and resets each loop, which is
        /// what lets the renderer drive a repeating motion from it.
        /// </summary>
        public float PhaseProgress =>
            Phase switch
            {
                ShufflePhase.Gather => Clamp01(_phaseElapsed / _gather),
                ShufflePhase.Swirl => Clamp01(_phaseElapsed / _swirlCycle),
                ShufflePhase.Stack => Clamp01(_phaseElapsed / _stack),
                ShufflePhase.Deal => Clamp01(_phaseElapsed / _deal),
                _ => 1f,
            };

        /// <summary>How many full swirl cycles have completed.</summary>
        public int SwirlCyclesCompleted { get; private set; }

        /// <summary>True once the whole sequence has run out.</summary>
        public bool IsDone => Phase == ShufflePhase.Done;

        /// <summary>True once the deal has been reported as ready.</summary>
        public bool FinishRequested => _finishRequested;

        /// <summary>
        /// Reports that the deal has landed. The swirl finishes its current
        /// cycle and the sequence moves on. Safe to call more than once, and
        /// safe to call before the shuffle has even reached the swirl.
        /// </summary>
        public void RequestFinish()
        {
            _finishRequested = true;
        }

        /// <summary>
        /// Advances the sequence and returns the phase now being drawn.
        /// </summary>
        /// <param name="deltaSeconds">
        /// Elapsed time since the last call, normally <c>Time.deltaTime</c>.
        /// </param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="deltaSeconds"/> is negative.
        /// </exception>
        public ShufflePhase Advance(float deltaSeconds)
        {
            if (deltaSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deltaSeconds), "Time cannot advance backwards.");
            }

            if (Phase == ShufflePhase.Done)
            {
                return Phase;
            }

            _phaseElapsed += deltaSeconds;
            if (Phase == ShufflePhase.Swirl)
            {
                _swirlTotal += deltaSeconds;
            }

            // A single long tick can cross more than one phase boundary, so
            // this drains rather than stepping once.
            bool moved = true;
            while (moved && Phase != ShufflePhase.Done)
            {
                moved = false;
                switch (Phase)
                {
                    case ShufflePhase.Gather when _phaseElapsed >= _gather:
                        _phaseElapsed -= _gather;
                        Phase = ShufflePhase.Swirl;
                        moved = true;
                        break;

                    case ShufflePhase.Swirl when _phaseElapsed >= _swirlCycle:
                        _phaseElapsed -= _swirlCycle;
                        SwirlCyclesCompleted++;
                        // Leave only on a cycle boundary, so the motion never
                        // stops halfway through a slide.
                        if (_finishRequested || _swirlTotal >= _maxSwirl)
                        {
                            Phase = ShufflePhase.Stack;
                        }
                        moved = true;
                        break;

                    case ShufflePhase.Stack when _phaseElapsed >= _stack:
                        _phaseElapsed -= _stack;
                        Phase = ShufflePhase.Deal;
                        moved = true;
                        break;

                    case ShufflePhase.Deal when _phaseElapsed >= _deal:
                        _phaseElapsed = 0f;
                        Phase = ShufflePhase.Done;
                        moved = true;
                        break;
                }
            }

            return Phase;
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }

    /// <summary>A stage of the opening shuffle.</summary>
    public enum ShufflePhase
    {
        /// <summary>Tiles sweeping in from off board.</summary>
        Gather = 0,

        /// <summary>Tiles sliding over each other; loops until the deal lands.</summary>
        Swirl = 1,

        /// <summary>Collapsing into a face-down stack.</summary>
        Stack = 2,

        /// <summary>Flying out to the seats.</summary>
        Deal = 3,

        /// <summary>Finished; the board takes over.</summary>
        Done = 4,
    }
}
