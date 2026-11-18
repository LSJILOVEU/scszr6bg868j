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
    // AI-P0: the AI scheduler asks two parts where a unit goes (dispatch) and where at the point it wants to stand
    // (placement). The default parts leave every battle as it was; these tests pin the seam with scripted parts.
    public sealed class AiPartsTests
    {
        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["parts-test"] = "{ \"id\": \"parts-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "parts-test");

        private static ControlPointDef Point(int id, int x, int owner) => new ControlPointDef(id, SimVec3.Metres(x, 0, 20), SimFix.FromInt(6), owner);

        // Unweaponed units of one team on a 2 m grid (x 21 to 59, z 30 to 38); every unit thinks every tick.
        private static M6World World(int units, ControlPointDef[] points, string ai = null, int team = 1, long speed = 1, int captureTicks = 3, SpawnAnchor[] bases = null)
        {
            var spawns = new UnitSpawn[units];
            for (int i = 0; i < units; i++) spawns[i] = M6World.U((ulong)i + 1, team, 21 + i % 20 * 2, 30 + i / 20 * 2, speed: speed);
            WorldRecipe recipe = M6World.RecipeOf(spawns, M6World.Pack(points, ticks: captureTicks, think: 1, bases: bases)).WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithAiRules(Pack(ai));
            return new M6World(recipe, WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null)));
        }

        private static ControlPointDef[] Four() => new[] { Point(1, 5, 1), Point(2, 150, 2), Point(3, 80, 0), Point(4, 115, 2) };

        private static ulong Key(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key)).Value;
        private static bool Inside(SimVec3 at, ControlPointDef p) =>
            CombatSystem.Horizontal(at - p.Center).LengthSquared <= new SimVec3(p.Radius, default, default).LengthSquared;
        private static ulong Tag(string tag, ulong digest) { var h = new StateHash(); h.Add(tag); h.Add(digest); return h.Value; }
        private static ulong Digest(SimpleAiSystem ai)
        { var writer = new SimStateWriter(); ai.SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static void Install(M6World w, IAiDispatch dispatch, IAiPlacement placement)
        {
            SimpleAiSystem ai = w.System<SimpleAiSystem>();
            if (dispatch != null) typeof(SimpleAiSystem).GetField("dispatch", Private).SetValue(ai, dispatch);
            if (placement != null) typeof(SimpleAiSystem).GetField("placement", Private).SetValue(ai, placement);
        }

        // A dispatch that sends each unit where the test says and keeps what the scheduler showed it.
        private sealed class ScriptedDispatch : IAiDispatch
        {
            internal Func<ulong, int> Choice;
            internal readonly List<AiUnit> Seen = new List<AiUnit>();
            internal long Tick = -1;
            internal int Points = -1, Frontline = -2;
            internal SimVec3[] Centers;
            internal int[] Owners;
            internal bool HasBase, HasOtherBase;
            internal SimVec3 Base;
            public ulong Fold(ulong digest) => Tag("test.dispatch", digest);
            public int Pick(in AiUnit unit, AiField field)
            {
                Seen.Add(unit); Tick = field.Tick; Points = field.PointCount; Frontline = field.Frontline(unit.Team);
                Centers = new SimVec3[field.PointCount]; Owners = new int[field.PointCount];
                for (int i = 0; i < field.PointCount; i++) { Centers[i] = field.Center(i); Owners[i] = field.Owner(i); }
                HasBase = field.TryBase(unit.Team, out Base); HasOtherBase = field.TryBase(unit.Team + 2, out _);
                return Choice(unit.Id.Value);
            }
        }

        // A placement that wants every unit near one spot and keeps what the scheduler asked it.
        private sealed class ScriptedPlacement : IAiPlacement
        {
            internal SimVec3 Spot;
            internal readonly List<(ulong id, int point, SimVec3 here)> Asked = new List<(ulong, int, SimVec3)>();
            public ulong Fold(ulong digest) => Tag("test.placement", digest);
            public SimVec3 Anchor(in AiUnit unit, int point, AiField field) { Asked.Add((unit.Id.Value, point, unit.Here)); return Spot; }
        }

        // The scheduler shows the dispatch each thinking unit and the field, sends the unit to the point the dispatch
        // names (its own team's point included), and stops it when the dispatch names none.
        [Test]
        public void Parts_UnitsGoWhereTheDispatchSays()
        {
            const int units = 100;
            ControlPointDef[] points = Four();
            M6World w = World(units, points);
            var dispatch = new ScriptedDispatch { Choice = id => (int)(id % 5) - 1 };
            Install(w, dispatch, null);
            var before = new Dictionary<ulong, SimVec3>();
            for (ulong key = 1; key <= units; key++) before[Key(w, key)] = w.Read<Position>(key).Value;
            w.Step();

            Assert.That(dispatch.Seen.Count, Is.EqualTo(units));
            Assert.That(w.Sim.Clock.Tick, Is.EqualTo(1L));
            Assert.That(dispatch.Tick, Is.EqualTo(1L));
            Assert.That(dispatch.Points, Is.EqualTo(4));
            Assert.That(dispatch.Frontline, Is.EqualTo(0));
            Assert.That(dispatch.HasBase, Is.True);
            Assert.That(dispatch.Base, Is.EqualTo(M6World.P(6)));
            Assert.That(dispatch.HasOtherBase, Is.False);
            for (int i = 0; i < points.Length; i++)
            {
                Assert.That(dispatch.Centers[i], Is.EqualTo(points[i].Center), "point " + i);
                Assert.That(dispatch.Owners[i], Is.EqualTo(points[i].InitialOwner), "point " + i);
            }
            foreach (AiUnit seen in dispatch.Seen)
            {
                Assert.That(seen.Team, Is.EqualTo(1));
                Assert.That(seen.Here, Is.EqualTo(before[seen.Id.Value]));
            }
            var count = new int[points.Length + 1];
            for (ulong key = 1; key <= units; key++)
            {
                int want = (int)(Key(w, key) % 5) - 1;
                count[want + 1]++;
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.EqualTo(want >= 0), "unit " + key);
                if (want >= 0) Assert.That(Inside(intent.Target, points[want]), Is.True, "unit " + key);
            }
            for (int i = 0; i < count.Length; i++) Assert.That(count[i], Is.GreaterThan(0), "choice " + (i - 1));

            // From now on the dispatch names no point: every unit that was walking stops. The field shows the owners
            // as they are now, not as the match pack set them.
            dispatch.Choice = id => -1;
            ((PointState[])typeof(CaptureSystem).GetField("states", Private).GetValue(w.Capture))[2] = new PointState(2, 0, 0);
            w.Step();
            Assert.That(dispatch.Tick, Is.EqualTo(2L));
            Assert.That(dispatch.Owners, Is.EqualTo(new[] { 1, 2, 2, 2 }));
            for (ulong key = 1; key <= units; key++) Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "unit " + key);
        }

        // The slots of one point by distance to a spot, nearest first, equal distances to the lower slot number.
        private static List<SimVec3> SlotsNear(ControlPointDef point, SimVec3 spot, int take)
        {
            SimVec3[] slots = GuardSlots.Build(point.Center, point.Radius);
            var order = new List<int>();
            for (int i = 0; i < slots.Length; i++) order.Add(i);
            order.Sort((a, b) =>
            {
                int byDistance = CombatSystem.Horizontal(spot - slots[a]).LengthSquared.CompareTo(CombatSystem.Horizontal(spot - slots[b]).LengthSquared);
                return byDistance != 0 ? byDistance : a.CompareTo(b);
            });
            var result = new List<SimVec3>();
            for (int i = 0; i < take; i++) result.Add(slots[order[i]]);
            return result;
        }
        private static List<(long, long)> Flat(List<SimVec3> points)
        {
            var result = new List<(long, long)>();
            foreach (SimVec3 p in points) result.Add((p.X.Raw, p.Z.Raw));
            result.Sort();
            return result;
        }

        // The units stand west of the point; the placement wants them 4 m east of its centre. They take the slots
        // nearest to that spot, not the slots nearest to themselves, and the placement is asked about the point the
        // dispatch chose.
        [Test]
        public void Parts_UnitsTakeTheSlotsNearestTheAnchor()
        {
            const int units = 7;
            ControlPointDef[] points = { Point(1, 5, 1), Point(2, 100, 2) };
            M6World w = World(units, points);
            var placement = new ScriptedPlacement { Spot = SimVec3.Metres(104, 0, 20) };
            Install(w, null, placement);
            var before = new Dictionary<ulong, SimVec3>();
            for (ulong key = 1; key <= units; key++) before[Key(w, key)] = w.Read<Position>(key).Value;
            w.Step();

            var targets = new List<SimVec3>();
            for (ulong key = 1; key <= units; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                targets.Add(intent.Target);
            }
            Assert.That(Flat(targets), Is.EqualTo(Flat(SlotsNear(points[1], placement.Spot, units))));
            Assert.That(Flat(targets), Is.Not.EqualTo(Flat(SlotsNear(points[1], SimVec3.Metres(21, 0, 30), units))));
            Assert.That(placement.Asked.Count, Is.EqualTo(units));
            foreach ((ulong id, int point, SimVec3 here) in placement.Asked)
            {
                Assert.That(point, Is.EqualTo(1));
                Assert.That(here, Is.EqualTo(before[id]));
            }
        }

        // The spot lies exactly between slots 1 and 2 of the point: the unit takes slot 1.
        [Test]
        public void Parts_EqualDistancesToTheAnchorGoToTheLowerSlot()
        {
            ControlPointDef[] points = { Point(1, 5, 1), Point(2, 100, 2) };
            SimVec3[] slots = GuardSlots.Build(points[1].Center, points[1].Radius);
            var spot = new SimVec3(points[1].Center.X, default, slots[1].Z);
            Assert.That(slots[1].Z, Is.EqualTo(slots[2].Z));
            Assert.That(CombatSystem.Horizontal(spot - slots[1]).LengthSquared, Is.EqualTo(CombatSystem.Horizontal(spot - slots[2]).LengthSquared));
            List<SimVec3> nearest = SlotsNear(points[1], spot, 2);
            Assert.That(nearest[0], Is.EqualTo(slots[1]));
            Assert.That(nearest[1], Is.EqualTo(slots[2]));

            M6World w = World(1, points);
            Install(w, null, new ScriptedPlacement { Spot = spot });
            w.Step();
            MoveIntent intent = w.Read<MoveIntent>(1);
            Assert.That(intent.Active, Is.True);
            Assert.That((intent.Target.X.Raw, intent.Target.Z.Raw), Is.EqualTo((slots[1].X.Raw, slots[1].Z.Raw)));
        }

        // The digest folds in a fixed order: fair decisions, then the dispatch, then the placement.
        [TestCase(false)]
        [TestCase(true)]
        public void Parts_FoldOrderIsFairThenDispatchThenPlacement(bool fair)
        {
            M6World w = World(4, Four());
            var ai = new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, M7GuardTests.Adapter(w),
                fairDecisions: fair, decisionSeed: 7, parts: new AiParts(new ScriptedDispatch(), new ScriptedPlacement()));
            ulong digest = w.Recipe.Match.PolicyDigest;
            if (fair) { var h = new StateHash(); h.Add("battle.ai.fair-decisions.v1"); h.Add(digest); h.Add(true); h.Add(7UL); digest = h.Value; }
            Assert.That(Digest(ai), Is.EqualTo(Tag("test.placement", Tag("test.dispatch", digest))));
            Assert.That(Tag("test.placement", Tag("test.dispatch", digest)), Is.Not.EqualTo(Tag("test.dispatch", Tag("test.placement", digest))));
        }

        // No parts, empty parts and the default parts written out are the same battle: the match pack's digest.
        [Test]
        public void Parts_DefaultsAreNearestAndNearestSlot()
        {
            M6World w = World(4, Four());
            IMoveOrders adapter = M7GuardTests.Adapter(w);
            var none = new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, adapter);
            var empty = new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, adapter, parts: new AiParts());
            var written = new SimpleAiSystem(w.Combat, w.Recipe.Combat, w.Recipe.Match, w.Capture, adapter,
                parts: new AiParts(new NearestDispatch(0, 1), new NearestSlotPlacement()));
            Assert.That(Digest(none), Is.EqualTo(w.Recipe.Match.PolicyDigest));
            Assert.That(Digest(empty), Is.EqualTo(w.Recipe.Match.PolicyDigest));
            Assert.That(Digest(written), Is.EqualTo(w.Recipe.Match.PolicyDigest));
            Assert.That(Digest(w.System<SimpleAiSystem>()), Is.EqualTo(w.Recipe.Match.PolicyDigest));
            var parts = new AiParts();
            Assert.That(parts.Dispatch, Is.InstanceOf<NearestDispatch>());
            Assert.That(parts.Placement, Is.InstanceOf<NearestSlotPlacement>());
            var dispatch = new ScriptedDispatch(); var placement = new ScriptedPlacement();
            Assert.That(new AiParts(dispatch, null).Dispatch, Is.SameAs(dispatch));
            Assert.That(new AiParts(null, placement).Placement, Is.SameAs(placement));
            // The default placement wants the unit where it already is, whatever the point.
            var unit = new AiUnit(new StableId(9), 1, SimVec3.Metres(33, 0, 27));
            Assert.That(parts.Placement.Anchor(in unit, 2, null), Is.EqualTo(SimVec3.Metres(33, 0, 27)));
            Assert.That(parts.Placement.Fold(5), Is.EqualTo(5UL));
        }

        // Team 2 has no spawn anchor here, so the field keeps no front line for it and finds it on every call. All of
        // its units hold: they go to its owned point nearest to a point it does not own (index 1), not to the point
        // an attacker would take (index 2).
        [Test]
        public void Parts_FrontlineOfATeamWithoutAnAnchor()
        {
            const int units = 40;
            ControlPointDef[] points = { Point(1, 10, 2), Point(2, 60, 2), Point(3, 120, 1) };
            M6World w = World(units, points, "{ \"holdSharePermille\": 1000 }", team: 2, bases: new[] { new SpawnAnchor(1, M6World.P(150)) });
            w.Step();
            for (ulong key = 1; key <= units; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                Assert.That(Inside(intent.Target, points[1]), Is.True, "unit " + key);
            }
        }

        // The front line is found again on every Step. Team 1 owns x = 30 and all its units hold there; once the
        // neutral point at x = 60 is handed to team 1, the front line is that point (the nearest to x = 120) and the
        // holders are sent to it on their next think.
        [Test]
        public void Parts_FrontlineFollowsTheOwners()
        {
            const int units = 20;
            ControlPointDef[] points = { Point(1, 30, 1), Point(2, 60, 0), Point(3, 120, 2) };
            M6World w = World(units, points, "{ \"holdSharePermille\": 1000 }");
            w.Step();
            for (ulong key = 1; key <= units; key++) Assert.That(Inside(w.Read<MoveIntent>(key).Target, points[0]), Is.True, "unit " + key);

            var states = (PointState[])typeof(CaptureSystem).GetField("states", Private).GetValue(w.Capture);
            states[1] = new PointState(1, 0, 0);
            w.Step();
            for (ulong key = 1; key <= units; key++)
            {
                MoveIntent intent = w.Read<MoveIntent>(key);
                Assert.That(intent.Active, Is.True, "unit " + key);
                Assert.That(Inside(intent.Target, points[1]), Is.True, "unit " + key);
            }
        }

        // Writing the default ids, an empty section and no section are the same battle, tick by tick, alone and next
        // to the default parts' parameters.
        [TestCase(null, "{ \"dispatch\": \"nearest\", \"placement\": \"nearest-slot\" }")]
        [TestCase("{}", "{ \"dispatch\": \"nearest\" }")]
        [TestCase("{}", "{ \"placement\": \"nearest-slot\" }")]
        [TestCase("{ \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }",
            "{ \"dispatch\": \"nearest\", \"placement\": \"nearest-slot\", \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }")]
        public void Parts_DefaultIdsLeaveTheBattle(string plainAi, string writtenAi)
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41, null);
            M6World plain = new M6World(plainAi == null ? recipe : recipe.WithAiRules(Pack(plainAi)), FairEFixture.Map("control"));
            M6World written = new M6World(recipe.WithAiRules(Pack(writtenAi)), FairEFixture.Map("control"));
            Assert.That(written.Hash, Is.EqualTo(plain.Hash));
            Assert.That(Digest(written.System<SimpleAiSystem>()), Is.EqualTo(Digest(plain.System<SimpleAiSystem>())));
            for (int tick = 0; tick < 300; tick++)
            {
                written.Step(); plain.Step();
                Assert.That(written.Hash, Is.EqualTo(plain.Hash), "tick " + plain.Sim.Clock.Tick);
            }
        }

        [TestCase(null)]
        [TestCase("{}")]
        [TestCase("{ \"dispatch\": \"nearest\", \"placement\": \"nearest-slot\" }")]
        public void Parts_IdsAreReadWithDefaults(string ai)
        {
            AiRules rules = Pack(ai).Section<AiRules>();
            Assert.That(rules.Dispatch, Is.EqualTo("nearest"));
            Assert.That(rules.Placement, Is.EqualTo("nearest-slot"));
            Assert.That(ContentPolicies.DispatchIds, Is.EqualTo("nearest"));
            Assert.That(ContentPolicies.PlacementIds, Is.EqualTo("nearest-slot"));
        }

        [TestCase("dispatch", "\"nearest-slot\"", "unknown policy 'nearest-slot', allowed: nearest")]
        [TestCase("dispatch", "\"\"", "unknown policy '', allowed: nearest")]
        [TestCase("dispatch", "1", "must be a string")]
        [TestCase("placement", "\"nearest\"", "unknown policy 'nearest', allowed: nearest-slot")]
        [TestCase("placement", "\"Nearest-Slot\"", "unknown policy 'Nearest-Slot', allowed: nearest-slot")]
        [TestCase("placement", "true", "must be a string")]
        public void Parts_UnknownIdNamesPackEntryAndField(string field, string value, string why)
        {
            var e = Assert.Throws<ContentException>(() => Pack("{ \"" + field + "\": " + value + " }"));
            Assert.That(e.Message, Does.Contain("content pack 'parts-test'"));
            Assert.That(e.Message, Does.Contain("ai"));
            Assert.That(e.Message, Does.EndWith("field '" + field + "': " + why));
        }

        // The content's parameters reach the default dispatch through the id table, with and without AI rules.
        [Test]
        public void Parts_ContentBuildsTheDefaultParts()
        {
            AiParts none = ContentPolicies.Parts(null);
            Assert.That(none.Dispatch.Fold(5), Is.EqualTo(5UL));
            Assert.That(none.Placement, Is.InstanceOf<NearestSlotPlacement>());
            Assert.That(none.Placement.Fold(5), Is.EqualTo(5UL));
            AiParts both = ContentPolicies.Parts(Pack("{ \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }").Section<AiRules>());
            var hold = new StateHash(); hold.Add("battle.ai.hold-share.v1"); hold.Add(5UL); hold.Add(300);
            var spread = new StateHash(); spread.Add("battle.ai.attack-spread.v1"); spread.Add(hold.Value); spread.Add(3);
            Assert.That(both.Dispatch, Is.InstanceOf<NearestDispatch>());
            Assert.That(both.Dispatch.Fold(5), Is.EqualTo(spread.Value));
            Assert.That(both.Placement.Fold(5), Is.EqualTo(5UL));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Every think of every unit goes through both parts; with the defaults written out and both parameters on it
        // must not allocate.
        [Test]
        public void Parts_StepAllocatesZero()
        {
            const string ai = "{ \"dispatch\": \"nearest\", \"placement\": \"nearest-slot\", \"holdSharePermille\": 300, \"attackSpreadPoints\": 8 }";
            M6World warm = World(100, Four(), ai);
            warm.Step(30);
            M6World w = World(100, Four(), ai);
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
