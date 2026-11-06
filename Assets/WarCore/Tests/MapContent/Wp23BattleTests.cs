using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Combat and match on a terrain map. Hits across height differences are not tested here.
    public sealed class Wp23BattleTests
    {
        private const long S = Wp23.S;

        // 80 x 40 cells, a slope rising along x (1/16 m per metre), one wall box away from the default anchors.
        private static byte[] Slope((int x, int z, int w, int d)[] boxes = null) =>
            WcMapWriter.Write(Wp23.Make(80, 40, 0, 6 * S, boxes ?? new[] { (40, 10, 2, 8) }, 4 * S, (i, j) => (int)(i * (S / 16))));

        private static M6World World(UnitSpawn[] units, MatchPack match = null, bool reverse = false, byte[] map = null) =>
            new M6World(M6World.Endless(M6World.RecipeOf(units, match)).WithTerrainRules(Wp23.Pack()), map ?? Slope(), reverse);

        [Test]
        public void Reinforcement_LandsOnTheHeightField()
        {
            var w = World(new[] { M6World.U(1, 1, reserve: true) }, M6World.Pack(interval: 1, columns: 1, rows: 1));
            w.Step();
            Assert.That(w.Has<Position>(1), Is.True);
            SimVec3 at = w.Read<Position>(1).Value;
            Assert.That(at.X.Raw, Is.EqualTo(6 * S));
            Assert.That(at.Z.Raw, Is.EqualTo(20 * S));
            Assert.That(at.Y.Raw, Is.EqualTo(6 * (S / 16)), "the deployer places the unit at the ground height of the cell");
        }

        [Test]
        public void Reinforcement_IsRefusedOnBlockedGround_AndCountsTheAttempt()
        {
            var w = World(new[] { M6World.U(1, 1, reserve: true) }, M6World.Pack(interval: 1, columns: 1, rows: 1), map: Slope(new[] { (5, 19, 3, 3) }));
            w.Step();
            Assert.That(w.Has<Position>(1), Is.False);
            Assert.That(w.Reinforce.Attempts(1), Is.EqualTo(1));
            Assert.That(w.Reinforce.Deployed.Length, Is.EqualTo(0));
        }

        [Test]
        public void FireOffBattle_RunsDeterministically_AndNobodyFiresOrDies()
        {
            UnitSpawn[] units = { M6World.U(1, 1, 30, 20, speed: 4000), M6World.U(2, 2, 34, 20, speed: 4000), M6World.U(3, 2, 20, 30, speed: 4000) };
            // The default match pack thinks about once in a million ticks, so the units would never move; think every 10 ticks.
            var a = World(units, M6World.Pack(think: 10)); var b = World(units, M6World.Pack(think: 10), reverse: true);
            Combatant before = a.Read<Combatant>(1);
            SimVec3[] start = { a.Read<Position>(1).Value, a.Read<Position>(2).Value, a.Read<Position>(3).Value };
            for (int tick = 0; tick < 600; tick++)
            {
                a.Step(); b.Step();
                if (tick % 150 == 0) Assert.That(b.Hash, Is.EqualTo(a.Hash), "tick " + tick);
            }
            Assert.That(b.Hash, Is.EqualTo(a.Hash));
            foreach (ulong key in new ulong[] { 1, 2, 3 })
            {
                Assert.That(a.Dead(key), Is.False, "unit " + key);
                Assert.That(a.Read<Combatant>(key).DueMilli, Is.EqualTo(0), "unit " + key + " never fired");
            }
            Assert.That(a.Read<Combatant>(1).Ammo, Is.EqualTo(before.Ammo));
            for (int i = 0; i < 3; i++)
            {
                SimVec3 end = a.Read<Position>((ulong)(i + 1)).Value;
                Assert.That((end.X.Raw, end.Z.Raw), Is.Not.EqualTo((start[i].X.Raw, start[i].Z.Raw)), "unit " + (i + 1) + " moved on the slope");
                Assert.That(end.Y.Raw, Is.EqualTo(end.X.Raw / 16).Within(1), "unit " + (i + 1) + " still follows the height field");
            }
        }
    }
}
