using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Tests.Blast1
{
    // BLAST-1 B2 to B6 and B8: how one blast lands. Unless a test says otherwise the blast point is 0.875 m up (the body centre
    // height), so the 3D distance to a unit standing on the flat ground equals the horizontal one.
    public sealed class Blast1DamageTests
    {
        private const long M = Blast1World.M, C = Blast1World.Centre, X = 20 * M, Z = 20 * M;

        private static Blast1World One(long h, BlastSpec? spec = null)
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules(spec ?? Blast1World.Frag())), new[] { Blast1World.U(X + h, Z, 2) });
            w.Blast(X, C, Z);
            return w;
        }

        // B2: inner 2 m, outer 6 m, damage 100. 1 m and 2 m full; 4 m half; 5 m a quarter; 5.9 m by the integer formula.
        [TestCase(1 * M, 100 * M)]
        [TestCase(2 * M, 100 * M)]
        [TestCase(4 * M, 50 * M)]
        [TestCase(5 * M, 25 * M)]
        [TestCase(386662L, 163850L)]
        [TestCase(2 * M + 1, 6553575L)]
        public void DamageFallsOffWithDistance(long h, long damage)
        {
            Assert.That(Blast1World.Expected(h), Is.EqualTo(damage), "the card's formula");
            Blast1World w = One(h);
            w.Step();
            Assert.That(w.Hits(), Is.EqualTo(new[] { (0, damage, h) }));
            Assert.That(w.Blasts.Records.Length, Is.EqualTo(1));
            Assert.That(w.Blasts.Records[0].VictimCount, Is.EqualTo(1));
            w.Step();
            Assert.That(w.Health(0), Is.EqualTo(100 * M - damage));
        }

        // B2: at the outer radius and beyond nothing is issued; damage that rounds to 0 issues nothing either.
        [TestCase(6 * M, 100)]
        [TestCase(7 * M, 100)]
        [TestCase(6 * M - 1, 1)]
        public void NothingAtTheEdge(long h, int damage)
        {
            Blast1World w = One(h, Blast1World.Frag(damage: damage));
            w.Step();
            Assert.That(w.Blasts.HitCount, Is.Zero);
            Assert.That(w.Blasts.Records[0].VictimCount, Is.Zero);
            w.Step();
            Assert.That(w.Health(0), Is.EqualTo(100 * M));
        }

        // The distance is 3D to the body centre: a unit 3 m above the blast's level and 2 m out is 3.606 m away, not 2 m.
        [Test]
        public void DistanceIsThreeDimensionalToTheBodyCentre()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 2 * M, Z, 2, y: 3 * M) });
            w.Blast(X, C, Z);
            w.Step();
            long d = Blast1World.Raw(2 * M, 3 * M, 0).Length.Raw;
            Assert.That(d, Is.InRange(236290L, 236296L), "sqrt(13) m");
            Assert.That(w.Hits(), Is.EqualTo(new[] { (0, Blast1World.Expected(d), d) }));
            // Measured from the foot the same unit would be 2.19 m away (from the point 0.875 m up), so the centre is what counts.
            var foot = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            foot.Blast(X, 0, Z, 0);
            foot.Step();
            long d2 = Blast1World.Raw(4 * M, C, 0).Length.Raw;
            Assert.That(foot.Hits(), Is.EqualTo(new[] { (0, Blast1World.Expected(d2), d2) }));
        }

        // A pack without a body: the body centre is the foot.
        [Test]
        public void WithoutABodyTheCentreIsTheFoot()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules(), body: false), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            w.Blast(X, 0, Z);
            w.Step();
            Assert.That(w.Hits(), Is.EqualTo(new[] { (0, 50 * M, 4 * M) }));
        }

        // B3: who is hit. Unit 0 is the source (team 1); 1 is team 1, 2 team 2, 3 team 0, 4 team 2 and dead.
        private static Blast1World Teams(bool friendly)
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules(Blast1World.Frag(friendly))), new[]
            {
                Blast1World.U(X + M, Z, 1), Blast1World.U(X - M, Z, 1), Blast1World.U(X, Z + M, 2), Blast1World.U(X, Z - M, 0),
                Blast1World.U(X + 30 * M, Z, 2),
            });
            w.Blast(X + 30 * M, C, Z);
            w.Step(); w.Step();
            Assert.That(w.Dead(4), Is.True);
            return w;
        }

        [Test]
        public void FriendlyHitsBothTeamsAndTheSource()
        {
            Blast1World w = Teams(true);
            w.Blast(X, C, Z, 0, w.Id(0), 1);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void UnfriendlyHitsOnlyTheOtherTeam()
        {
            Blast1World w = Teams(false);
            w.Blast(X, C, Z, 0, w.Id(0), 1);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 2 }));
            w.Blast(X, C, Z, 0, w.Id(2), 2);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 0, 1 }));
        }

        // A scripted blast (no source, team 0) that is not friendly hits both teams; team 0 and the dead never.
        [Test]
        public void AScriptedBlastHitsEveryTeam()
        {
            Blast1World w = Teams(false);
            w.Blast(X, C, Z);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        // A dead unit within reach issues nothing.
        [Test]
        public void TheDeadAreNotHit()
        {
            Blast1World w = Teams(true);
            w.Blast(X + 30 * M, C, Z);
            w.Step();
            Assert.That(w.Blasts.HitCount, Is.Zero);
        }

        // B4: a unit standing on a corner of the spatial index cells (2 m) is hit once.
        [Test]
        public void AUnitOnACellCornerIsHitOnce()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X, Z, 2), Blast1World.U(X + 2 * M, Z + 2 * M, 2) });
            w.Blast(X + M, C, Z + M);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 0, 1 }));
        }

        // B4 and C3: one blast's hits are recorded in StableId order, not in the query's order (nearest first). Both ways round:
        // in one of the two worlds the nearer unit has the larger StableId.
        [TestCase(false)]
        [TestCase(true)]
        public void HitsAreInStableIdOrder(bool swap)
        {
            long near = M, far = 3 * M;
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()),
                new[] { Blast1World.U(X + (swap ? far : near), Z, 2), Blast1World.U(X - (swap ? near : far), Z, 2) });
            w.Blast(X, C, Z);
            w.Step();
            Assert.That(w.Blasts.HitCount, Is.EqualTo(2));
            Assert.That(w.Blasts.Hit(0).Victim.Value, Is.LessThan(w.Blasts.Hit(1).Victim.Value));
        }

        // B5: a 3 m wall between the blast on the ground and a unit 4 m away stops it; a 0.5 m wall does not; without walls
        // the 3 m wall does not either.
        [TestCase(3 * M, true, false)]
        [TestCase(M / 2, true, true)]
        [TestCase(3 * M, false, true)]
        public void Walls(long top, bool walls, bool hit)
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules(Blast1World.Frag(walls: walls))), new[] { Blast1World.U(X + 4 * M, Z, 2) },
                new[] { new Ml3World.Wall(X + 3 * M, Z - 2 * M, X + 3 * M + M / 2, Z + 2 * M, 0, top) });
            w.Blast(X, 0, Z);
            w.Step();
            Assert.That(w.Blasts.HitCount, Is.EqualTo(hit ? 1 : 0));
        }

        // B5: the line starts 0.25 m over the blast point. A 1 m wall right next to the blast stops it (a line lifted much more
        // would clear it).
        [Test]
        public void TheLiftIsAQuarterMetre()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) },
                new[] { new Ml3World.Wall(X + M / 5, Z - 2 * M, X + 2 * M / 5, Z + 2 * M, 0, M) });
            w.Blast(X, 0, Z);
            w.Step();
            Assert.That(w.Blasts.HitCount, Is.Zero);
        }

        // Height field 12 x 4 m, grid 1 m, heights along x (raw).
        private static HeightField Field(params int[] alongX)
        {
            var points = new int[13 * 5];
            for (int j = 0; j <= 4; j++)
                for (int i = 0; i <= 12; i++) points[j * 13 + i] = i < alongX.Length ? alongX[i] : alongX[alongX.Length - 1];
            return new HeightField(12, 4, default, default, points);
        }

        // B5 on a height field: the blast lies on the ground at the foot of a slope whose first metre rises 0.45 m and then
        // flattens; a unit 4 m up the slope is hit (the line from the ground point itself would pass under the shoulder at
        // x = 3, the lifted one does not). Behind a 3 m ridge nobody is hit.
        [Test]
        public void SlopeAndRidge()
        {
            HeightField slope = Field(0, 0, 0, 29491, 32768, 36045, 39322);
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(6 * M, 2 * M, 2, y: 39322) }, ground: slope);
            w.Blast(2 * M, 0, 2 * M);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 0 }));
            Assert.That(slope.TryFirstBelow(Blast1World.Raw(2 * M, 0, 2 * M), Blast1World.Raw(6 * M, 39322 + C, 2 * M), out _), Is.True,
                "unlifted, the line goes under the shoulder");

            HeightField ridge = Field(0, 0, 0, 98304, 196608, 98304, 0);
            var r = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(6 * M, 2 * M, 2), Blast1World.U(M, 2 * M, 2) }, ground: ridge);
            r.Blast(2 * M, 0, 2 * M);
            r.Step();
            Assert.That(r.HitUnits(), Is.EqualTo(new[] { 1 }), "only the unit on the blast's side of the ridge");
        }

        // B5 with floor plates: a blast on the ground under a plate does not reach a unit standing on the plate right above.
        [Test]
        public void APlateStopsIt()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(4 * M, 2 * M, 2, y: Ml5Plates.Top), Blast1World.U(4 * M + 3 * M, 2 * M, 2) },
                plates: Ml5Plates.M());
            w.Blast(4 * M, 0, 2 * M);
            w.Step();
            Assert.That(w.HitUnits(), Is.EqualTo(new[] { 1 }));
        }

        // B6: a shielded unit gets the command with damage 0 and keeps its health.
        [Test]
        public void AShieldedUnitTakesZero()
        {
            var guard = new ShieldSome();
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + M, Z, 2), Blast1World.U(X - M, Z, 2) }, guard: guard);
            guard.Add(w.Id(0));
            w.Blast(X, C, Z);
            w.Step();
            Assert.That(w.Hits().OrderBy(h => h.Unit), Is.EqualTo(new[] { (0, 0L, M), (1, 100 * M, M) }));
            Assert.That(w.Blasts.Records[0].VictimCount, Is.EqualTo(2));
            Assert.That((w.DamageFacts(0), w.DamageFacts(1)), Is.EqualTo((new[] { 0L }, new[] { 100 * M })), "the shielded unit's command is issued, with 0");
            w.Step();
            Assert.That(w.Health(0), Is.EqualTo(100 * M));
            Assert.That(w.Dead(1), Is.True);
        }

        // B8: one source, two blasts on one tick, one victim 4 m away: two commands, 50 each, the victim dies.
        [Test]
        public void TwoBlastsOfOneSourceBothLand()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X - 20 * M, Z, 1), Blast1World.U(X + 4 * M, Z, 2) });
            w.Blast(X, C, Z, 0, w.Id(0), 1);
            w.Blast(X, C, Z, 0, w.Id(0), 1);
            Assert.DoesNotThrow(() => w.Step(), "the two keys differ by the serial");
            Assert.That(w.Hits(), Is.EqualTo(new[] { (1, 50 * M, 4 * M), (1, 50 * M, 4 * M) }));
            Assert.That(w.DamageFacts(1), Is.EqualTo(new[] { 50 * M, 50 * M }));
            w.Step();
            Assert.That(w.Health(1), Is.Zero);
            Assert.That(w.Dead(1), Is.True);
        }

        // B8: a scripted blast (no source) still lands, and two of them on one tick both land.
        [Test]
        public void ScriptedBlastsCarryNoSource()
        {
            var w = new Blast1World(Blast1World.Pack(Blast1World.Rules()), new[] { Blast1World.U(X + 4 * M, Z, 2) });
            w.Blast(X, C, Z); w.Blast(X, C, Z);
            Assert.DoesNotThrow(() => w.Step());
            Assert.That(w.Blasts.Records.ToArray().Select(r => (r.Source, r.Team, r.Serial)), Is.EqualTo(new[] { (default(StableId), 0, 1L), (default(StableId), 0, 2L) }));
            w.Step();
            Assert.That(w.Dead(0), Is.True);
        }
    }
}
