using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;
using static WarCore.Tests.DashT.DashTWorlds;

namespace WarCore.Tests.DashT
{
    // DASH-T acceptance 6: the busy hook. A busy unit's request is refused and writes nothing; a dash already running is not
    // asked about. Unbound, nobody is busy (DashTFlatLockTests and every M7a test run unbound).
    public sealed class DashTBusyTests
    {
        private sealed class Busy : IUnitBusy
        {
            internal readonly HashSet<EntityId> Units = new HashSet<EntityId>();
            internal int Asked;
            public bool IsBusy(EntityId unit) { Asked++; return Units.Contains(unit); }
        }

        private static IEnumerable<SimSession> Worlds()
        {
            yield return World(Map(), new[] { Dasher(1, 10500, 4500), Dasher(2, 10500, 1500) });
            yield return DashWorlds.World(new[] { DashWorlds.Dasher(1, 40500, 12500), DashWorlds.Dasher(2, 40500, 6500) }, DashWorlds.Map());
        }

        [Test]
        public void ABusyUnitIsRefusedAndNothingIsWritten()
        {
            foreach (SimSession s in Worlds())
            {
                var rule = new Busy();
                DashWorlds.Loco(s).BindBusy(rule);
                rule.Units.Add(M3TestWorld.Unit(s, 1));
                s.StepOnce();
                byte[] before = M3TestWorld.Sim(s).SaveRetainedSnapshot();
                ulong hash = s.StateHash;
                Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.False, "unit 1 is busy");
                Assert.That(rule.Asked, Is.EqualTo(1));
                Assert.That(M3TestWorld.Sim(s).SaveRetainedSnapshot(), Is.EqualTo(before), "a refused request writes nothing");
                Assert.That(s.StateHash, Is.EqualTo(hash));
                Assert.That(DashWorlds.State(s), Is.EqualTo(DashState.Initial));
                Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX, 2), Is.True, "unit 2 is not busy");
                rule.Units.Clear();
                Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True, "unit 1 is free again");
            }
        }

        // The busy rule is asked when a dash is requested, not on its steps: a unit that becomes busy mid-dash finishes it.
        [Test]
        public void ARunningDashIsNotAsked()
        {
            SimSession s = World(Map(), new[] { Dasher(1, 10500, 4500) });
            var rule = new Busy();
            DashWorlds.Loco(s).BindBusy(rule);
            Assert.That(DashWorlds.Dash(s, DashWorlds.PlusX), Is.True);
            rule.Units.Add(M3TestWorld.Unit(s));
            int asked = rule.Asked;
            List<SimVec3> track = DashWorlds.Track(s, 6);
            Assert.That(track[6].X.Raw - track[0].X.Raw, Is.EqualTo(262144), "the whole dash");
            Assert.That(rule.Asked, Is.EqualTo(asked), "not asked while stepping");
        }

        [Test]
        public void TheRuleIsRequired()
        {
            SimSession s = World(Map(), new[] { Dasher(1, 10500, 4500) });
            Assert.That(() => DashWorlds.Loco(s).BindBusy(null), Throws.TypeOf<ArgumentNullException>());
        }
    }
}
