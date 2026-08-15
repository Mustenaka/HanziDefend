using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Deploy
{
    public enum CardCategory
    {
        Unit,
        Unlock,
        Buff,
        Global
    }

    public sealed class CardOfferItem
    {
        public CardOfferItem(CardCategory category, string contentId)
        {
            if (string.IsNullOrWhiteSpace(contentId))
                throw new ArgumentException("Card content id is required.", nameof(contentId));
            if (category == CardCategory.Unlock)
                throw new ArgumentException(
                    "An unlock card carries its minted shape; use the UnlockCard constructor.",
                    nameof(category));
            Category = category;
            ContentId = contentId;
        }

        public CardOfferItem(UnlockCard unlock)
        {
            Unlock = unlock ?? throw new ArgumentNullException(nameof(unlock));
            Category = CardCategory.Unlock;
            ContentId = unlock.ContentId;
        }

        public CardCategory Category { get; }

        public string ContentId { get; }

        /// <summary>The minted cell-unlock shape; null for every other category.</summary>
        public UnlockCard Unlock { get; }
    }

    public sealed class CardOffer
    {
        internal CardOffer(IReadOnlyList<CardOfferItem> cards, bool isLucky)
        {
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            Cards = new ReadOnlyCollection<CardOfferItem>(new List<CardOfferItem>(cards));
            IsLucky = isLucky;
        }

        public IReadOnlyList<CardOfferItem> Cards { get; }

        public bool IsLucky { get; }
    }

    public readonly struct EffectiveCardWeights
    {
        public EffectiveCardWeights(float unit, float unlock, float buff, float global)
        {
            Unit = unit;
            Unlock = unlock;
            Buff = buff;
            Global = global;
        }

        public float Unit { get; }
        public float Unlock { get; }
        public float Buff { get; }
        public float Global { get; }
        public float Total => Unit + Unlock + Buff + Global;
    }

    /// <summary>
    /// Engine-independent owner of a run's deployment hand and economy state. The class accepts
    /// no arbitrary RNG: every offer is generated exclusively from <see cref="RngStreams.CardDraw"/>.
    /// </summary>
    public sealed class CardEconomy
    {
        private readonly GameConfig config;
        private readonly DeploymentGridOrientation orientation;
        private LevelDef level;
        private bool freeOfferDrawn;
        private ulong nextDeploymentSequence = 1;

        private CardEconomy(
            GameConfig config,
            LevelDef level,
            RunState state,
            DeploymentGrid grid,
            RngStreams randomStreams,
            DeploymentGridOrientation orientation)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.level = level ?? throw new ArgumentNullException(nameof(level));
            State = state ?? throw new ArgumentNullException(nameof(state));
            Grid = grid ?? throw new ArgumentNullException(nameof(grid));
            RandomStreams = randomStreams ?? throw new ArgumentNullException(nameof(randomStreams));
            this.orientation = orientation;
            nextDeploymentSequence = DeriveNextDeploymentSequence(grid.Placements);
            SnapshotToRunState();
        }

        public RunState State { get; private set; }

        public DeploymentGrid Grid { get; private set; }

        public RngStreams RandomStreams { get; private set; }

        public int NextRefreshCost => Formula.RefreshCost(State.RefreshCount, config.Economy);

        /// <summary>Coin price of the next bought unlock card; rises with every purchase this run.</summary>
        public int NextUnlockPurchaseCost =>
            Formula.UnlockPurchaseCost(State.UnlockPurchaseCount, config.Economy);

        public bool CanPurchaseUnlockCard =>
            !Grid.IsFullyUnlocked && State.Coins >= NextUnlockPurchaseCost;

        public bool CanDrawFreeOffer => !freeOfferDrawn && CurrentOffer == null;

        public bool CanRefresh => freeOfferDrawn && CurrentOffer != null && State.Coins >= NextRefreshCost;

        /// <summary>The currently usable hand. Null until the stage's one free offer is drawn.</summary>
        public CardOffer CurrentOffer { get; private set; }

        public event Action<CardOffer> OfferDrawn;
        public event Action<CardOffer> HandChanged;
        public event Action<int> CoinsChanged;
        public event Action DeploymentsChanged;
        public event Action<IReadOnlyList<string>> EffectsChanged;

        public static CardEconomy StartNew(
            GameConfig config,
            LevelDef firstLevel,
            uint seed,
            DeploymentGridOrientation orientation = DeploymentGridOrientation.ColumnsHorizontal)
        {
            firstLevel = RequireLevel(config, firstLevel);
            var streams = new RngStreams(seed);
            var state = CreateFreshState(firstLevel, streams.MasterSeed);
            DeploymentGrid grid = CreateGrid(config, firstLevel, state.UnlockedCells, orientation);
            return new CardEconomy(config, firstLevel, state, grid, streams, orientation);
        }

        public static CardEconomy Restore(
            GameConfig config,
            LevelDef level,
            RunState state,
            DeploymentGridOrientation orientation = DeploymentGridOrientation.ColumnsHorizontal)
        {
            level = RequireLevel(config, level);
            if (state == null) throw new ArgumentNullException(nameof(state));

            bool[] mask = state.UnlockedCells != null && state.UnlockedCells.Length == level.GridWidth * level.GridHeight
                ? state.UnlockedCells
                : null;
            var streams = new RngStreams(state.Seed);
            if (state.RngStreamsState != null) streams.RestoreState(state.RngStreamsState);

            DeploymentGrid grid = CreateGrid(config, level, mask, orientation);
            RestorePlacements(config, state.DeployedGrid, grid);
            state.GridWidth = grid.Width;
            state.GridHeight = grid.Height;
            state.UnlockedCells = grid.SaveUnlockMask();
            state.OwnedEffects = state.OwnedEffects ?? Array.Empty<string>();
            state.DeployedGrid = state.DeployedGrid ?? Array.Empty<DeployedUnitState>();
            return new CardEconomy(config, level, state, grid, streams, orientation);
        }

        public EffectiveCardWeights GetEffectiveWeights()
        {
            CardWeightsDef configured = config.Economy.CardWeights;
            float unit = configured.Unit;
            float unlock = Grid.IsFullyUnlocked ? 0f : configured.Unlock;
            float buff = configured.Buff;
            float global = configured.Global;
            float configuredTotal = unit + configured.Unlock + buff + global;
            float effectiveTotal = unit + unlock + buff + global;
            if (effectiveTotal <= 0f)
                throw new InvalidOperationException("The effective card category weight total must be positive.");
            float scale = configuredTotal / effectiveTotal;
            return new EffectiveCardWeights(unit * scale, unlock * scale, buff * scale, global * scale);
        }

        /// <summary>Draws an offer. Any data error leaves both RNG and guarantee state unchanged.</summary>
        public CardOffer DrawOffer()
        {
            if (freeOfferDrawn)
                throw new InvalidOperationException("This minor stage's free offer has already been drawn; use TryRefresh.");
            if (CurrentOffer != null)
                throw new InvalidOperationException("A current offer already exists; use TryRefresh to replace it.");

            RunState temporaryState = CloneDrawState(State);
            RngStreams temporaryStreams = CloneStreams(RandomStreams);
            CardOffer offer = BuildOffer(temporaryState, temporaryStreams);
            CommitDraw(temporaryState, temporaryStreams);
            freeOfferDrawn = true;
            CurrentOffer = offer;
            OfferDrawn?.Invoke(offer);
            HandChanged?.Invoke(CurrentOffer);
            return offer;
        }

        /// <summary>
        /// Charges the formula-derived refresh price and draws a new offer atomically. Insufficient
        /// coins consume neither money, refresh count, guarantee state nor random state.
        /// </summary>
        public bool TryRefresh(out CardOffer offer)
        {
            if (!freeOfferDrawn || CurrentOffer == null)
                throw new InvalidOperationException("Draw the minor stage's free offer before refreshing.");

            int cost = NextRefreshCost;
            if (State.Coins < cost)
            {
                offer = null;
                return false;
            }

            RunState temporaryState = CloneDrawState(State);
            RngStreams temporaryStreams = CloneStreams(RandomStreams);
            offer = BuildOffer(temporaryState, temporaryStreams);

            int updatedCoins = State.Coins - cost;
            int updatedRefreshCount = checked(State.RefreshCount + 1);
            State.Coins = updatedCoins;
            State.RefreshCount = updatedRefreshCount;
            CommitDraw(temporaryState, temporaryStreams);
            CurrentOffer = offer;
            CoinsChanged?.Invoke(State.Coins);
            OfferDrawn?.Invoke(offer);
            HandChanged?.Invoke(CurrentOffer);
            return true;
        }

        /// <summary>
        /// Replaces the current offer after an external rewarded-ad service has granted the
        /// action. This rule-layer API deliberately performs no currency comparison or charge;
        /// the View can only request it through its injected command callback.
        /// </summary>
        public bool TryRewardedRefresh(out CardOffer offer)
        {
            if (!freeOfferDrawn || CurrentOffer == null)
                throw new InvalidOperationException("Draw the minor stage's free offer before refreshing.");

            RunState temporaryState = CloneDrawState(State);
            RngStreams temporaryStreams = CloneStreams(RandomStreams);
            offer = BuildOffer(temporaryState, temporaryStreams);
            CommitDraw(temporaryState, temporaryStreams);
            CurrentOffer = offer;
            OfferDrawn?.Invoke(offer);
            HandChanged?.Invoke(CurrentOffer);
            return true;
        }

        /// <summary>Places a hand card using a deterministic, run-local deployment id.</summary>
        public DeploymentApplyResult PlaceUnit(CardOfferItem card, GridCoordinate anchor)
        {
            ulong candidateSequence = nextDeploymentSequence;
            string deploymentId;
            do
            {
                deploymentId = $"deploy_{candidateSequence:D8}";
                candidateSequence++;
            }
            while (Grid.TryGetPlacement(deploymentId, out _));

            DeploymentApplyResult result = PlaceUnit(card, deploymentId, anchor);
            nextDeploymentSequence = candidateSequence;
            return result;
        }

        public DeploymentApplyResult PlaceUnit(
            CardOfferItem card,
            string deploymentId,
            GridCoordinate anchor)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            RequireCurrentCard(card);
            if (card.Category != CardCategory.Unit)
                throw new ArgumentException("Only a unit card can be placed on the deployment grid.", nameof(card));

            UnitDef definition = config.GetUnit(card.ContentId);
            RequireEligibleUnit(definition);
            var unit = new DeploymentUnit(
                deploymentId,
                definition.Id,
                (int)definition.Tier,
                UnitFootprint.FromDefinition(definition));
            DeploymentEvaluation evaluation = Grid.Evaluate(unit, anchor);
            if (!evaluation.IsValid)
                throw new InvalidOperationException(evaluation.Message);
            DeploymentApplyResult result = Grid.Apply(unit, anchor);
            Consume(card);
            SnapshotToRunState();
            HandChanged?.Invoke(CurrentOffer);
            DeploymentsChanged?.Invoke();
            return result;
        }

        /// <summary>
        /// Takes a deployed unit off the grid and puts its card back in hand — the exact inverse of
        /// <see cref="PlaceUnit(CardOfferItem, GridCoordinate)"/>, and the only sanctioned way to
        /// undo a placement.
        ///
        /// <para><b>Merged units are refused.</b> A hand card is always worth one placement at the
        /// unit's base tier, so returning a level-3 unit as a single card would quietly destroy two
        /// merges, and returning several would mint cards out of nothing. Either way the merge
        /// economy would shift, and this method exists to undo a misdrop, not to reprice merging.
        /// Un-merging, if it is ever wanted, is its own design decision.</para>
        /// </summary>
        /// <param name="deploymentId">The placement to take back.</param>
        /// <param name="failureReason">Player-facing reason when the answer is no.</param>
        public bool TryReturnUnitToHand(string deploymentId, out string failureReason)
        {
            failureReason = string.Empty;
            if (string.IsNullOrEmpty(deploymentId))
            {
                failureReason = "没有可撤回的单位";
                return false;
            }

            if (!Grid.TryGetPlacement(deploymentId, out DeploymentPlacement placement))
            {
                failureReason = "部署单位已不存在";
                return false;
            }

            UnitDef definition = config.GetUnit(placement.UnitId);
            if (placement.Level != (int)definition.Tier)
            {
                failureReason = $"{definition.Name} 已合成到 {placement.Level} 级，不能拆回手牌";
                return false;
            }

            if (CurrentOffer == null)
            {
                failureReason = "本小关的手牌尚未发放";
                return false;
            }

            if (!Grid.Remove(deploymentId))
            {
                failureReason = "撤回失败";
                return false;
            }

            var returned = new List<CardOfferItem>(CurrentOffer.Cards.Count + 1);
            returned.AddRange(CurrentOffer.Cards);
            returned.Add(new CardOfferItem(CardCategory.Unit, definition.Id));
            CurrentOffer = new CardOffer(returned, CurrentOffer.IsLucky);

            SnapshotToRunState();
            HandChanged?.Invoke(CurrentOffer);
            DeploymentsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Plays a held unlock card at an anchor. Obtaining the card is what costs money or an ad
        /// view, so playing one is free; an illegal anchor leaves both the card and the mask alone.
        /// </summary>
        public bool TryPlayUnlockCard(
            CardOfferItem card,
            GridCoordinate anchor,
            out UnlockApplyResult result)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            RequireCurrentCard(card);
            if (card.Category != CardCategory.Unlock || card.Unlock == null)
                throw new ArgumentException("Only an unlock card can unlock grid cells.", nameof(card));

            UnlockEvaluation evaluation = Grid.EvaluateUnlock(card.Unlock, anchor);
            if (!evaluation.IsValid)
            {
                result = null;
                return false;
            }

            result = Grid.ApplyUnlock(card.Unlock, anchor);
            Consume(card);
            SnapshotToRunState();
            HandChanged?.Invoke(CurrentOffer);
            DeploymentsChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Adds one randomly minted unlock card to the hand without charging anything. The rewarded
        /// ad path calls this after its <c>IAdService</c> has already granted the reward; like
        /// <see cref="TryRewardedRefresh"/> this rule-layer API performs no currency check itself.
        /// </summary>
        public bool TryGrantUnlockCard(out CardOfferItem card)
        {
            if (Grid.IsFullyUnlocked)
            {
                card = null;
                return false;
            }

            card = MintUnlockCardIntoHand();
            HandChanged?.Invoke(CurrentOffer);
            return true;
        }

        /// <summary>Buys one randomly minted unlock card. Insufficient coins change nothing.</summary>
        public bool TryPurchaseUnlockCard(out CardOfferItem card)
        {
            int cost = NextUnlockPurchaseCost;
            if (Grid.IsFullyUnlocked || State.Coins < cost)
            {
                card = null;
                return false;
            }

            card = MintUnlockCardIntoHand();
            State.Coins = checked(State.Coins - cost);
            State.UnlockPurchaseCount = checked(State.UnlockPurchaseCount + 1);
            CoinsChanged?.Invoke(State.Coins);
            HandChanged?.Invoke(CurrentOffer);
            return true;
        }

        /// <summary>
        /// Opens the automatic cell a cleared minor stage grants. Returns the cells actually opened,
        /// which is empty once the whole playfield is unlocked.
        /// </summary>
        public IReadOnlyList<GridCoordinate> AutoUnlockForClearedMinorStage()
        {
            int budget = config.Economy.GridUnlock.AutoUnlockPerMinorStage;
            var opened = new List<GridCoordinate>(Math.Max(0, budget));
            for (int step = 0; step < budget; step++)
            {
                UnlockApplyResult result = Grid.AutoUnlockNearestToBase();
                if (result == null) break;
                for (int index = 0; index < result.NewlyUnlockedCells.Count; index++)
                {
                    opened.Add(result.NewlyUnlockedCells[index]);
                }
            }

            if (opened.Count == 0) return Array.Empty<GridCoordinate>();

            SnapshotToRunState();
            DeploymentsChanged?.Invoke();
            return opened.AsReadOnly();
        }

        public void AcquireEffect(CardOfferItem card)
        {
            if (card == null) throw new ArgumentNullException(nameof(card));
            RequireCurrentCard(card);
            if (card.Category != CardCategory.Buff && card.Category != CardCategory.Global)
                throw new ArgumentException("Only a buff or global card grants an owned effect.", nameof(card));
            RequirePooledEffect(card);
            EffectDef definition = config.GetEffect(card.ContentId);

            var effects = new List<string>(State.OwnedEffects ?? Array.Empty<string>());
            string[] updatedEffects = State.OwnedEffects;
            if (definition.Stacking == EffectStackingRule.Stack || !effects.Contains(card.ContentId))
            {
                effects.Add(card.ContentId);
                updatedEffects = effects.ToArray();
            }
            Consume(card);
            State.OwnedEffects = updatedEffects;
            HandChanged?.Invoke(CurrentOffer);
            EffectsChanged?.Invoke(Array.AsReadOnly(State.OwnedEffects));
        }

        public void CreditBattleDrops(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int updatedCoins = checked(State.Coins + amount);
            State.Coins = updatedCoins;
            CoinsChanged?.Invoke(State.Coins);
        }

        public void AdvanceToMinorStage(LevelDef nextLevel)
        {
            nextLevel = RequireLevel(config, nextLevel);
            if (nextLevel.StageIndex != State.StageIndex + 1)
                throw new ArgumentException(
                    $"Minor stages must advance sequentially from {State.StageIndex} to {State.StageIndex + 1}.",
                    nameof(nextLevel));
            if (Grid.Width != nextLevel.GridWidth || Grid.Height != nextLevel.GridHeight)
                throw new InvalidOperationException(
                    $"Inherited grid {Grid.Width}x{Grid.Height} is incompatible with level '{nextLevel.Id}'.");
            SnapshotToRunState();
            level = nextLevel;
            State.StageIndex = nextLevel.StageIndex;
            CurrentOffer = null;
            freeOfferDrawn = false;
            HandChanged?.Invoke(null);
        }

        public void ResetForMajorStage(LevelDef firstLevel, uint seed)
        {
            firstLevel = RequireLevel(config, firstLevel);
            level = firstLevel;
            RandomStreams = new RngStreams(seed);
            State = CreateFreshState(firstLevel, RandomStreams.MasterSeed);
            Grid = CreateGrid(config, firstLevel, State.UnlockedCells, orientation);
            CurrentOffer = null;
            freeOfferDrawn = false;
            nextDeploymentSequence = 1;
            SnapshotToRunState();
            CoinsChanged?.Invoke(State.Coins);
            DeploymentsChanged?.Invoke();
            EffectsChanged?.Invoke(Array.AsReadOnly(State.OwnedEffects));
            HandChanged?.Invoke(null);
        }

        public void SnapshotToRunState()
        {
            State.Seed = RandomStreams.MasterSeed;
            State.GridWidth = Grid.Width;
            State.GridHeight = Grid.Height;
            State.UnlockedCells = Grid.SaveUnlockMask();
            IReadOnlyList<DeploymentPlacement> placements = Grid.Placements;
            var deployed = new DeployedUnitState[placements.Count];
            for (int index = 0; index < placements.Count; index++)
            {
                DeploymentPlacement placement = placements[index];
                deployed[index] = new DeployedUnitState
                {
                    DeploymentId = placement.DeploymentId,
                    UnitId = placement.UnitId,
                    Level = placement.Level,
                    Col = placement.Anchor.Column,
                    Row = placement.Anchor.Row
                };
            }
            State.DeployedGrid = deployed;
            State.OwnedEffects = State.OwnedEffects ?? Array.Empty<string>();
            State.RngStreamsState = RandomStreams.SaveState();
        }

        /// <summary>Mints one unlock card from the card-draw stream and appends it to the hand.</summary>
        private CardOfferItem MintUnlockCardIntoHand()
        {
            if (CurrentOffer == null)
                throw new InvalidOperationException(
                    "Draw the minor stage's free offer before acquiring an unlock card.");

            var card = new CardOfferItem(UnlockCardFactory.Draw(RandomStreams.CardDraw));
            var cards = new List<CardOfferItem>(CurrentOffer.Cards) { card };
            CurrentOffer = new CardOffer(cards, CurrentOffer.IsLucky);
            State.RngStreamsState = RandomStreams.SaveState();
            return card;
        }

        private CardOffer BuildOffer(RunState temporaryState, RngStreams temporaryStreams)
        {
            CardOfferRulesDef rules = config.Economy.CardOffer;
            bool lucky = temporaryStreams.CardDraw.NextFloat() < rules.LuckyChance;
            int count = checked(rules.BaseCount + (lucky ? rules.LuckyExtraCount : 0));
            var cards = new List<CardOfferItem>(count);

            if (UnitCardPoolPolicy.TryDrawGuaranteedCard(
                    config,
                    temporaryState,
                    Grid,
                    temporaryStreams.CardDraw,
                    out UnitDef guaranteed))
            {
                cards.Add(new CardOfferItem(CardCategory.Unit, guaranteed.Id));
            }

            while (cards.Count < count)
            {
                CardCategory category = DrawCategory(temporaryStreams.CardDraw);
                cards.Add(DrawCard(category, temporaryState, temporaryStreams.CardDraw));
            }
            return new CardOffer(cards, lucky);
        }

        private CardCategory DrawCategory(Rng rng)
        {
            EffectiveCardWeights weights = GetEffectiveWeights();
            int index = rng.NextWeightedIndex(new[]
            {
                weights.Unit,
                weights.Unlock,
                weights.Buff,
                weights.Global
            });
            return (CardCategory)index;
        }

        private CardOfferItem DrawCard(CardCategory category, RunState temporaryState, Rng rng)
        {
            switch (category)
            {
                case CardCategory.Unit:
                {
                    IReadOnlyList<UnitDef> eligible = UnitCardPoolPolicy.GetEligibleUnits(config, Grid);
                    if (eligible.Count == 0) throw new InvalidOperationException("The current grid has no eligible unit cards.");
                    UnitDef unit = eligible.Count == 1 ? eligible[0] : eligible[rng.NextInt(eligible.Count)];
                    UnitCardPoolPolicy.RecordOfferedUnit(config, temporaryState, unit);
                    return new CardOfferItem(category, unit.Id);
                }
                case CardCategory.Unlock:
                    if (Grid.IsFullyUnlocked) throw new InvalidOperationException("An unlock card cannot be drawn once every cell is unlocked.");
                    return new CardOfferItem(UnlockCardFactory.Draw(rng));
                case CardCategory.Buff:
                    return new CardOfferItem(category, DrawId(config.Economy.CardPool.BuffEffectIds, rng));
                case CardCategory.Global:
                    return new CardOfferItem(category, DrawId(config.Economy.CardPool.GlobalEffectIds, rng));
                default:
                    throw new ArgumentOutOfRangeException(nameof(category), category, "Unknown card category.");
            }
        }

        private static string DrawId(IReadOnlyList<string> ids, Rng rng)
        {
            if (ids == null || ids.Count == 0) throw new InvalidOperationException("A card content pool is empty.");
            return ids.Count == 1 ? ids[0] : ids[rng.NextInt(ids.Count)];
        }

        private void CommitDraw(RunState temporaryState, RngStreams temporaryStreams)
        {
            State.HasOfferedGuaranteedMachineryCard = temporaryState.HasOfferedGuaranteedMachineryCard;
            RandomStreams.RestoreState(temporaryStreams.SaveState());
            State.RngStreamsState = RandomStreams.SaveState();
        }

        private void RequireCurrentCard(CardOfferItem card)
        {
            if (CurrentOffer == null)
                throw new InvalidOperationException("There is no current offer to consume.");
            for (int index = 0; index < CurrentOffer.Cards.Count; index++)
            {
                if (ReferenceEquals(CurrentOffer.Cards[index], card)) return;
            }
            throw new InvalidOperationException("The card is not an unconsumed item from the current offer.");
        }

        private void Consume(CardOfferItem card)
        {
            var remaining = new List<CardOfferItem>(CurrentOffer.Cards.Count - 1);
            bool removed = false;
            for (int index = 0; index < CurrentOffer.Cards.Count; index++)
            {
                CardOfferItem candidate = CurrentOffer.Cards[index];
                if (!removed && ReferenceEquals(candidate, card))
                {
                    removed = true;
                    continue;
                }
                remaining.Add(candidate);
            }
            if (!removed) throw new InvalidOperationException("The card was already consumed.");
            CurrentOffer = new CardOffer(remaining, CurrentOffer.IsLucky);
        }

        private void RequireEligibleUnit(UnitDef unit)
        {
            IReadOnlyList<UnitDef> eligible = UnitCardPoolPolicy.GetEligibleUnits(config, Grid);
            for (int index = 0; index < eligible.Count; index++)
            {
                if (string.Equals(eligible[index].Id, unit.Id, StringComparison.Ordinal)) return;
            }
            throw new InvalidOperationException(
                $"Unit card '{unit.Id}' does not fit the {Grid.UnlockedCellCount} unlocked cells.");
        }

        private void RequirePooledEffect(CardOfferItem card)
        {
            IReadOnlyList<string> pool = card.Category == CardCategory.Buff
                ? config.Economy.CardPool.BuffEffectIds
                : config.Economy.CardPool.GlobalEffectIds;
            for (int index = 0; index < pool.Count; index++)
            {
                if (string.Equals(pool[index], card.ContentId, StringComparison.Ordinal)) return;
            }
            throw new InvalidOperationException(
                $"Effect card '{card.ContentId}' is not in the configured {card.Category} pool.");
        }

        private static RunState CloneDrawState(RunState source)
        {
            return new RunState
            {
                StageIndex = source.StageIndex,
                GridWidth = source.GridWidth,
                GridHeight = source.GridHeight,
                HasOfferedGuaranteedMachineryCard = source.HasOfferedGuaranteedMachineryCard
            };
        }

        private static RngStreams CloneStreams(RngStreams source)
        {
            var clone = new RngStreams(source.MasterSeed);
            clone.RestoreState(source.SaveState());
            return clone;
        }

        private static void RestorePlacements(
            GameConfig config,
            IReadOnlyList<DeployedUnitState> saved,
            DeploymentGrid grid)
        {
            if (saved == null) return;
            for (int index = 0; index < saved.Count; index++)
            {
                DeployedUnitState entry = saved[index]
                    ?? throw new InvalidOperationException($"RunState deployment at index {index} is null.");
                UnitDef definition = config.GetUnit(entry.UnitId);
                var unit = new DeploymentUnit(
                    entry.DeploymentId,
                    definition.Id,
                    entry.Level,
                    UnitFootprint.FromDefinition(definition));
                var anchor = new GridCoordinate(entry.Col, entry.Row);
                DeploymentEvaluation evaluation = grid.Evaluate(unit, anchor);
                if (evaluation.Action != DeploymentActionKind.Place)
                    throw new InvalidOperationException(
                        $"Cannot restore deployment '{entry.DeploymentId}': {evaluation.Message}");
                grid.Apply(unit, anchor);
            }
        }

        private static ulong DeriveNextDeploymentSequence(IReadOnlyList<DeploymentPlacement> placements)
        {
            const string prefix = "deploy_";
            ulong next = 1;
            for (int index = 0; index < placements.Count; index++)
            {
                string id = placements[index].DeploymentId;
                if (id == null || !id.StartsWith(prefix, StringComparison.Ordinal)) continue;
                string suffix = id.Substring(prefix.Length);
                if (ulong.TryParse(suffix, out ulong parsed) && parsed >= next)
                    next = checked(parsed + 1);
            }
            return next;
        }

        /// <summary>
        /// Builds the run grid. A null mask starts from the level's initial unlock rect; a supplied
        /// mask is the one inherited across minor stages.
        /// </summary>
        private static DeploymentGrid CreateGrid(
            GameConfig config,
            LevelDef level,
            bool[] unlockedMask,
            DeploymentGridOrientation orientation)
        {
            IEnumerable<GridCoordinate> unlocked = unlockedMask == null
                ? DeploymentGrid.EnumerateRect(level.InitialUnlock)
                : MaskToCells(unlockedMask, level.GridWidth, level.GridHeight);
            return new DeploymentGrid(
                level.GridWidth,
                level.GridHeight,
                unlocked,
                orientation,
                config.Economy.CardPool,
                config.Economy.GridUnlock.BaseAnchorRowOffset);
        }

        private static IEnumerable<GridCoordinate> MaskToCells(bool[] mask, int width, int height)
        {
            if (mask.Length != checked(width * height))
                throw new InvalidOperationException(
                    $"Run grid mask holds {mask.Length} cells but the level declares {width}x{height}.");
            var cells = new List<GridCoordinate>(mask.Length);
            for (int row = 0; row < height; row++)
            for (int column = 0; column < width; column++)
            {
                if (mask[(row * width) + column]) cells.Add(new GridCoordinate(column, row));
            }
            return cells;
        }

        private static RunState CreateFreshState(LevelDef firstLevel, uint seed)
        {
            var mask = new bool[checked(firstLevel.GridWidth * firstLevel.GridHeight)];
            foreach (GridCoordinate cell in DeploymentGrid.EnumerateRect(firstLevel.InitialUnlock))
            {
                mask[(cell.Row * firstLevel.GridWidth) + cell.Column] = true;
            }

            return new RunState
            {
                Coins = firstLevel.StartCoins,
                DeployedGrid = Array.Empty<DeployedUnitState>(),
                OwnedEffects = Array.Empty<string>(),
                RefreshCount = 0,
                StageIndex = firstLevel.StageIndex,
                GridWidth = firstLevel.GridWidth,
                GridHeight = firstLevel.GridHeight,
                UnlockedCells = mask,
                UnlockPurchaseCount = 0,
                Seed = seed,
                HasOfferedGuaranteedMachineryCard = false
            };
        }

        private static LevelDef RequireLevel(GameConfig config, LevelDef level)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (level == null) throw new ArgumentNullException(nameof(level));
            return config.GetLevel(level.Id);
        }
    }
}
