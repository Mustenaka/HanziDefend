using System;
using HanziDefend.Gameplay.Battle;

namespace HanziDefend.View.Feedback
{
    /// <summary>Forwards gameplay events to the existing BattleView first, then to feedback.</summary>
    public sealed class FeedbackEventRouter : IBattleEncounterEvents, IBattleEffectEvents
    {
        private readonly IBattleEvents primary;
        private readonly IBattleEncounterEvents primaryEncounter;
        private readonly IBattleEffectEvents primaryEffects;
        private readonly FeedbackPresenter feedback;

        public FeedbackEventRouter(IBattleEvents primaryEvents, FeedbackPresenter feedbackPresenter)
        {
            primary = primaryEvents ?? throw new ArgumentNullException(nameof(primaryEvents));
            primaryEncounter = primaryEvents as IBattleEncounterEvents ?? NullBattleEvents.Instance;
            primaryEffects = primaryEvents as IBattleEffectEvents ?? NullBattleEffectEvents.Instance;
            feedback = feedbackPresenter ?? throw new ArgumentNullException(nameof(feedbackPresenter));
        }

        public void UnitSpawned(UnitSpawnedEvent eventData)
        {
            primary.UnitSpawned(eventData);
            feedback.UnitSpawned(eventData);
        }

        public void UnitAttacked(UnitAttackedEvent eventData)
        {
            primary.UnitAttacked(eventData);
            feedback.UnitAttacked(eventData);
        }

        public void DamageDealt(DamageDealtEvent eventData)
        {
            primary.DamageDealt(eventData);
            feedback.DamageDealt(eventData);
        }

        public void UnitDied(UnitDiedEvent eventData)
        {
            primary.UnitDied(eventData);
            feedback.UnitDied(eventData);
        }

        public void CoinDropped(CoinDroppedEvent eventData)
        {
            primary.CoinDropped(eventData);
            feedback.CoinDropped(eventData);
        }

        public void WaveStarted(WaveStartedEvent eventData)
        {
            primaryEncounter.WaveStarted(eventData);
            feedback.WaveStarted(eventData);
        }

        public void BossSpawned(BossSpawnedEvent eventData)
        {
            primaryEncounter.BossSpawned(eventData);
            feedback.BossSpawned(eventData);
        }

        public void BaseDamaged(BaseDamagedEvent eventData)
        {
            primaryEncounter.BaseDamaged(eventData);
            feedback.BaseDamaged(eventData);
        }

        public void BattleSettled(BattleSettledEvent eventData)
        {
            primaryEncounter.BattleSettled(eventData);
            feedback.BattleSettled(eventData);
        }

        public void EffectApplied(EffectAppliedEvent eventData)
        {
            primaryEffects.EffectApplied(eventData);
            feedback.EffectApplied(eventData);
        }

        public void EffectExpired(EffectExpiredEvent eventData)
        {
            primaryEffects.EffectExpired(eventData);
            feedback.EffectExpired(eventData);
        }

        public void UnitHealed(UnitHealedEvent eventData)
        {
            primaryEffects.UnitHealed(eventData);
            feedback.UnitHealed(eventData);
        }

        public void ShieldChanged(ShieldChangedEvent eventData)
        {
            primaryEffects.ShieldChanged(eventData);
            feedback.ShieldChanged(eventData);
        }

        public void CoinsModified(CoinsModifiedEvent eventData)
        {
            primaryEffects.CoinsModified(eventData);
            feedback.CoinsModified(eventData);
        }
    }
}
