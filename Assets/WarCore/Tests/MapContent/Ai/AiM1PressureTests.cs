using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // AI-M1, the pressure side (PressureRules.Strikes): melee presses too. A unit that landed strikes bring down this tick
    // is a teammate brought down, whatever FallOnKill says; an enemy strike that lands on or is blocked by a unit within the
    // near radius is a near shot. Without Strikes the strike records press nobody.
    public sealed class AiM1PressureTests
    {
        // Team 1 in a row along x, 2 m apart: A, C (2 m from A), D (4 m), E (6 m). Team 2: B beside A, F far away.
        private const ulong C = 2, D = 3, E = 4, F = 11;
        private const long Row = 20500;

        private static M6World Row1() => World(new[]
        {
            Still(A, 30500, Row, team: 1), Still(C, 32500, Row, team: 1), Still(D, 34500, Row, team: 1), Still(E, 36500, Row, team: 1),
            Still(B, 30500, Row + 1000), Still(F, 60500, 5500),
        }, "{ }", Guns(8), plates: false);

        internal static PressureRules Rules(bool strikes = true, bool kill = false, int deathShock = 2000, int nearShock = 1000, int nearRadius = 3000,
            int deathRadius = 3000, int pinnedAt = 2000) =>
            new PressureRules(deathRadius, deathShock, nearRadius, nearShock, 12, 7000, 1000, pinnedAt, 0, 1000, kill, strikes);

        internal static StrikeRecord Strike(M6World w, StrikeKind kind, ulong striker, ulong target, long damage) =>
            new StrikeRecord(kind, Id(w, striker), Id(w, target), 0, damage, At(w, striker));
        private static FireRecord Hit(M6World w, ulong shooter, ulong victim, long damage, int team = 2) =>
            new FireRecord(Id(w, shooter), Id(w, victim), At(w, shooter), At(w, victim), default, damage, team);
        private static FireRecord Shot(M6World w, ulong shooter, SimVec3 end, int team = 2) =>
            new FireRecord(Id(w, shooter), default, At(w, shooter), end, default, 0, team, FireOutcome.Open);
        private static int[] Values(M6World w, Pressure p) => new[] { p.Value(Id(w, A)), p.Value(Id(w, C)), p.Value(Id(w, D)), p.Value(Id(w, E)), p.Value(Id(w, B)) };
        private static long Health(M6World w, ulong key) => w.Combat.HealthRaw(Id(w, key));
        private static readonly FireRecord[] NoFire = new FireRecord[0];

        // 2. A teammate cut down by one strike: the teammates within the death radius feel it, the fallen and the enemy do not.
        // The near shock is off here (nearShock 0), so only the fall presses.
        [Test]
        public void ATeammateCutDownPresses()
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(nearShock: 0));
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, Health(w, C)) });
            Assert.That(Values(w, p), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }), "A and D stand 2 m from C; E 4 m; C fell; B is the enemy");
        }

        // 2. A wound is no fall, under either rule for fire; several strikes on one unit add up; a strike without damage, a
        // blocked strike, a swing in the air and a swing that started bring nobody down.
        [TestCase(false)]
        [TestCase(true)]
        public void OnlyAKillIsAFall(bool kill)
        {
            M6World w = Row1();
            long health = Health(w, C);
            var wound = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
            wound.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, health - 1) });
            Assert.That(Values(w, wound), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }), "one short of the health");
            var sum = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
            sum.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, health - 1), Strike(w, StrikeKind.Landed, F, C, 1) });
            Assert.That(Values(w, sum), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }), "two strikes add up to one fall");
            foreach (StrikeKind other in new[] { StrikeKind.Blocked, StrikeKind.Whiffed, StrikeKind.Started })
            {
                var none = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
                none.Advance(NoFire, new[] { Strike(w, other, B, C, health) });
                Assert.That(Values(w, none), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }), other.ToString());
            }
            var zero = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
            zero.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, 0) });
            Assert.That(Values(w, zero), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }), "a landed strike without damage");
        }

        // 2. A shot and a strike that kill together are one fall. Under the "hit" rule the shot alone is already a fall and
        // the unit is not counted again by the strike; under the "kill" rule the two add up.
        [TestCase(false)]
        [TestCase(true)]
        public void AShotAndAStrikeAreOneFall(bool kill)
        {
            M6World w = Row1();
            long health = Health(w, C);
            var p = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
            p.Advance(new[] { Hit(w, F, C, health / 2) }, new[] { Strike(w, StrikeKind.Landed, B, C, health - health / 2) });
            Assert.That(Values(w, p), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }));
            // The fire's own rule is untouched: a wounding shot is a fall under "hit" and none under "kill".
            var shot = new Pressure(w.Combat, Rules(kill: kill, nearShock: 0));
            shot.Advance(new[] { Hit(w, F, C, 1) }, new StrikeRecord[0]);
            Assert.That(shot.Value(Id(w, A)), Is.EqualTo(kill ? 0 : 2000));
        }

        // Halves one unit's health on tick 1.
        private sealed class Wound : IRetainedGameplaySystem
        {
            private readonly M6World world; private readonly ulong key;
            internal Wound(M6World world, ulong key) { this.world = world; this.key = key; }
            public int Order => 25;
            public string StateTypeId => "test.wound";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                if (c.Clock.Tick != 1) return;
                StableId id = Id(world, key);
                c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, 1, 0, id, 1, 0), 1, world.Recipe.Combat.MaxHealthRaw / 2);
            }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash hash) { }
        }

        // 2. The health a strike must reach is the unit's health now, not its full health: a wounded unit falls to a strike
        // that would only wound a fresh one.
        [Test]
        public void AWoundedUnitFallsToASmallerStrike()
        {
            M6World w = Row1();
            w.Sim.RegisterGameplay("test.wound", new Wound(w, C), SimSystemPhase.Queries);
            w.Step(2);
            long full = w.Recipe.Combat.MaxHealthRaw, left = Health(w, C);
            Assert.That(left, Is.EqualTo(full - full / 2));
            var p = new Pressure(w.Combat, Rules(nearShock: 0));
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, left - 1) });
            Assert.That(Values(w, p), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }));
            var q = new Pressure(w.Combat, Rules(nearShock: 0));
            q.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, left) });
            Assert.That(Values(w, q), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }));
        }

        // 2. A strike by a teammate that kills is a fall like a shot by a teammate (the fallen list does not ask who did it),
        // and two units cut down beside one unit press it twice.
        [Test]
        public void EveryFallCountsOnce()
        {
            M6World w = Row1();
            var friendly = new Pressure(w.Combat, Rules(nearShock: 0));
            friendly.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, A, C, Health(w, C)) });
            Assert.That(Values(w, friendly), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }));
            var two = new Pressure(w.Combat, Rules(nearShock: 0));
            two.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, A, Health(w, A)), Strike(w, StrikeKind.Landed, B, D, Health(w, D)),
                Strike(w, StrikeKind.Landed, B, A, 5) });
            Assert.That(Values(w, two), Is.EqualTo(new[] { 0, 4000, 0, 2000, 0 }), "C stands between A and D; E beside D");
        }

        // 3. An enemy strike that lands on or is blocked by a unit presses the units within the near radius of that unit, the
        // unit itself too, once a tick; a swing in the air, a swing that started and a teammate's strike do not.
        [Test]
        public void AStrikeNearbyIsANearShot()
        {
            M6World w = Row1();
            foreach (StrikeKind kind in new[] { StrikeKind.Landed, StrikeKind.Blocked })
            {
                var p = new Pressure(w.Combat, Rules(deathShock: 0));
                p.Advance(NoFire, new[] { Strike(w, kind, B, C, 1), Strike(w, kind, B, C, 1) });
                Assert.That(Values(w, p), Is.EqualTo(new[] { 1000, 1000, 1000, 0, 0 }), kind + ": A and D 2 m from C, C itself, not E (4 m), not the striker");
            }
            foreach (StrikeKind kind in new[] { StrikeKind.Whiffed, StrikeKind.Started })
            {
                var p = new Pressure(w.Combat, Rules(deathShock: 0));
                p.Advance(NoFire, new[] { Strike(w, kind, B, C, 1) });
                Assert.That(Values(w, p), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }), kind.ToString());
            }
            var own = new Pressure(w.Combat, Rules(deathShock: 0));
            own.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, A, C, 1) });
            Assert.That(Values(w, own), Is.EqualTo(new[] { 0, 0, 0, 0, 1000 }), "a teammate's strike presses no teammate; B, 2.2 m from C, is its enemy");
            // The other way round: team 1's strike on B presses B.
            var theirs = new Pressure(w.Combat, Rules(deathShock: 0));
            theirs.Advance(NoFire, new[] { Strike(w, StrikeKind.Blocked, A, B, 0) });
            Assert.That(Values(w, theirs), Is.EqualTo(new[] { 0, 0, 0, 0, 1000 }), "the unit whose block was struck, alone");
        }

        // 3. The near radius is measured from the struck unit, horizontally: at the radius counts, 1 mm more does not.
        [TestCase(2000, 1000)]
        [TestCase(1999, 0)]
        public void TheNearRadiusIsMeasuredFromTheStruckUnit(int radius, int expected)
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(deathShock: 0, nearRadius: radius));
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, 1) });
            Assert.That(p.Value(Id(w, D)), Is.EqualTo(expected), "D stands 2 m from C");
            Assert.That(p.Value(Id(w, C)), Is.EqualTo(1000));
        }

        // 3. A shot and a strike near one unit in one tick press it once.
        [Test]
        public void ANearShotAndANearStrikePressOnce()
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(deathShock: 0));
            p.Advance(new[] { Shot(w, F, At(w, C)) }, new[] { Strike(w, StrikeKind.Landed, B, C, 1) });
            Assert.That(Values(w, p), Is.EqualTo(new[] { 1000, 1000, 1000, 0, 0 }));
            // A fall and a near strike add up.
            var both = new Pressure(w.Combat, Rules());
            both.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, Health(w, C)) });
            Assert.That(Values(w, both), Is.EqualTo(new[] { 3000, 1000, 3000, 0, 0 }), "C still stands this tick (damage lands after) and feels the blow, not its own fall");
        }

        // 5. Without Strikes the strike records press nobody (a tick with strikes and no fire is skipped), and FallOnKill
        // still reads them for a unit the fire hit.
        [Test]
        public void WithoutStrikesMeleePressesNobody()
        {
            M6World w = Row1();
            long health = Health(w, C);
            var off = new Pressure(w.Combat, Rules(strikes: false));
            off.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, C, health), Strike(w, StrikeKind.Blocked, B, A, 0) });
            Assert.That(Values(w, off), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }));
            var kill = new Pressure(w.Combat, Rules(strikes: false, kill: true, nearShock: 0));
            kill.Advance(new[] { Hit(w, F, C, 1) }, new[] { Strike(w, StrikeKind.Landed, B, C, health - 1) });
            Assert.That(Values(w, kill), Is.EqualTo(new[] { 2000, 0, 2000, 0, 0 }), "as before: the shot and the strike add up for a unit the fire hit");
            var only = new Pressure(w.Combat, Rules(strikes: false, kill: true, nearShock: 0));
            only.Advance(new[] { Hit(w, F, E, 1) }, new[] { Strike(w, StrikeKind.Landed, B, C, health) });
            Assert.That(Values(w, only), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }), "a unit only strikes hit is not looked at");
        }

        // 2 and 3, with a dead body in the records: a strike on a unit that is gone presses nobody; a striker that is gone
        // makes no near shot.
        [Test]
        public void RecordsOfUnitsThatAreGonePressNobody()
        {
            M6World w = Row1();
            var gone = new StableId(0x7fffffffffff0001UL);
            var p = new Pressure(w.Combat, Rules());
            p.Advance(NoFire, new[]
            {
                new StrikeRecord(StrikeKind.Landed, Id(w, B), gone, 0, 1000 * M, At(w, B)),
                new StrikeRecord(StrikeKind.Landed, gone, Id(w, C), 0, 1, At(w, B)),
            });
            Assert.That(Values(w, p), Is.EqualTo(new[] { 0, 0, 0, 0, 0 }));
        }

        // 4. With sources the pressure remembers where the strike came from: where the striker stood when the swing started,
        // not where the struck unit stands. A shot of the same tick comes first; above the pinned line the source stays.
        [Test]
        public void TheSourceIsWhereTheStrikerStood()
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(deathShock: 0, pinnedAt: 500), true);
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Blocked, B, A, 0) });
            Assert.That(p.TryPressedFrom(Id(w, A), out SimVec3 from), Is.True);
            Assert.That(from, Is.EqualTo(At(w, B)));
            Assert.That(from, Is.Not.EqualTo(At(w, A)));
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Blocked, F, A, 0) });
            Assert.That(p.Value(Id(w, A)), Is.EqualTo(1988));
            Assert.That(p.TryPressedFrom(Id(w, A), out from) && from.Equals(At(w, B)), Is.True, "pinned: the first source stays");

            var both = new Pressure(w.Combat, Rules(deathShock: 0), true);
            both.Advance(new[] { Shot(w, F, At(w, A)) }, new[] { Strike(w, StrikeKind.Blocked, B, A, 0) });
            Assert.That(both.TryPressedFrom(Id(w, A), out from) && from.Equals(At(w, F)), Is.True, "the shot before the strike");
            var blade = new Pressure(w.Combat, Rules(deathShock: 0), true);
            blade.Advance(new[] { Shot(w, F, At(w, E)) }, new[] { Strike(w, StrikeKind.Blocked, B, A, 0) });
            Assert.That(blade.TryPressedFrom(Id(w, A), out from) && from.Equals(At(w, B)), Is.True, "a shot that ends far away is no near shot");
        }

        // 4. A tick without a near shot or strike: the source is the first fall that counted, and the units the fire brought
        // down come before the units the strikes brought down. The near radius is 1 m here, so the two falls 2 m away are
        // falls only.
        [Test]
        public void AFallByFireComesBeforeAFallByStrike()
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(nearRadius: 1000), true);
            p.Advance(new[] { Hit(w, F, D, Health(w, D)) }, new[] { Strike(w, StrikeKind.Landed, B, A, Health(w, A)) });
            Assert.That(p.Value(Id(w, C)), Is.EqualTo(4000), "A and D fell 2 m from C");
            Assert.That(p.TryPressedFrom(Id(w, C), out SimVec3 from) && from.Equals(At(w, F)), Is.True);
            // A fall by a strike alone: where the striker stood.
            var strike = new Pressure(w.Combat, Rules(nearRadius: 1000), true);
            strike.Advance(NoFire, new[] { Strike(w, StrikeKind.Landed, B, A, Health(w, A)) });
            Assert.That(strike.TryPressedFrom(Id(w, C), out from) && from.Equals(At(w, B)), Is.True);
        }

        // 4. A record that is not a landed strike brings nobody down whatever number it carries: the fall is the landed
        // strike's, and so is the source.
        [Test]
        public void OnlyALandedStrikeIsTheSourceOfAFall()
        {
            M6World w = Row1();
            var p = new Pressure(w.Combat, Rules(nearRadius: 1000), true);
            p.Advance(NoFire, new[] { Strike(w, StrikeKind.Blocked, F, A, 1), Strike(w, StrikeKind.Landed, B, A, Health(w, A)) });
            Assert.That(p.Value(Id(w, C)), Is.EqualTo(2000), "A fell 2 m from C");
            Assert.That(p.TryPressedFrom(Id(w, C), out SimVec3 from) && from.Equals(At(w, B)), Is.True);
        }

        // Step reads the strikes combat made this tick: a blade (A) cuts at an enemy (B) that stands still, and B's
        // teammate 2 m away (C2) feels the blow in the very tick the strike lands or is blocked. Without the rule it feels nothing.
        [TestCase(true)]
        [TestCase(false)]
        public void StepReadsTheStrikesOfTheTick(bool strikes)
        {
            const ulong C2 = 12;
            string ai = "{ \"meleeChaseRange\": 12, " + (strikes ? "\"pressureStrikes\": true, " : "") + AiM1MeleeTests.Pressed + " }";
            M6World w = World(new[] { Blade(A, 30500, Row), Still(B, 32000, Row), Still(C2, 34000, Row) }, ai,
                Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)), plates: false);
            Pressure p = w.System<Pressure>();
            bool struck = false;
            for (int tick = 0; tick < 300 && !struck; tick++)
            {
                w.Step();
                foreach (StrikeRecord strike in w.Combat.Strikes)
                    if (strike.Kind == StrikeKind.Landed || strike.Kind == StrikeKind.Blocked) struck = true;
                if (!struck) Assert.That(p.Value(Id(w, C2)), Is.EqualTo(0), "nothing pressed yet");
            }
            Assert.That(struck, Is.True, "the blade reached its enemy");
            Assert.That(p.Value(Id(w, C2)), Is.EqualTo(strikes ? 2500 : 0));
        }
    }
}
