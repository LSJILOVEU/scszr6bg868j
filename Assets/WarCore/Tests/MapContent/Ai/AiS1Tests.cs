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
    // AI-S1: the engage part (ai.engage) decides what a unit that sees an enemy does. "hold" (default) stands and fires as
    // before; "bound" moves a stretch and fires a stretch, each unit on its own phase, some units never move, and nobody
    // moves closer once the enemy is within boundMinRangeMm.
    public sealed class AiS1Tests
    {
        private const string S1 = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }";
        // The part's constants, written out (BoundEngage.ShareSalt, PhaseSalt).
        private const ulong ShareSalt = 0x3c6ef372fe94f82bUL, PhaseSalt = 0xa54ff53a5f1d36f1UL;
        private const int Think = 30, Move = 60, Fire = 90, Cycle = Move + Fire;

        private static ContentPack Pack(string ai) => ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
        {
            ["s1-test"] = "{ \"id\": \"s1-test\", \"extends\": \"rwr\"" + (ai == null ? "" : ", \"ai\": " + ai) + " }"
        }, FileContentSource.Rwr()), "s1-test");

        private static string Bound(int move = Move, int fire = Fire, int share = 700, int range = 12000) =>
            "{ \"engage\": \"bound\", \"boundMoveTicks\": " + move + ", \"boundFireTicks\": " + fire + ", \"boundSharePermille\": " + share + ", \"boundMinRangeMm\": " + range + " }";

        private static byte[] Flat() => WcMapWriter.Write(Wp23.Make(160, 60, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(), 2 * SimFix.Scale, null));
        // One point, owned by team 2 at (100, 20), radius 6; it never flips.
        private static ControlPointDef[] OnePoint() => new[] { new ControlPointDef(1, SimVec3.Metres(100, 0, 20), SimFix.FromInt(6), 2) };

        // The plain fixture: 20 armed attackers abreast at x = 40 (z = 10.5, 11.5, ..., 29.5), 60 m from the point; 10 armed
        // defenders in its circle (x 98 / 102, z 16..24). Shots do no damage, nobody dies.
        private const int Attackers = 20, Defenders = 10;
        private static M6World Field(string ai, int think = Think)
        {
            var spawns = new List<UnitSpawn>();
            for (int i = 0; i < Attackers; i++) spawns.Add(new UnitSpawn((ulong)i + 1, 0, 1, 40000, 10500 + 1000 * i, 6000, 1, 0, false));
            for (int i = 0; i < Defenders; i++) spawns.Add(M6World.U((ulong)(Attackers + i + 1), 2, i < 5 ? 98 : 102, 16 + i % 5 * 2, weapon: 1, speed: 6000));
            WorldRecipe recipe = M6World.RecipeOf(spawns.ToArray(), M6World.Pack(OnePoint(), ticks: 1000000, think: think), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId);
            if (ai != null) recipe = recipe.WithPack(Pack(ai));
            return new M6World(recipe, Flat());
        }

        private static M6World Battle(ulong seed, bool? fair, string ai) =>
            new M6World(FairEFixture.Recipe("control", seed).WithPack(Pack(FairEFixture.Ai(ai, fair))), FairEFixture.Map("control"));

        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static AiField FieldOf(M6World w) => (AiField)typeof(SimpleAiSystem).GetField("field", Private).GetValue(w.System<SimpleAiSystem>());
        private static ulong Key(M6World w, ulong key) => w.Sim.Entities.StableOf(w.Unit(key)).Value;
        private static ulong Digest(SimpleAiSystem ai)
        { var writer = new SimStateWriter(); ai.SaveState(writer); return new SimStateReader(writer.ToArray()).UInt64(); }
        private static ulong BoundTag(ulong digest, int move, int fire, int share, int range)
        { var h = new StateHash(); h.Add("battle.ai.bound.v1"); h.Add(digest); h.Add(move); h.Add(fire); h.Add(share); h.Add(range); return h.Value; }
        private static long Horizontal2(SimVec3 a, SimVec3 b) { long dx = a.X.Raw - b.X.Raw, dz = a.Z.Raw - b.Z.Raw; return dx * dx + dz * dz; }
        private static int Entries(TargetSuppress suppress) => (int)typeof(TargetSuppress).GetProperty("Count", Private).GetValue(suppress);
        private static long Metres2(int m) => (long)m * SimFix.Scale * m * SimFix.Scale;

        // The splitmix64 finalizer, written out independently of AiParts.Mix.
        private static ulong Finalize(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }
        private static bool Advancer(ulong stable, int share = 700) => (int)(Finalize(stable ^ ShareSalt) % 1000UL) < share;
        private static long Phase(ulong stable) => (long)(Finalize(stable ^ PhaseSalt) % Cycle);
        private static bool InMoveWindow(ulong stable, long tick) => (tick + Phase(stable)) % Cycle < Move;
        // The scheduler's think rule without fair decisions: (tick + key % thinkInterval) % thinkInterval == 0.
        private static bool Thinks(ulong stable, long tick) => (tick + (long)(stable % Think)) % Think == 0;
        private static long NearestDefender2(M6World w, SimVec3 here)
        {
            long best = long.MaxValue;
            for (ulong d = Attackers + 1; d <= Attackers + Defenders; d++) best = Math.Min(best, Horizontal2(here, w.Read<Position>(d).Value));
            return best;
        }

        // ---- Parts ----

        [Test]
        public void Permille_IsTheFinalizerModuloAThousand()
        {
            foreach (ulong key in new ulong[] { 0, 1, 2, 0xDEADBEEF, ulong.MaxValue })
                foreach (ulong salt in new ulong[] { 0, ShareSalt, 7 })
                    Assert.That(AiParts.Permille(key, salt), Is.EqualTo((int)(Finalize(key ^ salt) % 1000UL)), "key " + key + " salt " + salt);
        }

        [Test]
        public void Parts_DefaultEngageIsHold()
        {
            Assert.That(new AiParts().Engage, Is.InstanceOf<HoldEngage>());
            Assert.That(ContentPolicies.Parts(null).Engage, Is.InstanceOf<HoldEngage>());
            Assert.That(ContentPolicies.Parts(Pack("{}").Section<AiRules>()).Engage, Is.InstanceOf<HoldEngage>());
            Assert.That(new HoldEngage().Fold(5), Is.EqualTo(5UL));
            var hold = new HoldEngage();
            var unit = new AiUnit(new StableId(1), 1, SimVec3.Metres(0, 0, 0));
            Assert.That(hold.Decide(in unit, SimVec3.Metres(100, 0, 0), null), Is.EqualTo(AiEngage.Hold));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundEngage(0, 90, 700, 12000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundEngage(60, 0, 700, 12000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundEngage(60, 90, -1, 12000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundEngage(60, 90, 1001, 12000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoundEngage(60, 90, 700, -1));
        }

        // The part on its own: share, window and minimum range, at the exact edges.
        [Test]
        public void Bound_DecideAtTheEdges()
        {
            M6World w = Field(null);
            AiField field = FieldOf(w);
            var bound = new BoundEngage(Move, Fire, 1000, 12000);
            const ulong key = 7;
            long phase = Phase(key);
            SimVec3 here = SimVec3.Metres(40, 0, 20);
            var unit = new AiUnit(new StableId(key), 1, here);
            SimVec3 At(long mm) => new SimVec3(here.X + SimFix.Ratio(mm, 1000), here.Y, here.Z);
            int checkedMove = 0, checkedFire = 0;
            for (int t = 0; t < 2 * Cycle; t++)
            {
                w.Step();
                long tick = field.Tick;
                bool window = (tick + phase) % Cycle < Move;
                Assert.That(bound.Decide(in unit, At(30000), field), Is.EqualTo(window ? AiEngage.Advance : AiEngage.Hold), "tick " + tick);
                if (!window) { checkedFire++; continue; }
                checkedMove++;
                // 12 m - 1 mm holds, exactly 12 m advances.
                Assert.That(bound.Decide(in unit, At(11999), field), Is.EqualTo(AiEngage.Hold), "tick " + tick);
                Assert.That(bound.Decide(in unit, At(12000), field), Is.EqualTo(AiEngage.Advance), "tick " + tick);
                // Horizontal only: height does not count (50 m up, 1 mm inside the range still holds).
                SimVec3 above = At(11999); above = new SimVec3(above.X, above.Y + SimFix.FromInt(50), above.Z);
                Assert.That(bound.Decide(in unit, above, field), Is.EqualTo(AiEngage.Hold), "tick " + tick);
                above = At(12000); above = new SimVec3(above.X, above.Y + SimFix.FromInt(50), above.Z);
                Assert.That(bound.Decide(in unit, above, field), Is.EqualTo(AiEngage.Advance), "tick " + tick);
            }
            Assert.That((checkedMove, checkedFire), Is.EqualTo((2 * Move, 2 * Fire)));
            // Share 0: nobody advances; share 1000: everybody can.
            var none = new BoundEngage(Move, Fire, 0, 12000);
            for (ulong k = 1; k <= 200; k++)
            {
                var u = new AiUnit(new StableId(k), 1, here);
                Assert.That(none.Decide(in u, At(30000), field), Is.EqualTo(AiEngage.Hold));
            }
            // The share edge: a unit whose trait equals the share holds, one more permille lets it advance.
            int trait = AiParts.Permille(key, ShareSalt);
            long inWindow = 0;
            while ((field.Tick + inWindow + phase) % Cycle >= Move) inWindow++;
            for (long s = 0; s < inWindow; s++) w.Step();
            Assert.That(new BoundEngage(Move, Fire, trait, 0).Decide(in unit, At(30000), field), Is.EqualTo(AiEngage.Hold));
            Assert.That(new BoundEngage(Move, Fire, trait + 1, 0).Decide(in unit, At(30000), field), Is.EqualTo(AiEngage.Advance));
            // Share 700: the advancers are exactly the keys whose trait is below 700.
            var seven = new BoundEngage(Move, Fire, 700, 0);
            for (ulong k = 1; k <= 200; k++)
            {
                var u = new AiUnit(new StableId(k), 1, here);
                bool window = (field.Tick + Phase(k)) % Cycle < Move;
                Assert.That(seven.Decide(in u, At(30000), field) == AiEngage.Advance, Is.EqualTo(Advancer(k) && window), "key " + k);
            }
        }

        // ---- Battles on the plain fixture ----

        private sealed class Track
        {
            internal readonly List<SimVec3> At = new List<SimVec3>();
            internal readonly List<bool> Active = new List<bool>();
            internal readonly List<long> Tick = new List<long>();
        }
        private static Track[] Run(M6World w, int ticks)
        {
            var tracks = new Track[Attackers];
            for (int i = 0; i < Attackers; i++) tracks[i] = new Track();
            AiField field = FieldOf(w);
            for (int t = 0; t < ticks; t++)
            {
                w.Step();
                for (int i = 0; i < Attackers; i++)
                {
                    ulong key = (ulong)i + 1;
                    tracks[i].At.Add(w.Read<Position>(key).Value);
                    tracks[i].Active.Add(w.Read<MoveIntent>(key).Active);
                    tracks[i].Tick.Add(field.Tick);
                }
            }
            return tracks;
        }

        // The first tick a unit that walked stops; -1 when it never stops.
        private static int FirstStop(Track t)
        {
            for (int i = 1; i < t.Active.Count; i++) if (t.Active[i - 1] && !t.Active[i]) return i;
            return -1;
        }

        // Hold: every attacker stops on the think it first sees a defender (within 35 m) and stays put for 300 ticks;
        // nobody comes closer than 20 m.
        [TestCase(null)]
        [TestCase("{ \"engage\": \"hold\" }")]
        public void Hold_StopsOnFirstSightAndStays(string ai)
        {
            M6World w = Field(ai);
            Track[] tracks = Run(w, 600);
            for (int i = 0; i < Attackers; i++)
            {
                ulong stable = Key(w, (ulong)i + 1);
                int stop = FirstStop(tracks[i]);
                Assert.That(stop, Is.GreaterThan(0), "unit " + (i + 1));
                Assert.That(stop + 300, Is.LessThan(tracks[i].At.Count));
                Assert.That(Thinks(stable, tracks[i].Tick[stop]), Is.True, "unit " + (i + 1) + " stopped off its think");
                for (int k = stop; k <= stop + 300; k++)
                {
                    Assert.That(tracks[i].At[k], Is.EqualTo(tracks[i].At[stop]), "unit " + (i + 1) + " tick " + tracks[i].Tick[k]);
                    Assert.That(tracks[i].Active[k], Is.False);
                }
                long nearest = long.MaxValue;
                for (ulong d = Attackers + 1; d <= Attackers + Defenders; d++) nearest = Math.Min(nearest, Horizontal2(tracks[i].At[stop], w.Read<Position>(d).Value));
                Assert.That(nearest, Is.LessThanOrEqualTo(Metres2(35)), "unit " + (i + 1));
                Assert.That(nearest, Is.GreaterThan(Metres2(20)), "unit " + (i + 1));
                // The think before the stop did not see anyone yet (the unit was still walking).
                Assert.That(tracks[i].Active[stop - Think], Is.True, "unit " + (i + 1));
            }
        }

        // Bound: the advancers are exactly the keys whose share trait is below 700; three of them, phases worked out by
        // hand, have a move order on every think in their move window and none on every think in their fire window while
        // they see a defender at 13..30 m; the non-advancers behave as under hold.
        [Test]
        public void Bound_AdvancersMoveInTheirWindowOnly()
        {
            M6World w = Field(S1);
            var advancers = new List<int>();
            for (int i = 0; i < Attackers; i++) if (Advancer(Key(w, (ulong)i + 1))) advancers.Add(i);
            // Worked out once from the stable keys of units 1..20 with the formula above (and checked outside the code):
            // units 1, 2 and 4 are the first advancers, phases 147, 21 and 66.
            Assert.That(advancers.Count, Is.EqualTo(ExpectedAdvancers));
            int[] chosen = { advancers[0], advancers[1], advancers[2] };
            long[] phases = { Phase(Key(w, (ulong)chosen[0] + 1)), Phase(Key(w, (ulong)chosen[1] + 1)), Phase(Key(w, (ulong)chosen[2] + 1)) };
            Assert.That(phases, Is.EqualTo(ExpectedPhases));

            Track[] tracks = Run(w, 900);
            foreach (int i in chosen)
            {
                ulong stable = Key(w, (ulong)i + 1);
                int moveThinks = 0, fireThinks = 0;
                for (int k = 0; k < tracks[i].Tick.Count; k++)
                {
                    long tick = tracks[i].Tick[k];
                    if (!Thinks(stable, tick)) continue;
                    // Position before this tick's move is the one recorded a tick earlier.
                    SimVec3 before = k == 0 ? tracks[i].At[0] : tracks[i].At[k - 1];
                    long nearest = NearestDefender2(w, before);
                    if (nearest > Metres2(30) || nearest < Metres2(13)) continue;
                    bool window = InMoveWindow(stable, tick);
                    Assert.That(tracks[i].Active[k], Is.EqualTo(window), "unit " + (i + 1) + " tick " + tick);
                    if (window) moveThinks++; else fireThinks++;
                }
                Assert.That(moveThinks, Is.GreaterThan(0), "unit " + (i + 1));
                Assert.That(fireThinks, Is.GreaterThan(0), "unit " + (i + 1));
            }
            // Advancers get closer than any unit does under hold; non-advancers stop on first sight and stay as under hold.
            int closer = 0;
            for (int i = 0; i < Attackers; i++)
            {
                if (advancers.Contains(i))
                {
                    if (NearestDefender2(w, tracks[i].At[tracks[i].At.Count - 1]) < Metres2(20)) closer++;
                    continue;
                }
                int stop = FirstStop(tracks[i]);
                Assert.That(stop, Is.GreaterThan(0), "unit " + (i + 1));
                for (int k = stop; k <= stop + 300; k++)
                    Assert.That(tracks[i].At[k], Is.EqualTo(tracks[i].At[stop]), "unit " + (i + 1) + " tick " + tracks[i].Tick[k]);
            }
            Assert.That(closer, Is.EqualTo(advancers.Count));
        }

        private const int ExpectedAdvancers = 11;
        private static readonly long[] ExpectedPhases = { 147, 21, 66 };

        // One attacker, one enemy straight ahead on the way to the point, everybody an advancer. With the enemy 1 mm
        // inside boundMinRangeMm the attacker never gets a move order; at exactly the range it does.
        [TestCase(11999, false)]
        [TestCase(12000, true)]
        public void Bound_MinimumRangeInTheScheduler(int gapMm, bool moves)
        {
            var spawns = new[]
            {
                new UnitSpawn(1, 0, 1, 40000, 20000, 6000, 1, 0, false),
                new UnitSpawn(2, 0, 2, 40000 + gapMm, 20000, 6000, 2, 1, false),
            };
            WorldRecipe recipe = M6World.RecipeOf(spawns, M6World.Pack(OnePoint(), ticks: 1000000, think: Think), M6World.Guns(new ZeroResolver()))
                .WithRuleSet(MatchMode.TestNeverEndsId).WithPack(Pack(Bound(share: 1000)));
            var w = new M6World(recipe, Flat());
            SimVec3 start = w.Read<Position>(1).Value;
            Assert.That(Horizontal2(start, w.Read<Position>(2).Value), Is.EqualTo(Horizontal2(SimVec3.Metres(0, 0, 0), new SimVec3(SimFix.Ratio(gapMm, 1000), default, default))));
            bool everActive = false;
            for (int t = 0; t < 2 * Cycle; t++)
            {
                w.Step();
                everActive |= w.Read<MoveIntent>(1).Active;
                if (!moves) Assert.That(w.Read<Position>(1).Value, Is.EqualTo(start), "tick " + t);
            }
            Assert.That(everActive, Is.EqualTo(moves));
        }

        // An Advance think records the sighting as hold does, and neither suppression nor search runs: the order goes to
        // a guard slot at the point, not to the enemy, and the suppression table stays empty.
        [Test]
        public void Bound_AdvanceSkipsSuppressionAndSearch()
        {
            const string memory = "\"targetMemoryTicks\": 300, \"searchTicks\": 90, \"suppressTicks\": 90";
            var spawns = new[]
            {
                new UnitSpawn(1, 0, 1, 40000, 20000, 6000, 1, 0, false),
                new UnitSpawn(2, 0, 2, 62000, 32000, 6000, 2, 1, false),
            };
            M6World Make(string engage)
            {
                WorldRecipe recipe = M6World.RecipeOf(spawns, M6World.Pack(OnePoint(), ticks: 1000000, think: Think), M6World.Guns(new ZeroResolver()))
                    .WithRuleSet(MatchMode.TestNeverEndsId).WithPack(Pack("{ " + memory + engage + " }"));
                return new M6World(recipe, Flat());
            }
            M6World bound = Make(", \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 1000, \"boundMinRangeMm\": 12000");
            M6World hold = Make("");
            TargetMemory contactsB = bound.System<TargetMemory>(), contactsH = hold.System<TargetMemory>();
            TargetSuppress suppress = bound.System<TargetSuppress>();
            Assert.That(contactsB, Is.Not.Null); Assert.That(suppress, Is.Not.Null); Assert.That(bound.System<TargetSearch>(), Is.Not.Null);
            AiField field = FieldOf(bound);
            ulong me = Key(bound, 1), enemy = Key(bound, 2);
            SimVec3 point = OnePoint()[0].Center;
            int advances = 0, firstThink = -1;
            for (int t = 0; t < 300; t++)
            {
                SimVec3 before = bound.Read<Position>(1).Value;
                bound.Step(); hold.Step();
                long tick = field.Tick;
                if (!Thinks(me, tick)) continue;
                if (firstThink < 0)
                {
                    // The first think: both worlds stand where they spawned and record the same sighting.
                    firstThink = t;
                    Assert.That(contactsH.TryRecall(new StableId(me), tick, out StableId th, out SimVec3 ah, out long sh), Is.True);
                    Assert.That(contactsB.TryRecall(new StableId(me), tick, out StableId tb, out SimVec3 ab, out long sb), Is.True);
                    Assert.That((tb, ab, sb), Is.EqualTo((th, ah, sh)));
                }
                if (Horizontal2(before, bound.Read<Position>(2).Value) < Metres2(13)) break;
                Assert.That(contactsB.TryRecall(new StableId(me), tick, out StableId target, out _, out long seen), Is.True, "tick " + tick);
                Assert.That((target.Value, seen), Is.EqualTo((enemy, tick)), "tick " + tick);
                Assert.That(Entries(suppress), Is.EqualTo(0), "tick " + tick);
                MoveIntent order = bound.Read<MoveIntent>(1);
                Assert.That(order.Active, Is.EqualTo(InMoveWindow(me, tick)), "tick " + tick);
                if (!order.Active) continue;
                advances++;
                Assert.That(Horizontal2(order.Target, point), Is.LessThanOrEqualTo(Metres2(6)), "tick " + tick + ": the order must go to the point");
            }
            Assert.That(firstThink, Is.InRange(0, Think - 1));
            Assert.That(advances, Is.GreaterThan(0));
        }

        // ---- Battles on the control layout ----

        // Writing the default out is the same battle tick by tick, with the same digest.
        [Test]
        public void Battle_HoldWrittenOutLeavesTheBattle()
        {
            WorldRecipe recipe = FairEFixture.Recipe("control", 41);
            M6World written = new M6World(recipe.WithPack(Pack("{ \"engage\": \"hold\" }")), FairEFixture.Map("control"));
            M6World plain = new M6World(recipe, FairEFixture.Map("control"));
            Assert.That(Digest(written.System<SimpleAiSystem>()), Is.EqualTo(Digest(plain.System<SimpleAiSystem>())));
            for (int tick = 0; tick < 600; tick++)
            {
                written.Step(); plain.Step();
                Assert.That(written.Hash, Is.EqualTo(plain.Hash), "tick " + plain.Sim.Clock.Tick);
            }
        }

        [Test]
        public void Battle_BoundChangesTheBattle()
        {
            M6World on = Battle(41, null, S1), off = new M6World(FairEFixture.Recipe("control", 41), FairEFixture.Map("control"));
            on.Step(900); off.Step(900);
            bool differs = false;
            for (ulong key = 1; key <= 300 && !differs; key++)
                differs = !on.Read<Position>(key).Value.Equals(off.Read<Position>(key).Value);
            Assert.That(differs, Is.True);
        }

        private const string Combo1 = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, \"holdSharePermille\": 300, \"attackSpreadPoints\": 3 }";
        private const string Combo2 = "{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000, \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }";

        // Same seed twice, tick by tick; a snapshot taken in the middle of unit 1's move window restores to the same battle.
        [TestCase(null, S1)]
        [TestCase(false, S1)]
        [TestCase(true, S1)]
        [TestCase(null, Combo1)]
        [TestCase(true, Combo1)]
        [TestCase(null, Combo2)]
        [TestCase(true, Combo2)]
        public void Battle_RepeatAndRestoreInTheMoveWindow(bool? fair, string ai)
        {
            M6World first = Battle(41, fair, ai), repeat = Battle(41, fair, ai);
            Assert.That(repeat.Hash, Is.EqualTo(first.Hash));
            ulong one = Key(first, 1);
            AiField field = FieldOf(first);
            int stepped = 0;
            // Contact happens after a few hundred ticks; save at the first tick from 600 on that sits mid-window for unit 1.
            while (stepped < 600 || (field.Tick + Phase(one)) % Cycle != Move / 2)
            {
                first.Step(); repeat.Step(); stepped++;
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

        // Digest: bound folds last with its tag and four parameters; each parameter changes it; snapshots refuse each other.
        [TestCase(false)]
        [TestCase(true)]
        public void Battle_PolicyDigestAndSnapshots(bool fair)
        {
            string[] ais = { "{}", S1, Bound(move: 90), Bound(fire: 60), Bound(share: 699), Bound(range: 11999), Combo2 };
            var worlds = new M6World[ais.Length]; var digests = new ulong[ais.Length]; var saved = new byte[ais.Length][];
            for (int i = 0; i < ais.Length; i++)
            {
                worlds[i] = Battle(41, fair, ais[i]);
                digests[i] = Digest(worlds[i].System<SimpleAiSystem>());
            }
            ulong d = digests[0];
            Assert.That(digests[1], Is.EqualTo(BoundTag(d, 60, 90, 700, 12000)));
            Assert.That(digests[2], Is.EqualTo(BoundTag(d, 90, 90, 700, 12000)));
            Assert.That(digests[3], Is.EqualTo(BoundTag(d, 60, 60, 700, 12000)));
            Assert.That(digests[4], Is.EqualTo(BoundTag(d, 60, 90, 699, 12000)));
            Assert.That(digests[5], Is.EqualTo(BoundTag(d, 60, 90, 700, 11999)));
            // Engage folds after the slot ring.
            M6World p1 = Battle(41, fair, "{ \"slotSpacingMm\": 2000, \"slotRingMm\": 6000, \"placement\": \"scatter\", \"scatterMm\": 6000 }");
            Assert.That(digests[6], Is.EqualTo(BoundTag(Digest(p1.System<SimpleAiSystem>()), 60, 90, 700, 12000)));
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

        // ---- Content ----

        [Test]
        public void Content_FieldsAreReadAndBuildThePart()
        {
            AiRules rules = Pack(S1).Section<AiRules>();
            Assert.That((rules.Engage, rules.BoundMoveTicks, rules.BoundFireTicks, rules.BoundSharePermille, rules.BoundMinRangeMm),
                Is.EqualTo(("bound", 60, 90, 700, 12000)));
            IAiEngage part = ContentPolicies.Parts(rules, default, Think).Engage;
            Assert.That(part, Is.InstanceOf<BoundEngage>());
            Assert.That(part.Fold(5), Is.EqualTo(BoundTag(5, 60, 90, 700, 12000)));
            AiRules plain = Pack("{}").Section<AiRules>();
            Assert.That((plain.Engage, plain.BoundMoveTicks, plain.BoundFireTicks, plain.BoundSharePermille, plain.BoundMinRangeMm), Is.EqualTo(("hold", 0, 0, 0, 0)));
            // Range edges are accepted.
            Assert.That(Pack(Bound(move: 1, fire: 1, share: 0, range: 0)).Section<AiRules>().BoundMoveTicks, Is.EqualTo(1));
            Assert.That(Pack(Bound(move: 100000, fire: 100000, share: 1000, range: 512000)).Section<AiRules>().BoundMinRangeMm, Is.EqualTo(512000));
            // The playtest layer's own rules stay when a layer on top selects bound.
            AiRules layered = ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string>
            {
                ["s1-layer"] = "{ \"id\": \"s1-layer\", \"extends\": \"" + DemoRecipes.PlaytestLayer + "\", \"ai\": " + S1 + " }"
            }, FileContentSource.Rwr()), "s1-layer").Section<AiRules>();
            Assert.That((layered.HoldSharePermille, layered.Engage), Is.EqualTo((300, "bound")));
        }

        [TestCase("{ \"engage\": \"charge\" }", "engage", "unknown policy 'charge', allowed: hold, bound")]
        [TestCase("{ \"boundMoveTicks\": 60 }", "boundMoveTicks", "belongs to engage 'bound', but engage is 'hold'")]
        [TestCase("{ \"engage\": \"hold\", \"boundMinRangeMm\": 12000 }", "boundMinRangeMm", "belongs to engage 'bound', but engage is 'hold'")]
        [TestCase("{ \"engage\": \"hold\", \"boundSharePermille\": 700 }", "boundSharePermille", "belongs to engage 'bound', but engage is 'hold'")]
        [TestCase("{ \"engage\": \"hold\", \"boundFireTicks\": 90 }", "boundFireTicks", "belongs to engage 'bound', but engage is 'hold'")]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 0, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }", "boundMoveTicks", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 100001, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }", "boundMoveTicks", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 0, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }", "boundFireTicks", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 100001, \"boundSharePermille\": 700, \"boundMinRangeMm\": 12000 }", "boundFireTicks", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": -1, \"boundMinRangeMm\": 12000 }", "boundSharePermille", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 1001, \"boundMinRangeMm\": 12000 }", "boundSharePermille", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": -1 }", "boundMinRangeMm", null)]
        [TestCase("{ \"engage\": \"bound\", \"boundMoveTicks\": 60, \"boundFireTicks\": 90, \"boundSharePermille\": 700, \"boundMinRangeMm\": 512001 }", "boundMinRangeMm", null)]
        public void Content_BadFieldNamesPackEntryAndField(string ai, string field, string why)
        {
            var e = Assert.Throws<ContentException>(() => Pack(ai));
            Assert.That(e.Message, Does.StartWith("content pack 's1-test' layer 's1-test' ai field '" + field + "': "));
            if (why != null) Assert.That(e.Message, Does.EndWith(why));
        }

        [TestCase("boundMoveTicks")]
        [TestCase("boundFireTicks")]
        [TestCase("boundSharePermille")]
        [TestCase("boundMinRangeMm")]
        public void Content_BoundWithoutAFieldIsAnError(string missing)
        {
            var fields = new List<string>();
            foreach (var (name, value) in new[] { ("boundMoveTicks", 60), ("boundFireTicks", 90), ("boundSharePermille", 700), ("boundMinRangeMm", 12000) })
                if (name != missing) fields.Add("\"" + name + "\": " + value);
            var e = Assert.Throws<ContentException>(() => Pack("{ \"engage\": \"bound\", " + string.Join(", ", fields) + " }"));
            Assert.That(e.Message, Is.EqualTo("content pack 's1-test' layer 's1-test' ai is missing field '" + missing + "'"));
        }

        [Test]
        public void Content_ParametersOfAnUnselectedEngageAreFound()
        {
            Assert.That(AiSection.Misplaced(f => f == "boundMoveTicks", "nearest", "nearest-slot", "hold"), Is.EqualTo(3));
            Assert.That(AiSection.Misplaced(f => f == "boundMinRangeMm", "nearest", "nearest-slot", "other"), Is.EqualTo(6));
            Assert.That(AiSection.Misplaced(f => f.StartsWith("bound"), "nearest", "nearest-slot", "bound"), Is.EqualTo(-1));
            // Without the engage argument the default (hold) is selected.
            Assert.That(AiSection.Misplaced(f => f == "boundFireTicks", "nearest", "nearest-slot"), Is.EqualTo(4));
            // Engage parameters do not depend on the dispatch or the placement.
            Assert.That(AiSection.Misplaced(f => f == "boundSharePermille", "other", "other", "bound"), Is.EqualTo(-1));
        }

        // The windows must be whole multiples of the battle's thinkInterval, checked where the battle is built.
        [TestCase(45, 90, "boundMoveTicks")]
        [TestCase(60, 100, "boundFireTicks")]
        public void Content_WindowsMustBeWholeThinks(int move, int fire, string field)
        {
            AiRules rules = Pack(Bound(move: move, fire: fire)).Section<AiRules>();
            var e = Assert.Throws<ContentException>(() => ContentPolicies.Parts(rules, default, Think));
            Assert.That(e.Message, Is.EqualTo("content pack 's1-test' layer 's1-test' ai field '" + field + "': must be a whole multiple of the battle's thinkInterval (30)"));
            Assert.That(ContentPolicies.Parts(rules, default, 5).Engage, Is.InstanceOf<BoundEngage>());
            Assert.That(ContentPolicies.Parts(Pack(S1).Section<AiRules>(), default, Think).Engage, Is.InstanceOf<BoundEngage>());
            // Through the recipe: building the battle fails the same way.
            Exception thrown = Assert.Catch(() => Field(Bound(move: move, fire: fire)));
            while (thrown is TargetInvocationException && thrown.InnerException != null) thrown = thrown.InnerException;
            Assert.That(thrown, Is.InstanceOf<ContentException>());
            Assert.That(thrown.Message, Is.EqualTo(e.Message));
        }

#if !UNITY_EDITOR && !UNITY_5_3_OR_NEWER
        // Every think of a unit in sight goes through the bound part; it must not allocate.
        [Test]
        public void Battle_StepAllocatesZero()
        {
            M6World warm = Field(S1);
            warm.Step(300);
            M6World w = Field(S1);
            w.Step(300);
            AllocationMeter.Stabilize();
            long total = 0;
            for (int i = 0; i < Cycle; i++)
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
