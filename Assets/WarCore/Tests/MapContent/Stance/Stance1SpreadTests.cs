using NUnit.Framework;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Aim1.Aim1Worlds;

namespace WarCore.Tests.Stance
{
    // STANCE-1 S9 (rest, wired after AIM-1): the stance is a gear of ShotAngle's spread line. Unit 1 (team 1) stands still at
    // (20, 10) m or (50, 10) m; unit 2 (team 2, unarmed) at (50, 40) m. Nobody takes damage, so the shooter keeps firing.
    public sealed class Stance1SpreadTests
    {
        private const string Harmless = "\"combat\": { \"resolver\": \"damage\" }, \"weapons\": [ "
            + "{ \"id\": \"rwr.weapon-1\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-2\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-3\", \"effectRaw\": 0 } ]";

        private static readonly (string, string)[] Layers =
        {
            ("s9-harmless", "{ \"id\": \"s9-harmless\", \"extends\": \"rwr-stance\", " + Harmless + " }"),
            // rwr.weapon-1 with a fixed spread of 2 thousandths of a degree, and with none.
            ("s9-tiny", "{ \"id\": \"s9-tiny\", \"extends\": \"s9-harmless\", \"weapons\": [ "
                + "{ \"id\": \"rwr.weapon-1\", \"spreadBase\": 2, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 2 } ] }"),
            ("s9-tiny-none", "{ \"id\": \"s9-tiny-none\", \"extends\": \"s9-harmless\", \"weapons\": [ "
                + "{ \"id\": \"rwr.weapon-1\", \"spreadBase\": 0, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 0 } ] }"),
        };

        private static SimSession Duel(string layer, int level, long xMm = 50000)
        {
            SimSession s = World(Load(layer, Layers), new[] { Unit(1, 1, 1, xMm, 10000), Unit(2, 2, 2, 50000, 40000, weapon: -1) });
            if (level != 0) Assert.That(s.Stances.ForceStance(Id(s, 1), level), Is.True);
            return s;
        }

        private static int Angle(SimSession s, int weaponAngle) => CombatOf(s).ShotAngle(Id(s, 1), State(s, 1), weaponAngle, s.Tick);

        [Test]
        public void S9_ShotAngleTakesTheStanceRow()
        {
            SimSession s = Duel("s9-harmless", 0);
            Assert.That(Angle(s, 3000), Is.EqualTo(3000), "standing: 1000");
            Assert.That(s.Stances.ForceStance(Id(s, 1), 1), Is.True);
            Assert.That(Angle(s, 3000), Is.EqualTo(2100), "crouched: 700");
            Assert.That(s.Stances.ForceStance(Id(s, 1), 2), Is.True);
            Assert.That(Angle(s, 3000), Is.EqualTo(1200), "prone: 400");
            Assert.That(s.Stances.TryStance(Id(s, 1), 0), Is.True);
            Assert.That(Angle(s, 3000), Is.EqualTo(3000), "prone to standing: the wider, standing");
            SimSession field = World(Load("aim1-harmless-field"), new[] { Unit(1, 1, 1, 50000, 10000), Unit(2, 2, 2, 50000, 40000, weapon: -1) });
            Assert.That(Angle(field, 3000), Is.EqualTo(3000), "no stances: no gear");
        }

        [Test]
        public void S9_AProneAngleOfTwoDrawsNoSpreadNumber()
        {
            SimSession prone = Duel("s9-tiny", 2), none = Duel("s9-tiny-none", 2), standing = Duel("s9-tiny", 0);
            prone.StepOnce(); none.StepOnce(); standing.StepOnce();
            Assert.That(CombatOf(prone).Fires.Length, Is.EqualTo(1));
            Assert.That(CombatOf(none).Fires.Length, Is.EqualTo(1));
            Assert.That(CombatOf(standing).Fires.Length, Is.EqualTo(1));
            Assert.That(RngHash(prone), Is.EqualTo(RngHash(none)), "2 * 400 / 1000 = 0: no spread draw");
            Assert.That(RngHash(standing), Is.Not.EqualTo(RngHash(none)), "standing, an angle of 2 draws one spread number");
            Assert.That(State(prone, 1).SpreadStored, Is.EqualTo(2), "the weapon's state keeps its own angle");
        }

        [Test]
        public void S9_TheWeaponsSpreadStateIsTheSameInEveryStance()
        {
            SimSession standing = Duel("s9-harmless", 0, 20000), crouched = Duel("s9-harmless", 1, 20000), prone = Duel("s9-harmless", 2, 20000);
            int shots = 0;
            for (int tick = 1; tick <= 40 && shots < 3; tick++)
            {
                standing.StepOnce(); crouched.StepOnce(); prone.StepOnce();
                Assert.That(CombatOf(crouched).Fires.Length, Is.EqualTo(CombatOf(standing).Fires.Length), "tick " + tick);
                Assert.That(CombatOf(prone).Fires.Length, Is.EqualTo(CombatOf(standing).Fires.Length), "tick " + tick);
                if (CombatOf(standing).Fires.Length == 0) continue;
                shots++;
                foreach (SimSession other in new[] { crouched, prone })
                {
                    Assert.That(State(other, 1).SpreadStored, Is.EqualTo(State(standing, 1).SpreadStored), "shot " + shots);
                    Assert.That(State(other, 1).SpreadLastMilli, Is.EqualTo(State(standing, 1).SpreadLastMilli), "shot " + shots);
                }
            }
            Assert.That(shots, Is.EqualTo(3));
            Assert.That(Angle(crouched, 3000), Is.EqualTo(2100));
        }
    }
}
