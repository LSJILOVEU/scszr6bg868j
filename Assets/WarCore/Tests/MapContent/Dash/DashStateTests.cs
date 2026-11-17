using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // M7a acceptance 4: the dash state is in the snapshot and the hash; a world without dash is unchanged.
    public sealed class DashStateTests
    {
        private static SimSession Run()
        {
            SimSession s = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500), DashWorlds.Plain(2, 30500, 12500) }, DashWorlds.Map((40, 0, 1, 20)),
                new[] { BridgeCommand.MoveTo(1, 1, 60500, 6500) });
            s.StepOnce();
            Assert.That(DashWorlds.Dash(s, SimVec3.Metres(3, 0, 1)), Is.True);
            return s;
        }

        private static List<ulong> Hashes(SimSession s, int n)
        {
            var hashes = new List<ulong>();
            for (int i = 0; i < n; i++) { s.StepOnce(); hashes.Add(s.StateHash); }
            return hashes;
        }

        // Same seed twice: the same hash on every tick through the dash, the order that follows and a second dash.
        [Test]
        public void SameSeed_SameHashEveryTick()
        {
            SimSession a = Run(), b = Run();
            List<ulong> first = Hashes(a, 70), second = Hashes(b, 70);
            Assert.That(DashWorlds.Dash(a, DashWorlds.MinusX), Is.True);
            Assert.That(DashWorlds.Dash(b, DashWorlds.MinusX), Is.True);
            first.AddRange(Hashes(a, 20)); second.AddRange(Hashes(b, 20));
            Assert.That(second, Is.EqualTo(first));
        }

        // Saved after the fourth dash step (mid-dash, cooldown running), restored into a fresh world of the same recipe: the next
        // 80 ticks have the same hashes and the dash state, positions and snapshot bytes come back exactly.
        [Test]
        public void MidDashSnapshot_RoundTrips()
        {
            SimSession source = Run();
            for (int i = 0; i < 4; i++) source.StepOnce();
            Assert.That(DashWorlds.Loco(source).IsDashing(M3TestWorld.Unit(source)), Is.True);
            byte[] saved = M3TestWorld.Sim(source).SaveRetainedSnapshot();
            DashState state = DashWorlds.State(source);

            SimSession restored = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500), DashWorlds.Plain(2, 30500, 12500) },
                DashWorlds.Map((40, 0, 1, 20)), new[] { BridgeCommand.MoveTo(1, 1, 60500, 6500) });
            M3TestWorld.Sim(restored).RestoreRetainedSnapshot(saved);
            Assert.That(DashWorlds.State(restored), Is.EqualTo(state));
            Assert.That(M3TestWorld.Component<DashSpec>(restored), Is.EqualTo(M3TestWorld.Component<DashSpec>(source)));
            Assert.That(M3TestWorld.Sim(restored).SaveRetainedSnapshot(), Is.EqualTo(saved), "snapshot bytes round-trip");
            Assert.That(restored.StateHash, Is.EqualTo(source.StateHash));
            Assert.That(Hashes(restored, 80), Is.EqualTo(Hashes(source, 80)));
            Assert.That(M3TestWorld.Position(restored), Is.EqualTo(M3TestWorld.Position(source)));
        }

        // The dash state moves the hash: the same world with and without the request differs from the request on.
        [Test]
        public void Request_ChangesTheHash()
        {
            SimSession a = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500) }, DashWorlds.Map());
            SimSession b = DashWorlds.World(new[] { DashWorlds.Dasher(1, 5500, 12500) }, DashWorlds.Map());
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash));
            DashWorlds.Dash(a, DashWorlds.PlusX);
            Assert.That(a.StateHash, Is.Not.EqualTo(b.StateHash));
        }

        // The component hashes (IStateHash, used by the legacy state hash) see every field of the spec and the state.
        [Test]
        public void ComponentHashes_CoverEveryField()
        {
            ulong Of(IStateHash value) { var hash = new StateHash(); value.WriteState(hash); return hash.Value; }
            var state = new DashState(SimFix.FromInt(3), SimFix.FromInt(4), 7, 10, 67);
            var states = new[]
            {
                state, new DashState(SimFix.FromInt(2), SimFix.FromInt(4), 7, 10, 67), new DashState(SimFix.FromInt(3), SimFix.FromInt(5), 7, 10, 67),
                new DashState(SimFix.FromInt(3), SimFix.FromInt(4), 8, 10, 67), new DashState(SimFix.FromInt(3), SimFix.FromInt(4), 7, 9, 67),
                new DashState(SimFix.FromInt(3), SimFix.FromInt(4), 7, 10, 68),
            };
            var specs = new[] { new DashSpec(SimFix.FromInt(20), 10, 60), new DashSpec(SimFix.FromInt(19), 10, 60),
                new DashSpec(SimFix.FromInt(20), 11, 60), new DashSpec(SimFix.FromInt(20), 10, 61) };
            var seen = new HashSet<ulong>();
            foreach (DashState s in states) Assert.That(seen.Add(Of(s)), Is.True, "state " + s.TotalX + "," + s.TotalZ + "," + s.Start + "," + s.Ticks + "," + s.Ready);
            foreach (DashSpec s in specs) Assert.That(seen.Add(Of(s)), Is.True, "spec " + s.Distance + "," + s.Ticks + "," + s.Cooldown);
            Assert.That(Of(state), Is.EqualTo(Of(new DashState(SimFix.FromInt(3), SimFix.FromInt(4), 7, 10, 67))));
        }

        // A dash world's snapshot is refused by a world built without dash (its components are not registered there).
        [Test]
        public void DashSnapshot_IsRefusedByAWorldWithoutDash()
        {
            SimSession source = Run();
            byte[] saved = M3TestWorld.Sim(source).SaveRetainedSnapshot();
            SimSession plain = DashWorlds.World(new[] { DashWorlds.Plain(1, 5500, 12500, team: 1), DashWorlds.Plain(2, 30500, 12500) },
                DashWorlds.Map((40, 0, 1, 20)), new[] { BridgeCommand.MoveTo(1, 1, 60500, 6500) });
            Assert.That(() => M3TestWorld.Sim(plain).RestoreRetainedSnapshot(saved), Throws.Exception);
        }

        // Without a dash on any unit the world registers no dash component, and units made from a unit type without dash fields
        // carry none: their world is byte for byte the world of the same units built without OfType.
        [Test]
        public void NoDash_WorldIsUnchanged()
        {
            ContentPack pack = DashContentTests.Load("\"speed\":6000");
            UnitSpawn typed = UnitSpawn.OfType(1, pack, "runner", 1, 1, 5500, 12500);
            Assert.That(typed.HasDash, Is.False);
            Assert.That(pack.Section<UnitDashes>().Of("runner"), Is.Null);
            var spec = pack.SpawnOfUnitType("runner");
            var plain = new UnitSpawn(1, spec.Archetype, 1, 5500, 12500, spec.SpeedMmPerSec, 1, spec.Weapon, false, spec.Melee, spec.Block);
            SimSession a = DashWorlds.World(new[] { typed }, DashWorlds.Map()), b = DashWorlds.World(new[] { plain }, DashWorlds.Map());
            Assert.That(() => M3TestWorld.Sim(a).Entities.Resolve<DashState>(), Throws.Exception, "no dash state component");
            Assert.That(() => M3TestWorld.Sim(a).Entities.Resolve<DashSpec>(), Throws.Exception, "no dash spec component");
            Assert.That(M3TestWorld.Sim(a).SaveRetainedSnapshot(), Is.EqualTo(M3TestWorld.Sim(b).SaveRetainedSnapshot()));
            Assert.That(a.StateHash, Is.EqualTo(b.StateHash));
        }
    }
}
