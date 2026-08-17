using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    /// <summary>
    /// Immutable description of a wave-start point on the encounter timeline.
    /// </summary>
    internal readonly struct ScheduledWaveStart
    {
        internal ScheduledWaveStart(
            string waveSetId,
            int waveIndex,
            EnemyRank rewardRank,
            double scheduledTime)
        {
            WaveSetId = waveSetId;
            WaveIndex = waveIndex;
            RewardRank = rewardRank;
            ScheduledTime = scheduledTime;
        }

        internal string WaveSetId { get; }

        internal int WaveIndex { get; }

        internal EnemyRank RewardRank { get; }

        internal double ScheduledTime { get; }
    }

    /// <summary>
    /// Immutable description of one combatant spawn on the encounter timeline.
    /// </summary>
    internal readonly struct ScheduledSpawn
    {
        internal ScheduledSpawn(
            string waveSetId,
            int waveIndex,
            EnemyRank rewardRank,
            string unitId,
            int level,
            Vector2 position,
            int groupIndex,
            int ordinal,
            double scheduledTime)
        {
            WaveSetId = waveSetId;
            WaveIndex = waveIndex;
            RewardRank = rewardRank;
            UnitId = unitId;
            Level = level;
            Position = position;
            GroupIndex = groupIndex;
            Ordinal = ordinal;
            ScheduledTime = scheduledTime;
        }

        internal string WaveSetId { get; }

        internal int WaveIndex { get; }

        internal EnemyRank RewardRank { get; }

        internal string UnitId { get; }

        internal int Level { get; }

        internal Vector2 Position { get; }

        internal int GroupIndex { get; }

        internal int Ordinal { get; }

        internal double ScheduledTime { get; }
    }

    /// <summary>
    /// Precompiles a level's wave data into a deterministic timeline and advances
    /// it independently of rendering and physics. Wave delays are relative to the
    /// previous wave's start, while spawn groups inside a wave run in parallel.
    /// </summary>
    internal sealed class WaveScheduler
    {
        private readonly List<ScheduledItem> timeline;
        private int nextItemIndex;
        private double lastAdvancedTime = double.NegativeInfinity;

        internal WaveScheduler(GameConfig config, LevelDef level, Rng battleRng)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (level == null)
            {
                throw new ArgumentNullException(nameof(level));
            }

            if (battleRng == null)
            {
                throw new ArgumentNullException(nameof(battleRng));
            }

            WaveSetDef waveSet = config.GetWaveSet(level.WaveSetId);
            Position2Def spawnCenter = config.Economy.Battle.EnemySpawnCenter;
            TotalWaveCount = waveSet.Waves.Length;
            timeline = CompileTimeline(
                WaveTimeline.Compile(config, waveSet),
                spawnCenter,
                battleRng);
        }

        internal int TotalWaveCount { get; }

        /// <summary>
        /// The most recently started configured wave index, or zero before the
        /// first wave begins.
        /// </summary>
        internal int CurrentWaveIndex { get; private set; }

        /// <summary>
        /// True after every wave-start and spawn item has been emitted.
        /// </summary>
        internal bool IsComplete => nextItemIndex >= timeline.Count;

        internal void Advance(
            double now,
            Action<ScheduledWaveStart> onWaveStarted,
            Action<ScheduledSpawn> onSpawn)
        {
            if (double.IsNaN(now) || double.IsInfinity(now) || now < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(now), "Timeline time must be finite and non-negative.");
            }

            if (now < lastAdvancedTime)
            {
                throw new ArgumentOutOfRangeException(nameof(now), "Timeline time cannot move backwards.");
            }

            if (onWaveStarted == null)
            {
                throw new ArgumentNullException(nameof(onWaveStarted));
            }

            if (onSpawn == null)
            {
                throw new ArgumentNullException(nameof(onSpawn));
            }

            lastAdvancedTime = now;
            while (nextItemIndex < timeline.Count
                   && timeline[nextItemIndex].ScheduledTime <= now)
            {
                ScheduledItem item = timeline[nextItemIndex++];
                if (item.Kind == ScheduledItemKind.WaveStart)
                {
                    CurrentWaveIndex = item.WaveStart.WaveIndex;
                    onWaveStarted(item.WaveStart);
                }
                else
                {
                    onSpawn(item.Spawn);
                }
            }
        }

        /// <summary>
        /// Turns the compiled wave timeline into schedulable items. Timing comes wholly from
        /// <see cref="WaveTimeline"/> so validation, the balance tooling and the live encounter can
        /// never disagree about when a wave lands; this method only adds the random spawn X.
        ///
        /// <para><see cref="WaveTimeline.Spawns"/> is walked in declaration order on purpose — one
        /// random draw per spread spawn, in exactly the order the old inline compiler used, so the
        /// refactor leaves every seed's spawn positions byte-identical.</para>
        /// </summary>
        private static List<ScheduledItem> CompileTimeline(
            WaveTimeline compiled,
            Position2Def spawnCenter,
            Rng battleRng)
        {
            var result = new List<ScheduledItem>();
            for (int index = 0; index < compiled.WaveStarts.Count; index++)
            {
                WaveStartPoint start = compiled.WaveStarts[index];
                result.Add(ScheduledItem.ForWaveStart(new ScheduledWaveStart(
                    compiled.WaveSetId,
                    start.WaveIndex,
                    start.RewardRank,
                    start.TimeSeconds)));
            }

            for (int index = 0; index < compiled.Spawns.Count; index++)
            {
                WaveSpawnPoint spawn = compiled.Spawns[index];
                float x = spawnCenter.X;
                if (spawn.SpreadX > 0f)
                {
                    x += (battleRng.NextFloat() * 2f - 1f) * spawn.SpreadX;
                }

                result.Add(ScheduledItem.ForSpawn(new ScheduledSpawn(
                    compiled.WaveSetId,
                    spawn.WaveIndex,
                    spawn.RewardRank,
                    spawn.UnitId,
                    spawn.Level,
                    new Vector2(x, spawnCenter.Y),
                    spawn.GroupIndex,
                    spawn.Ordinal,
                    spawn.TimeSeconds)));
            }

            result.Sort(ScheduledItemComparer.Instance);
            return result;
        }

        private enum ScheduledItemKind
        {
            WaveStart = 0,
            Spawn = 1
        }

        private readonly struct ScheduledItem
        {
            private ScheduledItem(
                ScheduledItemKind kind,
                ScheduledWaveStart waveStart,
                ScheduledSpawn spawn)
            {
                Kind = kind;
                WaveStart = waveStart;
                Spawn = spawn;
            }

            internal ScheduledItemKind Kind { get; }

            internal ScheduledWaveStart WaveStart { get; }

            internal ScheduledSpawn Spawn { get; }

            internal double ScheduledTime => Kind == ScheduledItemKind.WaveStart
                ? WaveStart.ScheduledTime
                : Spawn.ScheduledTime;

            internal int WaveIndex => Kind == ScheduledItemKind.WaveStart
                ? WaveStart.WaveIndex
                : Spawn.WaveIndex;

            internal int GroupIndex => Kind == ScheduledItemKind.WaveStart
                ? -1
                : Spawn.GroupIndex;

            internal int Ordinal => Kind == ScheduledItemKind.WaveStart
                ? -1
                : Spawn.Ordinal;

            internal static ScheduledItem ForWaveStart(ScheduledWaveStart value)
            {
                return new ScheduledItem(ScheduledItemKind.WaveStart, value, default);
            }

            internal static ScheduledItem ForSpawn(ScheduledSpawn value)
            {
                return new ScheduledItem(ScheduledItemKind.Spawn, default, value);
            }
        }

        private sealed class ScheduledItemComparer : IComparer<ScheduledItem>
        {
            internal static readonly ScheduledItemComparer Instance = new ScheduledItemComparer();

            private ScheduledItemComparer()
            {
            }

            public int Compare(ScheduledItem left, ScheduledItem right)
            {
                int order = left.ScheduledTime.CompareTo(right.ScheduledTime);
                if (order != 0) return order;

                order = left.WaveIndex.CompareTo(right.WaveIndex);
                if (order != 0) return order;

                order = left.Kind.CompareTo(right.Kind);
                if (order != 0) return order;

                order = left.GroupIndex.CompareTo(right.GroupIndex);
                return order != 0 ? order : left.Ordinal.CompareTo(right.Ordinal);
            }
        }
    }
}
