using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Ml11aWorld;
using static WarCore.Tests.Ml11b1World;

namespace WarCore.Tests
{
    // AI-M1: the content fields pressureStrikes and meleeNerve, and what the pressure system saves and hashes with strikes on.
    public sealed class AiM1StateTests
    {
        private const string Pressed = AiM1MeleeTests.Pressed;
        private const string Prefix = "content pack 'ml11a-test' layer 'ml11a-test' ai field ";
        private const string NeedsPressure = "needs the pressure fields (pressureDeathRadiusMm ...)";

        private static AiRules Read(string ai) => Rules(ai).Section<AiRules>();
        private static M6World Duel(string ai) =>
            World(new[] { Blade(A, 30500, 20500), Still(B, 38500, 20500) }, ai, Guns(8, MeleeWorld.Spec(radius: 3, maxTargets: 1, engage: 1.8)), plates: false);
        private static byte[] Saved(Pressure p) { var writer = new SimStateWriter(); p.SaveState(writer); return writer.ToArray(); }
        private static ulong Hashed(Pressure p) { var hash = new StateHash(); p.WriteState(hash); return hash.Value; }

        // 8. pressureStrikes: read only with the pressure fields; false is the same as not written.
        [Test]
        public void PressureStrikesIsReadWithThePressureFields()
        {
            Assert.That(Read("{ " + Pressed + " }").Pressure.Value.Strikes, Is.False);
            Assert.That(Read("{ " + Pressed + ", \"pressureStrikes\": false }").Pressure.Value.Strikes, Is.False);
            Assert.That(Read("{ " + Pressed + ", \"pressureStrikes\": true }").Pressure.Value.Strikes, Is.True);
            Assert.That(Read("{ " + Pressed + ", \"pressureStrikes\": true, \"pressureFall\": \"kill\" }").Pressure.Value.FallOnKill, Is.True);
            foreach (string value in new[] { "true", "false" })
            {
                var alone = Assert.Throws<ContentException>(() => Rules("{ \"pressureStrikes\": " + value + " }"));
                Assert.That(alone.Message, Is.EqualTo(Prefix + "'pressureStrikes': " + NeedsPressure));
            }
            Assert.Throws<ContentException>(() => Rules("{ " + Pressed + ", \"pressureStrikes\": 1 }"));
        }

        // 8. meleeNerve: needs the pressure fields and a chase range above zero, whatever its value; false is the same as
        // not written.
        [Test]
        public void MeleeNerveNeedsPressureAndAChaseRange()
        {
            Assert.That(Read("{ \"meleeChaseRange\": 12, " + Pressed + " }").MeleeNerve, Is.False);
            Assert.That(Read("{ \"meleeChaseRange\": 12, \"meleeNerve\": false, " + Pressed + " }").MeleeNerve, Is.False);
            Assert.That(Read("{ \"meleeChaseRange\": 1, \"meleeNerve\": true, " + Pressed + " }").MeleeNerve, Is.True);
            foreach (string value in new[] { "true", "false" })
            {
                var noPressure = Assert.Throws<ContentException>(() => Rules("{ \"meleeChaseRange\": 12, \"meleeNerve\": " + value + " }"));
                Assert.That(noPressure.Message, Is.EqualTo(Prefix + "'meleeNerve': " + NeedsPressure));
                var noChase = Assert.Throws<ContentException>(() => Rules("{ \"meleeNerve\": " + value + ", " + Pressed + " }"));
                Assert.That(noChase.Message, Is.EqualTo(Prefix + "'meleeNerve': needs meleeChaseRange"));
                var zero = Assert.Throws<ContentException>(() => Rules("{ \"meleeChaseRange\": 0, \"meleeNerve\": " + value + ", " + Pressed + " }"));
                Assert.That(zero.Message, Is.EqualTo(Prefix + "'meleeNerve': needs meleeChaseRange"));
            }
        }

        // 8. meleeNerveFightMm: 0 when not written, 0 to 512000, and only next to meleeNerve: true.
        [Test]
        public void TheFightRangeNeedsTheMeleeNerve()
        {
            string With(string nerve, string fight) => "{ \"meleeChaseRange\": 12, " + nerve + "\"meleeNerveFightMm\": " + fight + ", " + Pressed + " }";
            const string On = "\"meleeNerve\": true, ";
            Assert.That(Read("{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + Pressed + " }").MeleeNerveFightMm, Is.EqualTo(0));
            foreach (string fine in new[] { "0", "512000" }) Assert.DoesNotThrow(() => Rules(With(On, fine)));
            Assert.That(Read(With(On, "0")).MeleeNerveFightMm, Is.EqualTo(0));
            Assert.That(Read(With(On, "4000")).MeleeNerveFightMm, Is.EqualTo(4000));
            Assert.That(Read(With(On, "512000")).MeleeNerveFightMm, Is.EqualTo(512000));
            Assert.Throws<ContentException>(() => Rules(With(On, "-1")));
            Assert.Throws<ContentException>(() => Rules(With(On, "512001")));
            Assert.Throws<ContentException>(() => Rules(With(On, "true")));
            foreach (string nerve in new[] { "", "\"meleeNerve\": false, " })
                foreach (string fight in new[] { "0", "4000" })
                {
                    var alone = Assert.Throws<ContentException>(() => Rules(With(nerve, fight)));
                    Assert.That(alone.Message, Is.EqualTo(Prefix + "'meleeNerveFightMm': needs meleeNerve"));
                }
        }

        // 8 and 9. The world: the fields reach the systems. Strikes add 4 to the pressure's version; the melee nerve changes
        // no version.
        [Test]
        public void TheFieldsReachTheWorld()
        {
            M6World plain = Duel("{ \"meleeChaseRange\": 12, " + Pressed + " }");
            M6World strikes = Duel("{ \"meleeChaseRange\": 12, \"pressureStrikes\": true, " + Pressed + " }");
            M6World nerve = Duel("{ \"meleeChaseRange\": 12, \"meleeNerve\": true, " + Pressed + " }");
            Assert.That(plain.System<Pressure>().StateVersion, Is.EqualTo(1));
            Assert.That(strikes.System<Pressure>().StateVersion, Is.EqualTo(5));
            Assert.That(nerve.System<Pressure>().StateVersion, Is.EqualTo(1));
            Assert.That((Part<bool>(plain, "meleeNerve"), Part<bool>(strikes, "meleeNerve"), Part<bool>(nerve, "meleeNerve")), Is.EqualTo((false, false, true)));
            Assert.That(Ml11aWorld.Ai(nerve).StateVersion, Is.EqualTo(Ml11aWorld.Ai(plain).StateVersion));
            Assert.That(Digest(strikes), Is.EqualTo(Digest(plain)), "the pressure's rules are not part of the scheduler's digest");

            Assert.That(strikes.Hash, Is.Not.EqualTo(plain.Hash), "the version is hashed");
        }

        // 9. The eight versions are all different: sources 1 or 2, plus 2 for the kill rule, plus 4 for strikes.
        [Test]
        public void EveryCombinationHasItsOwnVersion()
        {
            M6World w = Duel("{ }");
            int expected = 1;
            foreach (bool strikes in new[] { false, true })
                foreach (bool kill in new[] { false, true })
                    foreach (bool sources in new[] { false, true })
                        Assert.That(new Pressure(w.Combat, AiM1PressureTests.Rules(strikes, kill), sources).StateVersion, Is.EqualTo(expected++),
                            "strikes " + strikes + " kill " + kill + " sources " + sources);
        }

        // 9. The bytes and the hash: with strikes the saved rules carry one more flag after the kill flag, the entries are as
        // they were, and the state reads back to the same bytes and hash. Without strikes not a byte changes.
        [TestCase(false)]
        [TestCase(true)]
        public void TheStateCarriesAFlagOnlyWithStrikes(bool kill)
        {
            M6World w = Duel("{ }");
            var off = new Pressure(w.Combat, AiM1PressureTests.Rules(false, kill));
            var on = new Pressure(w.Combat, AiM1PressureTests.Rules(true, kill));
            var shot = new[] { new FireRecord(Id(w, B), default, At(w, B), At(w, A), default, 0, 2, FireOutcome.Open) };
            off.Advance(shot, new StrikeRecord[0]); on.Advance(shot, new StrikeRecord[0]);
            Assert.That((off.Value(Id(w, A)), on.Value(Id(w, A))), Is.EqualTo((1000, 1000)));

            byte[] Expected(bool flag, bool value = true)
            {
                var writer = new SimStateWriter();
                foreach (int number in new[] { 3000, 2000, 3000, 1000, 12, 7000, 1000, 2000, 0, 1000 }) writer.Int32(number);
                if (kill) writer.Bool(true);
                if (flag) writer.Bool(value);
                writer.Int32(1); writer.UInt64(Id(w, A).Value); writer.Int32(1000); writer.Bool(false);
                return writer.ToArray();
            }
            Assert.That(Saved(off), Is.EqualTo(Expected(false)));
            Assert.That(Saved(on), Is.EqualTo(Expected(true)));

            var again = new Pressure(w.Combat, AiM1PressureTests.Rules(true, kill));
            again.LoadState(new SimStateReader(Saved(on)));
            Assert.That(again.Value(Id(w, A)), Is.EqualTo(1000));
            Assert.That(Saved(again), Is.EqualTo(Saved(on)));
            Assert.That(Hashed(again), Is.EqualTo(Hashed(on)));
            Assert.That(Hashed(on), Is.Not.EqualTo(Hashed(off)), "the version is in the hash");
            // The ten numbers still have to agree.
            var other = new Pressure(w.Combat, AiM1PressureTests.Rules(true, kill, deathShock: 1999));
            var differ = Assert.Throws<SimSnapshotException>(() => other.LoadState(new SimStateReader(Saved(on))));
            Assert.That(differ.Message, Is.EqualTo("AI pressure rules differ."));
            // A flag that is not set is not the state of these rules.
            var unset = Assert.Throws<SimSnapshotException>(() => again.LoadState(new SimStateReader(Expected(true, false))));
            Assert.That(unset.Message, Is.EqualTo("AI pressure rules differ."));
        }

        // 9. A snapshot of a world with strikes is not read into a world without them, and the other way round: the
        // snapshot's manifest carries the version.
        [Test]
        public void ASnapshotIsNotReadAcrossTheSwitch()
        {
            string ai = "{ \"meleeChaseRange\": 12, " + Pressed + " }", with = "{ \"meleeChaseRange\": 12, \"pressureStrikes\": true, " + Pressed + " }";
            M6World plain = Duel(ai), strikes = Duel(with);
            plain.Step(3); strikes.Step(3);
            byte[] saved = strikes.Sim.SaveRetainedSnapshot();
            M6World same = Duel(with);
            same.Sim.RestoreRetainedSnapshot(saved);
            Assert.That(same.Hash, Is.EqualTo(strikes.Hash));
            var refused = Assert.Throws<SimSnapshotException>(() => Duel(ai).Sim.RestoreRetainedSnapshot(saved));
            Assert.That(refused.Message, Does.Contain("battle.ai-pressure v5").And.Contain("battle.ai-pressure v1"));
            Assert.Throws<SimSnapshotException>(() => Duel(with).Sim.RestoreRetainedSnapshot(plain.Sim.SaveRetainedSnapshot()));
        }
    }
}
