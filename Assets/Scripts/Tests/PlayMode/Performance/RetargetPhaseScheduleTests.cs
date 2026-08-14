using HanziDefend.Gameplay.Performance;
using NUnit.Framework;

namespace HanziDefend.Tests.PlayMode.Performance
{
    [TestFixture]
    public sealed class RetargetPhaseScheduleTests
    {
        [Test]
        public void ResolvePhaseCount_UsesOneSlotPerFixedTick()
        {
            Assert.That(
                RetargetPhaseSchedule.ResolvePhaseCount(0.2d, 1d / 30d),
                Is.EqualTo(6));
        }

        [Test]
        public void Slots_AreDeterministicAndEvenlyDistributed()
        {
            const int phaseCount = 6;
            var counts = new int[phaseCount];
            for (int entityId = 1; entityId <= 12; entityId++)
            {
                counts[RetargetPhaseSchedule.GetPhaseSlot(entityId, phaseCount)]++;
            }

            Assert.That(counts, Is.All.EqualTo(2));
            Assert.That(
                RetargetPhaseSchedule.GetPhaseSlot(7, phaseCount),
                Is.EqualTo(1));
        }

        [Test]
        public void NextDeadline_AddsEntityPhaseWithoutRngInput()
        {
            const double now = 10d;
            const double interval = 0.2d;
            const int phaseCount = 5;

            double deadline = RetargetPhaseSchedule.GetNextDeadline(
                now,
                7,
                interval,
                phaseCount);

            Assert.That(deadline, Is.EqualTo(10.28d).Within(0.000000001d));
        }
    }
}
