using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // A3b fixtures: A3's memory geometry (A2's map and wall, grass 500) with the recipe written here so the weapon can be
    // swapped. Observer A (key 1, team 1, armed) stands on the only control point, owned by team 1, so its AI never moves
    // it; it thinks on ticks 6, 16, ... Target B (key 2, team 2, unarmed, squad 2) holds on tick 1 and walks into the grass
    // on tick 20: A selects it for the last time on tick 46 (S), with P its position then, and sees nothing from the
    // thinking tick 56 on. Friend F (key 5, team 1, squad 3, unarmed) stands east of the line A -> P and is placed on it
    // by the tests. A2 (key 4, team 1, armed) is a second shooter south of A.
    internal static class A3b
    {
        internal const ulong KeyA = A3.KeyA, KeyB = A3.KeyB, KeyA2 = 4, KeyF = 5;
        internal const long MoveTick = 20, Seen = 46, Since = 56;
        internal const ulong IdA = 5811216920586418494UL, IdA2 = 15958299891356236636UL;
        internal static readonly SimVec3 BStart = A2.Cell(21, 18), BGrass = A2.Cell(21, 24), FHome = A2.Cell(26, 16), A2At = A2.Cell(22, 13);
        internal static readonly SimVec3 P = Raw(1409024, 0, 1382794);
        internal static readonly SimVec3 Mid = Raw((1474560 + 1409024) / 2, 0, (688128 + 1382794) / 2);
        internal static readonly SimVec3 Behind = Raw(1369702, 0, 1799618);
        internal static readonly SimFix R30 = SimFix.FromInt(30);

        internal static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));
        internal static string V(SimVec3 v) => "(" + v.X.Raw + ", " + v.Y.Raw + ", " + v.Z.Raw + ")";
        internal static bool Same(SimVec3 a, SimVec3 b) => a.X.Raw == b.X.Raw && a.Y.Raw == b.Y.Raw && a.Z.Raw == b.Z.Raw;

        // The ai section: the memory, then the given fields in the pack's order.
        internal static string Ai(int memory, int? suppress = null, int? search = null, int? reaction = null, int? sense = null)
        {
            string s = "{ \"targetMemoryTicks\": " + memory;
            if (search != null) s += ", \"searchTicks\": " + search.Value;
            if (reaction != null) s += ", \"reactionTicks\": " + reaction.Value;
            if (sense != null) s += ", \"senseTicks\": " + sense.Value;
            if (suppress != null) s += ", \"suppressTicks\": " + suppress.Value;
            return s + " }";
        }

        // The range fixture's weapon: as A2's rifle but falloff 5..15 m.
        internal static WeaponDef Rifle15() => new WeaponDef(30000, 3, 60000, SimFix.FromInt(5), SimFix.FromInt(15), 0);
        internal static CombatPack Combat15() =>
            new CombatPack(new[] { Rifle15() }, new RwrHitResolver(), new SightLimited(new NearestVisibleEnemy(), A2.Sight), 100 * A2.S);
        internal static UnitSpawn Friend() => new UnitSpawn(KeyF, 0, 3, FHome.X.Raw * 1000 / A2.S, FHome.Z.Raw * 1000 / A2.S, 3000, 1, -1, false);
        internal static UnitSpawn Second() => A2.Soldier(KeyA2, 1, A2At, 0, 3000);

        internal static WcMap Map(Func<int, int, int> height) => height == null ? A2.Map(A2.DWalls)
            : Wp23.Make(A2.Width, A2.Depth, 0, 4 * A2.S, A2.DWalls, 2 * A2.S, height, A2.Names, (x, z) => A2.InPatch(x, z) ? (byte)1 : (byte)0);
        internal static M6World World(string ai, CombatPack combat = null, bool reverse = false, Func<int, int, int> height = null, params UnitSpawn[] more)
        {
            var units = new List<UnitSpawn> { A3.Observer(), A3.Target(KeyB, 2, BStart) };
            units.AddRange(more);
            WorldRecipe recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units.ToArray(), Array.Empty<BridgeCommand>())
                .WithMatch(A3.Match()).WithCombat(combat ?? A2.Combat(0)).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(A3.Load(A3.PackJson(A3.Grass500, ai))).WithStandingOrders();
            return new M6World(recipe, WcMapWriter.Write(Map(height)), reverse);
        }

        // B holds on tick 1 and walks to the grass on tick 20; with F, squad 3 holds on tick 2. Drive keeps B under command.
        internal static InputFrame Script(long tick) =>
            tick == 1 ? A3.Command(SquadCommandKind.Hold, 2) : tick == MoveTick ? A3.Command(SquadCommandKind.MoveTo, 2, BGrass) : default;
        internal static InputFrame FScript(long tick) => tick == 2 ? A3.Command(SquadCommandKind.Hold, 3) : Script(tick);
        internal static InputFrame Back(long tick, long at = 150) => tick == at ? A3.Command(SquadCommandKind.MoveTo, 2, BStart) : Script(tick);
        internal static void Step(M6World w, InputFrame scripted) => w.Sim.Step(A3.Drive(w, scripted));

        // Teleports a unit between two ticks (A2Fixture.cs Place).
        internal static void Place(M6World w, ulong key, SimVec3 at) { w.Set(key, new Position(at)); w.Sim.CompleteSpatialBoundary(); }
        internal static void SetTeam(M6World w, ulong key, int team)
        {
            Combatant u = w.Read<Combatant>(key);
            w.Set(key, new Combatant(team, u.Weapon, u.Ammo, u.DueMilli, u.SpreadStored, u.SpreadLastMilli));
        }
        internal static SimVec3 At(M6World w, ulong key) => w.Read<Position>(key).Value;
        internal static long Due(M6World w) => w.Read<Combatant>(KeyA).DueMilli;
        internal static int Ammo(M6World w) => w.Read<Combatant>(KeyA).Ammo;
        internal static EntityId A(M6World w) => w.Unit(KeyA);
        internal static bool Selects(M6World w) => w.Combat.Pack.Selector.TrySelect(w.Combat, A(w), R30, out _);

        // Entry count of the registered suppression and of the AI fire state (internal; read by reflection, A3Fixture.cs Count).
        internal static int Count(M6World w) =>
            (int)typeof(TargetSuppress).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<TargetSuppress>());
        internal static int FireCount(M6World w) =>
            (int)typeof(ReactionFire).GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(w.System<ReactionFire>());
        // The suppression entry (Seen, Since) of a unit, read from the private dictionary (A3aFixture.cs Entry); false without one.
        internal static bool Entry(M6World w, ulong key, out long seen, out long since) => EntryOf(w, A3.Id(w, key).Value, out seen, out since);
        // The same by the observer's stable id, for ids that name no spawned unit.
        internal static bool EntryOf(M6World w, ulong id, out long seen, out long since)
        {
            var system = w.System<TargetSuppress>();
            object dict = typeof(TargetSuppress).GetField("entries", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(system);
            var args = new object[] { id, null };
            bool found = (bool)dict.GetType().GetMethod("TryGetValue").Invoke(dict, args);
            seen = since = 0;
            if (!found) return false;
            Type entry = args[1].GetType();
            seen = (long)entry.GetField("Seen", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(args[1]);
            since = (long)entry.GetField("Since", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(args[1]);
            return true;
        }

        // A's fire record of the tick just stepped, if any.
        internal static bool Fired(M6World w, out FireRecord record)
        {
            StableId a = A3.Id(w, KeyA);
            foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(a)) { record = f; return true; }
            record = default;
            return false;
        }
        internal static bool Fired(M6World w) => Fired(w, out _);

        // Runs ticks 1..last: before(w, tick) before each step, after(w, tick) after it; returns A's fire ticks.
        internal static List<long> Run(M6World w, long last, Func<long, InputFrame> script, Action<M6World, long> before = null,
            Action<M6World, long> after = null, List<ulong> hashes = null)
        {
            var fires = new List<long>();
            for (long tick = 1; tick <= last; tick++)
            {
                before?.Invoke(w, tick);
                Step(w, script(tick));
                hashes?.Add(w.Hash);
                if (Fired(w)) fires.Add(tick);
                after?.Invoke(w, tick);
            }
            return fires;
        }
        internal static long[] Ticks(params long[] ticks) => ticks;

        // Both allocation orders step together with the same script and callbacks: equal state hashes on every tick.
        internal static void ReverseEqual(string ai, long last, Func<long, InputFrame> script, Action<M6World, long> before = null,
            CombatPack combat = null, params UnitSpawn[] more)
        {
            M6World f = World(ai, combat, false, null, more), r = World(ai, combat, true, null, more);
            for (long tick = 1; tick <= last; tick++)
            {
                before?.Invoke(f, tick); before?.Invoke(r, tick);
                Step(f, script(tick)); Step(r, script(tick));
                Assert.That(r.Hash, Is.EqualTo(f.Hash), "tick " + tick);
            }
        }

        // The shot went towards P: the horizontal direction to End and to P point the same way, and the cross product is within
        // |P - Origin| x 64 raw (the spread of the fixture's weapon stays far inside it).
        internal static void AssertTowards(FireRecord f, SimVec3 p)
        {
            SimVec3 d = CombatSystem.Horizontal(f.End - f.Origin), q = CombatSystem.Horizontal(p - f.Origin);
            long cross = d.X.Raw * q.Z.Raw - d.Z.Raw * q.X.Raw, dot = d.X.Raw * q.X.Raw + d.Z.Raw * q.Z.Raw;
            Assert.That(dot, Is.GreaterThan(0L));
            Assert.That(Math.Abs(cross), Is.LessThanOrEqualTo(q.Length.Raw * 64L));
        }
        internal static void AssertNoFriendRecord(M6World w)
        {
            foreach (FireRecord f in w.Combat.Fires)
            {
                if (!f.Shooter.Equals(A3.Id(w, KeyA))) continue;
                Assert.That(f.Outcome, Is.Not.EqualTo(FireOutcome.FriendBlocked).And.Not.EqualTo(FireOutcome.FriendHit));
                Assert.That(f.Target.Equals(A3.Id(w, KeyF)), Is.False);
            }
        }
    }
}
