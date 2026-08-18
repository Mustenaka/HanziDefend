using System;
using System.Collections.Generic;
using HanziDefend.Data;
using UnityEngine;

namespace HanziDefend.Gameplay.Battle
{
    /// <summary>One deployment cell's barracks state, as the view sees it.</summary>
    public readonly struct DeploymentSlotSnapshot
    {
        internal DeploymentSlotSnapshot(
            int ticket,
            string definitionId,
            int level,
            Vector2 position,
            double nextSpawnTimeSeconds,
            float remainingSeconds,
            int liveCount,
            int liveCap,
            bool isHeldAtCap)
        {
            Ticket = ticket;
            DefinitionId = definitionId;
            Level = level;
            Position = position;
            NextSpawnTimeSeconds = nextSpawnTimeSeconds;
            RemainingSeconds = remainingSeconds;
            LiveCount = liveCount;
            LiveCap = liveCap;
            IsHeldAtCap = isHeldAtCap;
        }

        public int Ticket { get; }
        public string DefinitionId { get; }
        public int Level { get; }
        public Vector2 Position { get; }
        public double NextSpawnTimeSeconds { get; }
        public float RemainingSeconds { get; }
        public int LiveCount { get; }
        public int LiveCap { get; }

        /// <summary>True while the cell is full and its cooldown is therefore paused.</summary>
        public bool IsHeldAtCap { get; }
    }

    /// <summary>Outcome of registering a deployment cell as a barracks.</summary>
    public readonly struct DeploymentEntry
    {
        internal DeploymentEntry(bool isOnField, int entityId, int ticket, double entryTimeSeconds)
        {
            IsOnField = isOnField;
            EntityId = entityId;
            Ticket = ticket;
            EntryTimeSeconds = entryTimeSeconds;
        }

        /// <summary>True when the first unit walked on at once, i.e. <c>spawnMode: Instant</c>.</summary>
        public bool IsOnField { get; }

        /// <summary>Entity id of the first unit, or zero while it is still on cooldown.</summary>
        public int EntityId { get; }

        /// <summary>Run-local id of the cell's barracks.</summary>
        public int Ticket { get; }

        /// <summary>When this cell first puts a unit on the field.</summary>
        public double EntryTimeSeconds { get; }
    }

    public sealed partial class BattleSystem
    {
        private readonly List<DeploymentSlot> deploymentSlots = new List<DeploymentSlot>();
        private readonly Dictionary<int, DeploymentSlot> slotByLiveEntityId =
            new Dictionary<int, DeploymentSlot>();
        private readonly List<string> persistentAllyEffectIds = new List<string>();
        private int nextDeploymentTicket = 1;

        /// <summary>Deployment cells that are producing units.</summary>
        public int DeploymentSlotCount => deploymentSlots.Count;

        /// <summary>Cells whose first unit has not walked on yet.</summary>
        public int PendingDeploymentCount
        {
            get
            {
                int total = 0;
                for (int index = 0; index < deploymentSlots.Count; index++)
                {
                    if (!deploymentSlots[index].HasProduced)
                    {
                        total++;
                    }
                }

                return total;
            }
        }

        /// <summary>Ally units alive on the field right now.</summary>
        public int AllyFieldUnitCount => aliveAllyUnits.Count;

        /// <summary>
        /// Registers a deployment cell as a barracks: it puts its first unit on the field after that
        /// unit's own <c>cooldown</c>, and then <b>keeps producing on the same cooldown whether or
        /// not the previous one is still alive</b>.
        ///
        /// <para>The upstream brief asks for spawning "according to deployment and cooldown", and
        /// describes the intended feel as bullet-hell with units dying easily. M1-00 §3.1 compressed
        /// that into "Delayed spawns when its cooldown expires", which reads as one-shot, and
        /// WO-F1 §B implemented the compressed sentence. A cell is a barracks, not one soldier.</para>
        ///
        /// <para>A cell keeps at most <c>economy.deployment.liveCapByFootprintCells</c> units alive
        /// at once. Without that cap the units that never die because they stand at the back stack
        /// without bound; with it, a unit capped at one makes cooldown-driven and death-driven
        /// respawn the same behaviour — which is why the first delay and the repeat interval share
        /// one number rather than needing a second field.</para>
        ///
        /// <para>Units not yet produced do not exist on the battlefield in any sense: no space, no
        /// targeting, no area damage, no settlement (WO-F1 §B, unchanged).</para>
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
            float interval = ResolveEntryDelay(definition, request.Level);
            int ticket = nextDeploymentTicket++;
            var slot = new DeploymentSlot(
                ticket,
                definition,
                request.Level,
                request.Position,
                request.RewardRank,
                interval,
                ResolveLiveCap(definition));
            deploymentSlots.Add(slot);

            if (interval <= 0f)
            {
                // Instant units are on the field now; the cell is still a barracks afterwards.
                int immediateId = ProduceFrom(slot);
                return new DeploymentEntry(true, immediateId, ticket, SimulatedTimeSeconds);
            }

            slot.NextSpawnTime = SimulatedTimeSeconds + interval;
            deploymentEvents.AllyDeploymentQueued(new AllyDeploymentQueuedEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                ticket,
                definition.Id,
                request.Level,
                request.Position,
                interval,
                slot.NextSpawnTime));
            return new DeploymentEntry(false, 0, ticket, slot.NextSpawnTime);
        }

        /// <summary>
        /// Registers a run-carried buff so it also reaches units produced later. Applied once
        /// immediately, then to each new arrival restricted to itself, so a Stack-rule buff cannot
        /// pile up on the units already standing there.
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

        public IReadOnlyList<DeploymentSlotSnapshot> CaptureDeploymentSlots()
        {
            ThrowIfDisposed();
            var result = new DeploymentSlotSnapshot[deploymentSlots.Count];
            for (int index = 0; index < deploymentSlots.Count; index++)
            {
                DeploymentSlot slot = deploymentSlots[index];
                result[index] = new DeploymentSlotSnapshot(
                    slot.Ticket,
                    slot.Definition.Id,
                    slot.Level,
                    slot.Position,
                    slot.NextSpawnTime,
                    (float)Math.Max(0d, slot.NextSpawnTime - SimulatedTimeSeconds),
                    slot.LiveCount,
                    slot.LiveCap,
                    slot.LiveCount >= slot.LiveCap);
            }

            return result;
        }

        /// <summary>
        /// Interval between productions, which is also the first unit's delay. <c>Instant</c> is
        /// zero; <c>Delayed</c> reads the unit's own <c>cooldown</c> curve — the ladder already in
        /// units.json. No new field.
        /// </summary>
        internal static float ResolveEntryDelay(UnitDef definition, int level)
        {
            if (definition.SpawnMode != UnitSpawnMode.Delayed)
            {
                return 0f;
            }

            return Formula.StatAtLevel(definition.Cooldown.Base, definition.Cooldown.Growth, level);
        }

        private int ResolveLiveCap(UnitDef definition)
        {
            DeploymentRulesDef deployment = config.Economy.Deployment;
            int cells = HanziDefend.Gameplay.Deploy.UnitFootprint.FromDefinition(definition).OccupiedCellCount;
            return Math.Max(1, deployment == null ? 1 : deployment.LiveCapForUnit(definition.Id, cells));
        }

        /// <summary>
        /// Runs every barracks. Called at the head of a tick, before the wave scheduler, so an ally
        /// and an enemy scheduled for the same instant meet in the same tick rather than one apart.
        ///
        /// <para><b>A full cell pauses its cooldown rather than letting it free-run.</b> A
        /// free-running timer banks charges while the cell is full and dumps them the instant one
        /// unit dies, which reads as "nothing for twenty seconds, then four at once" and spikes
        /// straight into the field limit. Pausing means a freed cell resumes its normal rhythm from
        /// the moment it frees. Recorded in DECISIONS.md.</para>
        /// </summary>
        private void ReleaseDueDeployments()
        {
            if (deploymentSlots.Count == 0)
            {
                return;
            }

            int fieldLimit = config.Economy.Deployment == null
                ? 0
                : config.Economy.Deployment.AllyFieldUnitLimit;
            for (int index = 0; index < deploymentSlots.Count; index++)
            {
                DeploymentSlot slot = deploymentSlots[index];

                // At most one production per cell per tick: a cooldown shorter than a tick cannot
                // become an unbounded inner loop, and the field limit stays enforceable.
                if (slot.Interval <= 0f || slot.NextSpawnTime > SimulatedTimeSeconds)
                {
                    continue;
                }

                if (slot.LiveCount >= slot.LiveCap)
                {
                    continue;
                }

                if (fieldLimit > 0 && aliveAllyUnits.Count >= fieldLimit)
                {
                    continue;
                }

                ProduceFrom(slot);
                slot.NextSpawnTime = SimulatedTimeSeconds + slot.Interval;
            }
        }

        private int ProduceFrom(DeploymentSlot slot)
        {
            int entityId = SpawnUnit(
                slot.Definition,
                slot.Level,
                slot.Position,
                slot.RewardRank,
                null,
                null);
            slot.LiveCount++;
            slot.HasProduced = true;
            slotByLiveEntityId[entityId] = slot;
            ApplyPersistentAllyEffects(entityId);
            deploymentEvents.AllyDeploymentEntered(new AllyDeploymentEnteredEvent(
                NextEventSequence(),
                TickIndex,
                SimulatedTimeSeconds,
                slot.Ticket,
                entityId,
                slot.Definition.Id,
                slot.Level,
                slot.Position,
                slot.NextSpawnTime));
            return entityId;
        }

        /// <summary>Frees a cell's headcount when one of its units dies.</summary>
        private void ReleaseDeploymentSlotFor(int entityId)
        {
            if (!slotByLiveEntityId.TryGetValue(entityId, out DeploymentSlot slot))
            {
                return;
            }

            slotByLiveEntityId.Remove(entityId);
            if (slot.LiveCount > 0)
            {
                slot.LiveCount--;
            }

            // A cell held at capacity carries a stale deadline. Restart the cooldown here so a death
            // is followed by one full interval rather than by an instant free replacement.
            if (slot.Interval > 0f && slot.NextSpawnTime <= SimulatedTimeSeconds)
            {
                slot.NextSpawnTime = SimulatedTimeSeconds + slot.Interval;
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

        private sealed class DeploymentSlot
        {
            internal DeploymentSlot(
                int ticket,
                UnitDef definition,
                int level,
                Vector2 position,
                EnemyRank rewardRank,
                float interval,
                int liveCap)
            {
                Ticket = ticket;
                Definition = definition;
                Level = level;
                Position = position;
                RewardRank = rewardRank;
                Interval = interval;
                LiveCap = liveCap;
            }

            internal int Ticket { get; }
            internal UnitDef Definition { get; }
            internal int Level { get; }
            internal Vector2 Position { get; }
            internal EnemyRank RewardRank { get; }
            internal float Interval { get; }
            internal int LiveCap { get; }
            internal double NextSpawnTime { get; set; }
            internal int LiveCount { get; set; }
            internal bool HasProduced { get; set; }
        }
    }
}
