using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // What an extension that implements only IWorldFeature (explicitly, inheriting nothing) can see and change. Every probe here
    // names only members that existed before F2b, so the recordings were made on the unmodified builder.
    internal sealed class F2bStateProbe : IWorldFeature
    {
        internal readonly List<string> Lines = new List<string>();
        string IWorldOption.OptionId => "test.f2b-state-probe";
        bool IWorldFeature.IsActive(WorldAssembly a) => true;
        void IWorldFeature.Validate(WorldAssembly a) => Note("Validate", a);
        void IWorldFeature.Prepare(WorldAssembly a) => Note("Prepare", a);
        void IWorldFeature.RegisterComponents(WorldAssembly a) => Note("RegisterComponents", a);
        void IWorldFeature.PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity) => Note("PlaceWall", a);
        void IWorldFeature.PlaceUnit(WorldAssembly a, int index, EntityId unit) => Note("PlaceUnit " + index, a);
        void IWorldFeature.RegisterSystems(WorldAssembly a) => Note("RegisterSystems", a);

        // The hook, the entities alive, the component and system manifests as they stand, and which assembly facts are set.
        private void Note(string hook, WorldAssembly a)
        {
            var line = new StringBuilder(hook);
            Simulation sim = a.Sim;
            if (sim != null)
            {
                int entities = 0;
                foreach (EntityId _ in sim.Entities.SnapshotEntitiesForTests()) entities++;
                line.Append("|entities=").Append(entities);
                line.Append("|components=").Append(AssemblyWorlds.Components(sim));
                line.Append("|systems=").Append(AssemblyWorlds.Systems(sim));
            }
            line.Append("|map=").Append(a.Map != null).Append(" terrain=").Append(a.Terrain != null)
                .Append(" ground=").Append(a.GroundRaw).Append(" walls=").Append(a.WallMask).Append(" units=").Append(a.Units != null)
                .Append(" standing=").Append(a.Standing.HasValue).Append(" controlled=").Append(a.Controlled != null)
                .Append(" combat=").Append(a.Combat != null).Append(" crowdPass=").Append(a.CrowdPass != null)
                .Append(" locomotion=").Append(a.Locomotion != null);
            Lines.Add(line.ToString());
        }

        internal static WorldRecipe World(string name, F2bStateProbe probe) =>
            (name == "PlayerMatch" ? F2bWorlds.PlayerMatch().WithMotion().WithStandingOrders() : F2bWorlds.BlockAiMatch()).With(probe);

        internal static List<string> Record()
        {
            var lines = new List<string>();
            foreach (string name in F2bWorlds.Names)
            {
                var probe = new F2bStateProbe();
                F2bWorlds.Build(World(name, probe));
                foreach (string line in probe.Lines) lines.Add(name + "|" + line);
            }
            return lines;
        }
    }

    // A motion extension written the way an option outside the builder writes one: it rewrites each unit's motion limits,
    // writes a combatant state of its own that the builder's combat state must replace, and binds a motion model that caps the
    // step at the limit.
    internal sealed class F2bMotionExtension : IWorldFeature
    {
        internal static readonly SimFix Cap = SimFix.Ratio(1, 20);
        string IWorldOption.OptionId => "test.f2b-motion";
        bool IWorldFeature.IsActive(WorldAssembly a) => true;
        void IWorldFeature.Validate(WorldAssembly a) { }
        void IWorldFeature.Prepare(WorldAssembly a) { }
        void IWorldFeature.RegisterComponents(WorldAssembly a) { }
        void IWorldFeature.PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity) { }
        void IWorldFeature.PlaceUnit(WorldAssembly a, int index, EntityId unit)
        {
            a.Sim.Entities.Set(a.MotionLimits, unit, new MotionLimits(Cap, default, default, default));
            a.Sim.Entities.Set(a.Sim.Entities.Resolve<Combatant>(), unit, new Combatant(9, -1, 0, 0));
        }
        void IWorldFeature.RegisterSystems(WorldAssembly a) => a.Locomotion.BindMotion(new Capped(a.Sim.Entities, a.MotionLimits));

        private sealed class Capped : IMotionModel
        {
            private readonly EntityStore entities;
            private readonly ComponentType<MotionLimits> limits;
            internal Capped(EntityStore entities, ComponentType<MotionLimits> limits) { this.entities = entities; this.limits = limits; }
            public SimFix StepSpeed(EntityId unit, SimFix wanted)
            {
                SimFix cap = entities.Get(limits, unit).MaxSpeed;
                return wanted > cap ? cap : wanted;
            }
        }
    }

    // Throws from one hook; the recipe carries a second error the builder finds later in the same build.
    internal sealed class F2bThrowingExtension : IWorldFeature
    {
        private readonly string hook;
        internal F2bThrowingExtension(string hook) { this.hook = hook; }
        string IWorldOption.OptionId => "test.f2b-throw-" + hook;
        bool IWorldFeature.IsActive(WorldAssembly a) => true;
        void IWorldFeature.Validate(WorldAssembly a) { }
        void IWorldFeature.Prepare(WorldAssembly a) { }
        void IWorldFeature.RegisterComponents(WorldAssembly a) { }
        void IWorldFeature.PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity) { }
        void IWorldFeature.PlaceUnit(WorldAssembly a, int index, EntityId unit) { if (hook == "PlaceUnit") throw new TimeoutException("probe " + hook); }
        void IWorldFeature.RegisterSystems(WorldAssembly a) { if (hook == "RegisterSystems") throw new TimeoutException("probe " + hook); }
    }

    public sealed class F2bExtensionTests
    {
        internal const string HooksPath = "Assets/WarCore/Tests/MapContent/Assembly/Golden/f2b-hooks.txt";
        internal const string MotionPath = "Assets/WarCore/Tests/MapContent/Assembly/Golden/f2b-motion.txt";
        internal const int MotionTicks = 120;

        internal static List<ulong> MotionRun()
        {
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch().WithMotion().With(new F2bMotionExtension()));
            F2bWorlds.Script(session, "PlayerMatch");
            var hashes = new List<ulong> { session.StateHash };
            for (int i = 0; i < MotionTicks; i++) { session.StepOnce(); hashes.Add(session.StateHash); }
            return hashes;
        }

        [Test]
        public void AnExplicitExtensionSeesTheRecordedWorldAtEveryHook() =>
            Assert.That(F2bStateProbe.Record(), Is.EqualTo(File.ReadAllLines(HooksPath)));

        [Test]
        public void AMotionExtensionKeepsItsLimits_AndTheBuilderCombatStateWins()
        {
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch().WithMotion().With(new F2bMotionExtension()));
            foreach (UnitSpawn spawn in F2bWorlds.PlayerMatch().Units)
            {
                Assert.That(M3TestWorld.Component<MotionLimits>(session, spawn.Key).MaxSpeed, Is.EqualTo(F2bMotionExtension.Cap), "unit " + spawn.Key);
                Combatant state = M3TestWorld.Component<Combatant>(session, spawn.Key);
                Assert.That(state.Team, Is.EqualTo(spawn.Team), "unit " + spawn.Key);
                Assert.That(state.Weapon, Is.EqualTo(0), "unit " + spawn.Key);
                Assert.That(state.Ammo, Is.EqualTo(8), "unit " + spawn.Key);
            }
            string[] golden = File.ReadAllLines(MotionPath);
            List<ulong> hashes = MotionRun();
            Assert.That(golden.Length, Is.EqualTo(MotionTicks + 1));
            for (int t = 0; t <= MotionTicks; t++)
                Assert.That(hashes[t].ToString("X16"), Is.EqualTo(golden[t]), "tick " + t);
        }

        // The extension's unit hook runs before the builder's combat state is written, so its error comes first.
        [Test]
        public void AnExtensionUnitErrorComesBeforeABadWeaponIndex()
        {
            UnitSpawn[] units = F2bWorlds.PlayerMatch().Units.ToArray();
            UnitSpawn u = units[0];
            units[0] = new UnitSpawn(u.Key, u.Archetype, u.Squad, u.XMm, u.ZMm, u.SpeedMmPerSec, u.Team, 5);
            WorldRecipe recipe = new WorldRecipe(43, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(F2bWorlds.Guns()).WithMatch(F2bWorlds.Match()).With(new F2bThrowingExtension("PlaceUnit"));
            var error = Assert.Throws<TimeoutException>(() => F2bWorlds.Build(recipe));
            Assert.That(error.Message, Is.EqualTo("probe PlaceUnit"));
            WorldRecipe plain = new WorldRecipe(43, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(F2bWorlds.Guns()).WithMatch(F2bWorlds.Match());
            Assert.Throws<IndexOutOfRangeException>(() => F2bWorlds.Build(plain));
        }

        // The extension's system hook runs before the block AI is checked.
        [Test]
        public void AnExtensionSystemErrorComesBeforeTheBlockAiGuard()
        {
            WorldRecipe broken = new WorldRecipe(47, MoverKind.InterimCrowd, F2bWorlds.BlockAiMatch().Units.ToArray(), Array.Empty<BridgeCommand>())
                .WithCombat(F2bWorlds.Blades()).WithMatch(F2bWorlds.Match()).WithBlockAi();
            var error = Assert.Throws<TimeoutException>(() => F2bWorlds.Build(broken.With(new F2bThrowingExtension("RegisterSystems"))));
            Assert.That(error.Message, Is.EqualTo("probe RegisterSystems"));
            var guard = Assert.Throws<ArgumentException>(() => F2bWorlds.Build(broken));
            Assert.That(guard.Message, Does.StartWith("The block AI needs combat with block specs and facing."));
        }
    }

    public sealed class F2bExtensionRecorder
    {
        [Category("Manual"), Test, Explicit("Records the F2b extension locks.")]
        public void RecordF2bExtensionLocks()
        {
            string dir = Environment.GetEnvironmentVariable("WARCORE_F2B_EXT_OUT");
            Assert.That(string.IsNullOrEmpty(dir), Is.False);
            string hooks = Path.Combine(dir, "f2b-hooks.txt"), motion = Path.Combine(dir, "f2b-motion.txt");
            Assert.That(File.Exists(hooks) || File.Exists(motion), Is.False);
            File.WriteAllLines(hooks, F2bStateProbe.Record(), new UTF8Encoding(false));
            var lines = new List<string>();
            foreach (ulong hash in F2bExtensionTests.MotionRun()) lines.Add(hash.ToString("X16"));
            File.WriteAllLines(motion, lines, new UTF8Encoding(false));
        }
    }
}
