using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AiS4Tests: what the scheduler does with a pinned unit when the battle has cover rules.
    public sealed partial class AiS4Tests
    {
        // The rule, worked out again from the unit's own lattice: every lattice point in a square around the unit that
        // reaches past the radius; the nearest standable one within the radius that has a wall within depth toward the
        // threat, a straight walk without a wall on it and no other living unit of team 1 on the last 1.5 m of it; ties to
        // the lower z, then the lower x. blocked counts the spots that only a wall on the walk ruled out, taken those that only a teammate did.
        // team: where the attackers stand (index = key - 1; nobody dies in these tests).
        //
        // A teammate is on the walk when its centre is within two body radii (0.5 m) of the last 1.5 m of the segment.
        // Worked out in doubles; within 3 mm of the limit the answer is left open (unsure) and the caller skips the think.
        private static bool Clear(SimVec3[] team, int self, SimVec3 from, SimVec3 to, ref bool unsure)
        {
            double ax = from.X.Raw / 65536.0, az = from.Z.Raw / 65536.0, bx = to.X.Raw / 65536.0, bz = to.Z.Raw / 65536.0;
            double dx = bx - ax, dz = bz - az, length2 = dx * dx + dz * dz, length = Math.Sqrt(length2);
            if (length > 1.5) { ax = bx - dx / length * 1.5; az = bz - dz / length * 1.5; dx = bx - ax; dz = bz - az; length2 = 2.25; }
            bool clear = true;
            for (int i = 0; i < team.Length; i++)
            {
                if (i == self) continue;
                double px = team[i].X.Raw / 65536.0 - ax, pz = team[i].Z.Raw / 65536.0 - az;
                double along = length2 > 0 ? Math.Clamp((px * dx + pz * dz) / length2, 0, 1) : 0;
                double ex = px - along * dx, ez = pz - along * dz, distance = Math.Sqrt(ex * ex + ez * ez);
                if (Math.Abs(distance - 0.5) < 0.003) unsure = true;
                else if (distance < 0.5) clear = false;
            }
            return clear;
        }
        private static SimVec3[] Team(M6World w)
        {
            var team = new SimVec3[Attackers];
            for (int i = 0; i < Attackers; i++) team[i] = At(w, (ulong)i + 1);
            return team;
        }
        private static bool Spot(M6World w, IMoveOrders moves, SimVec3[] team, int self, SimVec3 threat, int radiusMm, int stepMm, int depthMm,
            out SimVec3 spot, ref int blocked, ref int taken, ref bool unsure)
        {
            ulong stable = Id(w, (ulong)self + 1).Value; SimVec3 here = team[self];
            long step = SimFix.Ratio(stepMm, 1000).Raw, radius = SimFix.Ratio(radiusMm, 1000).Raw;
            long shiftX = (long)(Finalize(stable ^ SaltX) % (ulong)step), shiftZ = (long)(Finalize(stable ^ SaltZ) % (ulong)step);
            SimFix depth = SimFix.Ratio(depthMm, 1000);
            var found = new List<(long d2, long z, long x)>();
            long cells = radius / step + 3;
            long baseX = (here.X.Raw - shiftX) / step, baseZ = (here.Z.Raw - shiftZ) / step;
            for (long j = baseZ - cells; j <= baseZ + cells; j++)
                for (long i = baseX - cells; i <= baseX + cells; i++)
                {
                    long x = i * step + shiftX, z = j * step + shiftZ;
                    long dx = x - here.X.Raw, dz = z - here.Z.Raw, d2 = dx * dx + dz * dz;
                    if (d2 > radius * radius) continue;
                    var at = new SimVec3(SimFix.FromRaw(x), here.Y, SimFix.FromRaw(z));
                    if (!moves.CanStandAt(at) || !w.Combat.WallWithin(at, threat, depth)) continue;
                    if (!w.Combat.Sees(here, at)) { blocked++; continue; }
                    if (!Clear(team, self, here, at, ref unsure)) { taken++; continue; }
                    found.Add((d2, z, x));
                }
            found.Sort();
            spot = found.Count > 0 ? new SimVec3(SimFix.FromRaw(found[0].x), here.Y, SimFix.FromRaw(found[0].z)) : default;
            return found.Count > 0;
        }

        private static bool SameXz(SimVec3 a, SimVec3 b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;

        // Every attacker is kept pinned for four thinks, from the east on the first tick and from the north afterwards (which
        // must not change where it hides from). At each of its thinks a unit does exactly one of three things, each checked
        // against the rule above from where everybody stood before the tick: it stands in cover; it is ordered to the
        // nearest spot (also when it was walking already: again counts those); or it finds none and stands. Off its
        // think nothing is ordered.
        [TestCase(6000, 1000, 2000, 60)]
        [TestCase(9000, 1000, 2000, 60)]
        [TestCase(9000, 700, 1200, 60)]
        [TestCase(9000, 1000, 2000, 56)]
        public void Pinned_DoesWhatTheRuleSaysAtEveryThink(int radius, int step, int depth, int x)
        {
            M6World w = Yard(S4(radius, step, depth), Walls, x);
            Pressure p = w.System<Pressure>();
            Assert.That(p.StateVersion, Is.EqualTo(2));
            AiField field = FieldOf(w); IMoveOrders moves = MovesOf(w);
            SimFix reach = SimFix.Ratio(depth, 1000);
            int covered = 0, again = 0, ordered = 0, stood = 0, blocked = 0, taken = 0, unsure = 0;
            var shots = new FireRecord[Attackers];
            var before = new SimVec3[Attackers]; var orders = new MoveIntent[Attackers];
            w.Step();
            for (int t = 0; t < 4 * Think; t++)
            {
                for (int i = 0; i < Attackers; i++) shots[i] = Near(w, t == 0 ? East : North, At(w, (ulong)i + 1));
                p.Advance(shots);
                for (int i = 0; i < Attackers; i++) { before[i] = At(w, (ulong)i + 1); orders[i] = w.Read<MoveIntent>((ulong)i + 1); }
                w.Step();
                long tick = field.Tick;
                for (int i = 0; i < Attackers; i++)
                {
                    ulong key = (ulong)i + 1; StableId id = Id(w, key);
                    string what = "key " + key + " tick " + tick;
                    Assert.That(p.Level(id), Is.EqualTo(AiNerve.Pinned), what);
                    Assert.That(From(p, id), Is.EqualTo(East), what);
                    MoveIntent now = w.Read<MoveIntent>(key);
                    if (!Thinks(id.Value, tick))
                    {
                        if (now.Active) { Assert.That(orders[i].Active, Is.True, what); Assert.That(now.Target, Is.EqualTo(orders[i].Target), what); }
                        continue;
                    }
                    if (w.Combat.WallWithin(before[i], East, reach))
                    { Assert.That(now.Active, Is.False, what + " stands in cover"); covered++; continue; }
                    bool open = false;
                    bool finds = Spot(w, moves, before, i, East, radius, step, depth, out SimVec3 spot, ref blocked, ref taken, ref open);
                    if (open) { unsure++; continue; }
                    if (finds)
                    {
                        Assert.That(Horizontal2(spot, before[i]), Is.LessThanOrEqualTo(Raw2(radius)), what);
                        if (now.Active) Assert.That(SameXz(now.Target, spot), Is.True, what + " ordered to " + now.Target + ", the rule says " + spot);
                        else Assert.That(Horizontal2(At(w, key), spot), Is.LessThanOrEqualTo(Raw2(500)), what + " arrived at once");
                        ordered++; if (orders[i].Active) again++;
                        continue;
                    }
                    Assert.That(now.Active, Is.False, what + " finds no spot and stands");
                    stood++;
                }
            }
            // Every branch is taken. The far wall (54, 5) is in sight only within 9 m; a unit is still walking at its next
            // think only when the attackers start 8 m from the wall (x 56).
            string counts = $"covered {covered} again {again} ordered {ordered} stood {stood} blocked {blocked} taken {taken} unsure {unsure}";
            Assert.That(covered, Is.GreaterThanOrEqualTo(30), counts);
            Assert.That(ordered, Is.GreaterThanOrEqualTo(12), counts);
            Assert.That(stood, Is.GreaterThanOrEqualTo(3), counts);
            Assert.That(taken, Is.GreaterThanOrEqualTo(40), counts);
            Assert.That(unsure, Is.LessThanOrEqualTo(6), counts);
            if (radius == 9000) Assert.That(blocked, Is.GreaterThanOrEqualTo(30), counts);
            if (x == 56) Assert.That(again, Is.GreaterThanOrEqualTo(5), counts);
        }

        // The rule itself, asked from a chosen point for unit 1 (its teammates stand 20 m away). The lattice is the unit's
        // own, in steps of 1 m: Column is the lattice coordinate at or below a raw coordinate.
        private static bool Find(M6World w, SimVec3 here, SimVec3 threat, out SimVec3 spot)
        {
            object cover = Private<object>(w.System<SimpleAiSystem>(), "cover");
            var arguments = new object[] { w.Unit(1), Id(w, 1).Value, here, threat, null };
            bool found = (bool)cover.GetType().GetMethod("TryFind", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(cover, arguments);
            spot = found ? (SimVec3)arguments[4] : default;
            return found;
        }
        private static long Column(long raw, long shift) => (raw - shift) / SimFix.Scale * SimFix.Scale + shift;
        private static SimVec3 Raw(long x, long z) => new SimVec3(SimFix.FromRaw(x), default, SimFix.FromRaw(z));

        // How far the search reaches. The only covered points of the lattice are the column less than 1 m west of the wall
        // (depth 1 m). From a point on the lattice three columns west of it the spot is 3 m away, exactly the radius, and
        // counts. With a radius of 2.5 m the spot is three columns away as well: found from 2.4 m, not from 2.6 m.
        [TestCase(3000, 0, true)]
        [TestCase(2500, 600, true)]
        [TestCase(2500, 400, false)]
        public void Spot_IsLookedForAsFarAsTheRadius(int radius, int intoTheCellMm, bool finds)
        {
            M6World w = Yard(S4(radius, 1000, 1000), new[] { (64, 17, 1, 6) }, attackersX: 40);
            ulong stable = Id(w, 1).Value; long step = SimFix.Scale;
            long x = Column(64 * step - 1, (long)(Finalize(stable ^ SaltX) % (ulong)step)), z = Column(20 * step, (long)(Finalize(stable ^ SaltZ) % (ulong)step));
            Assert.That(w.Combat.WallWithin(Raw(x, z), East, SimFix.FromInt(1)), Is.True);
            Assert.That(w.Combat.WallWithin(Raw(x - step, z), East, SimFix.FromInt(1)), Is.False);
            Assert.That(Find(w, Raw(x - 3 * step + SimFix.Ratio(intoTheCellMm, 1000).Raw, z), East, out SimVec3 spot), Is.EqualTo(finds));
            if (finds) Assert.That(SameXz(spot, Raw(x, z)), Is.True, "found " + spot + ", the spot is " + Raw(x, z));
        }

        // Of two spots equally near, the one with the lower z; in one row, the one with the lower x. Asked from the middle
        // between two rows, 2 m west of the covered column behind the east wall; and from the middle between two columns,
        // 2 m south of the covered row behind a wall to the north.
        [TestCase(true)]
        [TestCase(false)]
        public void Spot_OfTwoEquallyNearIsTheLowerOne(bool rows)
        {
            M6World w = Yard(S4(3000, 1000, 1000), new[] { (64, 17, 1, 6), (58, 30, 8, 1) }, attackersX: 40);
            ulong stable = Id(w, 1).Value; long step = SimFix.Scale;
            long shiftX = (long)(Finalize(stable ^ SaltX) % (ulong)step), shiftZ = (long)(Finalize(stable ^ SaltZ) % (ulong)step);
            SimVec3 threat = rows ? East : North;
            long x = Column(rows ? 64 * step - 1 : 62 * step, shiftX), z = Column(rows ? 20 * step : 30 * step - 1, shiftZ);
            SimVec3 lower = Raw(x, z), other = rows ? Raw(x, z + step) : Raw(x + step, z);
            SimVec3 here = rows ? Raw(x - 2 * step, z + step / 2) : Raw(x + step / 2, z - 2 * step);
            Assert.That(w.Combat.WallWithin(lower, threat, SimFix.FromInt(1)) && w.Combat.WallWithin(other, threat, SimFix.FromInt(1)), Is.True);
            Assert.That(Horizontal2(here, lower), Is.EqualTo(Horizontal2(here, other)));
            Assert.That(Find(w, here, threat, out SimVec3 spot), Is.True);
            Assert.That(SameXz(spot, lower), Is.True, "found " + spot + ", the lower one is " + lower);
        }

        // The plain picture: the attackers stand 1 m west of the wall's line. Those right behind the wall (z 17.5 .. 22.5)
        // stay where they are; the others walk behind it and stand, as long as there is a spot they can get into; fire
        // from the north, which sees them all, moves nobody.
        [Test]
        public void Pinned_GoesBehindTheWallAndStaysThere()
        {
            M6World w = Yard(S4(), new[] { (64, 17, 1, 6) }, attackersX: 63);
            Pressure p = w.System<Pressure>(); IMoveOrders moves = MovesOf(w);
            var start = new SimVec3[Attackers];
            for (int i = 0; i < Attackers; i++) start[i] = At(w, (ulong)i + 1);
            var shots = new FireRecord[Attackers];
            void Press(SimVec3 from)
            {
                for (int i = 0; i < Attackers; i++) shots[i] = Near(w, from, At(w, (ulong)i + 1));
                p.Advance(shots);
                w.Step();
            }
            Press(East);
            for (int t = 1; t < 4 * Think; t++) Press(North);
            SimVec3[] stood = Team(w);
            for (int t = 0; t < 2 * Think; t++) Press(North);
            int behind = 0, blocked = 0, taken = 0;
            for (int i = 0; i < Attackers; i++)
            {
                ulong key = (ulong)i + 1; SimVec3 here = At(w, key);
                string what = "key " + key + " at " + here;
                Assert.That(here, Is.EqualTo(stood[i]), what + " stays");
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, what);
                Assert.That(From(p, Id(w, key)), Is.EqualTo(East), what);
                // Keys 8..13 stood behind it from the start (z 17.5 .. 22.5) and never moved.
                if (key >= 8 && key <= 13) Assert.That(here, Is.EqualTo(start[i]), what);
                if (!w.Combat.WallWithin(here, East, SimFix.FromInt(2)))
                {
                    // In the open: only where the rule has no spot for it (the others stand on them or in front of them).
                    bool unsure = false;
                    Assert.That(Spot(w, moves, stood, i, East, 9000, 1000, 2000, out _, ref blocked, ref taken, ref unsure) && !unsure, Is.False, what + " has a spot");
                    continue;
                }
                behind++;
                // Behind the wall: west of its face, within its length (a little over at the ends, where the line to the east slants).
                Assert.That(here.X.Raw, Is.LessThan(64L * SimFix.Scale), what);
                Assert.That(here.X.Raw, Is.GreaterThanOrEqualTo(62L * SimFix.Scale - SimFix.Scale / 2), what);
                Assert.That(here.Z.Raw, Is.InRange(16L * SimFix.Scale, 24L * SimFix.Scale), what);
                if (key < 8 || key > 13) Assert.That(Horizontal2(here, start[i]), Is.GreaterThan(Raw2(500)), what);
            }
            // Six stood there already; at least eight more got in (ten do with these numbers).
            Assert.That(behind, Is.GreaterThanOrEqualTo(14));
            Assert.That(taken, Is.GreaterThan(0), "those left in the open were kept out by teammates");
        }

        // A pinned unit that finds no spot drops the order it had, wherever it led. The attackers start 13 m from the wall
        // (no spot within 9 m): key 10 is on its way to a spot right behind the wall, key 12 to a point in the open
        // 8 m ahead. Each stands at its next think.
        [Test]
        public void Pinned_WithoutASpotDropsTheOrderItHad()
        {
            M6World w = Yard(S4(), new[] { (64, 17, 1, 6) }, attackersX: 50);
            Pressure p = w.System<Pressure>(); IMoveOrders moves = MovesOf(w); AiField field = FieldOf(w);
            SimVec3 far = Mm(63000, 19500), open = Mm(58000, 21500);
            Assert.That(w.Combat.WallWithin(far, East, SimFix.FromInt(2)) && w.Combat.Sees(At(w, 10), far), Is.True);
            Assert.That(w.Combat.WallWithin(open, East, SimFix.FromInt(2)), Is.False);
            moves.Order(Id(w, 10), far); moves.Order(Id(w, 12), open);
            var shots = new FireRecord[Attackers];
            int thought = 0;
            for (int t = 0; t < Think; t++)
            {
                for (int i = 0; i < Attackers; i++) shots[i] = Near(w, East, At(w, (ulong)i + 1));
                p.Advance(shots);
                bool ten = w.Read<MoveIntent>(10).Active, twelve = w.Read<MoveIntent>(12).Active;
                w.Step();
                foreach ((ulong key, bool before, SimVec3 target) in new[] { (10UL, ten, far), (12UL, twelve, open) })
                {
                    string what = "key " + key + " tick " + field.Tick;
                    if (!Thinks(Id(w, key).Value, field.Tick)) { Assert.That(w.Read<MoveIntent>(key).Active, Is.EqualTo(before), what); continue; }
                    Assert.That(before, Is.True, what + ": it walked until it thought");
                    Assert.That(Horizontal2(At(w, key), target), Is.GreaterThan(Raw2(1000)), what + ": it was not there yet");
                    Assert.That(w.Read<MoveIntent>(key).Active, Is.False, what + ": it stands");
                    thought++;
                }
            }
            Assert.That(thought, Is.EqualTo(2));
        }

        // Once it is calm again a unit leaves its cover and goes for the enemy point as before.
        [Test]
        public void Pinned_OnceCalmGoesOn()
        {
            M6World w = Yard(S4(decay: 30), new[] { (64, 17, 1, 6) }, attackersX: 63);
            Pressure p = w.System<Pressure>();
            var shots = new FireRecord[Attackers];
            for (int t = 0; t < 4 * Think; t++)
            {
                for (int i = 0; i < Attackers; i++) shots[i] = Near(w, East, At(w, (ulong)i + 1));
                p.Advance(shots);
                w.Step();
            }
            for (ulong key = 1; key <= Attackers; key++) Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
            // Decay 30: 10000 is under 2000 after 53 ticks; one more think and everybody walks.
            w.Step(53 + Think);
            for (ulong key = 1; key <= Attackers; key++)
            {
                Assert.That(p.Level(Id(w, key)), Is.EqualTo(AiNerve.Calm), "key " + key);
                MoveIntent order = w.Read<MoveIntent>(key);
                Assert.That(order.Active, Is.True, "key " + key);
                Assert.That(Horizontal2(order.Target, Theirs), Is.LessThanOrEqualTo(Raw2(6000)), "key " + key);
            }
        }

        // Pinned again after it was calm, a unit hides from the new fire. From the north the wall (1 m thick) is cover only
        // for a spot right south of it, and nobody stands in such a spot at the start.
        [Test]
        public void Pinned_AgainHidesFromTheNewFire()
        {
            M6World w = Yard(S4(decay: 30), new[] { (64, 17, 1, 6) }, attackersX: 63);
            Pressure p = w.System<Pressure>();
            StableId id = Id(w, 10);
            p.Advance(new[] { Near(w, East, At(w, 10)) });
            Assert.That(From(p, id), Is.EqualTo(East));
            // 3000 at decay 30 is under 2000 after 14 ticks without fire.
            for (int t = 0; t < 14; t++) p.Advance(ReadOnlySpan<FireRecord>.Empty);
            Assert.That(p.Level(id), Is.EqualTo(AiNerve.Calm));
            Assert.That(From(p, id), Is.EqualTo(East), "the old source stays while the entry lives");
            p.Advance(new[] { Near(w, North, At(w, 10)) });
            Assert.That(p.Level(id), Is.EqualTo(AiNerve.Pinned));
            Assert.That(From(p, id), Is.EqualTo(North));
        }

        // Without a wall a pinned unit stands as it did before cover existed: the same positions, tick for tick, as the
        // same battle without the cover fields.
        [Test]
        public void Pinned_WithoutAWallIsTheBattleWithoutCover()
        {
            M6World on = Yard(S4()), off = Yard(S3());
            Assert.That(off.System<Pressure>().StateVersion, Is.EqualTo(1));
            Pressure a = on.System<Pressure>(), b = off.System<Pressure>();
            var shots = new FireRecord[Attackers];
            for (int t = 0; t < 4 * Think; t++)
            {
                // Pinned for two thinks, then left alone.
                if (t < 2 * Think)
                {
                    for (int i = 0; i < Attackers; i++) shots[i] = Near(on, East, At(on, (ulong)i + 1));
                    a.Advance(shots); b.Advance(shots);
                }
                on.Step(); off.Step();
                for (ulong key = 1; key <= Attackers + Defenders; key++)
                {
                    Assert.That(At(on, key), Is.EqualTo(At(off, key)), "key " + key + " tick " + t);
                    Assert.That(a.Level(Id(on, key)), Is.EqualTo(b.Level(Id(off, key))), "key " + key + " tick " + t);
                }
                if (t == 2 * Think - 1)
                    for (ulong key = 1; key <= Attackers; key++) Assert.That(on.Read<MoveIntent>(key).Active, Is.False, "key " + key);
            }
        }

        // In sight of the enemy (28 m) it is the same: after eight thinks under fire every attacker stands, behind the wall
        // or where the rule has no spot for it. The memory, the search and the suppression are on. The six that start in
        // the wall's shadow never see an enemy; the others do.
        [Test]
        public void Pinned_InSightTakesCoverToo()
        {
            const string all = "\"targetMemoryTicks\": 300, \"searchTicks\": 90, \"suppressTicks\": 90";
            M6World w = Yard(S4(more: all), new[] { (84, 17, 1, 6) }, attackersX: 80);
            Pressure p = w.System<Pressure>(); IMoveOrders moves = MovesOf(w);
            Assert.That(w.System<TargetMemory>(), Is.Not.Null);
            var shots = new FireRecord[Attackers];
            for (int t = 0; t < 8 * Think; t++)
            {
                for (int i = 0; i < Attackers; i++) shots[i] = Near(w, East, At(w, (ulong)i + 1));
                p.Advance(shots);
                w.Step();
            }
            int covered = 0, blocked = 0, taken = 0;
            SimVec3[] team = Team(w);
            for (ulong key = 1; key <= Attackers; key++)
            {
                SimVec3 here = At(w, key); StableId id = Id(w, key);
                Assert.That(w.System<TargetMemory>().TryRecall(id, w.Sim.Clock.Tick, out _, out _, out _), Is.EqualTo(key < 8 || key > 13), "key " + key + " saw an enemy");
                Assert.That(From(p, id), Is.EqualTo(East), "key " + key);
                Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "key " + key);
                // Nobody went on toward the enemy: all are west of the wall's far face.
                Assert.That(here.X.Raw, Is.LessThan(85L * SimFix.Scale), "key " + key);
                if (w.Combat.WallWithin(here, East, SimFix.FromInt(2))) { covered++; continue; }
                bool unsure = false;
                Assert.That(Spot(w, moves, team, (int)key - 1, East, 9000, 1000, 2000, out _, ref blocked, ref taken, ref unsure) && !unsure, Is.False,
                    "key " + key + " stands in the open though there is a spot");
            }
            // Seventeen with these numbers; eleven of them saw the enemy on the way.
            Assert.That(covered, Is.GreaterThanOrEqualTo(15));
        }
    }
}
