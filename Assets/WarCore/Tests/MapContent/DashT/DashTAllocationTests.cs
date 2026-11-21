// Strict direct-byte proof needs the headless CoreCLR run (as the M0c2 allocation tests); Unity's counter cannot prove zero.
#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
using System;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using WarCore.Tests.Dash;
using static WarCore.Tests.DashT.DashTWorlds;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.DashT
{
    // DASH-T acceptance 8: Sim.Step does not allocate while units dash on a terrain world and on a plate world. 300 ticks warm up,
    // then one measured window of 1200 ticks after a positive control of the counter.
    //   Terrain: an 80 x 8 map with mud on every fourth column; unit 1 is ordered along it and dashes +Z / -Z (4 m) whenever its
    //   cooldown allows, so every dash ends in a rejoin; unit 2 dashes without an order.
    //   Plates: the demo house; unit 1 dashes up and down the stair (2 m along +X / -X, cooldown 10), changing layer each time;
    //   unit 2 outside dashes +Z / -Z with an order along the house.
    public sealed class DashTAllocationTests
    {
        private static long Measure(SimSession s, Func<bool> ask, out int dashes)
        {
            Simulation sim = M3TestWorld.Sim(s);
            int count = 0;
            for (int i = 0; i < 300; i++) { if (ask()) count++; sim.Step(default); }
            long c0 = GC.GetAllocatedBytesForCurrentThread();
            var control = new byte[32768];
            long controlBytes = GC.GetAllocatedBytesForCurrentThread() - c0;
            GC.KeepAlive(control);
            Assert.That(controlBytes, Is.GreaterThanOrEqualTo(32768), "the counter works");
            AllocationMeter.Stabilize();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1200; i++) { if (ask()) count++; sim.Step(default); }
            long used = GC.GetAllocatedBytesForCurrentThread() - before;
            dashes = count;
            return used;
        }

        [Test]
        public void Terrain_DoesNotAllocateWhileDashing()
        {
            SimSession s = World(Map(surface: (x, z) => x % 4 == 3 ? Mud : Grass, width: 80), new[] { Dasher(1, 2500, 1500), Dasher(2, 40500, 6500) });
            InterimLocomotion l = DashWorlds.Loco(s);
            EntityId one = M3TestWorld.Unit(s, 1), two = M3TestWorld.Unit(s, 2);
            l.Order(one, new SimVec3(SimFix.Ratio(155, 2), default, SimFix.Ratio(3, 2)));
            SimVec3 down = SimVec3.Metres(0, 0, -1);
            int n = 0;
            long used = Measure(s, () =>
            {
                bool a = l.TryDash(one, (n & 1) == 0 ? DashWorlds.PlusZ : down);
                if (a) n++;
                return l.TryDash(two, DashWorlds.PlusX) | a;
            }, out _);
            Assert.That(n, Is.EqualTo(25), "unit 1: a dash every 60 ticks");
            Assert.That(M3TestWorld.Position(s).X.Raw, Is.GreaterThan(SimFix.FromInt(20).Raw), "the order walks");
            Assert.That(used, Is.Zero, "bytes over 1200 ticks with dashes");
        }

        [Test]
        public void Plates_DoesNotAllocateWhileDashing()
        {
            SimSession s = House(new[] { Dasher(1, 9500, 9500, distanceMm: 2000, ticks: 2, cooldown: 10), Dasher(2, 2500, 13500, distanceMm: 2000, ticks: 2, cooldown: 10) });
            InterimLocomotion l = DashWorlds.Loco(s);
            EntityId one = M3TestWorld.Unit(s, 1), two = M3TestWorld.Unit(s, 2);
            EntityStore entities = M3TestWorld.Sim(s).Entities;
            ComponentType<VerticalState> vertical = entities.Resolve<VerticalState>();
            SimVec3 down = SimVec3.Metres(0, 0, -1);
            l.Order(two, new SimVec3(SimFix.Ratio(45, 2), default, SimFix.Ratio(27, 2)));
            int up = 0, side = 0;
            int layers = 0, last = 0;
            long used = Measure(s, () =>
            {
                int layer = entities.Read(vertical, one).Layer;
                if (layer != last) { layers++; last = layer; }
                bool a = l.TryDash(one, (up & 1) == 0 ? DashWorlds.PlusX : DashWorlds.MinusX);
                if (a) up++;
                bool b = l.TryDash(two, (side & 1) == 0 ? down : DashWorlds.PlusZ);
                if (b) side++;
                return a | b;
            }, out _);
            Assert.That(up, Is.GreaterThan(100), "stair dashes");
            Assert.That(layers, Is.GreaterThan(100), "layer changes");
            Assert.That(used, Is.Zero, "bytes over 1200 ticks with dashes");
        }
    }
}
#endif
