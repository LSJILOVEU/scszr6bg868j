using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // M6 acceptance 6: in the fixtures of acceptance 6 and 7 the seam is asked exactly once per tick for every active AI unit,
    // never for the player or for a standing unit, including on ticks where a unit is blocked, sidesteps or arrives. The
    // inertia model of each world is replaced by a stand-in that records each call and returns the wanted speed.
    public sealed class InertiaSeamTests
    {
        private static IEnumerable<string> Fixtures()
        {
            for (int i = 0; i < InertiaWorlds.Movers.Length; i++) yield return "mover" + i;
            yield return "wall"; yield return "crowd0"; yield return "crowd300"; yield return "player";
        }

        private static SimSession Make(string name)
        {
            if (name.StartsWith("mover", StringComparison.Ordinal))
            {
                var (mover, hill) = InertiaWorlds.Movers[name[5] - '0'];
                return InertiaWorlds.Mover(mover, hill, 9000, 18000);
            }
            switch (name)
            {
                case "wall": return InertiaWorlds.Wall(60000, 1000000, 0);
                case "crowd0": return InertiaWorlds.Crowd(6000, 0, 1000000, 0);
                case "crowd300": return InertiaWorlds.Crowd(6000, 300, 1000000, 0);
                default: return InertiaWorlds.Player(9000, 18000);
            }
        }

        private static ulong[] Keys(string name) =>
            name == "wall" ? new ulong[] { 1 } : name.StartsWith("crowd", StringComparison.Ordinal) ? new ulong[] { 1, 3 }
            : name == "player" ? new ulong[] { 1, 2, 3 } : new ulong[] { 1, 2 };

        [Test]
        public void EachActiveAiUnitIsAskedOncePerTick_AndNoOneElse()
        {
            int blocked = 0, sidestep = 0, arrived = 0, asked = 0;
            foreach (string name in Fixtures())
            {
                SimSession s = Make(name);
                WarCore.Sim.Sim sim = M3TestWorld.Sim(s);
                var spy = new RecordingMotion(sim.Clock);
                M3TestWorld.Locomotion(s).BindMotion(spy);
                ulong? player = s.ControlledKey;
                var units = new List<(ulong Key, EntityId Id)>();
                foreach (ulong key in Keys(name)) units.Add((key, M3TestWorld.Unit(s, key)));
                bool playerMoved = false;
                for (int tick = 1; tick <= 300; tick++)
                {
                    var before = new Dictionary<ulong, (bool Active, SimVec3 At)>();
                    foreach (var (key, id) in units) before[key] = (Wp23.Intent(s, key).Active, M3TestWorld.Position(s, key));
                    int from = spy.Calls.Count;
                    s.StepOnce();
                    var calls = new Dictionary<EntityId, int>();
                    for (int c = from; c < spy.Calls.Count; c++)
                    {
                        Assert.That(spy.Calls[c].Tick, Is.EqualTo((long)tick), name);
                        calls[spy.Calls[c].Unit] = calls.TryGetValue(spy.Calls[c].Unit, out int n) ? n + 1 : 1;
                    }
                    foreach (var (key, id) in units)
                    {
                        bool after = Wp23.Intent(s, key).Active;
                        SimVec3 at = M3TestWorld.Position(s, key);
                        calls.TryGetValue(id, out int count);
                        if (player == key)
                        {
                            Assert.That(count, Is.Zero, $"{name} tick {tick}: the player is not asked");
                            playerMoved |= !at.Equals(before[key].At);
                            continue;
                        }
                        // The order of the script arrives at tick 1, before the movement phase of that tick.
                        bool moving = before[key].Active || (tick == 1 && after) || (tick == 1 && !at.Equals(before[key].At));
                        Assert.That(count, Is.EqualTo(moving ? 1 : 0), $"{name} tick {tick} unit {key}");
                        if (!moving) { Assert.That(at, Is.EqualTo(before[key].At), $"{name} tick {tick} unit {key} stands"); continue; }
                        asked++;
                        if (at.Equals(before[key].At)) blocked++;
                        else if (at.Z != before[key].At.Z && name.StartsWith("crowd", StringComparison.Ordinal)) sidestep++;
                        if (!after && at.Equals(Wp23.Intent(s, key).Target)) arrived++;
                    }
                }
                if (player.HasValue) Assert.That(playerMoved, Is.True, name + ": the player walked");
            }
            TestContext.Out.WriteLine($"asked={asked} blocked={blocked} sidestep={sidestep} arrived={arrived}");
            Assert.That(blocked, Is.GreaterThan(0));
            Assert.That(sidestep, Is.GreaterThan(0));
            Assert.That(arrived, Is.GreaterThan(0));
        }
    }
}
