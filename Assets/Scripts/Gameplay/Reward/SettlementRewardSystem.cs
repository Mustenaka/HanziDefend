using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using HanziDefend.Data;

namespace HanziDefend.Gameplay.Reward
{
    public interface IAdService
    {
        bool ShowRewarded();
    }

    public sealed class MockAdService : IAdService
    {
        public bool ShowRewarded() => true;
    }

    public enum SettlementRewardKind
    {
        Buff,
        ActiveSkill
    }

    public sealed class SettlementRewardCard
    {
        internal SettlementRewardCard(string effectId, SettlementRewardKind kind)
        {
            EffectId = effectId ?? throw new ArgumentNullException(nameof(effectId));
            Kind = kind;
        }

        public string EffectId { get; }

        public SettlementRewardKind Kind { get; }
    }

    public sealed class SettlementOffer
    {
        internal SettlementOffer(IReadOnlyList<SettlementRewardCard> cards)
        {
            Cards = new ReadOnlyCollection<SettlementRewardCard>(
                new List<SettlementRewardCard>(cards ?? throw new ArgumentNullException(nameof(cards))));
        }

        public IReadOnlyList<SettlementRewardCard> Cards { get; }
    }

    public sealed class SettlementSelection
    {
        internal SettlementSelection(int slotIndex, SettlementRewardCard card)
        {
            SlotIndex = slotIndex;
            Card = card ?? throw new ArgumentNullException(nameof(card));
        }

        public int SlotIndex { get; }

        public SettlementRewardCard Card { get; }
    }

    public sealed class SettlementRewardSystem
    {
        private readonly GameConfig config;
        private readonly RunState state;
        private readonly RngStreams streams;
        private readonly IAdService adService;
        private readonly HashSet<int> freeRerollsUsed = new HashSet<int>();
        private readonly HashSet<int> adRerollCredits = new HashSet<int>();
        private readonly HashSet<int> adAwardsUsed = new HashSet<int>();
        private SettlementRewardCard[] cards;

        public SettlementRewardSystem(
            GameConfig config,
            RunState state,
            RngStreams streams,
            IAdService adService = null)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.streams = streams ?? throw new ArgumentNullException(nameof(streams));
            this.adService = adService ?? new MockAdService();
        }

        public RunState State => state;

        public SettlementOffer CurrentOffer { get; private set; }

        public bool IsSelectionComplete { get; private set; }

        public SettlementOffer CreateOffer()
        {
            if (CurrentOffer != null)
            {
                throw new InvalidOperationException("A settlement offer already exists.");
            }

            SettlementRewardRulesDef rules = RequireRules();
            cards = new SettlementRewardCard[rules.SlotCount];
            for (int slotIndex = 0; slotIndex < cards.Length; slotIndex++)
            {
                cards[slotIndex] = DrawCard(slotIndex, false);
            }

            return PublishOffer();
        }

        public SettlementOffer Reroll(int slotIndex)
        {
            RequireOpenOffer();
            ValidateSlot(slotIndex);
            bool useFreeReroll = !freeRerollsUsed.Contains(slotIndex);
            bool useAdReroll = !useFreeReroll && adRerollCredits.Contains(slotIndex);
            if (!useFreeReroll && !useAdReroll)
            {
                throw new InvalidOperationException($"Settlement slot {slotIndex} has no reroll remaining.");
            }

            SettlementRewardCard replacement = DrawCard(slotIndex, true);
            if (useFreeReroll)
            {
                freeRerollsUsed.Add(slotIndex);
            }
            else
            {
                adRerollCredits.Remove(slotIndex);
            }
            cards[slotIndex] = replacement;
            return PublishOffer();
        }

        public bool WatchAd(int slotIndex)
        {
            RequireOpenOffer();
            ValidateSlot(slotIndex);
            if (!freeRerollsUsed.Contains(slotIndex)
                || adRerollCredits.Contains(slotIndex)
                || adAwardsUsed.Contains(slotIndex))
            {
                return false;
            }

            if (!adService.ShowRewarded())
            {
                return false;
            }

            adAwardsUsed.Add(slotIndex);
            adRerollCredits.Add(slotIndex);
            return true;
        }

        public SettlementSelection Select(int slotIndex)
        {
            RequireOpenOffer();
            ValidateSlot(slotIndex);
            SettlementRewardCard selected = cards[slotIndex];
            config.GetEffect(selected.EffectId);
            var owned = new List<string>(state.OwnedEffects ?? Array.Empty<string>());
            if (!IsUnique(selected.EffectId) || !owned.Contains(selected.EffectId))
            {
                owned.Add(selected.EffectId);
            }

            state.OwnedEffects = owned.ToArray();
            state.RngStreamsState = streams.SaveState();
            IsSelectionComplete = true;
            return new SettlementSelection(slotIndex, selected);
        }

        private SettlementRewardCard DrawCard(int replacingSlotIndex, bool excludeCurrentCard)
        {
            SettlementRewardRulesDef rules = RequireRules();
            var excluded = new HashSet<string>(StringComparer.Ordinal);
            string[] owned = state.OwnedEffects ?? Array.Empty<string>();
            for (int index = 0; index < owned.Length; index++)
            {
                config.GetEffect(owned[index]);
                if (IsUnique(owned[index]))
                {
                    excluded.Add(owned[index]);
                }
            }

            if (cards != null)
            {
                for (int index = 0; index < cards.Length; index++)
                {
                    if (cards[index] != null
                        && excludeCurrentCard
                        && index == replacingSlotIndex)
                    {
                        excluded.Add(cards[index].EffectId);
                    }
                }
            }

            var buffs = Eligible(rules.BuffEffectIds, excluded);
            var activeSkills = Eligible(rules.ActiveSkillEffectIds, excluded);
            if (buffs.Count == 0 && activeSkills.Count == 0)
            {
                throw new InvalidOperationException("No eligible settlement reward remains.");
            }

            SettlementRewardKind kind;
            IReadOnlyList<string> pool;
            if (buffs.Count == 0)
            {
                kind = SettlementRewardKind.ActiveSkill;
                pool = activeSkills;
            }
            else if (activeSkills.Count == 0)
            {
                kind = SettlementRewardKind.Buff;
                pool = buffs;
            }
            else
            {
                int category = streams.Settlement.NextWeightedIndex(new[]
                {
                    (float)rules.BuffWeight,
                    (float)rules.ActiveSkillWeight
                });
                kind = category == 0 ? SettlementRewardKind.Buff : SettlementRewardKind.ActiveSkill;
                pool = category == 0 ? buffs : activeSkills;
            }

            int selectedIndex = pool.Count == 1 ? 0 : streams.Settlement.NextInt(pool.Count);
            return new SettlementRewardCard(pool[selectedIndex], kind);
        }

        private bool IsUnique(string effectId)
        {
            string[] uniqueIds = RequireRules().UniqueEffectIds ?? Array.Empty<string>();
            for (int index = 0; index < uniqueIds.Length; index++)
            {
                if (string.Equals(uniqueIds[index], effectId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private List<string> Eligible(IReadOnlyList<string> source, ISet<string> excluded)
        {
            var result = new List<string>();
            if (source == null)
            {
                return result;
            }

            for (int index = 0; index < source.Count; index++)
            {
                string effectId = source[index];
                config.GetEffect(effectId);
                if (!excluded.Contains(effectId))
                {
                    result.Add(effectId);
                }
            }

            return result;
        }

        private SettlementOffer PublishOffer()
        {
            state.RngStreamsState = streams.SaveState();
            CurrentOffer = new SettlementOffer(cards);
            return CurrentOffer;
        }

        private SettlementRewardRulesDef RequireRules()
        {
            SettlementRewardRulesDef rules = config.Economy?.SettlementReward;
            if (rules == null)
            {
                throw new InvalidOperationException("economy.settlementReward is required.");
            }

            if (rules.SlotCount <= 0 || rules.BuffWeight < 0 || rules.ActiveSkillWeight < 0
                || rules.BuffWeight + rules.ActiveSkillWeight <= 0)
            {
                throw new InvalidOperationException("economy.settlementReward contains invalid counts or weights.");
            }

            return rules;
        }

        private void RequireOpenOffer()
        {
            if (CurrentOffer == null)
            {
                throw new InvalidOperationException("Create a settlement offer first.");
            }

            if (IsSelectionComplete)
            {
                throw new InvalidOperationException("The settlement reward was already selected.");
            }
        }

        private void ValidateSlot(int slotIndex)
        {
            if (cards == null || slotIndex < 0 || slotIndex >= cards.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }
        }
    }
}
