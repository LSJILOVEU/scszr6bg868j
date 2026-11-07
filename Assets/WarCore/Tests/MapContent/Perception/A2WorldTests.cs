using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Perception
{
    // Acceptance 7 (no surface table), 8 (AI thinking and auto fire both use the cover), 9 (snapshots), 10 (content) and 11
    // (assembly with an unsupported selector).
    public sealed class A2WorldTests
    {
        private const string Grass500 = ", \"concealMilli\": 500";

        // 7: a terrain map with a height field but no surface table is not wrapped, even when the pack has cover.
        [Test]
        public void NoSurfaceTable_NotWrapped()
        {
            WcMap map = Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, Array.Empty<(int, int, int, int)>(), 2 * A2.S, (x, z) => 0);
            var w = new M6World(A2.Recipe(A2.DUnits(), A2.Combat(0), A2.Pack("", Grass500)), WcMapWriter.Write(map));
            Assert.That(w.Combat.Pack, Is.SameAs(w.Recipe.Combat));
        }

        // The surface table names grass 500 but no cell is grass: nothing has cover, the pack is not wrapped.
        [Test]
        public void CoverOnlyInTheNameTable_NotWrapped()
        {
            WcMap map = A2.Map(A2.DWalls, (x, z) => false);
            var w = new M6World(A2.Recipe(A2.DUnits(), A2.Combat(0), A2.Pack("", Grass500)), WcMapWriter.Write(map));
            Assert.That(w.Combat.Pack, Is.SameAs(w.Recipe.Combat));
        }

        // Fixture F: AI soldier A (key 1, team 1, weapon effect 0, 1 m/s) on cell (10, 10) walks east to the point on
        // cell (30, 10); B (key 2, team 2, no weapon) stands on cell (20, 25) under a squad hold, alone in squad 2.
        // A's path keeps the horizontal distance to B in [15, 18.03] m: inside (Lc, L] = (10, 20] for grass 500.
        // Without grass under B the grass lies on cells x 0..3, z 26..29, far from both, so the world is still wrapped.
        private static bool FarPatch(int x, int z) => x < 4 && z >= 26;
        private static M6World F(bool grassUnderB)
        {
            var units = new[] { A2.Soldier(1, 1, A2.Cell(10, 10), 0, 1000), A2.Soldier(2, 2, A2.Cell(20, 25), -1, 1000) };
            Func<int, int, bool> patch = grassUnderB ? A2.InPatch : FarPatch;
            return new M6World(A2.Recipe(units, A2.Combat(0), A2.Pack("", Grass500), standing: true), WcMapWriter.Write(A2.Map(null, patch)));
        }

        private const long FirstThink = 6;
        private static readonly long[] FireTicks = { 1, 31, 61, 121, 151, 181, 241, 271 };

        // 8: in grass, A neither stops at its first thinking tick nor fires; on bare ground it stops and fires on the
        // weapon's own rhythm (1 s between rounds, 3 rounds, 2 s reload: ticks 1, 31, 61, 121, 151, 181, 241, 271).
        [TestCase(true)]
        [TestCase(false)]
        public void Ai_ThinkAndFire_UseTheCover(bool grass)
        {
            M6World w = F(grass);
            StableId a = w.Sim.Entities.StableOf(w.Unit(1));
            Assert.That((A2.Think - (long)(a.Value % A2.Think)) % A2.Think, Is.EqualTo(FirstThink), "A's thinking phase");
            Assert.That(w.Combat.Pack.Selector, Is.InstanceOf<ConcealedSight>());
            SimVec3 b0 = w.Read<Position>(2).Value;
            var fires = new List<long>();
            SimVec3 last = w.Read<Position>(1).Value;
            int moved = 0;
            for (long tick = 1; tick <= 300; tick++)
            {
                w.Sim.Step(tick == 1 ? new InputFrame(default, default, squad: new SquadCommand(SquadCommandKind.Hold, 2, default, default)) : default);
                foreach (var f in w.Combat.Fires) { Assert.That(f.Shooter, Is.EqualTo(a)); fires.Add(tick); }
                SimVec3 here = w.Read<Position>(1).Value;
                Assert.That(w.Read<Position>(2).Value, Is.EqualTo(b0), "B holds, tick " + tick);
                SimWide d = CombatSystem.Horizontal(b0 - here).LengthSquared;
                Assert.That(d > new SimVec3(SimFix.FromInt(10), default, default).LengthSquared, Is.True, "beyond Lc, tick " + tick);
                Assert.That(d <= new SimVec3(SimFix.FromInt(20), default, default).LengthSquared, Is.True, "within L, tick " + tick);
                if (!here.Equals(last)) moved++;
                last = here;
                if (tick == FirstThink)
                {
                    bool active = w.Has<MoveIntent>(1) && w.Read<MoveIntent>(1).Active;
                    Assert.That(active, Is.EqualTo(grass), "A's move intent after its first thinking tick");
                }
            }
            if (grass)
            {
                Assert.That(fires, Is.Empty);
                Assert.That(moved, Is.GreaterThan(250), "A keeps walking");
            }
            else Assert.That(fires, Is.EqualTo(FireTicks));
        }

        // 9: a covered world saved at tick 150 and restored (both allocation orders) runs on exactly as the uninterrupted one.
        [TestCase(false)]
        [TestCase(true)]
        public void Snapshot_RestoresTheSameRun(bool reverse)
        {
            var expected = new List<ulong>();
            A2.HashRun(A2.D(A2.Pack("", Grass500)), 300, expected, out _);
            M6World a = A2.D(A2.Pack("", Grass500));
            a.Step(150);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World b = A2.D(A2.Pack("", Grass500), reverse);
            b.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: reverse);
            Assert.That(b.Hash, Is.EqualTo(expected[150]));
            // The allocation order really differs on the reverse restore: against a forward restore the units sit in other slots.
            M6World forward = A2.D(A2.Pack("", Grass500));
            forward.Sim.RestoreRetainedSnapshot(snapshot, reverseAllocation: false);
            var ids = new List<int>(); var restored = new List<int>();
            for (ulong key = 1; key <= 4; key++) { ids.Add(forward.Unit(key).Id); restored.Add(b.Unit(key).Id); }
            if (reverse) Assert.That(restored, Is.Not.EqualTo(ids)); else Assert.That(restored, Is.EqualTo(ids));
            for (int t = 151; t <= 300; t++) { b.Step(); Assert.That(b.Hash, Is.EqualTo(expected[t]), "tick " + t); }
        }

        // 9: other cover factors are other content: the manifest check refuses the snapshot.
        [Test]
        public void Snapshot_OtherCover_IsRefused()
        {
            M6World a = A2.D(A2.Pack("", Grass500));
            a.Step(150);
            byte[] snapshot = a.Sim.SaveRetainedSnapshot();
            M6World other = A2.D(A2.Pack("", ", \"concealMilli\": 400"));
            var error = Assert.Throws<SimSnapshotException>(() => other.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(error.Message, Is.EqualTo("Stable protocol, world or content manifest mismatch."));
        }

        // 10: the field's range.
        [TestCase(1)]
        [TestCase(1000)]
        public void Content_InRange(int value)
        {
            ContentPack pack = A2.Pack("", ", \"concealMilli\": " + value);
            Assert.That(pack.Terrain.TryFind("grass", out SurfaceDef grass), Is.True);
            Assert.That(grass.ConcealMilli, Is.EqualTo(value));
        }

        [TestCase(0)]
        [TestCase(1001)]
        public void Content_OutOfRange(int value)
        {
            var error = Assert.Throws<ContentException>(() => A2.Pack("", ", \"concealMilli\": " + value));
            Assert.That(error.Message, Is.EqualTo("content pack 'a2-test' layer 'a2-test' surface 'grass' field 'concealMilli': out of range [1, 1000]"));
        }

        [Test]
        public void SurfaceDef_Range()
        {
            Assert.That(new SurfaceDef("a", true, 1000, 1).ConcealMilli, Is.EqualTo(1000));
            Assert.That(new SurfaceDef("a", false, 0, 0, 7).ConcealMilli, Is.EqualTo(7));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceDef("a", true, 1000, 1, 0)).ParamName, Is.EqualTo("concealMilli"));
            Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => new SurfaceDef("a", true, 1000, 1, 1001)).ParamName, Is.EqualTo("concealMilli"));
        }

        // Canonical bytes with cover: the pre-A2 bytes, then the tag and each surface's factor in id order.
        [Test]
        public void Canonical_WithCover_AppendsTheBlock()
        {
            string tail = "434E434C" + "E8030000" + "F4010000";
            Assert.That(A2.Hex(A2.Pack("", Grass500).Terrain.Canonical()), Is.EqualTo(A2BaselineTests.CanonicalHex + tail));
        }

        // 11: a pack whose selector cover cannot wrap is refused when the world is built.
        [Test]
        public void Assembly_UnsupportedSelector_Throws()
        {
            var original = A2.Combat(0);
            var combat = new CombatPack(original.Weapons.ToArray(), original.Resolver, new A2SelectTests.OtherSelector(), original.MaxHealthRaw);
            var recipe = A2.Recipe(A2.DUnits(), combat, A2.Pack("", Grass500));
            Assert.Throws<NotSupportedException>(() => SimSession.Create(recipe, WcMapWriter.Write(A2.Map(A2.DWalls))));
        }
    }
}
