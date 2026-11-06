using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Guard slots and the AI's slot choice on terrain: a team walks to a control point and settles on slots. The height only
    // moves the units up and down; every distance and slot test must stay horizontal. The control point sits at height 0
    // while the ground under it does not, so a three-dimensional distance would change the choice.
    public sealed class Wp23GuardSlotTests
    {
        private const long S = Wp23.S;

        private static readonly UnitSpawn[] Team = {
            M6World.U(1, 1, 38, 18, speed: 4000), M6World.U(2, 1, 38, 20, speed: 4000), M6World.U(3, 1, 38, 22, speed: 4000),
            M6World.U(4, 1, 40, 19, speed: 4000), M6World.U(5, 1, 40, 21, speed: 4000), M6World.U(6, 1, 42, 20, speed: 4000),
            M6World.U(9, 2, 76, 5, speed: 4000) };

        // The control point sits on the hill, 20 m from the team, with its centre at height 0.
        private static readonly SimVec3 Hill = M6World.P(60);

        private static WcMap Map(long? slope) =>
            Wp23.Make(80, 40, 0, 6 * S, new[] { (30, 30, 2, 2) }, 4 * S, slope == null ? null : (Func<int, int, int>)((i, j) => (int)(i * slope.Value)));

        private static M6World World(long? slope)
        {
            WorldRecipe recipe = M6World.Endless(M6World.RecipeOf(Team, M6World.Pack(new[] { new ControlPointDef(7, Hill, SimFix.FromInt(6)) }, think: 10)));
            if (slope != null) recipe = recipe.WithTerrainRules(Wp23.Pack());
            return new M6World(recipe, WcMapWriter.Write(Map(slope)));
        }

        // 4096 raw per metre is 1/16 m per metre; 4800 is the steepest field that stays under the map's top (6 m over 80 cells).
        [TestCase(4096L), TestCase(4800L)]
        public void ASlopedWorld_MovesLikeTheFlatWorld_InTheHorizontalPlane(long slope)
        {
            M6World flat = World(null), hill = World(slope);
            for (int tick = 1; tick <= 600; tick++)
            {
                flat.Step(); hill.Step();
                if (tick % 25 != 0) continue;
                foreach (var spawn in Team)
                {
                    SimVec3 a = flat.Read<Position>(spawn.Key).Value, b = hill.Read<Position>(spawn.Key).Value;
                    Assert.That((b.X.Raw, b.Z.Raw), Is.EqualTo((a.X.Raw, a.Z.Raw)), "unit " + spawn.Key + " at tick " + tick);
                    Assert.That(b.Y.Raw, Is.EqualTo(b.X.Raw * slope / S).Within(2), "unit " + spawn.Key + " follows the ground at tick " + tick);
                }
                PointState f = flat.Capture.Points[0], h = hill.Capture.Points[0];
                Assert.That((h.Owner, h.Capturing, h.Progress), Is.EqualTo((f.Owner, f.Capturing, f.Progress)), "capture state at tick " + tick);
            }
        }

        [Test]
        public void TheTeamCapturesThePointOnTheHill_AndNobodyFiresOrDies()
        {
            var w = World(4096);
            w.Step(600);
            int inside = 0;
            foreach (var spawn in Team)
            {
                Assert.That(w.Dead(spawn.Key), Is.False, "unit " + spawn.Key);
                Assert.That(w.Read<Combatant>(spawn.Key).DueMilli, Is.EqualTo(0), "unit " + spawn.Key + " never fired or swung");
                if (spawn.Team == 1 && CombatSystem.Horizontal(w.Read<Position>(spawn.Key).Value - Hill).LengthSquared
                    <= new SimVec3(SimFix.FromInt(6), default, default).LengthSquared) inside++;
            }
            Assert.That(inside, Is.GreaterThan(0), "part of the team stands inside the capture radius");
            Assert.That(w.Capture.Points[0].Owner, Is.EqualTo(1), "the point changed hands");
        }
    }
}
