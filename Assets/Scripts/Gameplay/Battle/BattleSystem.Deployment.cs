using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    /// <summary>One deployed ally that has not entered the field yet, as the view sees it.</summary>
    public readonly struct PendingDeploymentSnapshot
    {
        internal PendingDeploymentSnapshot(
            int ticket,
            string definitionId,
            int level,
            Vector2 position,
            double entryTimeSeconds,
            float remainingSeconds)
        {
            Ticket = ticket;
            DefinitionId = definitionId;
            Level = level;
            Position = position;
            EntryTimeSeconds = entryTimeSeconds;
            RemainingSeconds = remainingSeconds;
        }

        public int Ticket { get; }
        public string DefinitionId { get; }
        public int Level { get; }
        public Vector2 Position { get; }
        public double EntryTimeSeconds { get; }
        public float RemainingSeconds { get; }
    }

    /// <summary>Outcome of a deployment: either already on the field, or queued behind a cooldown.</summary>
    public readonly struct DeploymentEntry
    {
        internal DeploymentEntry(bool isOnField, int entityId, int ticket, double entryTimeSeconds)
        {
            IsOnField = isOnField;
            EntityId = entityId;
            Ticket = ticket;
            EntryTimeSeconds = entryTimeSeconds;
        }

        /// <summary>True when the unit spawned immediately, i.e. <c>spawnMode: Instant</c>.</summary>
        public bool IsOnField { get; }

        /// <summary>The battle entity id, or zero while the deployment is still queued.</summary>
        public int EntityId { get; }

        /// <summary>Pending-slot id, or zero when the unit entered immediately.</summary>
        public int Ticket { get; }

        public double EntryTimeSeconds { get; }
    }

    public sealed partial class BattleSystem
    {
        private readonly List<PendingDeployment> pendingDeployments = new List<PendingDeployment>();
        private readonly List<string> persistentAllyEffectIds = new List<string>();
        private int nextDeploymentTicket = 1;

        /// <summary>Deployed allies still waiting out their entry cooldown.</summary>
        public int PendingDeploymentCount => pendingDeployments.Count;

        /// <summary>
        /// Puts a deployed ally onto the encounter timeline according to its <c>spawnMode</c>
        /// (M1-00 §3.1): <c>Instant</c> walks on at once, <c>Delayed</c> waits out its own
        /// <c>cooldown</c> seconds first.
        ///
        /// <para>A queued unit does not exist on the battlefield in any sense — it occupies no
        /// space, is not a target, takes no area damage and does not count towards settlement.
        /// That is the whole point: without it the twelve units of a real lineup all press forward
        /// at t=0 as one wall and the front line forms at the enemy spawn door.</para>
        ///
        /// <para><see cref="Spawn"/> stays the unconditional primitive that puts a unit on the
        /// field right now; it is what waves, death spawns and tests use.</para>
        /// </summary>
        public DeploymentEntry Deploy(UnitSpawnRequest request)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            if (string.IsNullOrWhiteSpace(request.DefinitionId))
            {
                throw new ArgumentException("Definition id is required.", nameof(request));
            }

            UnitDef definition = config.GetUnit(request.DefinitionId);
            float delay = ResolveEntryDelay(definition, request.Level);
            if (delay <= 0f)
            {
                int immediateId = SpawnUnit(
                    definition,
                    request.Level,
                    request.Position,
                    request.RewardRank,
                    null,
                    null);
                return new DeploymentEntry(true, immediateId, 0, SimulatedTimeSeconds);
            }

            int ticket = nextDeploymentTicket++;
            double entryTime = SimulatedTimeSeconds + delay;
            var pending = new PendingDeployment(
                ticket,
                definition,
                request.Level,
                request.Position,
                request.RewardRank,
                entryTime);
            InsertPending(pending);
            deploymentEvents.AllyDeploymentQueued(new AllyDeploymentQueuedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                ticket,
                definition.Id,
                request.Level,
                request.Position,
                delay,
                entryTime));
            return new DeploymentEntry(false, 0, ticket, entryTime);
        }

        /// <summary>
        /// Registers a run-carried buff so it also reaches allies that enter later. The effect is
        /// applied once immediately, exactly as a direct <see cref="ApplyEffect"/> would; every
        /// later arrival then receives it restricted to itself, so a Stack-rule buff cannot pile up
        /// on the units that were already standing there.
        /// </summary>
        public EffectExecutionResult RegisterPersistentAllyEffect(string effectId)
        {
            ThrowIfDisposed();
            ThrowIfSettled();
            EffectDef definition = config.GetEffect(effectId);
            persistentAllyEffectIds.Add(definition.Id);
            Position2Def origin = rules.AllyBasePosition;
            return ApplyEffect(definition.Id, new EffectExecutionContext(
                null,
                BattleTeam.Ally,
                new Vector2(origin.X, origin.Y),
                1));
        }

        public IReadOnlyList<PendingDeploymentSnapshot> CapturePendingDeployments()
        {
            ThrowIfDisposed();
            var result = new PendingDeploymentSnapshot[pendingDeployments.Count];
            for (int index = 0; index < pendingDeployments.Count; index++)
            {
                PendingDeployment pending = pendingDeployments[index];
                result[index] = new PendingDeploymentSnapshot(
                    pending.Ticket,
                    pending.Definition.Id,
                    pending.Level,
                    pending.Position,
                    pending.EntryTimeSeconds,
                    (float)Math.Max(0d, pending.EntryTimeSeconds - SimulatedTimeSeconds));
            }

            return result;
        }

        /// <summary>
        /// Entry delay in seconds. <c>Instant</c> is zero; <c>Delayed</c> reads the unit's own
        /// <c>cooldown</c> curve — the ladder already in units.json (卒 1.2 … 冲车 9.0). No new field.
        /// </summary>
        internal static float ResolveEntryDelay(UnitDef definition, int level)
        {
            if (definition.SpawnMode != UnitSpawnMode.Delayed)
            {
                return 0f;
            }

            return Formula.StatAtLevel(definition.Cooldown.Base, definition.Cooldown.Growth, level);
        }

        /// <summary>
        /// Releases every deployment whose entry time has arrived. Runs at the head of a tick, before
        /// the wave scheduler, so an ally and an enemy scheduled for the same instant meet in the
        /// same tick rather than a tick apart.
        /// </summary>
        private void ReleaseDueDeployments()
        {
            if (pendingDeployments.Count == 0)
            {
                return;
            }

            int released = 0;
            while (released < pendingDeployments.Count
                   && pendingDeployments[released].EntryTimeSeconds <= SimulatedTimeSeconds)
            {
                released++;
            }

            if (released == 0)
            {
                return;
            }

            var due = new PendingDeployment[released];
            pendingDeployments.CopyTo(0, due, 0, released);
            pendingDeployments.RemoveRange(0, released);
            for (int index = 0; index < due.Length; index++)
            {
                PendingDeployment pending = due[index];
                int entityId = SpawnUnit(
                    pending.Definition,
                    pending.Level,
                    pending.Position,
                    pending.RewardRank,
                    null,
                    null);
                ApplyPersistentAllyEffects(entityId);
                deploymentEvents.AllyDeploymentEntered(new AllyDeploymentEnteredEvent(
                    NextEventSequence(),
                    TickIndex,
                    SimulatedTimeSeconds,
                    pending.Ticket,
                    entityId,
                    pending.Definition.Id,
                    pending.Level,
                    pending.Position,
                    pending.EntryTimeSeconds));
            }
        }

        private void ApplyPersistentAllyEffects(int entityId)
        {
            if (persistentAllyEffectIds.Count == 0
                || !unitsById.TryGetValue(entityId, out BattleUnit unit))
            {
                return;
            }

            for (int index = 0; index < persistentAllyEffectIds.Count; index++)
            {
                ExecuteEffect(
                    config.GetEffect(persistentAllyEffectIds[index]),
                    new EffectExecutionContext(
                        null,
                        BattleTeam.Ally,
                        unit.Position,
                        unit.Level,
                        null,
                        entityId));
            }
        }

        /// <summary>Keeps the queue ordered by entry time, then by ticket, so releases are deterministic.</summary>
        private void InsertPending(PendingDeployment pending)
        {
            int index = pendingDeployments.Count;
            while (index > 0 && IsLaterThan(pendingDeployments[index - 1], pending))
            {
                index--;
            }

            pendingDeployments.Insert(index, pending);
        }

        private static bool IsLaterThan(PendingDeployment left, PendingDeployment right)
        {
            if (left.EntryTimeSeconds != right.EntryTimeSeconds)
            {
                return left.EntryTimeSeconds > right.EntryTimeSeconds;
            }

            return left.Ticket > right.Ticket;
        }

        private readonly struct PendingDeployment
        {
            internal PendingDeployment(
                int ticket,
                UnitDef definition,
                int level,
                Vector2 position,
                EnemyRank rewardRank,
                double entryTimeSeconds)
            {
                Ticket = ticket;
                Definition = definition;
                Level = level;
                Position = position;
                RewardRank = rewardRank;
                EntryTimeSeconds = entryTimeSeconds;
            }

            internal int Ticket { get; }
            internal UnitDef Definition { get; }
            internal int Level { get; }
            internal Vector2 Position { get; }
            internal EnemyRank RewardRank { get; }
            internal double EntryTimeSeconds { get; }
        }
    }
}
