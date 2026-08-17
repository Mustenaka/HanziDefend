using System;
using System.Collections.Generic;

namespace HanziDefend.Data
{
    /// <summary>One scheduled combatant, in the order the wave data declares it.</summary>
    public readonly struct WaveSpawnPoint
    {
        internal WaveSpawnPoint(
            int waveIndex,
            int act,
            EnemyRank rewardRank,
            string unitId,
            int level,
            float spreadX,
            int groupIndex,
            int ordinal,
            double timeSeconds,
            bool isBoss)
        {
            WaveIndex = waveIndex;
            Act = act;
            RewardRank = rewardRank;
            UnitId = unitId;
            Level = level;
            SpreadX = spreadX;
            GroupIndex = groupIndex;
            Ordinal = ordinal;
            TimeSeconds = timeSeconds;
            IsBoss = isBoss;
        }

        public int WaveIndex { get; }
        public int Act { get; }
        public EnemyRank RewardRank { get; }
        public string UnitId { get; }
        public int Level { get; }
        public float SpreadX { get; }
        public int GroupIndex { get; }
        public int Ordinal { get; }
        public double TimeSeconds { get; }
        public bool IsBoss { get; }
    }

    /// <summary>One wave's start point on the encounter timeline.</summary>
    public readonly struct WaveStartPoint
    {
        internal WaveStartPoint(int waveIndex, int act, EnemyRank rewardRank, double timeSeconds)
        {
            WaveIndex = waveIndex;
            Act = act;
            RewardRank = rewardRank;
            TimeSeconds = timeSeconds;
        }

        public int WaveIndex { get; }
        public int Act { get; }
        public EnemyRank RewardRank { get; }
        public double TimeSeconds { get; }
    }

    /// <summary>
    /// What one act actually does, measured rather than declared: when its first and last combatant
    /// land, how long the silence before it lasted, the longest hole inside it, and what it is made of.
    /// </summary>
    public sealed class WaveActSummary
    {
        internal WaveActSummary(
            int act,
            int firstWaveIndex,
            int lastWaveIndex,
            double firstSpawnSeconds,
            double lastSpawnSeconds,
            double gapBeforeSeconds,
            double longestInternalGapSeconds,
            int combatantCount,
            float combatantHp,
            int unarmored,
            int light,
            int heavy,
            int building)
        {
            Act = act;
            FirstWaveIndex = firstWaveIndex;
            LastWaveIndex = lastWaveIndex;
            FirstSpawnSeconds = firstSpawnSeconds;
            LastSpawnSeconds = lastSpawnSeconds;
            GapBeforeSeconds = gapBeforeSeconds;
            LongestInternalGapSeconds = longestInternalGapSeconds;
            CombatantCount = combatantCount;
            CombatantHp = combatantHp;
            Unarmored = unarmored;
            Light = light;
            Heavy = heavy;
            Building = building;
        }

        public int Act { get; }
        public int FirstWaveIndex { get; }
        public int LastWaveIndex { get; }
        public double FirstSpawnSeconds { get; }
        public double LastSpawnSeconds { get; }

        /// <summary>Silence between the previous act's last combatant and this act's first. Zero for act 1.</summary>
        public double GapBeforeSeconds { get; }

        /// <summary>Largest hole between two consecutive combatants inside this act — the "断档" measure.</summary>
        public double LongestInternalGapSeconds { get; }

        public double DurationSeconds => LastSpawnSeconds - FirstSpawnSeconds;

        /// <summary>Non-boss spawns; the castle is counted by <see cref="Building"/> only.</summary>
        public int CombatantCount { get; }

        /// <summary>Total non-boss hit points this act puts on the field.</summary>
        public float CombatantHp { get; }

        public int Unarmored { get; }
        public int Light { get; }
        public int Heavy { get; }
        public int Building { get; }

        public double UnarmoredRate => Rate(Unarmored);
        public double LightRate => Rate(Light);
        public double HeavyRate => Rate(Heavy);

        private double Rate(int value)
        {
            return CombatantCount <= 0 ? 0d : (double)value / CombatantCount;
        }
    }

    /// <summary>
    /// Turns a wave set into the timeline it actually produces. One compiler, three consumers: the
    /// battle scheduler spawns from it, config validation checks the three-act shape against it, and
    /// the balance tooling budgets against it — so none of them can disagree about when a wave lands.
    /// </summary>
    public sealed class WaveTimeline
    {
        private WaveTimeline(
            string waveSetId,
            IReadOnlyList<WaveStartPoint> waveStarts,
            IReadOnlyList<WaveSpawnPoint> spawns,
            IReadOnlyList<WaveActSummary> acts,
            double bossSpawnSeconds,
            double lastSpawnSeconds)
        {
            WaveSetId = waveSetId;
            WaveStarts = waveStarts;
            Spawns = spawns;
            Acts = acts;
            BossSpawnSeconds = bossSpawnSeconds;
            LastSpawnSeconds = lastSpawnSeconds;
        }

        public string WaveSetId { get; }

        public IReadOnlyList<WaveStartPoint> WaveStarts { get; }

        /// <summary>
        /// Spawns in declaration order (wave, then group, then ordinal) — deliberately not sorted by
        /// time. The scheduler draws one random X per spawn while walking this list, so the order is
        /// part of the RNG contract and re-sorting it here would silently reshuffle every seed.
        /// </summary>
        public IReadOnlyList<WaveSpawnPoint> Spawns { get; }

        public IReadOnlyList<WaveActSummary> Acts { get; }

        /// <summary>When the boss-rank spawn lands, or NaN when the set has none.</summary>
        public double BossSpawnSeconds { get; }

        public double LastSpawnSeconds { get; }

        public static WaveTimeline Compile(GameConfig config, WaveSetDef waveSet)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (waveSet == null)
            {
                throw new ArgumentNullException(nameof(waveSet));
            }

            var waveStarts = new List<WaveStartPoint>(waveSet.Waves.Length);
            var spawns = new List<WaveSpawnPoint>();
            double waveStartTime = 0d;
            double bossSpawnSeconds = double.NaN;

            for (int waveOrder = 0; waveOrder < waveSet.Waves.Length; waveOrder++)
            {
                WaveDef wave = waveSet.Waves[waveOrder];
                waveStartTime += wave.DelaySec;
                waveStarts.Add(new WaveStartPoint(wave.Index, wave.Act, wave.RewardRank, waveStartTime));

                for (int groupIndex = 0; groupIndex < wave.Spawns.Length; groupIndex++)
                {
                    WaveSpawnDef group = wave.Spawns[groupIndex];
                    bool isBoss = config.BossesById.ContainsKey(group.UnitId);
                    for (int ordinal = 0; ordinal < group.Count; ordinal++)
                    {
                        double time = waveStartTime + (double)ordinal * group.IntervalSec;
                        if (isBoss && (double.IsNaN(bossSpawnSeconds) || time < bossSpawnSeconds))
                        {
                            bossSpawnSeconds = time;
                        }

                        spawns.Add(new WaveSpawnPoint(
                            wave.Index,
                            wave.Act,
                            wave.RewardRank,
                            group.UnitId,
                            group.Level,
                            group.SpreadX,
                            groupIndex,
                            ordinal,
                            time,
                            isBoss));
                    }
                }
            }

            double lastSpawnSeconds = 0d;
            for (int index = 0; index < spawns.Count; index++)
            {
                if (spawns[index].TimeSeconds > lastSpawnSeconds)
                {
                    lastSpawnSeconds = spawns[index].TimeSeconds;
                }
            }

            return new WaveTimeline(
                waveSet.Id,
                waveStarts.AsReadOnly(),
                spawns.AsReadOnly(),
                SummarizeActs(config, waveSet, spawns),
                bossSpawnSeconds,
                lastSpawnSeconds);
        }

        public WaveActSummary GetAct(int act)
        {
            for (int index = 0; index < Acts.Count; index++)
            {
                if (Acts[index].Act == act)
                {
                    return Acts[index];
                }
            }

            throw new KeyNotFoundException($"Wave set '{WaveSetId}' has no act {act}.");
        }

        /// <summary>Total non-boss hit points across every act.</summary>
        public float TotalCombatantHp
        {
            get
            {
                float total = 0f;
                for (int index = 0; index < Acts.Count; index++)
                {
                    total += Acts[index].CombatantHp;
                }

                return total;
            }
        }

        public int TotalCombatantCount
        {
            get
            {
                int total = 0;
                for (int index = 0; index < Acts.Count; index++)
                {
                    total = checked(total + Acts[index].CombatantCount);
                }

                return total;
            }
        }

        private static IReadOnlyList<WaveActSummary> SummarizeActs(
            GameConfig config,
            WaveSetDef waveSet,
            List<WaveSpawnPoint> spawns)
        {
            var acts = new List<int>();
            for (int index = 0; index < waveSet.Waves.Length; index++)
            {
                int act = waveSet.Waves[index].Act;
                if (!acts.Contains(act))
                {
                    acts.Add(act);
                }
            }

            acts.Sort();
            var result = new List<WaveActSummary>(acts.Count);
            double previousActLastSpawn = double.NaN;
            for (int actIndex = 0; actIndex < acts.Count; actIndex++)
            {
                int act = acts[actIndex];
                var times = new List<double>();
                int firstWaveIndex = int.MaxValue;
                int lastWaveIndex = int.MinValue;
                int combatants = 0;
                float hp = 0f;
                int unarmored = 0;
                int light = 0;
                int heavy = 0;
                int building = 0;

                for (int index = 0; index < spawns.Count; index++)
                {
                    WaveSpawnPoint spawn = spawns[index];
                    if (spawn.Act != act)
                    {
                        continue;
                    }

                    times.Add(spawn.TimeSeconds);
                    if (spawn.WaveIndex < firstWaveIndex) firstWaveIndex = spawn.WaveIndex;
                    if (spawn.WaveIndex > lastWaveIndex) lastWaveIndex = spawn.WaveIndex;

                    if (spawn.IsBoss)
                    {
                        building++;
                        continue;
                    }

                    UnitDef unit = config.GetUnit(spawn.UnitId);
                    combatants++;
                    hp += Formula.StatAtLevel(unit.Hp.Base, unit.Hp.Growth, spawn.Level);
                    switch (unit.ArmorType)
                    {
                        case ArmorType.Unarmored: unarmored++; break;
                        case ArmorType.Light: light++; break;
                        case ArmorType.Heavy: heavy++; break;
                        case ArmorType.Building: building++; break;
                    }
                }

                times.Sort();
                double first = times.Count == 0 ? 0d : times[0];
                double last = times.Count == 0 ? 0d : times[times.Count - 1];
                double longestInternalGap = 0d;
                for (int index = 1; index < times.Count; index++)
                {
                    double gap = times[index] - times[index - 1];
                    if (gap > longestInternalGap)
                    {
                        longestInternalGap = gap;
                    }
                }

                double gapBefore = double.IsNaN(previousActLastSpawn) || times.Count == 0
                    ? 0d
                    : first - previousActLastSpawn;
                if (times.Count > 0)
                {
                    previousActLastSpawn = last;
                }

                result.Add(new WaveActSummary(
                    act,
                    firstWaveIndex == int.MaxValue ? 0 : firstWaveIndex,
                    lastWaveIndex == int.MinValue ? 0 : lastWaveIndex,
                    first,
                    last,
                    gapBefore,
                    longestInternalGap,
                    combatants,
                    hp,
                    unarmored,
                    light,
                    heavy,
                    building));
            }

            return result.AsReadOnly();
        }
    }
}
