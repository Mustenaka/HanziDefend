using System;
using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Reward;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class SettlementRewardTests
    {
        private GameConfig config;

        [SetUp]
        public void SetUp()
        {
            config = GameConfig.Load();
        }

        [Test]
        public void CreateOffer_ProducesExactlyThreeConfiguredRewards()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000001u, out _, out _);

            SettlementOffer offer = system.CreateOffer();

            Assert.That(offer.Cards, Has.Count.EqualTo(3));
            foreach (SettlementRewardCard card in offer.Cards)
            {
                string[] expectedPool = card.Kind == SettlementRewardKind.Buff
                    ? config.Economy.SettlementReward.BuffEffectIds
                    : config.Economy.SettlementReward.ActiveSkillEffectIds;
                Assert.That(expectedPool, Does.Contain(card.EffectId));
                Assert.That(config.GetEffect(card.EffectId), Is.Not.Null);
            }
        }

        [Test]
        public void CreateOffer_SameSeedProducesSameCardsAndKinds()
        {
            SettlementRewardSystem first = CreateSystem(0xD1000002u, out _, out _);
            SettlementRewardSystem second = CreateSystem(0xD1000002u, out _, out _);

            AssertOffersEqual(first.CreateOffer(), second.CreateOffer());
        }

        [Test]
        public void CreateOffer_AdvancesOnlySettlementStream()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000003u, out _, out RngStreams streams);
            RngStreamsState before = streams.SaveState();

            system.CreateOffer();
            RngStreamsState after = streams.SaveState();

            Assert.That(after.battle.state, Is.EqualTo(before.battle.state));
            Assert.That(after.cardDraw.state, Is.EqualTo(before.cardDraw.state));
            Assert.That(after.settlement.state, Is.Not.EqualTo(before.settlement.state));
        }

        [Test]
        public void CreateOffer_BattleAndCardDrawConsumptionCannotShiftSettlementSequence()
        {
            SettlementRewardSystem consumed = CreateSystem(0xD1000004u, out _, out RngStreams consumedStreams);
            SettlementRewardSystem untouched = CreateSystem(0xD1000004u, out _, out _);
            for (int index = 0; index < 500; index++)
            {
                consumedStreams.Battle.NextUInt();
                consumedStreams.CardDraw.NextUInt();
            }

            AssertOffersEqual(consumed.CreateOffer(), untouched.CreateOffer());
        }

        [Test]
        public void CreateOffer_CategoryDistributionTracksConfiguredEightyFiveFifteenWeights()
        {
            const int offerCount = 10000;
            var streams = new RngStreams(0xD1000005u);
            RunState state = CreateState(streams);
            int buffCount = 0;
            int cardCount = 0;
            for (int offerIndex = 0; offerIndex < offerCount; offerIndex++)
            {
                var system = new SettlementRewardSystem(config, state, streams, new MockAdService());
                SettlementOffer offer = system.CreateOffer();
                buffCount += offer.Cards.Count(card => card.Kind == SettlementRewardKind.Buff);
                cardCount += offer.Cards.Count;
            }

            float buffRate = buffCount / (float)cardCount;
            Assert.That(buffRate, Is.InRange(0.83f, 0.87f));
        }

        [Test]
        public void Reroll_ReplacesOnlyRequestedSlot()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000006u, out _, out _);
            SettlementOffer before = system.CreateOffer();

            SettlementOffer after = system.Reroll(1);

            Assert.That(after.Cards[0].EffectId, Is.EqualTo(before.Cards[0].EffectId));
            Assert.That(after.Cards[2].EffectId, Is.EqualTo(before.Cards[2].EffectId));
            Assert.That(after.Cards[1].EffectId, Is.Not.EqualTo(before.Cards[1].EffectId));
        }

        [Test]
        public void Reroll_EachSlotHasOneIndependentFreeUse()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000007u, out _, out _);
            system.CreateOffer();

            Assert.DoesNotThrow(() => system.Reroll(0));
            Assert.DoesNotThrow(() => system.Reroll(1));
            Assert.DoesNotThrow(() => system.Reroll(2));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(0));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(1));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(2));
        }

        [Test]
        public void Reroll_ExhaustedAttemptConsumesNoRandomState()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000008u, out _, out RngStreams streams);
            system.CreateOffer();
            system.Reroll(0);
            RngStreamsState before = streams.SaveState();

            Assert.Throws<InvalidOperationException>(() => system.Reroll(0));

            AssertStreamsEqual(before, streams.SaveState());
        }

        [Test]
        public void WatchAd_AfterFreeUseGrantsExactlyOneAdditionalReroll()
        {
            var ad = new RecordingAdService(true);
            SettlementRewardSystem system = CreateSystem(0xD1000009u, out _, out _, ad);
            system.CreateOffer();
            system.Reroll(0);

            Assert.That(system.WatchAd(0), Is.True);
            Assert.That(ad.CallCount, Is.EqualTo(1));
            Assert.DoesNotThrow(() => system.Reroll(0));
            Assert.That(system.WatchAd(0), Is.False);
            Assert.That(ad.CallCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(0));
        }

        [Test]
        public void WatchAd_BeforeFreeRerollDoesNotCallProviderOrGrantCredit()
        {
            var ad = new RecordingAdService(true);
            SettlementRewardSystem system = CreateSystem(0xD100000Au, out _, out _, ad);
            system.CreateOffer();

            Assert.That(system.WatchAd(1), Is.False);
            Assert.That(ad.CallCount, Is.Zero);
            Assert.DoesNotThrow(() => system.Reroll(1));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(1));
        }

        [Test]
        public void WatchAd_FailedProviderGrantsNoAdditionalReroll()
        {
            var ad = new RecordingAdService(false);
            SettlementRewardSystem system = CreateSystem(0xD100000Bu, out _, out _, ad);
            system.CreateOffer();
            system.Reroll(2);

            Assert.That(system.WatchAd(2), Is.False);
            Assert.That(ad.CallCount, Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(2));
        }

        [TestCase(-1)]
        [TestCase(3)]
        public void SlotCommands_RejectIndexesOutsideOffer(int slotIndex)
        {
            SettlementRewardSystem system = CreateSystem(0xD100000Cu, out _, out _);
            system.CreateOffer();

            Assert.Throws<ArgumentOutOfRangeException>(() => system.Reroll(slotIndex));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.WatchAd(slotIndex));
            Assert.Throws<ArgumentOutOfRangeException>(() => system.Select(slotIndex));
        }

        [Test]
        public void Select_AddsChosenEffectAndPersistsSettlementStream()
        {
            SettlementRewardSystem system = CreateSystem(
                0xD100000Du,
                out RunState state,
                out RngStreams streams);
            SettlementOffer offer = system.CreateOffer();

            SettlementSelection selection = system.Select(1);

            Assert.That(selection.SlotIndex, Is.EqualTo(1));
            Assert.That(selection.Card, Is.SameAs(offer.Cards[1]));
            Assert.That(state.OwnedEffects, Does.Contain(selection.Card.EffectId));
            Assert.That(state.RngStreamsState.settlement.state, Is.EqualTo(streams.Settlement.State));
            Assert.That(system.IsSelectionComplete, Is.True);
        }

        [Test]
        public void Select_CannotBeRepeatedOrFollowedByMoreRerolls()
        {
            SettlementRewardSystem system = CreateSystem(0xD100000Eu, out _, out _);
            system.CreateOffer();
            system.Select(0);

            Assert.Throws<InvalidOperationException>(() => system.Select(1));
            Assert.Throws<InvalidOperationException>(() => system.Reroll(1));
            Assert.Throws<InvalidOperationException>(() => system.WatchAd(1));
        }

        [Test]
        public void Select_AcrossMinorStagesAccumulatesOwnedEffects()
        {
            var streams = new RngStreams(0xD100000Fu);
            RunState state = CreateState(streams);
            var first = new SettlementRewardSystem(config, state, streams, new MockAdService());
            string firstId = first.CreateOffer().Cards[0].EffectId;
            first.Select(0);

            var second = new SettlementRewardSystem(config, state, streams, new MockAdService());
            string secondId = second.CreateOffer().Cards[1].EffectId;
            second.Select(1);

            Assert.That(state.OwnedEffects, Has.Length.EqualTo(2));
            Assert.That(state.OwnedEffects[0], Is.EqualTo(firstId));
            Assert.That(state.OwnedEffects[1], Is.EqualTo(secondId));
        }

        [Test]
        public void CreateOffer_NeverOffersAnAlreadyOwnedUniqueEffect()
        {
            config.Economy.SettlementReward.BuffWeight = 0;
            config.Economy.SettlementReward.ActiveSkillWeight = 100;
            var streams = new RngStreams(0xD1000010u);
            RunState state = CreateState(streams, "skill_reinforce");
            var system = new SettlementRewardSystem(config, state, streams, new MockAdService());

            SettlementOffer offer = system.CreateOffer();

            Assert.That(offer.Cards.All(card => card.Kind == SettlementRewardKind.ActiveSkill), Is.True);
            Assert.That(offer.Cards.Select(card => card.EffectId), Does.Not.Contain("skill_reinforce"));
        }

        [Test]
        public void CreateOffer_StackableOwnedBuffRemainsEligibleAndCanAccumulate()
        {
            config.Economy.SettlementReward.BuffWeight = 100;
            config.Economy.SettlementReward.ActiveSkillWeight = 0;
            config.Economy.SettlementReward.BuffEffectIds = new[] { "buff_atk_up" };
            var streams = new RngStreams(0xD1000011u);
            RunState state = CreateState(streams, "buff_atk_up");
            var system = new SettlementRewardSystem(config, state, streams, new MockAdService());

            SettlementOffer offer = system.CreateOffer();
            system.Select(0);

            Assert.That(offer.Cards.All(card => card.EffectId == "buff_atk_up"), Is.True);
            Assert.That(state.OwnedEffects.Count(value => value == "buff_atk_up"), Is.EqualTo(2));
        }

        [Test]
        public void CreateOffer_SecondCallIsRejectedWithoutAdvancingRandomState()
        {
            SettlementRewardSystem system = CreateSystem(0xD1000012u, out _, out RngStreams streams);
            system.CreateOffer();
            RngStreamsState before = streams.SaveState();

            Assert.Throws<InvalidOperationException>(() => system.CreateOffer());

            AssertStreamsEqual(before, streams.SaveState());
        }

        private SettlementRewardSystem CreateSystem(
            uint seed,
            out RunState state,
            out RngStreams streams,
            IAdService adService = null)
        {
            streams = new RngStreams(seed);
            state = CreateState(streams);
            return new SettlementRewardSystem(config, state, streams, adService ?? new MockAdService());
        }

        private static RunState CreateState(RngStreams streams, params string[] ownedEffects)
        {
            return new RunState
            {
                Seed = streams.MasterSeed,
                StageIndex = 1,
                OwnedEffects = ownedEffects ?? Array.Empty<string>(),
                RngStreamsState = streams.SaveState()
            };
        }

        private static void AssertOffersEqual(SettlementOffer first, SettlementOffer second)
        {
            Assert.That(second.Cards, Has.Count.EqualTo(first.Cards.Count));
            for (int index = 0; index < first.Cards.Count; index++)
            {
                Assert.That(second.Cards[index].EffectId, Is.EqualTo(first.Cards[index].EffectId));
                Assert.That(second.Cards[index].Kind, Is.EqualTo(first.Cards[index].Kind));
            }
        }

        private static void AssertStreamsEqual(RngStreamsState expected, RngStreamsState actual)
        {
            Assert.That(actual.battle.state, Is.EqualTo(expected.battle.state));
            Assert.That(actual.cardDraw.state, Is.EqualTo(expected.cardDraw.state));
            Assert.That(actual.settlement.state, Is.EqualTo(expected.settlement.state));
        }

        private sealed class RecordingAdService : IAdService
        {
            private readonly bool result;

            public RecordingAdService(bool result)
            {
                this.result = result;
            }

            public int CallCount { get; private set; }

            public bool ShowRewarded()
            {
                CallCount++;
                return result;
            }
        }
    }
}
