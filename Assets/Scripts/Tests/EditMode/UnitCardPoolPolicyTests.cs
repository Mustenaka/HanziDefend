using System.Collections.Generic;
using System.Linq;
using HanziDefend.Data;
using HanziDefend.Gameplay.Deploy;
using NUnit.Framework;

namespace HanziDefend.Tests.EditMode
{
    public sealed class UnitCardPoolPolicyTests
    {
        /// <summary>Units offerable while the unlocked area is one cell wide: every 1-wide shape.</summary>
        private static readonly string[] NarrowPoolIds =
        {
            "bing", "gong", "huo", "mao", "nub", "zu"
        };

        private static readonly string[] FullPoolIds =
        {
            "bing", "chc", "dao", "dun", "gong", "huo", "lia", "mao", "nub", "nuc",
            "qqi", "tie", "zqi", "zu"
        };

        [Test]
        public void GetEligibleUnits_OneWideUnlockedArea_ReturnsOnlyOneWideFootprints()
        {
            string[] actual = UnitCardPoolPolicy.GetEligibleUnits(GameConfig.Load(), NarrowGrid())
                .Select(value => value.Id)
                .OrderBy(value => value, System.StringComparer.Ordinal)
                .ToArray();

            Assert.That(actual, Is.EqualTo(NarrowPoolIds));
        }

        [Test]
        public void GetEligibleUnits_StartingCentreMask_ReturnsAllSharedPlayerUnits()
        {
            string[] actual = UnitCardPoolPolicy.GetEligibleUnits(GameConfig.Load(), StartingGrid())
                .Select(value => value.Id)
                .OrderBy(value => value, System.StringComparer.Ordinal)
                .ToArray();

            Assert.That(actual, Is.EqualTo(FullPoolIds));
        }

        [Test]
        public void GetEligibleUnits_ExcludesEnemyExclusiveUnits()
        {
            GameConfig config = GameConfig.Load();
            UnitDef[] actual = UnitCardPoolPolicy.GetEligibleUnits(config, StartingGrid()).ToArray();
            string[] ids = actual.Select(value => value.Id).ToArray();

            Assert.That(actual.All(value => value.Faction == UnitFaction.Ally), Is.True);
            Assert.That(ids, Does.Not.Contain("e_lang"));
            Assert.That(ids, Does.Not.Contain("e_liu"));
            Assert.That(ids, Does.Not.Contain("e_shan"));
        }

        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void TryDrawGuaranteedCard_StagesBeforePreBossStageDoNotTrigger(int stageIndex)
        {
            GameConfig config = GameConfig.Load();
            var runState = new RunState { StageIndex = stageIndex };

            bool drawn = UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, runState, StartingGrid(), new Rng(123u), out UnitDef unit);

            Assert.That(drawn, Is.False);
            Assert.That(unit, Is.Null);
            Assert.That(runState.HasOfferedGuaranteedMachineryCard, Is.False);
        }

        [Test]
        public void TryDrawGuaranteedCard_StageFourNarrowMaskForcesNubAndRecordsOffer()
        {
            GameConfig config = GameConfig.Load();
            var runState = new RunState { StageIndex = 4 };

            bool drawn = UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, runState, NarrowGrid(), new Rng(123u), out UnitDef unit);

            Assert.That(drawn, Is.True);
            Assert.That(unit.Id, Is.EqualTo("nub"), "nub is the only 1-wide siege unit");
            Assert.That(unit.AtkType, Is.EqualTo(AttackType.Siege));
            Assert.That(runState.HasOfferedGuaranteedMachineryCard, Is.True);
        }

        [Test]
        public void TryDrawGuaranteedCard_MaskTooSmallForAnySiegeShapeWaitsInsteadOfThrowing()
        {
            GameConfig config = GameConfig.Load();
            var runState = new RunState { StageIndex = 4 };
            // A single unlocked cell holds no 1x2 and no 2x2, so no siege footprint fits at all.
            var singleCell = new DeploymentGrid(
                7, 7, new[] { new GridCoordinate(3, 3) },
                DeploymentGridOrientation.ColumnsHorizontal,
                config.Economy.CardPool);

            bool drawn = UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, runState, singleCell, new Rng(123u), out UnitDef unit);

            Assert.That(drawn, Is.False);
            Assert.That(unit, Is.Null);
            Assert.That(runState.HasOfferedGuaranteedMachineryCard, Is.False);
        }

        [Test]
        public void TryDrawGuaranteedCard_PreviouslyOfferedMachineryDoesNotTriggerAgain()
        {
            GameConfig config = GameConfig.Load();
            var runState = new RunState { StageIndex = 4 };
            UnitCardPoolPolicy.RecordOfferedUnit(config, runState, config.GetUnit("nub"));
            var rng = new Rng(123u);
            uint before = rng.State;

            bool drawn = UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, runState, StartingGrid(), rng, out UnitDef unit);

            Assert.That(drawn, Is.False);
            Assert.That(unit, Is.Null);
            Assert.That(rng.State, Is.EqualTo(before));
        }

        [Test]
        public void TryDrawGuaranteedCard_WideMaskSameSeedSelectsSameEligibleMachinery()
        {
            GameConfig config = GameConfig.Load();
            var firstState = new RunState { StageIndex = 4 };
            var secondState = new RunState { StageIndex = 4 };

            Assert.That(UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, firstState, StartingGrid(), new Rng(0xC0FFEEu), out UnitDef first), Is.True);
            Assert.That(UnitCardPoolPolicy.TryDrawGuaranteedCard(
                config, secondState, StartingGrid(), new Rng(0xC0FFEEu), out UnitDef second), Is.True);

            Assert.That(first.Id, Is.EqualTo(second.Id));
            Assert.That(new[] { "nub", "nuc", "chc" }, Does.Contain(first.Id));
            Assert.That(first.AtkType, Is.EqualTo(AttackType.Siege));
            Assert.That(firstState.HasOfferedGuaranteedMachineryCard, Is.True);
            Assert.That(secondState.HasOfferedGuaranteedMachineryCard, Is.True);
        }

        [Test]
        public void RunState_JsonRoundTripPreservesGuaranteeUnlockMaskAndAllRngStreams()
        {
            var streams = new RngStreams(0x12345678u);
            streams.Battle.NextUInt();
            streams.CardDraw.NextUInt();
            streams.Settlement.NextUInt();
            bool[] mask = StartingGrid().SaveUnlockMask();
            var before = new RunState
            {
                StageIndex = 4,
                GridWidth = 7,
                GridHeight = 7,
                UnlockedCells = mask,
                UnlockPurchaseCount = 2,
                DeployedGrid = new[]
                {
                    new DeployedUnitState
                    {
                        DeploymentId = "deployment-7",
                        UnitId = "gong",
                        Level = 2,
                        Col = 3,
                        Row = 3
                    }
                },
                Seed = streams.MasterSeed,
                HasOfferedGuaranteedMachineryCard = true,
                RngStreamsState = streams.SaveState(),
                RngState = streams.Battle.SaveState()
            };

            RunState after = JsonCodec.Deserialize<RunState>(JsonCodec.Serialize(before));

            Assert.That(after.HasOfferedGuaranteedMachineryCard, Is.True);
            Assert.That(after.GridWidth, Is.EqualTo(7));
            Assert.That(after.GridHeight, Is.EqualTo(7));
            Assert.That(after.UnlockedCells, Is.EqualTo(mask));
            Assert.That(after.UnlockedCells.Count(value => value), Is.EqualTo(9));
            Assert.That(after.UnlockPurchaseCount, Is.EqualTo(2));
            Assert.That(after.DeployedGrid.Single().DeploymentId, Is.EqualTo("deployment-7"));
            Assert.That(after.DeployedGrid.Single().UnitId, Is.EqualTo("gong"));
            Assert.That(after.RngStreamsState.battle.state, Is.EqualTo(before.RngStreamsState.battle.state));
            Assert.That(after.RngStreamsState.cardDraw.state, Is.EqualTo(before.RngStreamsState.cardDraw.state));
            Assert.That(after.RngStreamsState.settlement.state, Is.EqualTo(before.RngStreamsState.settlement.state));
            Assert.That(after.RngState.state, Is.EqualTo(before.RngState.state), "legacy state");
        }

        private static DeploymentGrid StartingGrid()
        {
            return DeploymentGrid.CreateFromConfig(GameConfig.Load(), "level_1_1");
        }

        /// <summary>A one-cell-wide unlocked strip; the narrowest mask a real run can hold.</summary>
        private static DeploymentGrid NarrowGrid()
        {
            GameConfig config = GameConfig.Load();
            var cells = new List<GridCoordinate>
            {
                new GridCoordinate(3, 2),
                new GridCoordinate(3, 3),
                new GridCoordinate(3, 4)
            };
            return new DeploymentGrid(
                7, 7, cells, DeploymentGridOrientation.ColumnsHorizontal, config.Economy.CardPool);
        }
    }
}
