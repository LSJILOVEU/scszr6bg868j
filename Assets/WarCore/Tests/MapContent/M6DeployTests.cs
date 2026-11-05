using System;
using System.Collections.Generic;
using System.Linq;
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
    public sealed class M6DeployTests
    {
        // Record candidate order, but delegate every decision to the real BattleAdapter.
        private sealed class ObservedDeployer : IUnitDeployer
        {
            private readonly IUnitDeployer inner;
            internal readonly List<SimVec3> Candidates = new List<SimVec3>();
            internal readonly List<bool> Accepted = new List<bool>();
            public ulong PolicyKey => inner.PolicyKey;
            internal ObservedDeployer(M6World world)
            {
                var field = typeof(ReinforceSystem).GetField("deployer", BindingFlags.Instance | BindingFlags.NonPublic);
                inner = (IUnitDeployer)field.GetValue(world.Reinforce);
                field.SetValue(world.Reinforce, this);
            }
            public bool TryDeploy(EntityId unit, SimVec3 at)
            {
                Candidates.Add(at);
                bool accepted = inner.TryDeploy(unit, at);
                Accepted.Add(accepted);
                return accepted;
            }
        }

        [TestCase(32768, false)] // 0.5 m: touching radii is still below the deployment clearance.
        [TestCase(49152, false)] // 0.75 m
        [TestCase(65535, false)] // 1 m - 1 raw
        [TestCase(65536, true)]  // Exactly 2 * (0.25 + 0.25) m
        public void D5_RealDeploymentLocksOneMetreClearance(int distanceRaw, bool accepted)
        {
            var w = new M6World(M6World.RecipeOf(new[] {
                M6World.U(1, 1, reserve: true), M6World.U(2, 0, 7)
            }, M6World.Pack(interval: 1, columns: 1, rows: 1)));
            var candidate = M6World.P(6);
            w.Set(2, new Position(candidate + new SimVec3(SimFix.FromRaw(distanceRaw), default, default)));
            var observed = new ObservedDeployer(w);
            w.Step();
            Assert.That(observed.Candidates, Is.EqualTo(new[] { candidate }));
            Assert.That(observed.Accepted, Is.EqualTo(new[] { accepted }));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(1));
            Assert.That(w.Has<Position>(1), Is.EqualTo(accepted));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(accepted ? 1 : 0));
            if (accepted)
            {
                Assert.That(w.Reinforce.Deployed[0].Point, Is.EqualTo(candidate));
                Assert.That(w.Read<Position>(1).Value, Is.EqualTo(candidate));
            }
        }

        [Test]
        public void D5_RealDeploymentUsesFirstLegalCellInRotatingRowMajorOrder()
        {
            SimVec3 Point(int xQuarters, int zQuarters) => new SimVec3(SimFix.Ratio(xQuarters, 4), default, SimFix.Ratio(zQuarters, 4));
            var first = Point(18, 77); var second = Point(24, 77);
            var third = Point(30, 77); var fourth = Point(18, 83);
            var w = new M6World(M6World.Endless(M6World.RecipeOf(new[] {
                M6World.U(1, 1, reserve: true), M6World.U(2, 1, reserve: true),
                M6World.U(3, 0), M6World.U(4, 0)
            }, M6World.Pack(interval: 1, columns: 3, rows: 2))));
            w.Set(3, new Position(first)); w.Set(4, new Position(second));
            var observed = new ObservedDeployer(w);
            w.Step();
            Assert.That(observed.Candidates, Is.EqualTo(new[] { first, second, third }));
            Assert.That(observed.Accepted, Is.EqualTo(new[] { false, false, true }));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed[0].Point, Is.EqualTo(third));
            observed.Candidates.Clear(); observed.Accepted.Clear();
            w.Step();
            Assert.That(observed.Candidates, Is.EqualTo(new[] { second, third, fourth }));
            Assert.That(observed.Accepted, Is.EqualTo(new[] { false, false, true }));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed[0].Point, Is.EqualTo(fourth));
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(2));
        }

        [Test]
        public void D5_PLACEHOLDER_ExactAttemptsStableOrderComponentsAnchorsAndExhaustion()
        {
            var units = new[] { M6World.U(9, 1, reserve: true), M6World.U(1, 1, reserve: true), M6World.U(4, 1, reserve: true), M6World.U(2, 2, 70) };
            var points = new[] { new ControlPointDef(7, M6World.P(40), SimFix.FromInt(6), 1, true, M6World.P(40)) };
            var w = new M6World(M6World.Endless(M6World.RecipeOf(units, M6World.Pack(points, interval: 5))), reverse: true);
            var order = units.Where(u => u.Reserve).OrderBy(u => StableEntityKey.Placed(1, u.Key).Id).ToArray();
            for (int i = 0; i < 3; i++)
            {
                ulong key = order[i].Key; var old = w.Read<Combatant>(key);
                Assert.That(w.Has<Position>(key) || w.Has<Collider>(key) || w.Has<MoveIntent>(key), Is.False);
                w.Step(4); Assert.That(w.Reinforce.Deployed.Length, Is.Zero); w.Step();
                Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(i + 1)); Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(1));
                var record = w.Reinforce.Deployed[0]; Assert.That(record.Tick, Is.EqualTo((i + 1) * 5));
                Assert.That(record.Unit, Is.EqualTo(StableEntityKey.Placed(1, key).Id));
                Assert.That(w.Has<Position>(key) && w.Has<Collider>(key) && w.Has<MoveIntent>(key) && w.Has<MoveWaypoint>(key), Is.True);
                Assert.That(w.Read<Combatant>(key), Is.EqualTo(old)); Assert.That(w.Read<MoveIntent>(key).Active, Is.False);
                Assert.That(w.Read<MoveIntent>(key).SpeedPerTick, Is.EqualTo(SimFix.Ratio(1, 30000)));
                var anchor = M6World.P(i % 2 == 0 ? 6 : 40); var delta = record.Point - anchor;
                Assert.That(Math.Abs(delta.X.Raw), Is.LessThanOrEqualTo(SimFix.Ratio(9, 4).Raw));
                Assert.That(Math.Abs(delta.Z.Raw), Is.LessThanOrEqualTo(SimFix.Ratio(9, 4).Raw));
                for (int j = 0; j < i; j++) Assert.That((record.Point - w.Read<Position>(order[j].Key).Value).LengthSquared,
                    Is.GreaterThanOrEqualTo(new SimVec3(SimFix.Ratio(1, 2), default, default).LengthSquared));
            }
            w.Step(10); Assert.That(w.Reinforce.Successes(1), Is.EqualTo(3)); Assert.That(w.Reinforce.Deployed.Length, Is.Zero);
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(5)); Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(30));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void D5_PLACEHOLDER_UnavailableGridSkipsWithoutBankingAndRetries(bool wall)
        {
            var units = new[] { M6World.U(1, 1, reserve: true), M6World.U(2, 2, 6) };
            var pack = M6World.Pack(interval: 3, columns: 1, rows: 1);
            byte[] map = wall ? WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (5, 19, 2, 2))) : null;
            var w = new M6World(M6World.RecipeOf(units, pack), map);
            w.Step(6); Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(2)); Assert.That(w.Reinforce.Successes(1), Is.Zero);
            w.Set(2, new Position(M6World.P(70))); w.Step(2); Assert.That(w.Has<Position>(1), Is.False);
            w.Step(); Assert.That(w.Reinforce.Successes(1), Is.EqualTo(wall ? 0 : 1));
            Assert.That(w.Reinforce.NextTick(1), Is.EqualTo(12));
        }

        [Test]
        public void D5_PLACEHOLDER_SameRequestDeploymentsSeeEachOther()
        {
            var pack = M6World.Pack(interval: 1, bases: new[] { new SpawnAnchor(1, M6World.P(40)), new SpawnAnchor(2, M6World.P(40)) });
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, reserve: true), M6World.U(2, 2, reserve: true) }, pack));
            w.Step(); Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(2));
            Assert.That((w.Read<Position>(1).Value - w.Read<Position>(2).Value).LengthSquared,
                Is.GreaterThanOrEqualTo(new SimVec3(SimFix.FromInt(1), default, default).LengthSquared));
        }
    }
}
