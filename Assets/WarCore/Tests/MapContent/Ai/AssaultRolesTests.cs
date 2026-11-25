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
    // AI-P2: the assault-roles placement splits the attackers of a point into a frontal arc, a left and a right flank and
    // a rear group; flankers and the rear group walk to a waypoint (IAiWaypoint) before they take their slots.
    public sealed class AssaultRolesTests
    {
        // PLACEHOLDER values of the task (AB AIConfig.cs:25-27, 52-60, shares scaled to 1000 without sneak and interdict).
        private static readonly string[] Fields =
            { "frontPermille", "flankPermille", "rearPermille", "frontArcMm", "flankSideMm", "rearOutMm", "flankMinMm", "anchorMm", "arriveMm" };
        private static readonly int[] Values = { 425, 375, 200, 14000, 35000, 12000, 60000, 10000, 3000 };
        private const int Omit = int.MinValue;
        private static string Ai(string extra = null, int[] values = null)
        {
            values ??= Values;
            var text = new System.Text.StringBuilder("{ \"placement\": \"assault-roles\"");
            for (int i = 0; i < Fields.Length; i++) if (values[i] != Omit) text.Append(", \"").Append(Fields[i]).Append("\": ").Append(values[i]);
            if (extra != null) text.Append(", ").Append(extra);
            return text.Append(" }").ToString();
        }
        private static int[] With(params (int field, int value)[] changes)
        {
            var values = (int[])Values.Clone();
            foreach ((int field, int value) in changes) values[field] = value;
            return values;
        }

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["roles-test"] = "{ \"id\": \"roles-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "roles-test");

        private static ControlPointDef Point(int id, int x, int z, int owner) => new ControlPointDef(id, SimVec3.Metres(x, 0, z), SimFix.FromInt(6), owner);

        // Unweaponed units on an open 240 x 160 m map; every unit thinks every tick; capture takes so long the target stays neutral.
        private static M6World World(UnitSpawn[] units, ControlPointDef[] points, string ai, (int, int, int, int)[] walls = null, SpawnAnchor[] bases = null)
        {
            WorldRecipe recipe = M6World.RecipeOf(units, M6World.Pack(points, ticks: 100000, think: 1, bases: bases)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(Pack(ai));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(240, 160, 0, 4 * SimFix.Scale,
                walls ?? Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        // The role split written out independently of the part: splitmix64 finalizer of (key ^ salt).
        private const ulong Salt = 0x7f4a7c159e3779b9UL;
        private static ulong Finalize(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }
        // 0 front, 1 left, 2 right, 3 rear.
        private static int RoleOf(ulong id)
        {
            ulong m = Finalize(id ^ Salt); int roll = (int)(m % 1000UL);
            return roll < 425 ? 0 : roll < 800 ? ((m >> 20) & 1UL) == 0 ? 1 : 2 : 3;
        }
        private static ulong Key(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key)).Value;
        private static ulong Placed(ulong key) => StableEntityKey.Placed(1, key).Id.Value;

        private static bool Inside(SimVec3 at, ControlPointDef p) =>
            CombatSystem.Horizontal(at - p.Center).LengthSquared <= new SimVec3(p.Radius, default, default).LengthSquared;
        private static SimFix Distance(SimVec3 a, SimVec3 b) => CombatSystem.Horizontal(a - b).Length;
        private static (long, long) Flat(SimVec3 v) => (v.X.Raw, v.Z.Raw);

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static AiField Field(M6World w) => (AiField)typeof(SimpleAiSystem).GetField("field", Private).GetValue(w.System<SimpleAiSystem>());
        private static ulong Digest(SimpleAiSystem ai)
        { var writer = new SimStateWriter(); ai.SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }

        // Axis from the own point (20, 20) to the target (100, 80): (80, 60) m, normalized to raw (52428, 39321) (0.8 and
        // 0.6 of 65536, toward zero); left = (-39321, 52428). Offsets of whole metres are exact: 10 m = 655360 raw, so
        // axis * 10 m = (524280, 393210) and left * 10 m = (-393210, 524280); 35 m and 12 m likewise. The frontal unit's
        // offset is (Mix(2 ^ salt) >> 32) % 14001 - 7000 = -2956 mm = -193724 raw (toward zero), left * that =
        // (116232, -154976) raw with each product toward zero.
        [Test]
        public void Roles_AnchorsAndWaypointsByHand()
        {
            ControlPointDef[] points = { Point(1, 20, 20, 1), Point(2, 100, 80, 0), Point(3, 220, 140, 2) };
            M6World w = World(new[] { M6World.U(1, 1, 30, 30) }, points, Ai());
            w.Step();
            AiField field = Field(w);
            IAiPlacement placement = ContentPolicies.Parts(Pack(Ai()).Section<AiRules>()).Placement;
            var waypoint = (IAiWaypoint)placement;
            Assert.That(placement, Is.InstanceOf<AssaultRolesPlacement>());
            Assert.That(new[] { RoleOf(4), RoleOf(1), RoleOf(9), RoleOf(2) }, Is.EqualTo(new[] { 1, 2, 3, 0 }));
            SimVec3 here = SimVec3.Metres(30, 0, 30);
            (long, long) Anchor(ulong id) => Flat(placement.Anchor(new AiUnit(new StableId(id), 1, here), 1, field));
            (long, long)? Way(ulong id) => waypoint.TryWaypoint(new AiUnit(new StableId(id), 1, here), 1, field, out SimVec3 at) ? Flat(at) : ((long, long)?)null;

            Assert.That(Anchor(4), Is.EqualTo((6553600L - 393210, 5242880L + 524280)));             // left
            Assert.That(Way(4), Is.EqualTo(((long, long)?)(6553600L - 39321 * 35, 5242880L + 52428 * 35)));
            Assert.That(Anchor(1), Is.EqualTo((6553600L + 393210, 5242880L - 524280)));             // right
            Assert.That(Way(1), Is.EqualTo(((long, long)?)(6553600L + 39321 * 35, 5242880L - 52428 * 35)));
            Assert.That(Anchor(9), Is.EqualTo((6553600L + 524280, 5242880L + 393210)));             // rear
            Assert.That(Way(9), Is.EqualTo(((long, long)?)(6553600L + 52428 * 12, 5242880L + 39321 * 12)));
            Assert.That(Anchor(2), Is.EqualTo((6553600L - 524280 + 116232, 5242880L - 393210 - 154976))); // front
            Assert.That(Way(2), Is.Null);

            // The own point: the default (the unit itself, no waypoint). Close to the waypoint: none.
            Assert.That(Flat(placement.Anchor(new AiUnit(new StableId(4), 1, here), 0, field)), Is.EqualTo(Flat(here)));
            Assert.That(waypoint.TryWaypoint(new AiUnit(new StableId(4), 1, here), 0, field, out _), Is.False);
            var near = new SimVec3(SimFix.FromRaw(6553600L - 39321 * 35 + 3 * 65536), default, SimFix.FromRaw(5242880L + 52428 * 35));
            Assert.That(waypoint.TryWaypoint(new AiUnit(new StableId(4), 1, near), 1, field, out _), Is.False);
            var out3 = new SimVec3(SimFix.FromRaw(6553600L - 39321 * 35 + 3 * 65536 + 1), default, SimFix.FromRaw(5242880L + 52428 * 35));
            Assert.That(waypoint.TryWaypoint(new AiUnit(new StableId(4), 1, out3), 1, field, out _), Is.True);
            // Team 2 owns no point other than the far one: its axis starts there.
            Assert.That(Flat(placement.Anchor(new AiUnit(new StableId(9), 2, here), 1, field)), Is.Not.EqualTo(Anchor(9)));
        }

        // Team 1 owns no point: the axis starts at its spawn anchor (40, 80), so it runs along +x and left is +z. Team 2
        // has neither a point nor an anchor: the default.
        [Test]
        public void Roles_AxisFromTheSpawnAnchorElseDefault()
        {
            ControlPointDef[] points = { Point(1, 100, 80, 0), Point(2, 200, 140, 0) };
            M6World w = World(new[] { M6World.U(1, 1, 30, 30) }, points, Ai(), bases: new[] { new SpawnAnchor(1, SimVec3.Metres(40, 0, 80)) });
            w.Step();
            AiField field = Field(w);
            IAiPlacement placement = ContentPolicies.Parts(Pack(Ai()).Section<AiRules>()).Placement;
            SimVec3 here = SimVec3.Metres(30, 0, 30);
            Assert.That(Flat(placement.Anchor(new AiUnit(new StableId(4), 1, here), 0, field)), Is.EqualTo((100L * 65536, 90L * 65536)));
            Assert.That(((IAiWaypoint)placement).TryWaypoint(new AiUnit(new StableId(4), 1, here), 0, field, out SimVec3 at), Is.True);
            Assert.That(Flat(at), Is.EqualTo((100L * 65536, 115L * 65536)));
            Assert.That(Flat(placement.Anchor(new AiUnit(new StableId(4), 2, here), 0, field)), Is.EqualTo(Flat(here)));
            Assert.That(((IAiWaypoint)placement).TryWaypoint(new AiUnit(new StableId(4), 2, here), 0, field, out _), Is.False);
        }

        // Axis along +x: team 1 owns (20, 80); the target is (100, 80); team 2 owns two far points. 100 attackers start
        // between x 21 and 59. Left is +z.
        private static ControlPointDef[] Line() => new[] { Point(1, 20, 80, 1), Point(2, 100, 80, 0), Point(3, 200, 80, 2), Point(4, 220, 30, 2) };
        private static UnitSpawn[] Grid(int count = 100, long speed = 4000)
        {
            var units = new UnitSpawn[count];
            for (int i = 0; i < count; i++) units[i] = M6World.U((ulong)i + 1, 1, 21 + i % 20 * 2, 70 + i / 20 * 2, speed: speed);
            return units;
        }

        // First think: flankers and the rear group are ordered to their waypoint exactly, the front to a slot in the
        // front half. Once everyone has stopped, each role stands on its side of the point, counted exactly.
        [Test]
        public void Roles_CountsAndSidesAfterArrival()
        {
            ControlPointDef[] points = Line();
            M6World w = World(Grid(), points, Ai());
            var role = new int[101]; var count = new int[4];
            for (ulong key = 1; key <= 100; key++) { role[key] = RoleOf(Key(w, key)); count[role[key]]++; }
            Assert.That(count, Is.EqualTo(new[] { 38, 16, 26, 20 }));
            w.Step();
            SimVec3 center = points[1].Center;
            for (ulong key = 1; key <= 100; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                (long x, long z) target = Flat(intent.Target);
                switch (role[key])
                {
                    case 1: Assert.That(target, Is.EqualTo((100L * 65536, 115L * 65536)), "unit " + key); break;
                    case 2: Assert.That(target, Is.EqualTo((100L * 65536, 45L * 65536)), "unit " + key); break;
                    case 3: Assert.That(target, Is.EqualTo((112L * 65536, 80L * 65536)), "unit " + key); break;
                    default:
                        Assert.That(Inside(intent.Target, points[1]), Is.True, "unit " + key);
                        Assert.That(target.x, Is.LessThan(center.X.Raw), "unit " + key); break;
                }
            }
            int tick = 1;
            for (; tick < 3000; tick++)
            {
                bool any = false;
                for (ulong key = 1; key <= 100 && !any; key++) any = w.Read<MoveIntent>(key).Active;
                if (!any) break;
                w.Step();
            }
            Assert.That(tick, Is.EqualTo(ArrivalTick));
            var sides = new int[4];
            for (ulong key = 1; key <= 100; key++)
            {
                SimVec3 at = w.Read<Position>(key).Value;
                Assert.That(Inside(at, new ControlPointDef(2, center, SimFix.FromInt(7), 0)), Is.True, "unit " + key);
                bool onSide = role[key] switch
                {
                    0 => at.X < center.X, 1 => at.Z > center.Z, 2 => at.Z < center.Z, _ => at.X > center.X,
                };
                if (onSide) sides[role[key]]++;
            }
            Assert.That(sides, Is.EqualTo(count));
        }
        // Measured: the tick on which no unit has an active order any more.
        private const int ArrivalTick = 974;

        private static ulong FirstWithRole(int wanted)
        {
            for (ulong key = 1; ; key++) if (RoleOf(Placed(key)) == wanted) return key;
        }

        // One left flanker beside its own point. origin: the own point's x (80 m or 50 m from the target).
        private static (M6World w, ulong key, SimVec3 way, ControlPointDef target) Flanker(int origin, (int, int, int, int)[] walls = null)
        {
            ulong key = FirstWithRole(1);
            ControlPointDef[] points = { Point(1, 100 - origin, 80, 1), Point(2, 100, 80, 0), Point(3, 220, 30, 2) };
            M6World w = World(new[] { M6World.U(key, 1, 102 - origin, 82, speed: 4000) }, points, Ai(), walls);
            return (w, key, SimVec3.Metres(100, 0, 115), points[1]);
        }
        // Walks until it is inside the target; returns the closest it came to the waypoint before that.
        private static SimFix Closest(M6World w, ulong key, SimVec3 way, ControlPointDef target, out long entered)
        {
            SimFix closest = Distance(w.Read<Position>(key).Value, way);
            entered = -1;
            for (int tick = 0; tick < 2000; tick++)
            {
                w.Step();
                SimVec3 at = w.Read<Position>(key).Value;
                if (Inside(at, target)) { entered = w.Sim.Clock.Tick; break; }
                closest = SimFix.Min(closest, Distance(at, way));
            }
            return closest;
        }

        [Test]
        public void Roles_FarFlankerPassesItsWaypoint()
        {
            var (w, key, way, target) = Flanker(80);
            w.Step();
            Assert.That(Flat(w.Read<MoveIntent>(key).Target), Is.EqualTo(Flat(way)));
            SimFix closest = Closest(w, key, way, target, out long entered);
            Assert.That(entered, Is.GreaterThan(0L));
            Assert.That(closest, Is.LessThanOrEqualTo(SimFix.FromInt(3)));
        }

        [Test]
        public void Roles_NearAttackDoesNotDetour()
        {
            var (w, key, way, target) = Flanker(50);
            w.Step();
            Assert.That(Inside(w.Read<MoveIntent>(key).Target, target), Is.True);
            SimFix closest = Closest(w, key, way, target, out long entered);
            Assert.That(entered, Is.GreaterThan(0L));
            Assert.That(closest, Is.GreaterThan(SimFix.FromInt(20)));
        }

        // The waypoint (100, 115) lies inside a wall: the unit goes straight for its slot.
        [Test]
        public void Roles_WaypointInAWallIsSkipped()
        {
            var (w, key, way, target) = Flanker(80, new[] { (95, 110, 10, 10) });
            w.Step();
            MoveIntent intent = w.Read<MoveIntent>(key);
            Assert.That(intent.Active, Is.True);
            Assert.That(Inside(intent.Target, target), Is.True);
            Assert.That(intent.Target.Z, Is.GreaterThan(target.Center.Z));
        }

        // A neutral point centred on the waypoint (100, 115) puts a slot on it: an order there would read as a slot
        // order, so the unit goes straight for its slot at the target.
        [Test]
        public void Roles_WaypointOnASlotIsSkipped()
        {
            ulong key = FirstWithRole(1);
            ControlPointDef[] points = { Point(1, 20, 80, 1), Point(2, 100, 80, 0), Point(3, 100, 115, 0), Point(4, 220, 30, 2) };
            M6World w = World(new[] { M6World.U(key, 1, 22, 82, speed: 4000) }, points, Ai());
            w.Step();
            MoveIntent intent = w.Read<MoveIntent>(key);
            Assert.That(intent.Active, Is.True);
            Assert.That(Inside(intent.Target, points[1]), Is.True);
        }

        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed).WithPack(Pack(FairEFixture.Ai(ai, fair))), FairEFixture.Map("control"));

        // The line fixture with FAIR-E written on or off (or not at all) next to the part.
        private static M6World LineWorld(bool? fair, string ai)
        {
            WorldRecipe recipe = M6World.RecipeOf(Grid(), M6World.Pack(Line(), ticks: 100000, think: 1)).WithRuleSet(MatchMode.TestNeverEndsId);
            recipe = recipe.WithPack(Pack(FairEFixture.Ai(ai, fair)));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(240, 160, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }
        // Units whose active order points outside every point circle: on their way to a waypoint.
        private static int Waypointers(M6World w, ControlPointDef[] points)
        {
            int n = 0;
            for (ulong key = 1; key <= 100; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                if (!intent.Active) continue;
                // Beyond 13 m of every centre: outside a 6 m circle and its 6 m slot ring, so no slot order counts.
                bool near = false;
                foreach (ControlPointDef p in points) near |= Distance(intent.Target, p.Center) <= SimFix.FromInt(13);
                if (!near) n++;
            }
            return n;
        }

        // Same seed twice tick by tick, then a snapshot taken while flankers walk to their waypoints restores to the same
        // battle; with FAIR-E on and off and next to the hold share and the attack spread (the holders go to their own
        // point, where the part gives the default).
        [TestCase(null, null)]
        [TestCase(false, null)]
        [TestCase(true, null)]
        [TestCase(false, "\"holdSharePermille\": 300, \"attackSpreadPoints\": 3")]
        [TestCase(true, "\"holdSharePermille\": 300, \"attackSpreadPoints\": 3")]
        [TestCase(false, "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000")]
        [TestCase(true, "\"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"holdSharePermille\": 300, \"attackSpreadPoints\": 3")]
        public void Roles_RepeatAndRestoreInTheWaypointPhase(bool? fair, string extra)
        {
            string ai = Ai(extra);
            M6World first = LineWorld(fair, ai), repeat = LineWorld(fair, ai);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < RestoreTick; tick++)
            {
                first.Step(); repeat.Step();
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
            Assert.That(Waypointers(first, Line()), Is.GreaterThan(0));
            byte[] snapshot = first.Sim.SaveRetainedSnapshot();
            M6World restored = LineWorld(fair, ai);
            restored.Sim.RestoreRetainedSnapshot(snapshot);
            Assert.That(restored.Hash, Is.EqualTo(first.Hash));
            for (int tick = 0; tick < 600; tick++)
            {
                first.Step(); restored.Step(); repeat.Step();
                Assert.That(restored.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
                Assert.That(repeat.Hash, Is.EqualTo(first.Hash), "tick " + first.Sim.Clock.Tick);
            }
        }
        private const int RestoreTick = 40;

        // The part changes the battle against the same world without it.
        [Test]
        public void Roles_ChangesTheBattle()
        {
            M6World on = Battle(41, null, Ai()), off = new M6World(FairEFixture.Recipe("control", 41), FairEFixture.Map("control"));
            on.Step(600); off.Step(600);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = !on.Read<Position>(key).Value.Equals(off.Read<Position>(key).Value);
            Assert.That(differs, Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Roles_PolicyDigestAndSnapshots(bool fair)
        {
            M6World roles = Battle(41, fair, Ai()), plain = Battle(41, fair, "{}");
            SimpleAiSystem a = roles.System<SimpleAiSystem>(), b = plain.System<SimpleAiSystem>();
            Assert.That(a.StateVersion, Is.EqualTo(b.StateVersion));
            var expected = new StateHash(); expected.Add("battle.ai.assault-roles.v1"); expected.Add(Digest(b));
            foreach (int v in Values) expected.Add(v);
            Assert.That(Digest(a), Is.EqualTo(expected.Value));
            var wa = new SimStateWriter(); a.SaveState(wa);
            var wb = new SimStateWriter(); b.SaveState(wb);
            Assert.Throws<InvalidOperationException>(() => b.LoadState(new SimStateReader(wa.ToArray())));
            Assert.Throws<InvalidOperationException>(() => a.LoadState(new SimStateReader(wb.ToArray())));
            // Every parameter is in the digest.
            var seen = new HashSet<ulong> { Digest(a) };
            for (int f = 0; f < Fields.Length; f++)
            {
                // Shares move in pairs so they still add up to 1000.
                int[] changed = f < 3 ? With((f, Values[f] + 1), ((f + 1) % 3, Values[(f + 1) % 3] - 1)) : With((f, Values[f] + 1));
                Assert.That(seen.Add(Digest(Battle(41, fair, Ai(null, changed)).System<SimpleAiSystem>())), Is.True, Fields[f]);
            }
            roles.Step(); plain.Step();
            byte[] fromRoles = roles.Sim.SaveRetainedSnapshot(), fromPlain = plain.Sim.SaveRetainedSnapshot();
            Assert.Throws<InvalidOperationException>(() => plain.Sim.RestoreRetainedSnapshot(fromRoles));
            Assert.Throws<InvalidOperationException>(() => roles.Sim.RestoreRetainedSnapshot(fromPlain));
        }

        [TestCase(201, 1001)]
        [TestCase(199, 999)]
        public void Roles_SharesMustAddUpTo1000(int rear, int sum)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Ai(null, With((2, rear)))));
            Assert.That(e.Message, Does.Contain("content pack 'roles-test'"));
            Assert.That(e.Message, Does.Contain("ai"));
            Assert.That(e.Message, Does.EndWith("field 'rearPermille': frontPermille + flankPermille + rearPermille must be 1000, got " + sum));
        }

        // Registered in AiSection.Owners: written while another placement is selected, each is an error naming the field.
        [TestCase("{ \"anchorMm\": 10000 }", "anchorMm", "nearest-slot")]
        [TestCase("{ \"placement\": \"nearest-slot\", \"frontPermille\": 425 }", "frontPermille", "nearest-slot")]
        [TestCase("{ \"placement\": \"scatter\", \"scatterMm\": 6000, \"arriveMm\": 3000 }", "arriveMm", "scatter")]
        public void Roles_ParametersBelongToThePart(string ai, string field, string selected)
        {
            var e = Assert.Throws<ContentException>(() => Pack(ai));
            Assert.That(e.Message, Is.EqualTo("content pack 'roles-test' layer 'roles-test' ai field '" + field
                + "': belongs to placement 'assault-roles', but placement is '" + selected + "'"));
        }

        [TestCase(0, "frontPermille")]
        [TestCase(8, "arriveMm")]
        public void Roles_MissingParameterIsNamed(int field, string name)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Ai(null, With((field, Omit)))));
            Assert.That(e.Message, Is.EqualTo("content pack 'roles-test' layer 'roles-test' ai is missing field '" + name + "'"));
        }

        // Every parameter is held to its range.
        [TestCase(3, -1)] [TestCase(3, 50001)] [TestCase(4, 999)] [TestCase(4, 100001)] [TestCase(5, 999)] [TestCase(5, 100001)]
        [TestCase(6, 512001)] [TestCase(7, 999)] [TestCase(7, 50001)] [TestCase(8, 249)] [TestCase(8, 20001)]
        public void Roles_ParameterOutOfRangeIsNamed(int field, int value)
        {
            var e = Assert.Throws<ContentException>(() => Pack(Ai(null, With((field, value)))));
            Assert.That(e.Message, Does.StartWith("content pack 'roles-test' layer 'roles-test' ai field '" + Fields[field] + "': "));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Every think goes through the anchor and the waypoint; with the part on it must not allocate.
        [Test]
        public void Roles_StepAllocatesZero()
        {
            M6World warm = World(Grid(), Line(), Ai());
            warm.Step(30);
            M6World w = World(Grid(), Line(), Ai());
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
