using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;

namespace HanziDefend.Editor.Balance
{
    /// <summary>What a run actually looks like at the start of one minor stage's battle.</summary>
    public sealed class RunAccumulationStage
    {
        internal RunAccumulationStage(
            int stageIndex,
            IReadOnlyList<GridCoordinate> unlockedCells,
            IReadOnlyList<DeployedUnitState> deployments,
            int handsDealt,
            int freeHands,
            int paidRefreshes,
            int unlockPurchases,
            int coinsAtStart,
            int coinsLeftOver,
            int battleDrops,
            int occupiedCells,
            IReadOnlyList<string> ownedEffects)
        {
            StageIndex = stageIndex;
            UnlockedCells = unlockedCells;
            Deployments = deployments;
            HandsDealt = handsDealt;
            FreeHands = freeHands;
            PaidRefreshes = paidRefreshes;
            UnlockPurchases = unlockPurchases;
            CoinsAtStart = coinsAtStart;
            CoinsLeftOver = coinsLeftOver;
            BattleDrops = battleDrops;
            OccupiedCells = occupiedCells;
            OwnedEffects = ownedEffects;
        }

        public int StageIndex { get; }
        public IReadOnlyList<GridCoordinate> UnlockedCells { get; }
        public IReadOnlyList<DeployedUnitState> Deployments { get; }

        /// <summary>Preparation rounds actually played this stage: free hands plus paid refreshes.</summary>
        public int HandsDealt { get; }

        public int FreeHands { get; }
        public int PaidRefreshes { get; }
        public int UnlockPurchases { get; }
        public int CoinsAtStart { get; }
        public int CoinsLeftOver { get; }

        /// <summary>Coins this stage's battle pays out, assuming the player clears it.</summary>
        public int BattleDrops { get; }

        public int OccupiedCells { get; }
        public IReadOnlyList<string> OwnedEffects { get; }
        public int UnlockedCellCount => UnlockedCells.Count;
        public int UnitCount => Deployments.Count;
    }

    /// <summary>
    /// Plays a run forward through <see cref="CardEconomy"/> with a fixed, greedy policy, so the
    /// reference lineups describe a board a player can actually arrive at.
    ///
    /// <para>WO-F1 §0.5: the old reference lineups were five or six level-1 units on three columns,
    /// while a real stage-five board is ten-plus units across fifteen-plus unlocked cells. Every
    /// balance number derived from the old model described a different game. This simulator is the
    /// answer to "where did that lineup come from" — it is not a clever player, just a consistent
    /// one, and the policy is written out below so its bias is visible rather than implied.</para>
    ///
    /// <para><b>The policy.</b> Per minor stage: take every free hand the curve owes; play each hand
    /// out completely before asking for the next; then spend leftover coins, buying grid before
    /// buying re-rolls. Playing a hand means: unlock cards first (largest real gain, nearest the
    /// camp on ties), then unit cards (merge onto a same-id twin when the board has no room for a
    /// fresh one, otherwise place nearest the camp), then buff cards. Between stages it banks the
    /// battle's coin drops and takes the automatic per-stage cell.</para>
    ///
    /// <para>It consumes only <see cref="RngStreams.CardDraw"/>, through CardEconomy, so a seed
    /// reproduces a run exactly and no new random stream enters the project.</para>
    /// </summary>
    public static class RunAccumulationSimulator
    {
        /// <summary>Ceiling on coin-funded actions per stage; a stop, not a target.</summary>
        private const int MaximumPaidActionsPerStage = 24;

        /// <summary>
        /// Aura units (火 / 冰) the policy will keep on the board — one, not two.
        ///
        /// <para>An aura resolves from a single source: the battle looks up one aura provider, and
        /// two providers do not stack. Combined with their live cap of 1, a second aura unit is one
        /// deployment cell producing 120 hit points of body and zero damage. Measured on the
        /// stage-one board it cost about a hundred effective DPS out of nine cells.</para>
        /// </summary>
        private const int MaximumAuraUnits = 1;

        /// <summary>
        /// How much a cell's sustained hit-point output counts against its sustained damage in
        /// <see cref="PerCellValue"/>. HP throughput runs two to four times the DPS figures, so an
        /// unweighted sum would rank purely by tankiness.
        /// </summary>
        private const double HitPointWeight = 0.25d;

        /// <summary>Siege placements the board wants before siege stops jumping the queue.</summary>
        private const int MinimumSiegeUnits = 1;

        /// <summary>Unarmored / light / heavy shares of act three, used to weight effective DPS.</summary>
        private static readonly double[] LateArmourMix = { 0.34d, 0.34d, 0.32d };

        public static IReadOnlyList<RunAccumulationStage> Simulate(GameConfig config, uint seed)
        {
            return Simulate(config, seed, true);
        }

        /// <param name="allowSiege">
        /// False models the run where the player never keeps a siege card. It is not a handicap
        /// bolted onto the winning board — it is the same policy declining one card type, which is
        /// what WO-F1 §C means by a no-siege route that should be possible but expensive.
        /// </param>
        public static IReadOnlyList<RunAccumulationStage> Simulate(
            GameConfig config,
            uint seed,
            bool allowSiege)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var levels = new List<LevelDef>(config.Levels);
            levels.Sort((left, right) => left.StageIndex.CompareTo(right.StageIndex));
            CardEconomy economy = CardEconomy.StartNew(config, levels[0], seed);
            var result = new List<RunAccumulationStage>(levels.Count);

            for (int index = 0; index < levels.Count; index++)
            {
                LevelDef level = levels[index];
                int coinsAtStart = economy.State.Coins;
                int freeHands = 0;
                int paidRefreshes = 0;
                int purchases = 0;

                while (economy.CanDrawFreeOffer)
                {
                    economy.DrawOffer();
                    freeHands++;
                    PlayHand(config, economy, allowSiege);
                }

                while (economy.FreeOffersRemaining > 0 && economy.CurrentOffer != null)
                {
                    if (!economy.TryRefresh(out _))
                    {
                        break;
                    }

                    freeHands++;
                    PlayHand(config, economy, allowSiege);
                }

                for (int action = 0; action < MaximumPaidActionsPerStage; action++)
                {
                    // Grid before re-rolls: an unlocked cell is permanent and inherited, while a
                    // re-roll is spent the moment it is taken.
                    if (economy.CanPurchaseUnlockCard && economy.TryPurchaseUnlockCard(out _))
                    {
                        purchases++;
                        PlayHand(config, economy, allowSiege);
                        continue;
                    }

                    if (economy.CanRefresh && economy.TryRefresh(out _))
                    {
                        paidRefreshes++;
                        PlayHand(config, economy, allowSiege);
                        continue;
                    }

                    break;
                }

                economy.SnapshotToRunState();
                int drops = BattleDropCoins(config, level);
                result.Add(new RunAccumulationStage(
                    level.StageIndex,
                    economy.Grid.GetUnlockedCells(),
                    Snapshot(economy.State.DeployedGrid),
                    freeHands + paidRefreshes,
                    freeHands,
                    paidRefreshes,
                    purchases,
                    coinsAtStart,
                    economy.State.Coins,
                    drops,
                    CountOccupiedCells(config, economy),
                    Snapshot(economy.State.OwnedEffects)));

                if (index + 1 >= levels.Count)
                {
                    break;
                }

                economy.CreditBattleDrops(drops);
                economy.AutoUnlockForClearedMinorStage();
                economy.AdvanceToMinorStage(levels[index + 1]);
            }

            return result.AsReadOnly();
        }

        /// <summary>
        /// Coins a cleared battle pays out: one drop per body plus the castle. This is the number
        /// WO-F1 §D warns about — it rises with the enemy count, so the deal curve and the refresh
        /// price have to be re-derived whenever the wave budgets move.
        /// </summary>
        public static int BattleDropCoins(GameConfig config, LevelDef level)
        {
            WaveTimeline timeline = WaveTimeline.Compile(config, config.GetWaveSet(level.WaveSetId));
            int total = 0;
            for (int index = 0; index < timeline.Spawns.Count; index++)
            {
                total = checked(total + Formula.DropCoins(timeline.Spawns[index].RewardRank, config.Economy));
            }

            return total;
        }

        private static void PlayHand(GameConfig config, CardEconomy economy, bool allowSiege)
        {
            // Each action rebuilds the hand, so re-scan rather than iterating a stale snapshot.
            for (int guard = 0; guard < 64; guard++)
            {
                if (economy.CurrentOffer == null || !TryPlayOneCard(config, economy, allowSiege))
                {
                    return;
                }
            }
        }

        private static bool TryPlayOneCard(GameConfig config, CardEconomy economy, bool allowSiege)
        {
            IReadOnlyList<CardOfferItem> cards = economy.CurrentOffer.Cards;
            for (int index = 0; index < cards.Count; index++)
            {
                if (cards[index].Category == CardCategory.Unlock && TryPlayUnlock(economy, cards[index]))
                {
                    return true;
                }
            }

            foreach (CardOfferItem card in RankUnitCards(config, economy, cards, allowSiege))
            {
                if (TryPlayUnit(config, economy, card))
                {
                    return true;
                }
            }

            for (int index = 0; index < cards.Count; index++)
            {
                CardOfferItem card = cards[index];
                if (card.Category != CardCategory.Buff && card.Category != CardCategory.Global)
                {
                    continue;
                }

                // Global cards are one-shot battle skills rather than deployment; only the
                // persistent buff pool is worth taking during preparation.
                if (card.Category == CardCategory.Buff)
                {
                    economy.AcquireEffect(card);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The only judgement in the policy: which unit card to reach for first, and which not to
        /// play at all.
        ///
        /// <para>Two rules, both of them things a player works out inside one run. <b>Siege first</b>
        /// — the castle is a building, and arrow is 0.25x and slash 0.5x against Building armour, so
        /// a board with no siege on it has no route through act three at all. <b>At most two aura
        /// units</b> — 火 and 冰 have <c>atk: 0</c> and multiply what other units do, so a third one
        /// is a cell spent multiplying a shrinking number of attackers. Without this cap the greedy
        /// policy happily built a stage-five board of six aura units and two archers, which is not a
        /// lineup any player would keep.</para>
        ///
        /// <para>Everything else is ranked by footprint, following M1-04's value density: a bigger
        /// unit is a stronger unit, and cells are the scarce resource.</para>
        /// </summary>
        private static IEnumerable<CardOfferItem> RankUnitCards(
            GameConfig config,
            CardEconomy economy,
            IReadOnlyList<CardOfferItem> cards,
            bool allowSiege)
        {
            int auraCount = CountAuraUnits(config, economy);
            int siegeCount = CountSiegeUnits(config, economy);
            var ranked = new List<CardOfferItem>();
            for (int index = 0; index < cards.Count; index++)
            {
                CardOfferItem card = cards[index];
                if (card.Category != CardCategory.Unit)
                {
                    continue;
                }

                UnitDef definition = config.GetUnit(card.ContentId);
                if (IsAura(definition) && auraCount >= MaximumAuraUnits)
                {
                    continue;
                }

                if (!allowSiege && definition.AtkType == AttackType.Siege)
                {
                    continue;
                }

                ranked.Add(card);
            }

            ranked.Sort((left, right) => CompareUnitPreference(
                config,
                config.GetUnit(left.ContentId),
                config.GetUnit(right.ContentId),
                siegeCount < MinimumSiegeUnits));
            return ranked;
        }

        private static int CompareUnitPreference(
            GameConfig config,
            UnitDef left,
            UnitDef right,
            bool stillNeedsSiege)
        {
            // Siege jumps the queue only until the board owns a can-opener. The castle takes 0.25x
            // from arrow and 0.5x from slash, so a board with no siege has no route through act
            // three at all — but past the first one, siege is just another unit, and ranking it
            // ahead unconditionally cost the stage-one board a cell of 104/cell archer to buy a
            // 78/cell crossbow it already had.
            if (stillNeedsSiege)
            {
                bool leftSiege = left.AtkType == AttackType.Siege;
                bool rightSiege = right.AtkType == AttackType.Siege;
                if (leftSiege != rightSiege)
                {
                    return leftSiege ? -1 : 1;
                }
            }

            double leftValue = PerCellValue(config, left);
            double rightValue = PerCellValue(config, right);
            int byValue = rightValue.CompareTo(leftValue);
            return byValue != 0 ? byValue : string.CompareOrdinal(left.Id, right.Id);
        }

        /// <summary>
        /// What one deployment cell is worth once cells are barracks (WO-F3).
        ///
        /// <para>The old rule was "prefer the biggest footprint", which came from the one-shot era
        /// where a cell delivered one unit forever and a bigger unit was simply more unit. Under
        /// barracks a cell's output is <c>live cap × per-unit contribution</c> spread over its
        /// occupied cells, and the live cap <i>falls</i> as the footprint grows — so the old rule
        /// selected almost exactly the wrong end of the table. Measured: it built a stage-one board
        /// worth 443 effective DPS on nine cells where the same nine cells can hold 865.</para>
        ///
        /// <para>Two terms, because a cell buys both damage and a body that soaks: sustained damage
        /// <c>cap × effective DPS / cells</c>, plus sustained hit points <c>cap × hp / cooldown /
        /// cells</c>. The hit-point term is weighted at <see cref="HitPointWeight"/> because raw HP
        /// throughput runs two to four times the DPS numbers and would otherwise decide the whole
        /// ordering by itself.</para>
        /// </summary>
        private static double PerCellValue(GameConfig config, UnitDef unit)
        {
            var footprint = UnitFootprint.FromDefinition(unit);
            int cells = Math.Max(1, footprint.OccupiedCellCount);
            int cap = Math.Max(1, config.Economy.Deployment.LiveCapForUnit(unit.Id, cells));
            float cooldown = Math.Max(0.1f, unit.Cooldown.Base);

            double damagePerCell = cap * EffectiveDps(config, unit) / cells;
            double hitPointsPerCell = cap * unit.Hp.Base / cooldown / cells;
            return damagePerCell + (HitPointWeight * hitPointsPerCell);
        }

        /// <summary>
        /// Raw damage per second, weighted by the armour mix act three actually fields, so a unit
        /// that only looks strong against the armour class it never meets does not rank on it.
        /// </summary>
        private static double EffectiveDps(GameConfig config, UnitDef unit)
        {
            if (unit.AtkType == AttackType.None || unit.Atk.Base <= 0f)
            {
                return 0d;
            }

            double raw = unit.Atk.Base * unit.AtkSpeed.Base;
            double weighted =
                (LateArmourMix[0] * Formula.TypeMultiplier(unit.AtkType, ArmorType.Unarmored, config.Economy))
                + (LateArmourMix[1] * Formula.TypeMultiplier(unit.AtkType, ArmorType.Light, config.Economy))
                + (LateArmourMix[2] * Formula.TypeMultiplier(unit.AtkType, ArmorType.Heavy, config.Economy));
            return raw * weighted;
        }

        private static int CountSiegeUnits(GameConfig config, CardEconomy economy)
        {
            int total = 0;
            IReadOnlyList<DeploymentPlacement> placements = economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                if (config.GetUnit(placements[index].UnitId).AtkType == AttackType.Siege)
                {
                    total++;
                }
            }

            return total;
        }

        private static int CountAuraUnits(GameConfig config, CardEconomy economy)
        {
            int total = 0;
            IReadOnlyList<DeploymentPlacement> placements = economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                if (IsAura(config.GetUnit(placements[index].UnitId)))
                {
                    total++;
                }
            }

            return total;
        }

        private static bool IsAura(UnitDef definition)
        {
            return definition.AtkType == AttackType.None || definition.Atk.Base <= 0f;
        }

        private static bool TryPlayUnlock(CardEconomy economy, CardOfferItem card)
        {
            IReadOnlyList<GridCoordinate> anchors = economy.Grid.GetLegalUnlockAnchors(card.Unlock);
            if (anchors.Count == 0)
            {
                return false;
            }

            GridCoordinate best = anchors[0];
            int bestGain = -1;
            float baseColumn = (economy.Grid.Width - 1) * 0.5f;
            for (int index = 0; index < anchors.Count; index++)
            {
                UnlockEvaluation evaluation = economy.Grid.EvaluateUnlock(card.Unlock, anchors[index]);
                int gain = evaluation.NewlyUnlockedCells.Count;
                if (gain > bestGain
                    || (gain == bestGain && IsNearerBase(anchors[index], best, baseColumn)))
                {
                    best = anchors[index];
                    bestGain = gain;
                }
            }

            return economy.TryPlayUnlockCard(card, best, out _);
        }

        private static bool TryPlayUnit(GameConfig config, CardEconomy economy, CardOfferItem card)
        {
            UnitDef definition = config.GetUnit(card.ContentId);
            var footprint = UnitFootprint.FromDefinition(definition);
            IReadOnlyList<GridCoordinate> free = economy.Grid.GetAvailableAnchors(footprint);
            float baseColumn = (economy.Grid.Width - 1) * 0.5f;

            if (free.Count > 0)
            {
                GridCoordinate best = free[0];
                for (int index = 1; index < free.Count; index++)
                {
                    if (IsNearerBase(free[index], best, baseColumn))
                    {
                        best = free[index];
                    }
                }

                economy.PlaceUnit(card, best);
                return true;
            }

            // No room left. Merging a duplicate is the only way to keep taking cards, and it is
            // what a player does with a full board — note it neither helps nor hurts numerically
            // while every unit's growth curve is 0 (see DECISIONS.md, WO-F1).
            IReadOnlyList<DeploymentPlacement> placements = economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                DeploymentPlacement placement = placements[index];
                if (placement.UnitId != definition.Id
                    || placement.Level != (int)definition.Tier
                    || placement.Level >= DeploymentGrid.MaximumUnitLevel)
                {
                    continue;
                }

                economy.PlaceUnit(card, placement.Anchor);
                return true;
            }

            return false;
        }

        /// <summary>Ranking used for every "where do I put it" tie: nearer the camp, then centred.</summary>
        private static bool IsNearerBase(GridCoordinate candidate, GridCoordinate current, float baseColumn)
        {
            if (candidate.Row != current.Row)
            {
                return candidate.Row < current.Row;
            }

            float candidateOffset = Math.Abs(candidate.Column - baseColumn);
            float currentOffset = Math.Abs(current.Column - baseColumn);
            if (candidateOffset != currentOffset)
            {
                return candidateOffset < currentOffset;
            }

            return candidate.Column < current.Column;
        }

        private static int CountOccupiedCells(GameConfig config, CardEconomy economy)
        {
            int total = 0;
            IReadOnlyList<DeploymentPlacement> placements = economy.Grid.Placements;
            for (int index = 0; index < placements.Count; index++)
            {
                total += UnitFootprint.FromDefinition(config.GetUnit(placements[index].UnitId))
                    .OccupiedCellCount;
            }

            return total;
        }

        private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                return Array.Empty<T>();
            }

            var copy = new T[source.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }

        /// <summary>Readable dump of a simulated run, for DECISIONS.md and the review report.</summary>
        public static string Describe(IReadOnlyList<RunAccumulationStage> stages)
        {
            var text = new StringBuilder();
            for (int index = 0; index < stages.Count; index++)
            {
                RunAccumulationStage stage = stages[index];
                text.Append("S").Append(stage.StageIndex)
                    .Append(": cells ").Append(stage.UnlockedCellCount)
                    .Append(" (used ").Append(stage.OccupiedCells).Append(')')
                    .Append(", units ").Append(stage.UnitCount)
                    .Append(", hands ").Append(stage.HandsDealt)
                    .Append(" (free ").Append(stage.FreeHands)
                    .Append(" + paid ").Append(stage.PaidRefreshes).Append(')')
                    .Append(", unlock buys ").Append(stage.UnlockPurchases)
                    .Append(", coins ").Append(stage.CoinsAtStart)
                    .Append("->").Append(stage.CoinsLeftOver)
                    .Append(", drops ").Append(stage.BattleDrops)
                    .AppendLine();
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int unit = 0; unit < stage.Deployments.Count; unit++)
                {
                    DeployedUnitState deployment = stage.Deployments[unit];
                    string key = deployment.UnitId + "@L" + deployment.Level.ToString(CultureInfo.InvariantCulture);
                    counts.TryGetValue(key, out int existing);
                    counts[key] = existing + 1;
                }

                var keys = new List<string>(counts.Keys);
                keys.Sort(StringComparer.Ordinal);
                text.Append("    ");
                for (int key = 0; key < keys.Count; key++)
                {
                    if (key > 0) text.Append(", ");
                    text.Append(keys[key]).Append(" x").Append(counts[keys[key]]);
                }

                text.AppendLine();
            }

            return text.ToString();
        }
    }
}
