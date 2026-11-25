using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // SHOW-1: the showcase stages (Showcase), the projectile view data (ViewProjectiles.Read) and the HUD text
    // (ShowcaseHud). Each stage is played once with its script to tick 600 and the tests read what that run saw.
    public sealed class Show1Tests
    {
        private const int Ticks = 600;

        private sealed class Seen
        {
            public int AliveOne, AliveTwo, ReserveOne, ReserveTwo, Points, Entities, Team;
            public ulong Key;
            public UnitStatus Status;
            public string Hud;
            public bool Plates, HeightField;
            public int Surfaces;
            public float X0;
            public long FirstBand = -1, FirstPinned = -1, FirstBroken = -1, FirstFly = -1, Fires;
            public readonly List<ViewProjectile> FirstFlight = new List<ViewProjectile>();
            // The first bullet of the run that was still in the air one tick after it was fired: as fired, and one tick on.
            public ViewProjectile Launched, Flown;
            public long FlownTick = -1;
            public float ShooterX, ShooterZ;
            public int Struck, Missed, Damaged, DamagedNotStruck, FreshMoved, MaxInFlight;
            public readonly Dictionary<long, (float X, bool Blocking, long DashLeft, bool Alive)> At = new Dictionary<long, (float, bool, long, bool)>();
            public ulong Hash150, Hash600;
            public bool Finished;
        }

        private static readonly Dictionary<string, Seen> Cache = new Dictionary<string, Seen>();

        private static ShowcaseStage Stage(string id)
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
                if (stage.Id == id) return stage;
            throw new ArgumentException(id);
        }

        private static Seen Play(string id, bool cached = true)
        {
            if (cached && Cache.TryGetValue(id, out Seen known)) return known;
            ShowcaseStage stage = Stage(id);
            SimSession s = stage.Start();
            var seen = new Seen();
            ViewMatch m = s.Current.Match;
            seen.AliveOne = m.AliveOne; seen.AliveTwo = m.AliveTwo; seen.ReserveOne = m.ReserveOne; seen.ReserveTwo = m.ReserveTwo;
            seen.Points = s.Current.Points.Length;
            seen.Entities = s.Current.Entities.Length;
            seen.Key = s.ControlledKey ?? 0;
            Assert.That(s.Current.TryGet(seen.Key, out ViewEntity me), Is.True, id + ": the player's unit is in the first frame");
            seen.Team = me.Team;
            seen.X0 = me.X;
            Assert.That(s.TryUnitStatus(seen.Key, out seen.Status), Is.True, id + ": the player's unit has a status");
            seen.Hud = ShowcaseHud.Status(stage, IndexOf(id), Showcase.Stages.Count, s.Current, seen.Key, seen.Status, false, true, 0);
            seen.Plates = s.Map.Plates != null;
            seen.HeightField = s.Map.HasHeightField;
            seen.Surfaces = s.Map.SurfaceCount;
            foreach (var item in stage.Script()) s.SetPlayerInput(item.AtTick, item.Input);
            foreach (BridgeCommand command in stage.Commands()) s.Enqueue(command);
            var flying = new List<ViewProjectile>();
            var fresh = new List<ViewProjectile>();
            var landed = new List<ViewImpact>();
            for (int t = 1; t <= Ticks && !s.IsFinished; t++)
            {
                s.StepOnce();
                seen.Fires += s.Current.Fires.Length;
                foreach (ViewEntity e in s.Current.Entities)
                {
                    if (e.Band != 0 && seen.FirstBand < 0) seen.FirstBand = t;
                    if (e.Nerve == 1 && seen.FirstPinned < 0) seen.FirstPinned = t;
                    if (e.Nerve == 2 && seen.FirstBroken < 0) seen.FirstBroken = t;
                }
                flying.Clear();
                landed.Clear();
                ViewProjectiles.Read(s, flying, landed);
                seen.MaxInFlight = Math.Max(seen.MaxInFlight, flying.Count);
                foreach (ViewProjectile p in flying)
                    if (p.Fresh && (p.X != p.FromX || p.Y != p.FromY || p.Z != p.FromZ)) seen.FreshMoved++;
                if (flying.Count > 0 && seen.FirstFly < 0)
                {
                    seen.FirstFly = t;
                    seen.FirstFlight.AddRange(flying);
                    if (s.ControlledKey.HasValue && s.Current.TryGet(s.ControlledKey.Value, out ViewEntity shooter))
                    {
                        seen.ShooterX = shooter.X;
                        seen.ShooterZ = shooter.Z;
                    }
                }
                // A bullet one tick on is told by where it was a tick earlier: the point a bullet was fired from last tick.
                foreach (ViewProjectile p in flying)
                {
                    if (p.Fresh || seen.FlownTick >= 0) continue;
                    foreach (ViewProjectile q in fresh)
                        if (seen.FlownTick < 0 && p.Team == q.Team && p.FromX == q.X && p.FromY == q.Y && p.FromZ == q.Z)
                        {
                            seen.Launched = q; seen.Flown = p; seen.FlownTick = t;
                        }
                }
                fresh.Clear();
                foreach (ViewProjectile p in flying)
                    if (p.Fresh) fresh.Add(p);
                foreach (ViewImpact i in landed)
                {
                    if (i.Struck) seen.Struck++; else seen.Missed++;
                    if (i.Damaged) seen.Damaged++;
                    if (i.Damaged && !i.Struck) seen.DamagedNotStruck++;
                }
                if (t == 100 || t == 185 || t == 250 || t == 310)
                {
                    ulong? key = s.ControlledKey;
                    if (key.HasValue && s.Current.TryGet(key.Value, out ViewEntity now) && s.TryUnitStatus(key.Value, out UnitStatus status))
                        seen.At[t] = (now.X, status.Blocking, status.DashTicksLeft, now.Alive);
                }
                if (t == 150) seen.Hash150 = s.StateHash;
            }
            seen.Hash600 = s.StateHash;
            seen.Finished = s.IsFinished;
            if (cached) Cache[id] = seen;
            return seen;
        }

        private static int IndexOf(string id)
        {
            for (int i = 0; i < Showcase.Stages.Count; i++)
                if (Showcase.Stages[i].Id == id) return i;
            throw new ArgumentException(id);
        }

        [Test]
        public void TheStagesAreTheSixNamedOnes()
        {
            var ids = new List<string>();
            foreach (ShowcaseStage stage in Showcase.Stages)
            {
                ids.Add(stage.Id);
                Assert.That(stage.Title, Is.Not.Empty, stage.Id);
                Assert.That(stage.Shows.Count, Is.GreaterThanOrEqualTo(2), stage.Id + ": lines that say what is on");
                Assert.That(stage.Zoom, Is.InRange(8f, 30f), stage.Id);
            }
            Assert.That(ids, Is.EqualTo(new[] { "battle", "bullets", "house", "terrain", "melee", "hamlet" }));
        }

        [TestCase("battle", 100, 50, 7, 200)]
        [TestCase("bullets", 100, 50, 7, 200)]
        [TestCase("house", 8, 12, 3, 16)]
        [TestCase("terrain", 10, 30, 2, 20)]
        [TestCase("melee", 60, 30, 3, 120)]
        [TestCase("hamlet", 40, 20, 5, 80)]
        public void AStageStartsWithItsForcesAndThePlayerInTeamOne(string id, int alive, int reserve, int points, int entities)
        {
            Seen seen = Play(id);
            Assert.That(new[] { seen.AliveOne, seen.AliveTwo, seen.ReserveOne, seen.ReserveTwo }, Is.EqualTo(new[] { alive, alive, reserve, reserve }));
            Assert.That(seen.Points, Is.EqualTo(points));
            Assert.That(seen.Entities, Is.EqualTo(entities));
            Assert.That(seen.Key, Is.EqualTo(1UL), "the player starts in team 1's first unit");
            Assert.That(seen.Team, Is.EqualTo(1));
            Assert.That(seen.Finished, Is.False, "the stage is still going at tick " + Ticks);
        }

        [TestCase("battle")]
        [TestCase("bullets")]
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        [TestCase("hamlet")]
        public void AStagePlaysTheSameTwice(string id)
        {
            Seen first = Play(id), second = Play(id, cached: false);
            Assert.That(second.Hash150, Is.EqualTo(first.Hash150));
            Assert.That(second.Hash600, Is.EqualTo(first.Hash600));
            Assert.That(first.Hash600, Is.Not.EqualTo(first.Hash150));
        }

        // RC1-K5a: a stage's world saved at tick 300 and loaded into a world that has already played to tick 450 plays on as
        // the world that was never saved does (compared every 50 ticks to tick 600). The stages switch on features together
        // that no other save test combines.
        [TestCase("battle")]
        [TestCase("bullets")]
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        [TestCase("hamlet")]
        public void AStageSavedAtTick300PlaysOnTheSame(string id)
        {
            ShowcaseStage stage = Stage(id);
            M6World whole = new M6World(stage.Recipe(), stage.MapBytes()), early = new M6World(stage.Recipe(), stage.MapBytes()),
                resumed = new M6World(stage.Recipe(), stage.MapBytes());
            whole.Step(300); early.Step(300); resumed.Step(450);
            Assert.That(resumed.Hash, Is.Not.EqualTo(whole.Hash), "the stage moves on between tick 300 and tick 450");
            resumed.Sim.RestoreRetainedSnapshot(early.Sim.SaveRetainedSnapshot());
            Assert.That(resumed.Hash, Is.EqualTo(whole.Hash), "the loaded world is the saved one");
            for (int t = 301; t <= 600; t++)
            {
                whole.Step(); resumed.Step();
                if (t % 50 == 0) Assert.That(resumed.Hash, Is.EqualTo(whole.Hash), id + " tick " + t);
            }
        }

        // RC1-K2: a stage runs on one layer. Its world reads the pack of that layer, and the weapons, bodies and stances it
        // fights with are built from the same pack. The house as it was before AIM-2 is held to the same rule.
        [TestCase("battle")]
        [TestCase("bullets")]
        [TestCase("house")]
        [TestCase("terrain")]
        [TestCase("melee")]
        [TestCase("hamlet")]
        [TestCase("legacy house")]
        public void AStageReadsOneLayer(string id)
        {
            ShowcaseStage stage = id == "legacy house" ? Showcase.LegacyHouse : Stage(id);
            WorldRecipe recipe = stage.Recipe();
            Assert.That(recipe.Pack?.Id, Is.EqualTo(stage.Layer));
            Assert.That(recipe.Combat.PolicyDigest, Is.EqualTo(recipe.Pack.BuildCombat().PolicyDigest));
        }

        // RC1-K3: the code of a stage switches nothing on. Its recipe carries no switch of its own (the plate preview only
        // opens maps with floors), does not ask for the block AI, and its match rules are the ones its layer builds. What is
        // on comes from the layer: facing, the block AI (a shield with a chance for the AI, and facing), respawn. The ruler's
        // table of played stages names no switch in the code of a stage either.
        [TestCase("battle", true, false, true)]
        [TestCase("bullets", true, false, true)]
        [TestCase("house", false, false, true)]
        [TestCase("terrain", false, false, true)]
        [TestCase("melee", true, true, false)]
        [TestCase("hamlet", true, false, true)]
        [TestCase("legacy house", false, false, true)]
        public void AStageSwitchesNothingOnItself(string id, bool facing, bool blockAi, bool respawn)
        {
            ShowcaseStage stage = id == "legacy house" ? Showcase.LegacyHouse : Stage(id);
            WorldRecipe recipe = stage.Recipe();
            var own = new List<string>();
            foreach (IWorldOption option in recipe.Options)
                if (option.OptionId != "world.plates-preview") own.Add(option.OptionId);
            Assert.That(own, Is.Empty, "switches in the stage's code");
            Assert.That(recipe.BlockAiAsked, Is.False);
            Assert.That((recipe.Facing, recipe.BlockAi, recipe.Motion, recipe.Standing), Is.EqualTo((facing, blockAi, false, false)));
            Assert.That(recipe.Match.PolicyDigest,
                Is.EqualTo(recipe.Pack.BuildMatch(recipe.Match.Points.ToArray(), recipe.Match.Bases.ToArray()).PolicyDigest));
            Assert.That((recipe.Match.RespawnTicks > 0, recipe.Match.RespawnGuardTicks > 0), Is.EqualTo((respawn, respawn)));
            foreach (string line in File.ReadAllLines(Path.Combine(Directory.GetParent(ArchitectureTests.AssetRoot()).FullName, "tools/ruler/played.tsv")))
            {
                string[] cells = line.TrimEnd('\r').Split('\t');
                if (cells[0] == stage.Id) Assert.That(cells.Length, Is.EqualTo(3), "tools/ruler/played.tsv names a switch in the code of stage " + stage.Id);
            }
        }

        [Test]
        public void TheBattleFormsSquadsAndPinsAndBreaksSoldiers()
        {
            Seen seen = Play("battle");
            Assert.That(seen.FirstBand, Is.InRange(1, 60), "squads form in the first two seconds");
            Assert.That(seen.FirstPinned, Is.GreaterThan(0), "somebody was pinned");
            Assert.That(seen.FirstBroken, Is.GreaterThan(seen.FirstPinned), "somebody broke, after the first pin");
            Assert.That(seen.Fires, Is.GreaterThan(100), "rifles fire");
            Assert.That(seen.MaxInFlight, Is.EqualTo(0), "the battle's rifles hit on the tick they fire");
            Assert.That(seen.Struck + seen.Missed, Is.EqualTo(0));
        }

        [Test]
        public void TheBattlePlayerHasADashAndABlockAndTheScriptUsesBoth()
        {
            Seen seen = Play("battle");
            Assert.That(seen.Status.HasWeapon, Is.True);
            Assert.That(seen.Status.HasDash, Is.True);
            Assert.That(seen.At[100].X - seen.X0, Is.GreaterThan(10f), "the script walks east");
            Assert.That(seen.At[185].DashLeft, Is.GreaterThan(0), "the dash asked for at tick 180 is on its cooldown");
            Assert.That(seen.At[250].Blocking, Is.False);
            Assert.That(seen.At[310].Blocking, Is.True, "the block asked for at tick 300 is held");
        }

        [Test]
        public void TheBulletsStageFliesProjectilesInsteadOfInstantHits()
        {
            Seen seen = Play("bullets");
            Assert.That(seen.Fires, Is.EqualTo(0), "no instant fire record");
            Assert.That(seen.MaxInFlight, Is.GreaterThan(3));
            Assert.That(seen.Struck, Is.GreaterThan(10), "bullets that stopped on a body");
            Assert.That(seen.Missed, Is.GreaterThan(10), "bullets that stopped on a wall, the ground or their range");
            Assert.That(seen.Damaged, Is.InRange(1, seen.Struck));
            Assert.That(seen.DamagedNotStruck, Is.EqualTo(0));
            Assert.That(seen.FirstPinned, Is.EqualTo(-1), "the known gap the stage's lines name: pressure does not see projectiles");
            Assert.That(seen.Hash600, Is.Not.EqualTo(Play("battle").Hash600));
        }

        [Test]
        public void AFreshProjectileSitsAtTheShootersMuzzleAndThenMovesFiveMetresATick()
        {
            Seen seen = Play("bullets");
            Assert.That(seen.FirstFly, Is.EqualTo(120), "the script's first shot");
            Assert.That(seen.FirstFlight.Count, Is.EqualTo(1));
            ViewProjectile first = seen.FirstFlight[0];
            Assert.That(first.Fresh, Is.True);
            Assert.That(first.Team, Is.EqualTo(1));
            Assert.That(new[] { first.X, first.Y, first.Z }, Is.EqualTo(new[] { first.FromX, first.FromY, first.FromZ }));
            Assert.That(first.FromX, Is.EqualTo(seen.ShooterX).Within(0.01f));
            Assert.That(first.FromZ, Is.EqualTo(seen.ShooterZ).Within(0.01f));
            Assert.That(first.FromY, Is.EqualTo(1.5f).Within(0.01f), "the layer's muzzle height");
            Assert.That(seen.FreshMoved, Is.EqualTo(0), "a fresh projectile is drawn where it starts");

            // RC1-K5a: the script's first bullet can stop on a team mate within its first tick, so the flight is read off the
            // first bullet of the run that was still in the air one tick after it was fired.
            Assert.That(seen.FlownTick, Is.GreaterThan(seen.FirstFly), "a bullet is seen one tick after it was fired, where it was fired from");
            ViewProjectile launched = seen.Launched, flown = seen.Flown;
            Assert.That(launched.Fresh, Is.True);
            Assert.That(flown.Fresh, Is.False);
            double step = Math.Sqrt(Sq(flown.X - flown.FromX) + Sq(flown.Y - flown.FromY) + Sq(flown.Z - flown.FromZ));
            Assert.That(step, Is.EqualTo(5.0).Within(0.01), "150 m/s at 30 ticks a second");
            double flownFromMuzzle = Math.Sqrt(Sq(flown.X - launched.X) + Sq(flown.Y - launched.Y) + Sq(flown.Z - launched.Z));
            Assert.That(flownFromMuzzle, Is.EqualTo(5.0).Within(0.01), "its first tick of flight starts at the muzzle");
        }

        private static double Sq(double v) => v * v;

        [Test]
        public void ReadProjectilesTakesEitherListAlone()
        {
            SimSession s = Stage("bullets").Start();
            foreach (var item in Stage("bullets").Script()) s.SetPlayerInput(item.AtTick, item.Input);
            var both = new List<ViewProjectile>();
            var landed = new List<ViewImpact>();
            int flights = 0, landings = 0;
            for (int t = 1; t <= 200; t++)
            {
                s.StepOnce();
                both.Clear();
                landed.Clear();
                ViewProjectiles.Read(s, both, landed);
                var onlyFlying = new List<ViewProjectile>();
                ViewProjectiles.Read(s, onlyFlying, null);
                var onlyLanded = new List<ViewImpact>();
                ViewProjectiles.Read(s, null, onlyLanded);
                Assert.That(onlyFlying.Count, Is.EqualTo(both.Count));
                Assert.That(onlyLanded.Count, Is.EqualTo(landed.Count));
                flights += both.Count;
                landings += landed.Count;
            }
            Assert.That(flights, Is.GreaterThan(0));
            Assert.That(landings, Is.GreaterThan(0));
            Assert.That(() => ViewProjectiles.Read(null, both, landed), Throws.ArgumentNullException);
        }

        [Test]
        public void TheHouseHasFloorsAndThePlayerComesBackFromTheReserve()
        {
            Seen seen = Play("house");
            Assert.That(seen.Plates, Is.True);
            Assert.That(seen.Status.HasDash, Is.False);
            Assert.That(seen.At.ContainsKey(310), Is.True);
            Assert.That(seen.At[185].Alive, Is.False, "the scripted player falls in the doorway"); // AIM-2: falls at 108 (was 59)
            Assert.That(seen.At[310].Alive, Is.True, "and is playing again from the reserve");
            Assert.That(seen.FirstBand, Is.EqualTo(-1), "no squads on a map with floors");
        }

        [Test]
        public void TheTerrainStageIsAHeightFieldWithSurfaces()
        {
            Seen seen = Play("terrain");
            Assert.That(seen.HeightField, Is.True);
            Assert.That(seen.Surfaces, Is.EqualTo(3));
            Assert.That(seen.Fires, Is.GreaterThan(20));
        }

        [Test]
        public void TheMeleePlayerIsAGuardWithAShieldAndNoGun()
        {
            Seen seen = Play("melee");
            Assert.That(seen.Status.HasWeapon, Is.False);
            Assert.That(seen.Fires, Is.EqualTo(0));
            Assert.That(seen.At[185].Blocking, Is.False);
            Assert.That(seen.At[250].Blocking, Is.True, "the block asked for at tick 240");
            Assert.That(seen.At[310].Blocking, Is.False, "let go at tick 300");
            Assert.That(seen.At[100].X - seen.X0, Is.GreaterThan(5f));
        }

        [Test]
        public void TheColoursComeFromTheCartridgePalette()
        {
            foreach (ShowcaseStage stage in Showcase.Stages)
            {
                SimSession s = stage.Start();
                ShowcaseColors colors = stage.Colors(s.Map);
                Assert.That(colors.Team, Is.EqualTo(new[] { 0x2F6FD6, 0xD6452F }), stage.Id);
                Assert.That(colors.Surface.Count, Is.EqualTo(s.Map.SurfaceCount), stage.Id);
                Assert.That(colors.Wall, Is.Not.EqualTo(colors.Ground), stage.Id);
            }
            Assert.That(() => Stage("battle").Colors(null), Throws.ArgumentNullException);
        }

        [Test]
        public void TheStatusTextOfTheFirstFrame()
        {
            Assert.That(Play("battle").Hud, Is.EqualTo(
                "STAGE 1/6  Open battle    0:00\n" +
                "TEAM 1 (you)  alive 100  reserve 50      TEAM 2  alive 100  reserve 50\n" +
                "POINTS  1:-  2:-  3:T1  4:-  5:T2  6:-  7:-\n" +
                "YOU  #1  HP 100/100  AMMO 30  ready  DASH ready\n"));
            Assert.That(Play("melee").Hud, Is.EqualTo(
                "STAGE 5/6  Ancient melee    0:00\n" +
                "TEAM 1 (you)  alive 60  reserve 30      TEAM 2  alive 60  reserve 30\n" +
                "POINTS  1:-  2:-  3:-\n" +
                "YOU  #1  HP 100/100\n"));
        }

        [Test]
        public void TheStatusTextShowsReloadDashBlockBulletsAndDown()
        {
            ShowcaseStage stage = Stage("bullets");
            FrameView frame = stage.Start().Current;
            string Line(UnitStatus? status, bool alive, int inFlight)
                => ShowcaseHud.Status(stage, 1, 5, frame, 1, status, false, alive, inFlight);

            string busy = Line(new UnitStatus(40, 100, 0, waitTicksLeft: 45, reloading: true, blocking: true, hasDash: true, dashTicksLeft: 31), true, 3);
            Assert.That(busy, Does.Contain("BULLETS IN THE AIR  3\n"));
            Assert.That(busy, Does.Contain("YOU  #1  HP 40/100  AMMO 0  reloading 1.5 s  DASH 1.1 s  BLOCKING\n"));
            string idle = Line(new UnitStatus(100, 100, 30), true, 0);
            Assert.That(idle, Does.Not.Contain("BULLETS"));
            Assert.That(idle, Does.Contain("YOU  #1  HP 100/100  AMMO 30  ready\n"));
            Assert.That(Line(new UnitStatus(100, 100, 30), false, 0), Does.Contain("YOU  down\n"));
            Assert.That(Line(null, true, 0), Does.Contain("YOU  down\n"));
            Assert.That(ShowcaseHud.Status(stage, 1, 5, frame, null, new UnitStatus(100, 100, 30), false, true, 0), Does.Contain("YOU  down\n"));
            Assert.That(idle, Does.Not.Contain("MATCH OVER"));
            Assert.That(() => ShowcaseHud.Status(null, 0, 5, frame, 1, null, false, true, 0), Throws.ArgumentNullException);
            Assert.That(() => ShowcaseHud.Status(stage, 0, 5, null, 1, null, false, true, 0), Throws.ArgumentNullException);
        }

        [Test]
        public void TheStatusTextNamesTheWinnerWhenTheMatchIsOver()
        {
            ShowcaseStage stage = Stage("house");
            SimSession s = stage.Start();
            foreach (var item in stage.Script()) s.SetPlayerInput(item.AtTick, item.Input);
            for (int t = 0; t < 6000 && !s.IsFinished; t++) s.StepOnce();
            Assert.That(s.IsFinished, Is.True, "the house match ends");
            int winner = s.Current.Match.Winner;
            Assert.That(winner, Is.InRange(1, 2));
            string text = ShowcaseHud.Status(stage, 2, 5, s.Current, s.ControlledKey, null, true, false, 0);
            Assert.That(text, Does.Contain("MATCH OVER: team " + winner + " wins. Backspace = again, Tab = next stage\n"));
        }

        [Test]
        public void SecondsRoundUpToTheNextTenthAndTheClockCountsMinutes()
        {
            Assert.That(ShowcaseHud.Seconds(0), Is.EqualTo("0.0 s"));
            Assert.That(ShowcaseHud.Seconds(-90), Is.EqualTo("0.0 s"));
            Assert.That(ShowcaseHud.Seconds(1), Is.EqualTo("0.1 s"));
            Assert.That(ShowcaseHud.Seconds(3), Is.EqualTo("0.1 s"));
            Assert.That(ShowcaseHud.Seconds(4), Is.EqualTo("0.2 s"));
            Assert.That(ShowcaseHud.Seconds(30), Is.EqualTo("1.0 s"));
            Assert.That(ShowcaseHud.Seconds(31), Is.EqualTo("1.1 s"));
            Assert.That(ShowcaseHud.Seconds(150), Is.EqualTo("5.0 s"));
            Assert.That(ShowcaseHud.Clock(0), Is.EqualTo("0:00"));
            Assert.That(ShowcaseHud.Clock(-90), Is.EqualTo("0:00"));
            Assert.That(ShowcaseHud.Clock(29), Is.EqualTo("0:00"));
            Assert.That(ShowcaseHud.Clock(30), Is.EqualTo("0:01"));
            Assert.That(ShowcaseHud.Clock(1799), Is.EqualTo("0:59"));
            Assert.That(ShowcaseHud.Clock(1800), Is.EqualTo("1:00"));
            Assert.That(ShowcaseHud.Clock(19830), Is.EqualTo("11:01"));
        }

        [Test]
        public void TheHelpNamesEveryKeyAndTheMissingListSaysWhatIsNotBuilt()
        {
            foreach (string word in new[] { "W A S D", "left mouse button", "Left Shift", "Space", "G (real in stage 6; preview only elsewhere", "Y your team: stand, crouch, prone", "Q E", "Tab", "Backspace", "P pause", "V squad lines", "M missing list" })
                Assert.That(ShowcaseHud.Keys, Does.Contain(word));
            Assert.That(ShowcaseHud.GrenadeStub, Does.StartWith("GRENADE PREVIEW: looks only."));
            Assert.That(ShowcaseHud.GrenadeStub, Does.Contain("hurts nobody"));
            Assert.That(ShowcaseHud.GrenadeStub, Does.Contain("stage 6 has the real one"));

            string missing = ShowcaseHud.Missing();
            Assert.That(missing, Does.StartWith("NOT BUILT YET\n- AI grenades; the player's grenade is real in stage 6 only"));
            Assert.That(missing, Does.Not.Contain("nothing consumes it"), "SHOW-2: blasts are built, the throw is no longer unused in stage 6");
            Assert.That(missing, Does.Not.Contain("Stances: crouch and prone"), "SHOW-2: stances are built");
            Assert.That(missing.Split('\n').Length, Is.EqualTo(Showcase.Missing.Count + 2), "a title, one line each, a final line end");
            foreach (string gap in new[] { "AI grenades", "Your own crouch and prone", "Cover seeking on height-field maps", "Climbing", "commander", "water", "Health bars" })
                Assert.That(missing, Does.Contain(gap));

            string shows = ShowcaseHud.Shows(Stage("bullets"));
            Assert.That(shows, Does.StartWith("ON IN THIS STAGE\n- The open battle with rifles that fire projectiles (150 m/s)\n"));
            Assert.That(shows, Does.Contain("Known gap: the pressure system does not react to projectiles"));
            Assert.That(() => ShowcaseHud.Shows(null), Throws.ArgumentNullException);
        }

        [Test]
        public void TheSquadLineCountsTheLivingMembersAndNamesTheLeader()
        {
            SimSession s = Stage("battle").Start();
            Assert.That(ShowcaseHud.Squad(s.Current, 1), Is.EqualTo("SQUAD  none\n"), "nobody is in a squad on the first frame");
            Assert.That(ShowcaseHud.Squad(s.Current, null), Is.EqualTo(""));
            Assert.That(ShowcaseHud.Squad(s.Current, 999999), Is.EqualTo(""), "a key that is not in the frame");
            Assert.That(() => ShowcaseHud.Squad(null, 1), Throws.ArgumentNullException);
            for (int t = 0; t < 400; t++) s.StepOnce();
            FrameView frame = s.Current;
            int members = 0, leaders = 0, dead = 0;
            foreach (ViewEntity e in frame.Entities)
            {
                if (!e.Alive)
                {
                    Assert.That(ShowcaseHud.Squad(frame, e.Key), Is.EqualTo(""));
                    dead++;
                    continue;
                }
                if (e.Band == 0) continue;
                int alive = 0;
                foreach (ViewEntity other in frame.Entities)
                    if (other.Alive && other.Band == e.Band) alive++;
                if (e.Band == e.Key)
                {
                    Assert.That(ShowcaseHud.Squad(frame, e.Key), Is.EqualTo("SQUAD  you lead " + alive + " soldiers\n"));
                    leaders++;
                }
                else
                {
                    Assert.That(ShowcaseHud.Squad(frame, e.Key), Is.EqualTo("SQUAD  " + alive + " soldiers, leader #" + e.Band + "\n"));
                    members++;
                }
            }
            Assert.That(leaders, Is.GreaterThan(5));
            Assert.That(members, Is.GreaterThan(leaders));
            Assert.That(dead, Is.GreaterThan(0), "somebody has died by tick 400");
            Assert.That(ShowcaseHud.Score(12, 3), Is.EqualTo("SHOTS 12  KILLS 3\n"));
        }

        [Test]
        public void TheFieldNeedsASurfaceAndAPack()
        {
            Assert.That(() => WarCore.Content.ShowcaseField.MapBytes(null), Throws.ArgumentException);
            Assert.That(() => WarCore.Content.ShowcaseField.MapBytes(""), Throws.ArgumentException);
            Assert.That(() => WarCore.Content.ShowcaseField.BuildMatch(null), Throws.ArgumentNullException);
            ViewMap field = ViewMap.Parse(WarCore.Content.ShowcaseField.MapBytes("ancient.ground"));
            Assert.That(new[] { field.Width, field.Depth, field.Walls.Length }, Is.EqualTo(new[] { 96, 64, 0 }));
            Assert.That(field.SurfaceName(0), Is.EqualTo("ancient.ground"));
        }

        [Test]
        public void ASessionWithoutProjectileWeaponsReadsNothing()
        {
            SimSession s = Stage("melee").Start();
            var flying = new List<ViewProjectile> { default };
            var landed = new List<ViewImpact> { default };
            for (int t = 0; t < 30; t++) s.StepOnce();
            ViewProjectiles.Read(s, flying, landed);
            Assert.That(flying.Count, Is.EqualTo(1), "the lists are appended to, never cleared");
            Assert.That(landed.Count, Is.EqualTo(1));
        }
    }
}
