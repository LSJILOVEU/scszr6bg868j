using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Dash
{
    // M7b: the dash key in the player input, the bridge's edge trigger, the playtest recipe's dash and the HUD line.
    // Content layer rwr-m7-playtest gives both soldier types 4 m in 6 ticks with a 90-tick cooldown (PLACEHOLDER values).
    public sealed class M7bDashInputTests
    {
        private const long DistanceMm = 4000;
        private const int Ticks = 6, Cooldown = 90;

        private static SimSession Playtest() => WorldBuilder.Build(DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes(), false);

        private static ulong Player(SimSession s) => s.ControlledKey.Value;

        // Held input at the player's position: move (mx, mz), aim (ax, az) in mm, dash held or not.
        private static PlayerInput In(int mx, int mz, bool dash, long ax = 6000, long az = 30000) =>
            new PlayerInput(mx, mz, ax, az, dash: dash);

        private static void StepTo(SimSession s, long tick) { while (s.Tick < tick) s.StepOnce(); }

        // Step k of n of a dash covering total raw units: floor(total * k / n) - floor(total * (k - 1) / n), by hand.
        private static long Part(long total, int k, int n) =>
            (long)System.Math.Floor((double)total * k / n) - (long)System.Math.Floor((double)total * (k - 1) / n);

        private static DashState State(SimSession s) => M3TestWorld.Component<DashState>(s, Player(s));

        // Acceptance 3 (as agreed for respawn): every team 1 unit of the playtest, reserves too, has the dash; team 2 has none.
        [Test]
        public void M7bRecipes_OnlyThePlaytestPlayerTeamHasADash()
        {
            WorldRecipe play = DemoRecipes.MpPlaytest();
            int reserves = 0;
            foreach (UnitSpawn u in play.Units)
            {
                if (u.Key == play.PlayerKey) Assert.That(u.Team, Is.EqualTo(1));
                if (u.Team != 1) { Assert.That(u.HasDash, Is.False, "unit " + u.Key); continue; }
                if (u.Reserve) reserves++;
                Assert.That((u.DashDistanceMm, u.DashTicks, u.DashCooldownTicks), Is.EqualTo((DistanceMm, Ticks, Cooldown)), "unit " + u.Key);
            }
            Assert.That(reserves, Is.EqualTo(50));
            foreach (UnitSpawn u in DemoRecipes.MpRespawnDemo().Units) Assert.That(u.HasDash, Is.False, "respawn demo unit " + u.Key);
            foreach (UnitSpawn u in DemoRecipes.M7().Units) Assert.That(u.HasDash, Is.False, "M7 unit " + u.Key);
        }

        // Acceptance 2: pressed with the move input pointing -Z, the dash's steps run from the next tick and match DashPart.
        [Test]
        public void M7bPress_StepsFromTheNextTickAsDashPart()
        {
            SimSession s = Playtest();
            ulong key = Player(s);
            // The aim point lies off the -Z line, so a dash toward it would show in x.
            s.SetPlayerInput(1, In(0, -1000, true, 60000, 30000));
            SimVec3 start = M3TestWorld.Position(s, key);
            long total = -DistanceMm * SimFix.Scale / 1000;
            for (int k = 1; k <= Ticks; k++)
            {
                SimVec3 before = M3TestWorld.Position(s, key);
                s.StepOnce();
                SimVec3 after = M3TestWorld.Position(s, key);
                Assert.That(after.X.Raw - before.X.Raw, Is.EqualTo(0), "step " + k + " x");
                Assert.That(after.Z.Raw - before.Z.Raw, Is.EqualTo(Part(total, k, Ticks)), "step " + k + " z");
            }
            Assert.That(M3TestWorld.Position(s, key).Z.Raw - start.Z.Raw, Is.EqualTo(total));
            Assert.That(State(s).Start, Is.EqualTo(0));
            Assert.That(State(s).Ready, Is.EqualTo(Cooldown));
        }

        // Acceptance 2: held through the ready tick, the key does not dash again.
        [Test]
        public void M7bHeld_DoesNotRepeat()
        {
            SimSession s = Playtest();
            s.SetPlayerInput(1, In(0, -1000, true));
            StepTo(s, 1);
            DashState first = State(s);
            s.SetPlayerInput(2, In(1000, 0, true));
            StepTo(s, Cooldown + 30);
            Assert.That(State(s), Is.EqualTo(first));
        }

        // Acceptance 2: requested at tick 0, ready at tick 90. A press asked on tick 89 is refused; one asked on tick 90 is taken.
        [TestCase(Cooldown, false)]
        [TestCase(Cooldown + 1, true)]
        public void M7bCooldown_TickExact(long pressAt, bool taken)
        {
            SimSession s = Playtest();
            s.SetPlayerInput(1, In(0, -1000, true));
            s.SetPlayerInput(2, In(0, 0, false));
            s.SetPlayerInput(pressAt, In(1000, 0, true));
            StepTo(s, pressAt);
            DashState state = State(s);
            Assert.That(state.Start, Is.EqualTo(taken ? pressAt - 1 : 0));
            Assert.That(state.Ready, Is.EqualTo(taken ? pressAt - 1 + Cooldown : Cooldown));
            if (taken) Assert.That(state.TotalX.Raw, Is.EqualTo(DistanceMm * SimFix.Scale / 1000));
        }

        // With no move input the dash heads for the aim point; with the aim point on the unit too, nothing happens.
        [Test]
        public void M7bDirection_AimWhenNoMoveElseNothing()
        {
            SimSession s = Playtest();
            SimVec3 at = M3TestWorld.Position(s, Player(s));
            long xMm = at.X.Raw * 1000 / SimFix.Scale, zMm = at.Z.Raw * 1000 / SimFix.Scale;
            s.SetPlayerInput(1, In(0, 0, true, xMm, zMm + 10000));
            StepTo(s, 1);
            Assert.That((State(s).TotalX.Raw, State(s).TotalZ.Raw), Is.EqualTo((0L, DistanceMm * SimFix.Scale / 1000)));

            SimSession still = Playtest();
            SimVec3 p = M3TestWorld.Position(still, Player(still));
            Assert.That(p.X.Raw * 1000 % SimFix.Scale, Is.EqualTo(0), "the spawn point is a whole mm");
            still.SetPlayerInput(1, In(0, 0, true, p.X.Raw * 1000 / SimFix.Scale, p.Z.Raw * 1000 / SimFix.Scale));
            StepTo(still, 1);
            Assert.That(State(still), Is.EqualTo(DashState.Initial));
        }

        // After the player dies, respawn switches control to a team 1 reserve; the dash key works on that new unit.
        [Test]
        public void M7bRespawn_TheNewUnitDashes()
        {
            var w = new M6World(DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes());
            SimSession s = w.Session;
            ulong first = Player(s);
            Wp16TestWorld.KillAt(w, 1, first);
            long guard = 0;
            while (s.ControlledKey == first || s.ControlledKey == null) { s.StepOnce(); Assert.That(++guard, Is.LessThan(400)); }
            ulong next = s.ControlledKey.Value;
            Assert.That(w.Dead(first), Is.True);
            Assert.That(s.TryUnitStatus(first, out UnitStatus dead), Is.True);
            Assert.That(dead.HasDash, Is.True);
            Assert.That(HudLines.Build(s.Current, s.StateHash, first, dead, null), Does.Not.Contain("Dash"), "no dash line for a dead unit");
            long now = s.Tick;
            s.SetPlayerInput(now + 1, In(0, -1000, true));
            s.StepOnce();
            DashState state = M3TestWorld.Component<DashState>(s, next);
            Assert.That((state.Start, state.Ready), Is.EqualTo((now, now + Cooldown)));
        }

        // Without the dash key the playtest world never dashes.
        [Test]
        public void M7bNoKey_NeverDashes()
        {
            SimSession s = Playtest();
            s.SetPlayerInput(1, In(0, -1000, false));
            s.SetPlayerInput(30, In(1000, 0, false));
            StepTo(s, 120);
            Assert.That(State(s), Is.EqualTo(DashState.Initial));
        }

        private static (ulong[] hashes, IReadOnlyList<PlayerInputRecord> log) Run(IEnumerable<PlayerInputRecord> inputs, int ticks)
        {
            SimSession s = Playtest();
            foreach (PlayerInputRecord r in inputs) s.SetPlayerInput(r.AtTick, r.Input);
            var hashes = new ulong[ticks + 1];
            hashes[0] = s.StateHash;
            for (int t = 1; t <= ticks; t++) { s.StepOnce(); hashes[t] = s.StateHash; }
            return (hashes, s.PlayerLog);
        }

        // Acceptance 1: a recording with dash presses replays tick for tick, from the script and from the session's PlayerLog.
        [Test]
        public void M7bReplay_DashRecordingRepeatsEveryTick()
        {
            var script = new List<PlayerInputRecord>();
            void Add(long at, PlayerInput input) => script.Add((PlayerInputRecord)typeof(PlayerInputRecord)
                .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0]
                .Invoke(new object[] { at, input }));
            Add(1, In(0, -1000, true));
            Add(3, In(0, -1000, false));
            Add(40, In(1000, 0, true));
            Add(41, In(1000, 0, false));
            Add(95, In(0, 0, true, 30000, 41000));
            Add(140, In(-1000, 0, false));
            const int ticks = 200;
            var first = Run(script, ticks);
            var second = Run(script, ticks);
            var fromLog = Run(new List<PlayerInputRecord>(first.log), ticks);
            for (int t = 0; t <= ticks; t++)
            {
                Assert.That(second.hashes[t], Is.EqualTo(first.hashes[t]), "repeat tick " + t);
                Assert.That(fromLog.hashes[t], Is.EqualTo(first.hashes[t]), "PlayerLog tick " + t);
            }
            Assert.That(first.log.Count, Is.EqualTo(script.Count));
            // The dash presses were taken: the hash differs from the same script without the dash key from tick 1 on.
            var plain = new List<PlayerInputRecord>();
            foreach (PlayerInputRecord r in script)
            {
                PlayerInput i = r.Input;
                plain.Add((PlayerInputRecord)typeof(PlayerInputRecord)
                    .GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0]
                    .Invoke(new object[] { r.AtTick, new PlayerInput(i.MoveXMilli, i.MoveZMilli, i.AimXMm, i.AimZMm) }));
            }
            Assert.That(Run(plain, 1).hashes[1], Is.Not.EqualTo(first.hashes[1]));
        }

        // HUD: the cooldown line counts down from the press and reads ready at the ready tick; no line without a dash.
        [Test]
        public void M7bHud_CooldownLine()
        {
            SimSession s = Playtest();
            ulong key = Player(s);
            Assert.That(s.TryUnitStatus(key, out UnitStatus ready), Is.True);
            Assert.That((ready.HasDash, ready.DashTicksLeft), Is.EqualTo((true, 0L)));
            Assert.That(HudLines.Build(s.Current, s.StateHash, key, ready, null), Does.Contain("\nDash ready\n"));
            s.SetPlayerInput(1, In(0, -1000, true));
            StepTo(s, 1);
            s.TryUnitStatus(key, out UnitStatus one);
            Assert.That(one.DashTicksLeft, Is.EqualTo(Cooldown - 1));
            Assert.That(HudLines.Build(s.Current, s.StateHash, key, one, null), Does.Contain("\nDash in 89 ticks\n"));
            StepTo(s, Cooldown - 1);
            s.TryUnitStatus(key, out UnitStatus last);
            Assert.That(last.DashTicksLeft, Is.EqualTo(1));
            StepTo(s, Cooldown);
            s.TryUnitStatus(key, out UnitStatus again);
            Assert.That(again.DashTicksLeft, Is.EqualTo(0));

            SimSession demo = WorldBuilder.Build(DemoRecipes.MpRespawnDemo(), DemoRecipes.M7MapBytes(), false);
            ulong demoKey = demo.ControlledKey.Value;
            demo.TryUnitStatus(demoKey, out UnitStatus none);
            Assert.That(none.HasDash, Is.False);
            Assert.That(HudLines.Build(demo.Current, demo.StateHash, demoKey, none, null), Does.Not.Contain("Dash"));
        }

        // Input: LeftShift sets only Dash; PlayerInput equality and hash see it; the live adapter's click copy keeps it.
        [Test]
        public void M7bInput_KeyEqualityAndLiveCopy()
        {
            var down = new HashSet<string> { "LeftShift" };
            PlayerInput mapped = new PlayerInputMapper(PlayerKeys.Default).Map(new IsoCamera(10), 0, 0, 50, 50, 100, 100, true, false, k => down.Contains(k));
            Assert.That((mapped.Dash, mapped.Block, mapped.Throw, mapped.SkillSlot), Is.EqualTo((true, false, false, 0)));
            PlayerInput aimed = new PlayerInputMapper(PlayerKeys.Default).Map(new IsoCamera(10), 0, 0, 50, 50, 100, 100, true, false,
                k => down.Contains(k), new AimTarget(AimKind.Ground, 6f, 0.2f, 7f, 200, 0));
            Assert.That(aimed.Dash, Is.True);
            Assert.That(new PlayerInputMapper(PlayerKeys.Default).Map(new IsoCamera(10), 0, 0, 50, 50, 100, 100, true, false, k => false).Dash, Is.False);

            var a = new PlayerInput(1, 2, 3, 4);
            var b = new PlayerInput(1, 2, 3, 4, dash: true);
            Assert.That(a.Equals(b), Is.False);
            Assert.That(a.GetHashCode(), Is.Not.EqualTo(b.GetHashCode()));
            Assert.That(b.Equals(new PlayerInput(1, 2, 3, 4, dash: true)), Is.True);

            SimSession s = Playtest();
            var live = new LivePlayerInput();
            live.Apply(s, In(0, -1000, true), true);
            s.StepOnce();
            Assert.That(s.PlayerLog[0].Input.Primary, Is.True);
            Assert.That(s.PlayerLog[0].Input.Dash, Is.True);
        }
    }
}
