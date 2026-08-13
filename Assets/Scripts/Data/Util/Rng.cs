using System;
using System.Collections.Generic;

namespace HanziDefend.Data
{
    /// <summary>
    /// Serializable snapshot of the deterministic random-number generator.
    /// </summary>
    [Serializable]
    public sealed class RngState
    {
        public RngState()
        {
        }

        public RngState(uint value)
        {
            if (value == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Rng state must be non-zero.");
            }

            state = value;
        }

        public uint state;
    }

    /// <summary>
    /// Small deterministic xorshift32 generator used by every gameplay random choice.
    /// </summary>
    public sealed class Rng
    {
        private const uint ZeroSeedFallback = 0x6D2B79F5u;
        private const float UnitFloatScale = 1f / 16777216f;

        private uint state;

        public Rng(uint seed)
        {
            state = NormalizeSeed(seed);
        }

        public Rng(int seed)
            : this(unchecked((uint)seed))
        {
        }

        public Rng(RngState savedState)
        {
            RestoreState(savedState);
        }

        public uint State => state;

        public RngState SaveState()
        {
            return new RngState(state);
        }

        public void RestoreState(RngState savedState)
        {
            if (savedState == null)
            {
                throw new ArgumentNullException(nameof(savedState));
            }

            if (savedState.state == 0u)
            {
                throw new ArgumentException("Rng state must be non-zero.", nameof(savedState));
            }

            state = savedState.state;
        }

        public uint NextUInt()
        {
            unchecked
            {
                uint value = state;
                value ^= value << 13;
                value ^= value >> 17;
                value ^= value << 5;
                state = value;
                return value;
            }
        }

        public int NextInt(int maxExclusive)
        {
            return NextInt(0, maxExclusive);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    "maxExclusive must be greater than minInclusive.");
            }

            uint range = (uint)((long)maxExclusive - minInclusive);
            uint threshold = (0u - range) % range;
            uint sample;

            do
            {
                sample = NextUInt();
            }
            while (sample < threshold);

            return (int)(minInclusive + (long)(sample % range));
        }

        public float NextFloat()
        {
            return (NextUInt() >> 8) * UnitFloatScale;
        }

        public int NextWeightedIndex(IReadOnlyList<float> weights)
        {
            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (weights.Count == 0)
            {
                throw new ArgumentException("At least one weight is required.", nameof(weights));
            }

            double total = 0d;
            int lastPositiveIndex = -1;

            for (int index = 0; index < weights.Count; index++)
            {
                float weight = weights[index];
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                {
                    throw new ArgumentException(
                        $"Weight at index {index} must be finite and non-negative.",
                        nameof(weights));
                }

                if (weight > 0f)
                {
                    total += weight;
                    lastPositiveIndex = index;
                }
            }

            if (lastPositiveIndex < 0 || double.IsInfinity(total))
            {
                throw new ArgumentException(
                    "Weights must contain at least one positive value and have a finite sum.",
                    nameof(weights));
            }

            double selected = NextFloat() * total;
            double cumulative = 0d;

            for (int index = 0; index < weights.Count; index++)
            {
                cumulative += weights[index];
                if (selected < cumulative)
                {
                    return index;
                }
            }

            return lastPositiveIndex;
        }

        public T NextWeighted<T>(IReadOnlyList<T> values, IReadOnlyList<float> weights)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (weights == null)
            {
                throw new ArgumentNullException(nameof(weights));
            }

            if (values.Count != weights.Count)
            {
                throw new ArgumentException("Values and weights must have the same count.");
            }

            return values[NextWeightedIndex(weights)];
        }

        private static uint NormalizeSeed(uint seed)
        {
            return seed == 0u ? ZeroSeedFallback : seed;
        }

    }

    /// <summary>
    /// Serializable snapshot of every deterministic subsystem random stream.
    /// </summary>
    [Serializable]
    public sealed class RngStreamsState
    {
        public RngStreamsState()
        {
        }

        public RngStreamsState(
            RngState battle,
            RngState cardDraw,
            RngState settlement)
        {
            this.battle = battle ?? throw new ArgumentNullException(nameof(battle));
            this.cardDraw = cardDraw ?? throw new ArgumentNullException(nameof(cardDraw));
            this.settlement = settlement ?? throw new ArgumentNullException(nameof(settlement));
        }

        public RngState battle;
        public RngState cardDraw;
        public RngState settlement;
    }

    /// <summary>
    /// Owns independent deterministic streams derived from one replay seed.
    /// Consumers must use only the stream assigned to their subsystem so that
    /// drawing in one subsystem cannot shift another subsystem's sequence.
    /// </summary>
    public sealed class RngStreams
    {
        // Four-byte ASCII domain tags: BATT, CARD and SETT. These values and the
        // avalanche steps in DeriveSeed form the persisted replay contract.
        private const uint BattleDomain = 0x42415454u;
        private const uint CardDrawDomain = 0x43415244u;
        private const uint SettlementDomain = 0x53455454u;

        public RngStreams(uint masterSeed)
        {
            // Going through Rng deliberately reuses its documented zero-seed
            // fallback instead of duplicating that policy here.
            MasterSeed = new Rng(masterSeed).State;

            Battle = new Rng(DeriveSeed(MasterSeed, BattleDomain));
            CardDraw = new Rng(DeriveSeed(MasterSeed, CardDrawDomain));
            Settlement = new Rng(DeriveSeed(MasterSeed, SettlementDomain));

            BattleSeed = Battle.State;
            CardDrawSeed = CardDraw.State;
            SettlementSeed = Settlement.State;
        }

        public RngStreams(int masterSeed)
            : this(unchecked((uint)masterSeed))
        {
        }

        /// <summary>The normalized seed from which the three streams were derived.</summary>
        public uint MasterSeed { get; }

        /// <summary>The initial seed of the battle stream, useful in replay diagnostics.</summary>
        public uint BattleSeed { get; }

        /// <summary>The initial seed of the card-draw stream, useful in replay diagnostics.</summary>
        public uint CardDrawSeed { get; }

        /// <summary>The initial seed of the settlement stream, useful in replay diagnostics.</summary>
        public uint SettlementSeed { get; }

        public Rng Battle { get; }

        public Rng CardDraw { get; }

        public Rng Settlement { get; }

        public RngStreamsState SaveState()
        {
            return new RngStreamsState(
                Battle.SaveState(),
                CardDraw.SaveState(),
                Settlement.SaveState());
        }

        public void RestoreState(RngStreamsState savedState)
        {
            if (savedState == null)
            {
                throw new ArgumentNullException(nameof(savedState));
            }

            // Validate the complete snapshot before changing any stream, keeping
            // restoration atomic when serialized data is incomplete or corrupt.
            ValidateChildState(savedState.battle, nameof(savedState.battle));
            ValidateChildState(savedState.cardDraw, nameof(savedState.cardDraw));
            ValidateChildState(savedState.settlement, nameof(savedState.settlement));

            Battle.RestoreState(savedState.battle);
            CardDraw.RestoreState(savedState.cardDraw);
            Settlement.RestoreState(savedState.settlement);
        }

        private static uint DeriveSeed(uint normalizedMasterSeed, uint domain)
        {
            unchecked
            {
                // Fixed 32-bit avalanche finalizer. Each named domain is mixed
                // independently, so adding another stream never shifts these three.
                uint value = normalizedMasterSeed ^ domain;
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }

        private static void ValidateChildState(RngState childState, string fieldName)
        {
            if (childState == null)
            {
                throw new ArgumentException(
                    $"Rng stream state '{fieldName}' is required.",
                    nameof(childState));
            }

            if (childState.state == 0u)
            {
                throw new ArgumentException(
                    $"Rng stream state '{fieldName}' must be non-zero.",
                    nameof(childState));
            }
        }
    }
}
