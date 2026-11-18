using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-P1: the guard-slot spacing and an outer ring of slots are scheduler properties read from content
    // (ai.slotSpacingMm, ai.slotRingMm), and the placement part "scatter" gives each unit a spot of its own near the
    // point's centre. Defaults leave every battle as it was.
    public sealed class AiP1Tests
    {
        private const long Two = 2 * SimFix.Scale, Six = 6 * SimFix.Scale;
        private const string P1 = "{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }";

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["p1-test"] = "{ \"id\": \"p1-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "p1-test");

        private static ControlPointDef Point(int id, int x, int owner) => new ControlPointDef(id, SimVec3.Metres(x, 0, 20), SimFix.FromInt(6), owner);
        // Team 1 owns x = 5; the only point it goes for is x = 100, which never flips (capture takes a million ticks).
        private static ControlPointDef[] TwoPoints() => new[] { Point(1, 5, 1), Point(2, 100, 2) };
        private static ControlPointDef[] Four() => new[] { Point(1, 5, 1), Point(2, 150, 2), Point(3, 80, 0), Point(4, 115, 2) };

        // Unweaponed team-1 units on a 2 m grid (x 21 to 59, z 30 upwards); every unit thinks every tick.
        private static M6World World(int units, ControlPointDef[] points, string ai = null, long speed = 1, int captureTicks = 3)
        {
            var spawns = new UnitSpawn[units];
            for (int i = 0; i < units; i++) spawns[i] = M6World.U((ulong)i + 1, 1, 21 + i % 20 * 2, 30 + i / 20 * 2, speed: speed);
            WorldRecipe recipe = M6World.RecipeOf(spawns, M6World.Pack(points, ticks: captureTicks, think: 1)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithAiRules(Pack(ai));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed, fair).WithAiRules(Pack(ai)), FairEFixture.Map("control"));

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static AiField Field(M6World w) => (AiField)typeof(SimpleAiSystem).GetField("field", Private).GetValue(w.System<SimpleAiSystem>());
        private static ulong Key(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key)).Value;
        private static ulong Digest(SimpleAiSystem ai)
        { var writer = new SimStateWriter(); ai.SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }
        private static ulong Tag(string tag, ulong digest, long value) { var h = new StateHash(); h.Add(tag); h.Add(digest); h.Add(value); return h.Value; }
        private static ulong Tag(string tag, ulong digest, int value) { var h = new StateHash(); h.Add(tag); h.Add(digest); h.Add(value); return h.Value; }
        // The scheduler's arrival distance (a quarter metre, SimpleAiSystem.ArrivalSquared).
        private static bool Arrived(SimVec3 at, SimVec3 slot) => Horizontal2(at, slot) <= SimFix.Scale * SimFix.Scale / 16;
        private static long Horizontal2(SimVec3 a, SimVec3 b) { long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw; return dx * dx + dz * dz; }

        // ---- Lattice ----

        // 2 m spacing, radius 6 m: neighbours in a row are 131072 raw apart, row r sits at 131072 * r * 866025 / 1000000
        // (r = 1, 2, 3: 131072 * 866025 = 113,511,628,800 -> 113511; 262144 * 866025 = 227,023,257,600 -> 227023;
        // 393216 * 866025 = 340,534,886,400 -> 340534; each divided by 1000000 and truncated), and the circle holds rings 0..3 of the hexagon
        // (1 + 6 + 12 + 18 = 37 slots; ring 4's nearest slot is 2 m * 4 * sqrt(3)/2 = 6.93 m away, outside).
        [Test]
        public void Slots_TwoMetreSpacingByHand()
        {
            SimVec3 center = SimVec3.Metres(100, 0, 20);
            SimVec3[] slots = GuardSlots.Build(center, SimFix.FromInt(6), Two);
            Assert.That(slots.Length, Is.EqualTo(37));
            Assert.That(slots[0], Is.EqualTo(center));
            Assert.That(slots[1].X.Raw - center.X.Raw, Is.EqualTo(-65536L));
            Assert.That(slots[1].Z.Raw - center.Z.Raw, Is.EqualTo(113511L));
            var rows = new SortedSet<long>(); bool rowNeighbour = false;
            foreach (SimVec3 s in slots)
            {
                rows.Add(s.Z.Raw - center.Z.Raw);
                if (s.Z.Raw == center.Z.Raw && s.X.Raw - center.X.Raw == Two) rowNeighbour = true;
            }
            Assert.That(rowNeighbour, Is.True);
            Assert.That(rows, Is.EqualTo(new[] { -340534L, -227023L, -113511L, 0L, 113511L, 227023L, 340534L }));
            // 1 m (the default) holds rings 0..6: 1 + 6 + 12 + 18 + 24 + 30 + 36 = 127.
            Assert.That(GuardSlots.Build(center, SimFix.FromInt(6)).Length, Is.EqualTo(127));
            Assert.That(GuardSlots.Build(center, SimFix.FromInt(6), GuardSlots.SpacingRaw), Is.EqualTo(GuardSlots.Build(center, SimFix.FromInt(6))));
        }

        // A 6 m ring around a 6 m circle at 2 m spacing: the table covers 12 m, which is the 1 m circle scaled by two
        // (127 slots). Ring 0 gives the table the point had before, slot for slot.
        [Test]
        public void Slots_RingWidensTheTable()
        {
            ControlPointDef[] points = TwoPoints();
            var ring = new GuardSlots(points, Two, Six);
            var none = new GuardSlots(points, Two, 0);
            var plain = new GuardSlots(points);
            SimFix twelve = SimFix.FromInt(12);
            for (int p = 0; p < points.Length; p++)
            {
                Assert.That(ring.Count(p), Is.EqualTo(127), "point " + p);
                Assert.That(none.Count(p), Is.EqualTo(37), "point " + p);
                for (int s = ring.Start(p); s < ring.Start(p) + ring.Count(p); s++)
                    Assert.That(CombatSystem.Horizontal(ring.At(s) - points[p].Center).LengthSquared,
                        Is.LessThanOrEqualTo(new SimVec3(twelve, default, default).LengthSquared), "slot " + s);
                SimVec3[] before = GuardSlots.Build(points[p].Center, points[p].Radius);
                Assert.That(plain.Count(p), Is.EqualTo(before.Length));
                for (int i = 0; i < before.Length; i++) Assert.That(plain.At(plain.Start(p) + i), Is.EqualTo(before[i]), "slot " + i);
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new GuardSlots(points, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GuardSlots(points, Two, -1));
        }

        // Nearest finds a slot up to one spacing away, also beyond the circle's edge. The slot 6 m east of the centre
        // (and 12 m with the ring) is an edge slot; a position 1.5 m further east is 7.5 m (13.5 m) from the centre.
        [Test]
        public void Slots_NearestReachesOneSpacingPastTheEdge()
        {
            ControlPointDef[] points = TwoPoints();
            SimVec3 c = points[1].Center;
            var two = new GuardSlots(points, Two);
            Assert.That(two.TryFind(new SimVec3(c.X + SimFix.FromInt(6), c.Y, c.Z), 1, out int edge), Is.True);
            Assert.That(two.Nearest(new SimVec3(c.X + SimFix.Ratio(15, 2), c.Y, c.Z)), Is.EqualTo(two.Canonical(edge)));
            Assert.That(two.Nearest(new SimVec3(c.X + SimFix.Ratio(81, 10), c.Y, c.Z)), Is.EqualTo(-1));
            var one = new GuardSlots(points);
            Assert.That(one.Nearest(new SimVec3(c.X + SimFix.Ratio(15, 2), c.Y, c.Z)), Is.EqualTo(-1));
            Assert.That(one.TryFind(new SimVec3(c.X + SimFix.FromInt(6), c.Y, c.Z), 1, out int oneEdge), Is.True);
            Assert.That(one.Nearest(new SimVec3(c.X + SimFix.Ratio(65, 10), c.Y, c.Z)), Is.EqualTo(one.Canonical(oneEdge)));
            var ring = new GuardSlots(points, Two, Six);
            Assert.That(ring.TryFind(new SimVec3(c.X + SimFix.FromInt(12), c.Y, c.Z), 1, out int outer), Is.True);
            Assert.That(ring.Nearest(new SimVec3(c.X + SimFix.Ratio(27, 2), c.Y, c.Z)), Is.EqualTo(ring.Canonical(outer)));
        }

        // ---- Content ----

        [Test]
        public void Content_FieldsAreReadAndBuildTheParts()
        {
            AiRules rules = Pack(P1).Section<AiRules>();
            Assert.That(rules.SlotSpacingMm, Is.EqualTo(2000));
            Assert.That(rules.SlotRingMm, Is.EqualTo(6000));
            Assert.That(rules.Placement, Is.EqualTo("scatter"));
            Assert.That(rules.ScatterMm, Is.EqualTo(6000));
            AiParts parts = ContentPolicies.Parts(rules, TwoPoints());
            Assert.That(parts.Placement, Is.InstanceOf<ScatterPlacement>());
            Assert.That(parts.SlotSpacingRaw, Is.EqualTo(131072L));
            Assert.That(parts.SlotRingRaw, Is.EqualTo(393216L));
            Assert.That(parts.Placement.Fold(5), Is.EqualTo(Tag("battle.ai.scatter.v1", 5UL, 6000)));

            AiRules plain = Pack("{}").Section<AiRules>();
            Assert.That((plain.SlotSpacingMm, plain.SlotRingMm, plain.ScatterMm), Is.EqualTo((1000, 0, 0)));
            foreach (AiParts p in new[] { ContentPolicies.Parts(plain), ContentPolicies.Parts(null), new AiParts() })
            {
                Assert.That(p.SlotSpacingRaw, Is.EqualTo(GuardSlots.SpacingRaw));
                Assert.That(p.SlotRingRaw, Is.EqualTo(0L));
                Assert.That(p.Placement, Is.InstanceOf<NearestSlotPlacement>());
            }
            // Range edges are accepted.
            Assert.That(Pack("{ \"slotSpacingMm\": 500 }").Section<AiRules>().SlotSpacingMm, Is.EqualTo(500));
            Assert.That(Pack("{ \"slotSpacingMm\": 5000 }").Section<AiRules>().SlotSpacingMm, Is.EqualTo(5000));
            Assert.That(Pack("{ \"slotRingMm\": 0 }").Section<AiRules>().SlotRingMm, Is.EqualTo(0));
            Assert.That(Pack("{ \"slotRingMm\": 50000 }").Section<AiRules>().SlotRingMm, Is.EqualTo(50000));
            Assert.That(Pack("{ \"placement\": \"scatter\", \"scatterMm\": 1 }").Section<AiRules>().ScatterMm, Is.EqualTo(1));
            Assert.That(Pack("{ \"placement\": \"scatter\", \"scatterMm\": 50000 }").Section<AiRules>().ScatterMm, Is.EqualTo(50000));
            // Slot spacing and ring belong to the scheduler: accepted under every part.
            Assert.That(Pack("{ \"placement\": \"nearest-slot\", \"slotSpacingMm\": 2000, \"slotRingMm\": 3000 }").Section<AiRules>().SlotRingMm, Is.EqualTo(3000));
        }

        [TestCase("{ \"scatterMm\": 6000 }", "scatterMm", "belongs to placement 'scatter', but placement is 'nearest-slot'")]
        [TestCase("{ \"placement\": \"nearest-slot\", \"scatterMm\": 6000 }", "scatterMm", "belongs to placement 'scatter', but placement is 'nearest-slot'")]
        [TestCase("{ \"placement\": \"scatter\", \"scatterMm\": 0 }", "scatterMm", null)]
        [TestCase("{ \"placement\": \"scatter\", \"scatterMm\": 50001 }", "scatterMm", null)]
        [TestCase("{ \"slotSpacingMm\": 499 }", "slotSpacingMm", null)]
        [TestCase("{ \"slotSpacingMm\": 5001 }", "slotSpacingMm", null)]
        [TestCase("{ \"slotRingMm\": -1 }", "slotRingMm", null)]
        [TestCase("{ \"slotRingMm\": 50001 }", "slotRingMm", null)]
        public void Content_BadFieldNamesPackEntryAndField(string ai, string field, string why)
        {
            var e = Assert.Throws<ContentException>(() => Pack(ai));
            Assert.That(e.Message, Does.StartWith("content pack 'p1-test' layer 'p1-test' ai field '" + field + "': "));
            if (why != null) Assert.That(e.Message, Does.EndWith(why));
        }

        [Test]
        public void Content_ScatterWithoutItsFieldIsAnError()
        {
            var e = Assert.Throws<ContentException>(() => Pack("{ \"placement\": \"scatter\" }"));
            Assert.That(e.Message, Is.EqualTo("content pack 'p1-test' layer 'p1-test' ai is missing field 'scatterMm'"));
        }

        // The ownership table also covers the dispatch's parameters; only one dispatch exists today, so the check is
        // driven directly with another dispatch id.
        [Test]
        public void Content_ParametersOfAnUnselectedDispatchAreFound()
        {
            Func<string, bool> hold = f => f == "holdSharePermille", spread = f => f == "attackSpreadPoints", scatter = f => f == "scatterMm";
            Assert.That(AiSection.Misplaced(hold, "other", "nearest-slot"), Is.EqualTo(0));
            Assert.That(AiSection.Misplaced(spread, "other", "nearest-slot"), Is.EqualTo(1));
            Assert.That(AiSection.Misplaced(hold, "nearest", "nearest-slot"), Is.EqualTo(-1));
            Assert.That(AiSection.Misplaced(scatter, "nearest", "scatter"), Is.EqualTo(-1));
            Assert.That(AiSection.Misplaced(scatter, "nearest", "nearest-slot"), Is.EqualTo(2));
            Assert.That(AiSection.Misplaced(f => f == "slotSpacingMm" || f == "slotRingMm", "other", "other"), Is.EqualTo(-1));
        }

        // Slot zones (radius + ring) must stay one spacing apart: radius 6, ring 6, spacing 2 needs centres 26 m apart.
        [Test]
        public void Content_TouchingSlotZonesAreAnError()
        {
            AiRules rules = Pack(P1).Section<AiRules>();
            ControlPointDef[] apart = { new ControlPointDef(4, SimVec3.Metres(50, 0, 20), SimFix.FromInt(6)), new ControlPointDef(9, SimVec3.Metres(76, 0, 20), SimFix.FromInt(6)) };
            // Exactly one spacing apart loads.
            Assert.That(() => ContentPolicies.Parts(rules, apart), Throws.Nothing);
            Assert.That(ContentPolicies.Parts(rules, apart).SlotRingRaw, Is.EqualTo(393216L));
            ControlPointDef[] close = { apart[0], new ControlPointDef(9, new SimVec3(SimFix.FromInt(76) - SimFix.FromRaw(1), default, SimFix.FromInt(20)), SimFix.FromInt(6)) };
            var e = Assert.Throws<ContentException>(() => ContentPolicies.Parts(rules, close));
            Assert.That(e.Message, Is.EqualTo("content pack 'p1-test' layer 'p1-test' ai field 'slotRingMm': slot zones of points 4 and 9 are less than one slot spacing apart"));
            // Without a ring nothing is checked: the same points under the default ring are fine.
            Assert.That(ContentPolicies.Parts(Pack("{ \"slotSpacingMm\": 2000 }").Section<AiRules>(), close).SlotRingRaw, Is.EqualTo(0L));
            // Through the recipe: building the battle fails the same way.
            WorldRecipe recipe = M6World.RecipeOf(new[] { M6World.U(1, 1, 30, 30) }, M6World.Pack(close, think: 1)).WithAiRules(Pack(P1));
            // Features are invoked by reflection, so the content error arrives as the inner exception.
            Exception thrown = Assert.Catch(() => new M6World(recipe, WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null))));
            while (thrown is TargetInvocationException && thrown.InnerException != null) thrown = thrown.InnerException;
            Assert.That(thrown, Is.InstanceOf<ContentException>());
            Assert.That(thrown.Message, Is.EqualTo(e.Message));
        }

        // ---- Scatter ----

        // m = Mix(key ^ 0x6a09e667bb67ae85 ^ point), u = (m & 0xFFFFFFFF) % 12001 - 6000, v = (m >> 32) % 12001 - 6000 (mm),
        // raw = trunc(mm * 65536 / 1000). Worked out outside the code for point 1 at (100, 20) m:
        // key 1 -> (969, -4047) mm, key 2 -> (668, 380) mm, key 0xDEADBEEF -> (-639, 1574) mm.
        [Test]
        public void Scatter_AnchorsByHand()
        {
            M6World w = World(1, TwoPoints(), "{ \"placement\": \"scatter\", \"scatterMm\": 6000 }");
            AiField field = Field(w);
            var scatter = new ScatterPlacement(6000);
            (ulong key, long x, long z)[] expected = { (1, 6617104, 1045496), (2, 6597378, 1335623), (0xDEADBEEF, 6511723, 1413873) };
            foreach (var (key, x, z) in expected)
            {
                var unit = new AiUnit(new StableId(key), 1, SimVec3.Metres(30, 0, 30));
                SimVec3 at = scatter.Anchor(in unit, 1, field);
                Assert.That((at.X.Raw, at.Y.Raw, at.Z.Raw), Is.EqualTo((x, 0L, z)), "key " + key);
                // Where the unit stands does not matter; the point does.
                var moved = new AiUnit(new StableId(key), 2, SimVec3.Metres(90, 0, 10));
                Assert.That(scatter.Anchor(in moved, 1, field), Is.EqualTo(at));
                SimVec3 other = scatter.Anchor(in unit, 0, field);
                Assert.That((other.X.Raw - TwoPoints()[0].Center.X.Raw, other.Z.Raw - TwoPoints()[0].Center.Z.Raw), Is.Not.EqualTo((x - 100 * SimFix.Scale, z - 20 * SimFix.Scale)));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => new ScatterPlacement(0));
        }

        // The formula written out independently of the part (splitmix64 finalizer).
        private static ulong Finalize(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }

        // 100 units, point 1: the anchors fall into the four quadrants around the centre exactly as the formula says,
        // and the scheduler asks the part with the unit's key and the dispatched point.
        [Test]
        public void Scatter_QuadrantsOfAHundredUnits()
        {
            const int units = 100;
            M6World w = World(units, TwoPoints(), "{ \"placement\": \"scatter\", \"scatterMm\": 6000 }");
            AiField field = Field(w);
            var scatter = new ScatterPlacement(6000);
            SimVec3 c = TwoPoints()[1].Center;
            var want = new int[4]; var got = new int[4];
            for (ulong key = 1; key <= units; key++)
            {
                ulong m = Finalize(Key(w, key) ^ 0x6a09e667bb67ae85UL ^ 1UL);
                long u = (long)((m & 0xFFFFFFFFUL) % 12001UL) - 6000, v = (long)((m >> 32) % 12001UL) - 6000;
                want[(u < 0 ? 1 : 0) + (v < 0 ? 2 : 0)]++;
                var unit = new AiUnit(new StableId(Key(w, key)), 1, w.Read<Position>(key).Value);
                SimVec3 at = scatter.Anchor(in unit, 1, field);
                Assert.That((at.X.Raw - c.X.Raw, at.Z.Raw - c.Z.Raw), Is.EqualTo((SimFix.Ratio(u, 1000).Raw, SimFix.Ratio(v, 1000).Raw)), "unit " + key);
                got[(at.X.Raw < c.X.Raw ? 1 : 0) + (at.Z.Raw < c.Z.Raw ? 2 : 0)]++;
            }
            Assert.That(got, Is.EqualTo(want));
            // The same counts, worked out once from the keys: x+z+, x-z+, x+z-, x-z-.
            Assert.That(want, Is.EqualTo(new[] { 30, 19, 19, 32 }));
            Assert.That(want[0] + want[1] + want[2] + want[3], Is.EqualTo(units));
            TestContext.Out.WriteLine("AI_P1 quadrants=" + string.Join(",", want));
        }

        // ---- Battles ----

        // 60 units go for one 6 m point at 2 m spacing. With a 6 m ring all 60 end on different slots, none outside the
        // 12 m zone, no two closer than half a spacing. Without the ring the circle holds 37: 37 stand on slots, the
        // other 23 stop outside the circle (the scheduler's behaviour, kept as is).
        [TestCase(6000, 60, 0)]
        [TestCase(0, 37, 23)]
        public void Battle_SixtyUnitsAtOnePoint(int ring, int onSlots, int outside)
        {
            M6World w = Settled(ring);
            ControlPointDef target = TwoPoints()[1];
            SimVec3[] table = GuardSlots.Build(target.Center, target.Radius + SimFix.Ratio(ring, 1000), Two);
            var used = new HashSet<int>(); int stopOutside = 0;
            var at = new List<SimVec3>();
            for (ulong key = 1; key <= 60; key++)
            {
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key + " still moving");
                SimVec3 here = w.Read<Position>(key).Value; at.Add(here);
                int slot = -1;
                for (int s = 0; s < table.Length; s++) if (Arrived(here, table[s])) slot = s;
                if (slot >= 0) { Assert.That(used.Add(slot), Is.True, "slot " + slot + " twice"); continue; }
                SimFix zone = target.Radius + SimFix.Ratio(ring, 1000);
                Assert.That(CombatSystem.Horizontal(here - target.Center).LengthSquared, Is.GreaterThan(new SimVec3(zone, default, default).LengthSquared), "unit " + key);
                stopOutside++;
            }
            Assert.That(used.Count, Is.EqualTo(onSlots));
            Assert.That(stopOutside, Is.EqualTo(outside));
            if (ring > 0)
                for (int i = 0; i < at.Count; i++)
                    for (int j = i + 1; j < at.Count; j++)
                        Assert.That(Horizontal2(at[i], at[j]), Is.GreaterThanOrEqualTo(SimFix.Scale * SimFix.Scale), "units " + (i + 1) + ", " + (j + 1));
        }

        private const int SettleTicks = 1500;
        private static string SixtyAi(int ring) => "{ \"slotSpacingMm\": 2000, \"slotRingMm\": " + ring + " }";
        private static M6World Settled(int ring, Action<M6World> setup = null, int ticks = SettleTicks)
        {
            M6World w = World(60, TwoPoints(), SixtyAi(ring), speed: 6000, captureTicks: 1000000);
            setup?.Invoke(w);
            w.Step(ticks);
            return w;
        }

        // Without a ring 23 units wait outside the full circle. When one unit inside dies, a waiting unit walks into the
        // freed slot on its own (each think it tries the circle again).
        [Test]
        public void Battle_FreedSlotIsRefilledFromOutside()
        {
            M6World first = Settled(0);
            ControlPointDef target = TwoPoints()[1];
            SimVec3[] table = GuardSlots.Build(target.Center, target.Radius, Two);
            ulong inside = 0; var waiting = new List<ulong>();
            for (ulong key = 1; key <= 60; key++)
            {
                bool isInside = false;
                foreach (SimVec3 slot in table) isInside |= Arrived(first.Read<Position>(key).Value, slot);
                if (isInside && inside == 0) inside = key;
                if (!isInside) waiting.Add(key);
            }
            Assert.That(waiting.Count, Is.EqualTo(23));
            SimVec3 freed = first.Read<Position>(inside).Value;

            M6World w = Settled(0, x => x.Sim.RegisterGameplay("test.death", new M6Damage(x, SettleTicks + 1, inside), SimSystemPhase.Queries));
            for (ulong key = 1; key <= 60; key++) Assert.That(w.Read<Position>(key).Value, Is.EqualTo(first.Read<Position>(key).Value), "unit " + key);
            w.Step(SettleTicks);
            Assert.That(w.Dead(inside), Is.True);
            int moved = 0;
            foreach (ulong key in waiting)
                if (Arrived(w.Read<Position>(key).Value, freed)) moved++;
            Assert.That(moved, Is.EqualTo(1));
        }

        // Writing the defaults out is the same battle tick by tick, with the same digest.
        [TestCase("{ \"slotSpacingMm\": 1000 }")]
        [TestCase("{ \"slotRingMm\": 0 }")]
        [TestCase("{ \"slotSpacingMm\": 1000, \"slotRingMm\": 0, \"placement\": \"nearest-slot\" }")]
        public void Battle_DefaultsWrittenOutLeaveTheBattle(string ai)
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41, null);
            M6World written = new M6World(recipe.WithAiRules(Pack(ai)), FairEFixture.Map("control"));
            M6World plain = new M6World(recipe, FairEFixture.Map("control"));
            Assert.That(Digest(written.System<SimpleAiSystem>()), Is.EqualTo(Digest(plain.System<SimpleAiSystem>())));
            for (int tick = 0; tick < 300; tick++)
            {
                written.Step(); plain.Step();
                Assert.That(written.Hash, Is.EqualTo(plain.Hash), "tick " + plain.Sim.Clock.Tick);
            }
        }

        // Each option is live: the battle differs from the one without it.
        [TestCase("{ \"placement\": \"scatter\", \"scatterMm\": 6000 }")]
        [TestCase("{ \"slotSpacingMm\": 2000 }")]
        [TestCase("{ \"slotRingMm\": 6000 }")]
        public void Battle_EachOptionChangesTheBattle(string ai)
        {
            M6World on = Battle(41, null, ai), off = new M6World(FairEFixture.Recipe("control", 41, null), FairEFixture.Map("control"));
            on.Step(600); off.Step(600);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = !on.Read<Position>(key).Value.Equals(off.Read<Position>(key).Value);
            Assert.That(differs, Is.True);
        }

        [TestCase(null, P1)]
        [TestCase(false, P1)]
        [TestCase(true, P1)]
        [TestCase(true, "{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 12000, \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }")]
        public void Battle_RepeatAndRestoreEveryTick(bool? fair, string ai)
        {
            M6World first = Battle(41, fair, ai), repeat = Battle(41, fair, ai);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600; tick++)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = Battle(41, fair, ai);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600 && !first.Sim.IsFinished; tick++)
            {
                first.Step(); restored.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }

        // Digest: fair decisions, dispatch, placement, then slot spacing, then slot ring; every option differs from the
        // default and from the others, and their snapshots refuse each other.
        [TestCase(false)]
        [TestCase(true)]
        public void Battle_PolicyDigestAndSnapshots(bool fair)
        {
            string[] ais =
            {
                "{}",
                "{ \"slotSpacingMm\": 2000 }",
                "{ \"placement\": \"scatter\", \"scatterMm\": 6000 }",
                "{ \"slotRingMm\": 6000 }",
                P1,
            };
            var worlds = new M6World[ais.Length]; var digests = new ulong[ais.Length]; var saved = new byte[ais.Length][];
            for (int i = 0; i < ais.Length; i++)
            {
                worlds[i] = Battle(41, fair, ais[i]);
                digests[i] = Digest(worlds[i].System<SimpleAiSystem>());
                Assert.That(worlds[i].System<SimpleAiSystem>().StateVersion, Is.EqualTo(fair ? 4 : 3));
            }
            ulong d = digests[0];
            Assert.That(digests[1], Is.EqualTo(Tag("battle.ai.slot-spacing.v1", d, 131072L)));
            Assert.That(digests[2], Is.EqualTo(Tag("battle.ai.scatter.v1", d, 6000)));
            Assert.That(digests[3], Is.EqualTo(Tag("battle.ai.slot-ring.v1", d, 393216L)));
            Assert.That(digests[4], Is.EqualTo(Tag("battle.ai.slot-ring.v1", Tag("battle.ai.slot-spacing.v1", Tag("battle.ai.scatter.v1", d, 6000), 131072L), 393216L)));
            for (int i = 0; i < ais.Length; i++) { worlds[i].Step(); saved[i] = worlds[i].Sim.SaveRetainedSnapshot(); }
            for (int i = 0; i < ais.Length; i++)
                for (int j = 0; j < ais.Length; j++)
                {
                    if (i == j) continue;
                    Assert.That(digests[i], Is.Not.EqualTo(digests[j]));
                    var writer = new SimStateWriter(); worlds[i].System<SimpleAiSystem>().SaveState(writer);
                    var e = Assert.Throws<InvalidOperationException>(() => worlds[j].System<SimpleAiSystem>().LoadState(new SimStateReader(writer.ToArray())));
                    Assert.That(e.Message, Is.EqualTo("Battle configuration differs from snapshot."));
                    Assert.Throws<InvalidOperationException>(() => worlds[j].Sim.RestoreRetainedSnapshot(saved[i]));
                }
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Every think goes through the scatter part on the wider lattice; it must not allocate.
        [Test]
        public void Battle_StepAllocatesZero()
        {
            M6World warm = World(100, Four(), P1);
            warm.Step(30);
            M6World w = World(100, Four(), P1);
            w.Step(30);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < 30; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                w.Sim.Step(default);
                total += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            Assert.That(total, Is.EqualTo(0L));
        }
#endif
    }
}
