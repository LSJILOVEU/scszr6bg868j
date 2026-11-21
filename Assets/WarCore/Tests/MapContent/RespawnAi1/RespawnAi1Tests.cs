using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnAi1;
using static WarCore.Tests.RespawnAi1World;

namespace WarCore.Tests
{
    // RESPAWN-AI-1 acceptance A1-A11 on the fixture of RespawnAi1World (team 1's list is [B1, P]).
    public sealed class RespawnAi1Tests
    {
        // Every step a test expects to succeed runs inside Assert.DoesNotThrow, so an exception reads as a failed assertion.
        private static void Go(M6World w, int count = 1) => Assert.DoesNotThrow(() => w.Step(count));
        private static UnitSpawn InP(ulong key = Contester, int team = 2) => U(key, 41000, 21000, team);
        private static M6World Contested(bool skip) => World(Units(InP()), Pack(P(), skip));

        // A1: the switch written false is the switch left out: same digest, and the locked run of the old code.
        [Test]
        public void A1_FalseIsLeftOut_AndKeepsTheLockedRun()
        {
            Assert.That(Pack(P(), false).PolicyDigest, Is.EqualTo(OldPack(P()).PolicyDigest));
            Assert.That(Pack(P(), false).PolicyDigest, Is.EqualTo(RespawnAi1LockTests.PackDigest));
            Assert.That(Pack(P(), true).PolicyDigest, Is.Not.EqualTo(RespawnAi1LockTests.PackDigest));
            Assert.That((Pack(P(), false).ReinforceSkipContested, Pack(P(), true).ReinforceSkipContested, OldPack(P()).ReinforceSkipContested),
                Is.EqualTo((false, true, false)));
            M6World w = World(Units(InP()), Pack(P(), false));
            Assert.That(Run(w, 300), Is.EqualTo(RespawnAi1LockTests.Run300));
            Assert.That((w.Reinforce.StateVersion, Saved(w.Reinforce).Length, HashOf(w.Reinforce)), Is.EqualTo((1, 64, RespawnAi1LockTests.ReinforceHash300)));
        }

        // A2: with the switch an enemy in P's circle keeps team 1's reinforcements at B1; without it the second comes out at P.
        [Test]
        public void A2_TheContestedPointIsSkipped()
        {
            var on = Deploys(Contested(true), 1, 4 * Interval);
            Assert.That(on.Count, Is.EqualTo(4));
            Assert.That(on.ConvertAll(d => Near(d.point, B1)), Is.EqualTo(new[] { true, true, true, true }), "all four at B1");
            var off = Deploys(Contested(false), 1, 2 * Interval);
            Assert.That((off.Count, Near(off[0].point, B1), Near(off[1].point, PCentre)), Is.EqualTo((2, true, true)), "control: the second at P");
        }

        // The list team 1 was offered on its first attempt (tick 30), with the switch on (unless said) and a recorder bound.
        private static (bool, int, SimVec3, int)[] FirstList(List<UnitSpawn> units, ControlPointDef point, Action<M6World> setup = null, bool skip = true)
        {
            M6World w = World(units, Pack(point, skip));
            Recorder recorder = Record(w);
            setup?.Invoke(w);
            Go(w, Interval);
            Call call = recorder.Calls.Find(c => c.Team == 1);
            Assert.That(call.Options, Is.Not.Null, "team 1 was asked");
            return Of(call.Options);
        }

        private static readonly (bool, int, SimVec3, int)[] Both = { Base1, PEntry }, OnlyBase = { Base1 };

        // A3: the edge of the circle (exactly the radius from the centre) contests; one raw further does not.
        [TestCase(0L, true)]
        [TestCase(1L, false)]
        public void A3_TheCircleIncludesItsEdge(long extraRaw, bool contested)
        {
            var at = new SimVec3(SimFix.FromRaw(PCentre.X.Raw + 4 * SimFix.Scale + extraRaw), default, PCentre.Z);
            var list = FirstList(Units(U(Contester, 70000, 10000, 2)), P(), w => w.Sim.RegisterGameplay("test.move", new MoveAt(w, 1, Contester, at), SimSystemPhase.Requests));
            Assert.That(list, Is.EqualTo(contested ? OnlyBase : Both));
        }

        // A3: a dead enemy, a unit of team 1, of team 0 or of a team above TeamCount in the circle do not contest.
        [TestCase(2, true, false)]
        [TestCase(2, false, true)]
        [TestCase(1, false, false)]
        [TestCase(0, false, false)]
        [TestCase(3, false, false)]
        public void A3_WhoContests(int team, bool killed, bool contested)
        {
            var list = FirstList(Units(InP(Contester, team)), P(), w =>
            {
                if (killed) w.Sim.RegisterGameplay("test.kill", new M6Damage(w, 1, Contester), SimSystemPhase.Queries);
            });
            Assert.That(list, Is.EqualTo(contested ? OnlyBase : Both));
        }

        [Test]
        public void A3_TheDeadEnemyIsDead()
        {
            M6World w = World(Units(InP()), Pack(P(), true));
            w.Sim.RegisterGameplay("test.kill", new M6Damage(w, 1, Contester), SimSystemPhase.Queries);
            Go(w, 2);
            Assert.That(w.Dead(Contester), Is.True);
        }

        // A3: P owned by team 2, P neutral, P without a spawn: not in team 1's list, with the switch or without.
        [TestCase(2, true)]
        [TestCase(0, true)]
        [TestCase(1, false)]
        public void A3_OnlyOwnedSpawnPointsAreListed(int owner, bool spawn)
        {
            Assert.That(FirstList(Units(), P(owner, spawn)), Is.EqualTo(OnlyBase), "switch on");
            Assert.That(FirstList(Units(), P(owner, spawn), skip: false), Is.EqualTo(OnlyBase), "switch off");
            Assert.That(FirstList(Units(), P()), Is.EqualTo(Both), "control");
        }

        // A4: no base for team 1 and P contested: the attempt is wasted (counted, no success, nobody out); the enemy walks off at
        // tick 40 and the next attempt (tick 60) comes out at P.
        [Test]
        public void A4_AnEmptyListWastesTheAttempt()
        {
            M6World w = World(Units(InP()), Pack(P(), true, teamOneBase: false));
            w.Sim.RegisterGameplay("test.move", new MoveAt(w, 40, Contester, SimVec3.Metres(70, 0, 10)), SimSystemPhase.Requests);
            var deploys = Deploys(w, 1, Interval);
            Assert.That((deploys.Count, w.Reinforce.Attempts(1), w.Reinforce.Successes(1), w.Reinforce.NextTick(1)), Is.EqualTo((0, 1L, 0L, 60L)));
            deploys = Deploys(w, 1, Interval);
            Assert.That(deploys.Count, Is.EqualTo(1));
            Assert.That((deploys[0].tick, Near(deploys[0].point, PCentre)), Is.EqualTo((60L, true)));
            Assert.That((w.Reinforce.Attempts(1), w.Reinforce.Successes(1)), Is.EqualTo((2L, 1L)));
        }

        // A5 and A10 on floor plates (the ML-9b arena): P's centre and spawn on the floor (layer 1). An enemy under the floor
        // at the same plane position does not contest and team 1 comes out upstairs; an enemy upstairs in the circle does.
        private static ControlPointDef Upstairs() =>
            new ControlPointDef(5, Ml9bMatchTests.Up(41500, 32500), SimFix.FromInt(3), 1, true, Ml9bMatchTests.Up(41500, 32500), 1, 1);

        [TestCase(0, false)]
        [TestCase(1, true)]
        public void A5_OnPlatesOnlyTheSameLayerContests(int enemyLayer, bool contested)
        {
            var units = Units(U(Contester, 41500, 33500, 2, layer: enemyLayer));
            M6World w = null;
            Assert.DoesNotThrow(() => w = new M6World(Recipe(units, Pack(Upstairs(), true)).WithPlatesPreview(), Ml9bMatchTests.Arena()));
            Recorder recorder = Record(w, TeamOne(contested ? 0 : 1));
            var deploys = Deploys(w, 1, Interval);
            Call call = recorder.Calls.Find(c => c.Team == 1);
            var up = (false, 0, Ml9bMatchTests.Up(41500, 32500), 1);
            Assert.That(Of(call.Options), Is.EqualTo(contested ? OnlyBase : new[] { Base1, up }));
            Assert.That(deploys.Count, Is.EqualTo(1));
            DeployRecord record = default;
            foreach (DeployRecord r in w.Reinforce.Deployed) if (r.Team == 1) record = r;
            Assert.That((record.Layer, record.PointIndex), Is.EqualTo(contested ? (0, -1) : (1, 0)), "A10: the record's layer and point index");
            Assert.That(w.Read<Position>(KeyOf(w, record.Unit)).Value.Y.Raw, Is.EqualTo(contested ? 0 : 3 * Ml7Worlds.M));
        }

        // A6: not bound and bound to the explicit rotation: the same deployments (tick, unit, point) for 300 ticks.
        [Test]
        public void A6_TheDefaultIsTheRotation()
        {
            var plain = Deploys(Contested(false), 1, 300);
            M6World bound = Contested(false);
            bound.Reinforce.BindChooser(Rotation());
            var rotated = Deploys(bound, 1, 300);
            Assert.That(plain.Count, Is.EqualTo(6));
            Assert.That(plain.Exists(d => Near(d.point, PCentre)), Is.True);
            Assert.That(rotated, Is.EqualTo(plain));
        }

        // A7: a chooser that sends team 1 to P first: four deployments at P (its four cells), the fifth goes round to B1. Every
        // call gets this step's list, the team's attempt number and the team.
        [Test]
        public void A7_TheChoosersEntryIsTriedFirst()
        {
            M6World w = World(Units(), Pack(P(), false));
            Recorder recorder = Record(w, TeamOne(1));
            var deploys = new List<(long tick, ulong key, SimVec3 point)>();
            var attemptsSeen = new List<(long, long)>();
            for (int i = 0; i < 5; i++)
            {
                int before = recorder.Calls.Count;
                deploys.AddRange(Deploys(w, 1, Interval));
                Call call = recorder.Calls.FindLast(c => c.Team == 1);
                Assert.That(recorder.Calls.Count, Is.EqualTo(before + 2), "one call per team");
                Assert.That(Of(call.Options), Is.EqualTo(Both), "attempt " + i);
                attemptsSeen.Add((call.Attempt, w.Reinforce.Attempts(1) - 1));
                Assert.That(recorder.Calls.FindLast(c => c.Team == 2).Options.Length, Is.EqualTo(1), "team 2: its base only");
            }
            Assert.That(attemptsSeen.ConvertAll(a => a.Item1), Is.EqualTo(attemptsSeen.ConvertAll(a => a.Item2)));
            Assert.That(attemptsSeen.ConvertAll(a => a.Item1), Is.EqualTo(new List<long> { 0, 1, 2, 3, 4 }));
            Assert.That(deploys.ConvertAll(d => Near(d.point, PCentre)), Is.EqualTo(new[] { true, true, true, true, false }));
            Assert.That(Near(deploys[4].point, B1), Is.True, "round to B1");
            Assert.That(deploys.ConvertAll(d => d.point).FindAll(p => Near(p, PCentre)).Count, Is.EqualTo(new HashSet<SimVec3>(deploys.ConvertAll(d => d.point).GetRange(0, 4)).Count));
            Assert.That(recorder.Calls.FindLast(c => c.Team == 1).Unit, Is.EqualTo(w.Sim.Entities.StableOf(w.Unit(deploys[4].key))));
        }

        // A8: an index outside the list throws; binding after the first step, twice, or null throws.
        [TestCase(2)]
        [TestCase(-1)]
        public void A8_AnIndexOutsideTheListThrows(int first)
        {
            M6World w = World(Units(), Pack(P(), false));
            w.Reinforce.BindChooser(TeamOne(first));
            Go(w, Interval - 1);
            Exception e = Assert.Catch(() => w.Step()); // the step with the bad index
            while (e.InnerException != null && !(e is ArgumentOutOfRangeException)) e = e.InnerException;
            Assert.That(e, Is.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void A8_BindingRules()
        {
            M6World w = World(Units(), Pack(P(), false));
            Assert.Throws<ArgumentNullException>(() => w.Reinforce.BindChooser(null));
            Assert.DoesNotThrow(() => w.Reinforce.BindChooser(Rotation()));
            Assert.Throws<InvalidOperationException>(() => w.Reinforce.BindChooser(Rotation(5)), "twice");
            M6World stepped = World(Units(), Pack(P(), false));
            Go(stepped);
            Assert.Throws<InvalidOperationException>(() => stepped.Reinforce.BindChooser(Rotation()), "after the first step");
            M6World loaded = World(Units(), Pack(P(), false));
            loaded.Sim.RestoreRetainedSnapshot(World(Units(), Pack(P(), false)).Sim.SaveRetainedSnapshot());
            Assert.Throws<InvalidOperationException>(() => loaded.Reinforce.BindChooser(Rotation()), "after a load");
        }

        // A9: a bound world saved at 100 and restored into a fresh world bound with the same key runs on with the same hashes to
        // 300; another key, no chooser, or a chooser reading an unbound save is refused. The key is in the bytes and the hash.
        [Test]
        public void A9_TheChooserKeyIsSavedAndChecked()
        {
            M6World straight = Contested(true);
            straight.Reinforce.BindChooser(Rotation(1234));
            var hashes = new ulong[301];
            byte[] snapshot = null;
            for (int t = 1; t <= 300; t++)
            {
                Go(straight); hashes[t] = straight.Hash;
                if (t == 100) snapshot = straight.Sim.SaveRetainedSnapshot();
            }
            Assert.That((straight.Reinforce.StateVersion, Saved(straight.Reinforce).Length), Is.EqualTo((3, 72)));
            Assert.That((straight.Reinforce.Attempts(1), straight.Reinforce.Successes(1)), Is.EqualTo((10L, 4L)), "B1's four cells fill, P stays contested");

            M6World restored = Contested(true);
            restored.Reinforce.BindChooser(Rotation(1234));
            Assert.DoesNotThrow(() => restored.Sim.RestoreRetainedSnapshot(snapshot));
            Assert.That(restored.Hash, Is.EqualTo(hashes[100]));
            for (int t = 101; t <= 300; t++) { Go(restored); Assert.That(restored.Hash, Is.EqualTo(hashes[t]), "tick " + t); }

            M6World other = Contested(true);
            other.Reinforce.BindChooser(Rotation(1235));
            Assert.Catch(() => other.Sim.RestoreRetainedSnapshot(snapshot), "another key");
            Assert.Catch(() => Contested(true).Sim.RestoreRetainedSnapshot(snapshot), "no chooser");
            M6World unboundRun = Contested(true);
            Go(unboundRun, 100);
            M6World bound = Contested(true);
            bound.Reinforce.BindChooser(Rotation(1234));
            Assert.Catch(() => bound.Sim.RestoreRetainedSnapshot(unboundRun.Sim.SaveRetainedSnapshot()), "a chooser reading an unbound save");

            // The key reaches the hash: two bound systems with equal counters and different keys hash differently.
            M6World a = Contested(true), b = Contested(true);
            a.Reinforce.BindChooser(Rotation(1234)); b.Reinforce.BindChooser(Rotation(1235));
            Assert.That(HashOf(a.Reinforce), Is.Not.EqualTo(HashOf(b.Reinforce)));
            Assert.That(Saved(a.Reinforce), Is.Not.EqualTo(Saved(b.Reinforce)));
        }

        // A10: a deployment from B1 records layer 0 and point -1, one from P point 0; records are cleared on the next step.
        [Test]
        public void A10_RecordsCarryLayerAndPoint_AndClear()
        {
            M6World w = World(Units(), Pack(P(), false));
            Go(w, Interval);
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(2));
            DeployRecord first = w.Reinforce.Deployed[0];
            Assert.That((first.Team, first.Layer, first.PointIndex, Near(first.Point, B1)), Is.EqualTo((1, 0, -1, true)));
            Go(w);
            Assert.That(w.Reinforce.Deployed.Length, Is.Zero, "cleared on the next step");
            Go(w, Interval - 1);
            DeployRecord second = w.Reinforce.Deployed[0];
            Assert.That((second.Team, second.Layer, second.PointIndex, Near(second.Point, PCentre)), Is.EqualTo((1, 0, 0, true)));
        }

        // A11: reinforceSkipContested must be true or false (the error names the field); true and false give different digests,
        // false the digest of the field left out.
        [TestCase("1")]
        [TestCase("\"yes\"")]
        [TestCase("null")]
        public void A11_TheFieldMustBeABool(string value)
        {
            var e = Assert.Throws<ContentException>(() => RespawnDigestLockTests.Load(", \"reinforceSkipContested\": " + value));
            StringAssert.Contains("reinforceSkipContested", e.Message);
        }

        [Test]
        public void A11_TrueAndFalseDigests()
        {
            ulong Digest(string extra) => RespawnDigestLockTests.Load(extra).BuildMatch(RespawnDigestLockTests.Points(), RespawnDigestLockTests.Bases()).PolicyDigest;
            Assert.That(Digest(", \"reinforceSkipContested\": false"), Is.EqualTo(RespawnDigestLockTests.Digest));
            Assert.That(Digest(""), Is.EqualTo(RespawnDigestLockTests.Digest));
            Assert.That(Digest(", \"reinforceSkipContested\": true"), Is.Not.EqualTo(RespawnDigestLockTests.Digest));
            Assert.That(RespawnDigestLockTests.Load(", \"reinforceSkipContested\": true").Match.ReinforceSkipContested, Is.True);
            Assert.That(RespawnDigestLockTests.Load(", \"reinforceSkipContested\": true").BuildMatch(RespawnDigestLockTests.Points(), RespawnDigestLockTests.Bases())
                .ReinforceSkipContested, Is.True);
            Assert.That(RespawnDigestLockTests.Load("").Match.ReinforceSkipContested, Is.False);
        }

        // The bridge passes the plate layers to reinforcement (null without plates), as it does to respawn and capture.
        [Test]
        public void TheBridgePassesLayersToReinforcement()
        {
            M6World flat = World(Units(), Pack(P(), true));
            Assert.That(Ml7Worlds.Field<PointPresence>(flat.Reinforce, "presence"), Is.Not.Null);
            Assert.That(Ml7Worlds.Field<IUnitLayers>(Ml7Worlds.Field<PointPresence>(flat.Reinforce, "presence"), "layers"), Is.Null);
            M6World plates = new M6World(Recipe(Units(), Pack(Upstairs(), true)).WithPlatesPreview(), Ml9bMatchTests.Arena());
            Assert.That(Ml7Worlds.Field<IUnitLayers>(Ml7Worlds.Field<PointPresence>(plates.Reinforce, "presence"), "layers"), Is.Not.Null);
        }
    }
}
