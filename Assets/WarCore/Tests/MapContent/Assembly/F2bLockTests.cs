using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // An extension written the way an option class outside the builder writes it: it implements IWorldFeature explicitly and
    // inherits nothing. It records every call it gets, so the hooks an extension sees, their order and their count are locked.
    internal sealed class F2bHookProbe : IWorldFeature
    {
        internal readonly List<string> Calls = new List<string>();
        string IWorldOption.OptionId => "test.f2b-hook-probe";
        bool IWorldFeature.IsActive(WorldAssembly a) { Calls.Add("IsActive"); return true; }
        void IWorldFeature.Validate(WorldAssembly a) => Calls.Add("Validate");
        void IWorldFeature.Prepare(WorldAssembly a) => Calls.Add("Prepare map=" + (a.Map != null) + " sim=" + (a.Sim != null));
        void IWorldFeature.RegisterComponents(WorldAssembly a) => Calls.Add("RegisterComponents sim=" + (a.Sim != null));
        void IWorldFeature.PlaceWall(WorldAssembly a, WcMapWall wall, EntityId entity) => Calls.Add("PlaceWall " + wall.Chunk + ":" + wall.Placement);
        void IWorldFeature.PlaceUnit(WorldAssembly a, int index, EntityId unit) => Calls.Add("PlaceUnit " + index);
        void IWorldFeature.RegisterSystems(WorldAssembly a) =>
            Calls.Add("RegisterSystems locomotion=" + (a.Locomotion != null) + " combat=" + (a.Combat != null)
                + " controlled=" + (a.Controlled != null) + " crowdPass=" + (a.CrowdPass != null));

        internal static string[] Record()
        {
            var probe = new F2bHookProbe();
            F2bWorlds.Build(F2bWorlds.PlayerMatch().With(probe));
            return probe.Calls.ToArray();
        }
    }

    // A feature that reads the combat system and the player control from the assembly in its system hook and registers a
    // recording system (Order 90, Queries): every tick it notes the controlled unit's stable identity and this tick's fires.
    internal sealed class F2bFireProbe : WorldFeature
    {
        internal readonly List<StableId> Players = new List<StableId>();
        internal readonly List<int> Fires = new List<int>();
        public override string OptionId => "test.f2b-fire-probe";
        public override void RegisterSystems(WorldAssembly a) =>
            a.Sim.RegisterGameplay("test.f2b-fire-probe", new Recorder(this, a.Sim, a.Combat, a.Controlled), SimSystemPhase.Queries);

        private sealed class Recorder : IRetainedGameplaySystem
        {
            private readonly F2bFireProbe owner;
            private readonly Simulation sim;
            private readonly CombatSystem combat;
            private readonly WarCore.Control.PlayerControl controlled;
            internal Recorder(F2bFireProbe owner, Simulation sim, CombatSystem combat, WarCore.Control.PlayerControl controlled)
            { this.owner = owner; this.sim = sim; this.combat = combat; this.controlled = controlled; }
            public int Order => 90;
            public string StateTypeId => "test.f2b-fire-probe";
            public int StateVersion => 1;
            public void Step(SimContext context)
            {
                owner.Players.Add(sim.Entities.StableOf(controlled.Current));
                owner.Fires.Add(combat.Fires.Length);
            }
            public void SaveState(SimStateWriter writer) { }
            public void LoadState(SimStateReader reader) { }
            public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
        }

        internal const int Ticks = 60;

        // The fires of the player world without the probe, read from the session's frames.
        internal static int PlainFires()
        {
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch());
            F2bWorlds.Script(session, "PlayerMatch");
            int total = 0;
            for (int i = 0; i < Ticks; i++) { session.StepOnce(); total += session.Current.Fires.Length; }
            return total;
        }

        // The player stands and fires toward (33, 24) m from (30, 20) m: the aimed shot turns it to (3, 4) / 5.
        internal static string PlayerFacing()
        {
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch());
            session.SetPlayerInput(1, new PlayerInput(0, 0, 33000, 24000, true));
            for (int i = 0; i < 3; i++) session.StepOnce();
            Assert.That(session.TryFacing(F2bWorlds.PlayerKey, out long x, out long z), Is.True);
            return x + "," + z;
        }
    }

    public sealed class F2bLockTests
    {
        // Recorded on the unmodified builder (F2bRecorder). Never edit by hand.
        private static readonly string[] Surface =
        {
            "Advance|method|System.Int32(System.Int64)",
            "Alpha|property|System.Single get",
            "BlockedCell|method|System.Boolean(System.Int32,System.Int32)",
            "CommandLog|property|System.Collections.Generic.IReadOnlyList<WarCore.Presentation.BridgeCommand> get",
            "ControlledKey|property|System.Nullable<System.UInt64> get",
            "Create|method|WarCore.Presentation.SimSession(WarCore.Presentation.WorldRecipe) static",
            "Create|method|WarCore.Presentation.SimSession(WarCore.Presentation.WorldRecipe,System.Byte[]) static",
            "Current|property|WarCore.Presentation.FrameView get",
            "DisableStepTiming|method|System.Void()",
            "EnableStepTiming|method|System.Void(System.Int32=9000)",
            "EndReason|property|System.Int32 get",
            "EndTick|property|System.Int64 get",
            "Enqueue|method|System.Void(WarCore.Presentation.BridgeCommand)",
            "IsFinished|property|System.Boolean get",
            "Map|property|WarCore.Presentation.ViewMap get",
            "MaxTicksPerFrame|field|System.Int32 const",
            "PlayerLog|property|System.Collections.Generic.IReadOnlyList<WarCore.Presentation.PlayerInputRecord> get",
            "Previous|property|WarCore.Presentation.FrameView get",
            "RefreshView|method|System.Void()",
            "SetPlayerInput|method|System.Void(System.Int64,WarCore.Presentation.PlayerInput)",
            "StateHash|property|System.UInt64 get",
            "StepOnce|method|System.Void()",
            "StepTimingEnabled|property|System.Boolean get",
            "StepTiming|property|WarCore.Presentation.SimStepTiming get",
            "TickRate|field|System.Int32 const",
            "Tick|property|System.Int64 get",
            "ToView|method|WarCore.Presentation.ViewFire(WarCore.Combat.FireRecord&) static",
            "TryFacing|method|System.Boolean(System.UInt64,System.Int64& out,System.Int64& out)",
            "TryUnitStatus|method|System.Boolean(System.UInt64,WarCore.Presentation.UnitStatus& out)",
            "Winner|property|System.Int32 get",
            "get_Alpha|method|System.Single()",
            "get_CommandLog|method|System.Collections.Generic.IReadOnlyList<WarCore.Presentation.BridgeCommand>()",
            "get_ControlledKey|method|System.Nullable<System.UInt64>()",
            "get_Current|method|WarCore.Presentation.FrameView()",
            "get_EndReason|method|System.Int32()",
            "get_EndTick|method|System.Int64()",
            "get_IsFinished|method|System.Boolean()",
            "get_Map|method|WarCore.Presentation.ViewMap()",
            "get_PlayerLog|method|System.Collections.Generic.IReadOnlyList<WarCore.Presentation.PlayerInputRecord>()",
            "get_Previous|method|WarCore.Presentation.FrameView()",
            "get_StateHash|method|System.UInt64()",
            "get_StepTimingEnabled|method|System.Boolean()",
            "get_StepTiming|method|WarCore.Presentation.SimStepTiming()",
            "get_Tick|method|System.Int64()",
            "get_Winner|method|System.Int32()",
        };

        private static readonly string[] Hooks =
        {
            "IsActive",
            "Validate",
            "IsActive",
            "Prepare map=True sim=False",
            "IsActive",
            "RegisterComponents sim=True",
            "IsActive",
            "PlaceWall 0:1",
            "IsActive",
            "PlaceUnit 0",
            "IsActive",
            "PlaceUnit 1",
            "IsActive",
            "PlaceUnit 2",
            "IsActive",
            "PlaceUnit 3",
            "IsActive",
            "PlaceUnit 4",
            "IsActive",
            "PlaceUnit 5",
            "IsActive",
            "PlaceUnit 6",
            "IsActive",
            "PlaceUnit 7",
            "IsActive",
            "PlaceUnit 8",
            "IsActive",
            "PlaceUnit 9",
            "IsActive",
            "PlaceUnit 10",
            "IsActive",
            "PlaceUnit 11",
            "IsActive",
            "RegisterSystems locomotion=True combat=True controlled=True crowdPass=True",
        };

        private const int RecordedFires60 = 41;
        private const string RecordedFacing = "39321,52428";

        [Test]
        public void SimSessionPublicSurfaceIsUnchanged() => Assert.That(F2bWorlds.PublicSurface(), Is.EqualTo(Surface));

        [Test]
        public void AnExplicitExtensionSeesTheSameHooksInTheSameOrder()
        {
            string[] calls = F2bHookProbe.Record();
            Assert.That(calls, Is.EqualTo(Hooks));
            Assert.That(Array.FindAll(calls, c => c == "Validate").Length, Is.EqualTo(1));
            Assert.That(Array.FindAll(calls, c => c.StartsWith("PlaceUnit ", StringComparison.Ordinal)).Length, Is.EqualTo(12));
            Assert.That(Array.FindAll(calls, c => c.StartsWith("PlaceWall ", StringComparison.Ordinal)).Length, Is.EqualTo(1));
            Assert.That(Array.FindAll(calls, c => c == "RegisterSystems locomotion=True combat=True controlled=True crowdPass=True").Length, Is.EqualTo(1));
        }

        [TestCase("PlayerMatch", false)]
        [TestCase("PlayerMatch", true)]
        [TestCase("BlockAiMatch", false)]
        [TestCase("BlockAiMatch", true)]
        public void EveryTickHashMatchesTheRecording(string name, bool reverse)
        {
            ulong[] golden = F2bWorlds.Golden()[name + "|" + (reverse ? "reverse" : "forward")];
            F2bWorlds.Run run = F2bWorlds.Play(name, reverse);
            Assert.That(run.FinalTick, Is.EqualTo(F2bWorlds.LastTick));
            Assert.That(run.Finished, Is.False);
            Assert.That(golden.Length, Is.EqualTo(F2bWorlds.LastTick + 1));
            for (int t = 0; t <= F2bWorlds.LastTick; t++)
                Assert.That(run.Hashes[t], Is.EqualTo(golden[t]), name + " tick " + t);
        }

        // The worlds reach the parts the lock is about: shots, the player's own shots, deaths, and held blocks.
        [Test]
        public void TheLockedWorldsExerciseTheirBranches()
        {
            F2bWorlds.Run player = F2bWorlds.Play("PlayerMatch", false);
            Assert.That(player.Fires, Is.GreaterThan(0));
            Assert.That(player.PlayerFires, Is.GreaterThan(0));
            F2bWorlds.Run block = F2bWorlds.Play("BlockAiMatch", false);
            Assert.That(player.Deaths, Is.GreaterThan(0));
            Assert.That(block.HoldingTicks, Is.GreaterThan(0));
            Assert.That(block.Deaths, Is.GreaterThan(0));
        }

        [Test]
        public void AFeatureReadsCombatAndPlayerControlFromTheAssembly()
        {
            var probe = new F2bFireProbe();
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch().With(probe));
            F2bWorlds.Script(session, "PlayerMatch");
            for (int i = 0; i < F2bFireProbe.Ticks; i++) session.StepOnce();
            StableId player = StableEntityKey.Placed(1, F2bWorlds.PlayerKey).Id;
            Assert.That(probe.Players.Count, Is.EqualTo(F2bFireProbe.Ticks));
            Assert.That(probe.Players, Has.All.EqualTo(player));
            int total = 0;
            foreach (int fires in probe.Fires) total += fires;
            Assert.That(total, Is.EqualTo(RecordedFires60));
            Assert.That(F2bFireProbe.PlainFires(), Is.EqualTo(RecordedFires60));
        }

        // The F2a manifests read straight after the build, before any step: a system registered in the wrong phase must fail
        // here on the manifest, not later on the exception the misplaced system throws when it runs.
        [TestCaseSource(typeof(AssemblyWorlds), nameof(AssemblyWorlds.Names))]
        public void BuildTimeManifestsMatchTheF2aRecording(string world)
        {
            (WorldRecipe recipe, byte[] map) = AssemblyWorlds.Make(world);
            Simulation sim = M3TestWorld.Sim(AssemblyWorlds.Build(recipe, map, false));
            Assert.That(AssemblyWorlds.Systems(sim), Is.EqualTo(AssemblyGolden.Of(world)["systems"]), world + " systems");
            Assert.That(AssemblyWorlds.Components(sim), Is.EqualTo(AssemblyGolden.Of(world)["components"]), world + " components");
        }

        [Test]
        public void TheAimedPlayerShotTurnsThePlayer() => Assert.That(F2bFireProbe.PlayerFacing(), Is.EqualTo(RecordedFacing));

        [Test]
        public void UnitStatusCarriesThePackMaxHealth()
        {
            SimSession session = F2bWorlds.Build(F2bWorlds.PlayerMatch());
            Assert.That(session.TryUnitStatus(2, out UnitStatus status), Is.True);
            Assert.That(status.MaxHealth, Is.EqualTo(100f));
            Assert.That(status.Health, Is.EqualTo(100f));
        }
    }
}
