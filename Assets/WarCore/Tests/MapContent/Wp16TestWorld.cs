using System;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Every scenario uses the real WorldBuilder through M6World. Only damage is injected as a Sim command.
    internal static class Wp16TestWorld
    {
        internal static UnitSpawn U(ulong key, long x = 10000, long z = 10000, int team = 1,
            int squad = 1, long speed = 6000, int weapon = -1, bool reserve = false) =>
            new UnitSpawn(key, 0, squad, x, z, speed, team, weapon, reserve);
        internal static MatchPack Match(int think = 1000000, int interval = 9000, int max = 9000) =>
            M6World.Pack(new[] {
                new ControlPointDef(1, M6World.P(30, 30), SimFix.FromInt(2)),
                new ControlPointDef(2, M6World.P(60, 30), SimFix.FromInt(2))
            }, ticks: 8000, think: think, interval: interval, max: max);
        internal static WorldRecipe Recipe(UnitSpawn[] units = null, ulong player = 1, MatchPack match = null) =>
            M6World.RecipeOf(units ?? new[] { U(1), U(2, 70000, 35000, team: 2, squad: 2) }, match ?? Match()).WithPlayer(player);
        internal static SimVec3 Point(M6World w, ulong key = 1) => w.Read<Position>(key).Value;
        internal static InputFrame Input(int x, int z, SquadCommand? squad = null) =>
            new InputFrame(SimVec3.Metres(x, 0, z), default, squad: squad);
        internal static void Idle(M6World w, ulong key)
        {
            Assert.That(w.Read<MoveIntent>(key).Active, Is.False, "player must not retain AI/squad movement");
            Assert.That(w.Read<MoveWaypoint>(key).Has, Is.False, "player path must be cleared");
            Assert.That(w.Read<MoveWaypoint>(key).Point, Is.EqualTo(default(SimVec3)));
        }
        internal static int Switches(M6World w)
        {
            int count = 0;
            foreach (var item in w.Sim.Events.Read(w.Sim.Clock.Tick))
                if (item.Type.Value == PlayerControl.SwitchEventId) count++;
            Assert.That(w.Sim.Events.EventsDroppedThisTick, Is.Zero);
            Assert.That(w.Sim.Events.EventsDroppedTotal, Is.Zero);
            return count;
        }
        internal static StableSimEvent SwitchEvent(M6World w)
        {
            for (int i = 0; i < w.Sim.Events.Read(w.Sim.Clock.Tick).Length; i++)
            {
                var item = w.Sim.ReadStableEvent(w.Sim.Clock.Tick, i);
                if (item.Type.Value == PlayerControl.SwitchEventId) return item;
            }
            throw new InvalidOperationException("Expected a control switch event.");
        }
        internal static void SameEvents(M6World a, M6World b)
        {
            Assert.That(b.Sim.Events.Read(b.Sim.Clock.Tick).Length, Is.EqualTo(a.Sim.Events.Read(a.Sim.Clock.Tick).Length));
            for (int i = 0; i < a.Sim.Events.Read(a.Sim.Clock.Tick).Length; i++)
            {
                var x = a.Sim.ReadStableEvent(a.Sim.Clock.Tick, i);
                var y = b.Sim.ReadStableEvent(b.Sim.Clock.Tick, i);
                Assert.That(y.Source, Is.EqualTo(x.Source)); Assert.That(y.Target, Is.EqualTo(x.Target));
                Assert.That(y.Sequence, Is.EqualTo(x.Sequence)); Assert.That(y.Type, Is.EqualTo(x.Type));
                var hx = new StateHash(); var hy = new StateHash();
                x.Payload.WriteState(hx); y.Payload.WriteState(hy);
                Assert.That(hy.Value, Is.EqualTo(hx.Value));
            }
            Assert.That(b.Sim.Events.NextSequence, Is.EqualTo(a.Sim.Events.NextSequence));
            Assert.That(a.Sim.Events.EventsDroppedTotal, Is.Zero); Assert.That(b.Sim.Events.EventsDroppedTotal, Is.Zero);
        }
        internal static void KillAt(M6World w, long tick, params ulong[] keys) =>
            w.Sim.RegisterGameplay("test.wp16-death-" + tick, new M6Damage(w, tick, keys), SimSystemPhase.Queries);
    }
}
