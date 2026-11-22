using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // HC-1 S6 to S14: the one radius, read from the pack, in every place a unit is placed, walked, deployed or drawn. Each test
    // runs the same scene with the default body and with r = 0.15 m (9830 raw) and shows the difference on that one place.
    // The scene: a 16 x 8 m terrain map with a wall box over x 10..11, z 2..5 and a target 0.2 m west of the wall's west face
    // (x = 9.8 m): inside the default 0.25 m clearance, outside r = 0.15 m.
    public sealed class Hc1WorldTests
    {
        private const long S = SimFix.Scale, R15 = Hc1ContentTests.R15, Max = Hc1ContentTests.Max;
        private static readonly (int, int, int, int)[] Box = { (10, 2, 1, 3) };
        private static readonly BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly SimFix Thin = SimFix.FromRaw(R15);

        private static WcMap TerrainMap() => Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S, (i, j) => (int)(i * (S / 16)));
        private static WcMap FlatMap() => Wp23.Make(16, 8, 0, 6 * S, Box, 4 * S);
        private static CombatPack Combat(string radius) => Hc1ContentTests.Pack(radius).BuildCombat();

        private static SimSession Session(MoverKind mover, string radius, UnitSpawn[] units, BridgeCommand[] script = null, WcMap map = null) =>
            SimSession.Create(new WorldRecipe(41, mover, units, script ?? Array.Empty<BridgeCommand>()).WithTerrainRules(Wp23.Pack()).WithCombat(Combat(radius)),
                WcMapWriter.Write(map ?? TerrainMap()));

        private static long RadiusOf(SimSession s, ulong key = 1) =>
            M3TestWorld.Sim(s).Entities.Read(M3TestWorld.Sim(s).Entities.Resolve<Collider>(), M3TestWorld.Unit(s, key)).Radius.Raw;

        // ---- S6: the collider ----

        [TestCase(null, Max)]
        [TestCase("0.15", R15)]
        [TestCase("0.05", 3276L)]
        public void S6_PlacedUnitsGetThePacksRadius(string radius, long raw)
        {
            SimSession s = Session(MoverKind.InterimCrowd, radius, new[] { Wp23.Unit(1, 1, 3000, 3500), Wp23.Unit(2, 2, 4000, 3500) });
            Assert.That(RadiusOf(s, 1), Is.EqualTo(raw));
            Assert.That(RadiusOf(s, 2), Is.EqualTo(raw));
        }

        // A world with a body but no terrain places with the pack's radius too (MovementFeature on its own).
        [Test]
        public void S6_AFlatWorldPlacesWithThePacksRadius()
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 3000, 3500, 4000, 1) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(Combat("0.15"));
            Assert.That(RadiusOf(SimSession.Create(recipe, WcMapWriter.Write(FlatMap()))), Is.EqualTo(R15));
        }

        // A world without a combat pack keeps the default radius.
        [Test]
        public void S6_AWorldWithoutCombatKeepsTheDefault()
        {
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, new[] { new UnitSpawn(1, 0, 1, 3000, 3500, 4000) }, Array.Empty<BridgeCommand>());
            Assert.That(RadiusOf(SimSession.Create(recipe, WcMapWriter.Write(FlatMap()))), Is.EqualTo(Max));
        }

        private static M6World Battle(CombatPack combat, SimVec3? at = null)
        {
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, reserve: true), M6World.U(2, 0, 7) }, M6World.Pack(interval: 1, columns: 1, rows: 1), combat));
            if (at.HasValue) w.Set(2, new Position(at.Value));
            return w;
        }

        // A reserve unit deployed by the match gets the pack's radius, and keeps 2 * (r + r) from a unit standing there
        // (D5: 65536 raw for the default body; r = 9830 makes it 39320).
        [TestCase(39319L, false)]
        [TestCase(39320L, true)]
        public void S6_AReserveUnitIsDeployedWithThePacksRadiusAndClearance(long distanceRaw, bool accepted)
        {
            SimVec3 candidate = M6World.P(6);
            M6World w = Battle(Combat("0.15"), candidate + new SimVec3(SimFix.FromRaw(distanceRaw), default, default));
            Assert.That(w.Read<Collider>(2).Radius.Raw, Is.EqualTo(R15), "the unit placed at the start");
            w.Step();
            Assert.That(w.Has<Position>(1), Is.EqualTo(accepted));
            if (accepted) Assert.That(w.Read<Collider>(1).Radius.Raw, Is.EqualTo(R15), "the reserve unit");
        }

        [Test]
        public void S6_TheDefaultBodyKeepsTheOldClearance()
        {
            SimVec3 candidate = M6World.P(6);
            M6World refused = Battle(Combat(null), candidate + new SimVec3(SimFix.FromRaw(65535), default, default));
            refused.Step();
            Assert.That(refused.Has<Position>(1), Is.False);
            M6World accepted = Battle(Combat(null), candidate + new SimVec3(SimFix.FromRaw(65536), default, default));
            accepted.Step();
            Assert.That(accepted.Has<Position>(1), Is.True);
            Assert.That(accepted.Read<Collider>(1).Radius.Raw, Is.EqualTo(Max));
        }

        // ---- S7 and S8: walking ----

        private static (SimSession Session, SimVec3 End, List<SimVec3> Track) Walk(MoverKind mover, string radius, int ticks = 500)
        {
            SimSession s = Session(mover, radius, new[] { Wp23.Unit(1, 1, 5000, 3500) }, new[] { BridgeCommand.MoveTo(1, 1, 9800, 3500) });
            var track = new List<SimVec3>();
            for (int tick = 0; tick < ticks; tick++) { s.StepOnce(); track.Add(M3TestWorld.Position(s)); }
            return (s, track[track.Count - 1], track);
        }

        private static long ToTarget(SimVec3 p) => Math.Abs(p.X.Raw - Wp23.Mm(9800).Raw) + Math.Abs(p.Z.Raw - Wp23.Mm(3500).Raw);

        [TestCase(MoverKind.InterimPath)]
        [TestCase(MoverKind.InterimCrowd)]
        public void S7_ThinBodyWalksToThePointNearTheWall(MoverKind mover)
        {
            var (s, end, _) = Walk(mover, "0.15");
            Assert.That(ToTarget(end), Is.Zero, "arrives on the target, 0.2 m from the wall");
            Assert.That(M3TestWorld.Locomotion(s).HasFailed(M3TestWorld.Unit(s)), Is.False);
            Assert.That(Wp23.Intent(s).Active, Is.False);
        }

        [TestCase(MoverKind.InterimPath)]
        [TestCase(MoverKind.InterimCrowd)]
        public void S7_DefaultBodyStopsShortOfThePointAndFails(MoverKind mover)
        {
            var (s, end, track) = Walk(mover, null);
            NavGrid nav = TerrainDerivation.Derive(TerrainMap(), Wp23.Pack()).Nav;
            foreach (SimVec3 p in track) Assert.That(nav.SegmentClear(p, p, BodyRadius.Default), Is.True, "never inside the default clearance");
            Assert.That(ToTarget(end), Is.GreaterThan(Wp23.Mm(10).Raw), "stops more than 1 cm short");
            Assert.That(M3TestWorld.Locomotion(s).HasFailed(M3TestWorld.Unit(s)), Is.True);
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        [TestCase(16385L)]
        public void S7_TheMoverRefusesARadiusTheNavigationCannotProve(long raw)
        {
            EntityStore entities = M3TestWorld.Sim(Session(MoverKind.InterimPath, null, new[] { Wp23.Unit(1, 1, 3000, 3500) })).Entities;
            TerrainTables tables = TerrainDerivation.Derive(TerrainMap(), Wp23.Pack());
            Assert.Throws<ArgumentOutOfRangeException>(() => new InterimLocomotion(entities, 0, tables.Nav, false, null, null, tables, SimFix.FromRaw(raw)));
            Assert.DoesNotThrow(() => new InterimLocomotion(entities, 0, tables.Nav, false, null, null, tables, SimFix.FromRaw(16384)));
            Assert.DoesNotThrow(() => new InterimLocomotion(entities, 0, tables.Nav, false, null, null, tables, SimFix.FromRaw(1)));
        }

        // The thin body also keeps off the wall by its own radius: no sampled position is inside 0.15 m.
        [TestCase(MoverKind.InterimPath)]
        [TestCase(MoverKind.InterimCrowd)]
        public void S8_ThinBodyNeverEntersItsOwnClearance(MoverKind mover)
        {
            var (_, _, track) = Walk(mover, "0.15");
            NavGrid nav = TerrainDerivation.Derive(TerrainMap(), Wp23.Pack()).Nav;
            foreach (SimVec3 p in track) Assert.That(nav.SegmentClear(p, p, Thin), Is.True);
            Assert.That(track.Any(p => !nav.SegmentClear(p, p, BodyRadius.Default)), Is.True, "it does come closer than the default allows");
        }

        // ---- S9: a floor plate ----

        // The ML-8 house: the first floor is plate 2 over x 9..15, z 6..10 (layer 2, 3 m up). A target 0.2 m inside its west edge
        // (x = 9.2 m): the default body cannot stand there and is moved to the cell centre (9.5 m); r = 0.15 m stands on it.
        private static bool CanStandOnFloor(string radius, long xMm)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 11500, 7500, 3000, 1, layer: 2) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>()).WithCombat(Combat(radius)).WithPlatesPreview();
            SimSession s = SimSession.Create(recipe, PlateDemoMap.Bytes());
            return M3TestWorld.Locomotion(s).CanStandOn(2, new SimVec3(Wp23.Mm(xMm), SimFix.FromInt(3), Wp23.Mm(7500)));
        }

        [Test]
        public void S9_TheStandingQueryOnAFloorUsesTheRadius()
        {
            Assert.That(CanStandOnFloor("0.15", 9200), Is.True);
            Assert.That(CanStandOnFloor(null, 9200), Is.False);
            Assert.That(CanStandOnFloor(null, 9500), Is.True);
            Assert.That(CanStandOnFloor("0.15", 9100), Is.False, "0.1 m from the edge is too close for r = 0.15 as well");
        }

        private static SimVec3 OnFloor(string radius)
        {
            var units = new[] { new UnitSpawn(1, 0, 1, 11500, 7500, 3000, 1, layer: 2) };
            var recipe = new WorldRecipe(41, MoverKind.InterimCrowd, units, new[] { BridgeCommand.MoveTo(1, 1, 9200, 7500, 2) })
                .WithCombat(Combat(radius)).WithPlatesPreview();
            SimSession s = SimSession.Create(recipe, PlateDemoMap.Bytes());
            for (int tick = 0; tick < 200; tick++) s.StepOnce();
            Assert.That(M3TestWorld.Component<VerticalState>(s).Layer, Is.EqualTo(2));
            return M3TestWorld.Position(s);
        }

        [Test]
        public void S9_ThinBodyStandsNearTheEdgeOfAFloor()
        {
            SimVec3 thin = OnFloor("0.15"), standard = OnFloor(null);
            Assert.That(thin.X.Raw, Is.EqualTo(Wp23.Mm(9200).Raw));
            Assert.That(standard.X.Raw, Is.EqualTo(Wp23.Mm(9500).Raw));
            Assert.That(thin.Z.Raw, Is.EqualTo(standard.Z.Raw));
        }

        // ---- S10: deployment ----

        private static DeployGround Ground(WcMap map, SimFix? radius, bool terrain)
        {
            if (!terrain) return new DeployGround(WcMapNavigation.Create(map), null, map.FlatHeight.Raw, radius);
            TerrainTables tables = TerrainDerivation.Derive(map, Wp23.Pack());
            return new DeployGround(tables.Nav, tables, map.FlatHeight.Raw, radius);
        }

        private static SimVec3 P(long xMm, long zMm) => new SimVec3(Wp23.Mm(xMm), default, Wp23.Mm(zMm));

        [TestCase(true)]
        [TestCase(false)]
        public void S10_DeployGroundStandsAsCloseAsItsRadiusAllows(bool terrain)
        {
            WcMap map = terrain ? TerrainMap() : FlatMap();
            Assert.That(Ground(map, Thin, terrain).CanStand(P(9800, 3500)), Is.True, "0.2 m from the wall, r = 0.15");
            Assert.That(Ground(map, null, terrain).CanStand(P(9800, 3500)), Is.False, "0.2 m from the wall, default");
            Assert.That(Ground(map, SimFix.FromRaw(Max), terrain).CanStand(P(9800, 3500)), Is.False, "the default given in full");
            Assert.That(Ground(map, Thin, terrain).CanStand(P(9500, 3500)), Is.True);
            Assert.That(Ground(map, Thin, terrain).CanStand(P(10500, 3500)), Is.False, "inside the wall");
        }

        // The terrain test is exact: r from the wall stands, one raw closer does not.
        [Test]
        public void S10_TerrainDeploymentIsExactAtTheRadius()
        {
            DeployGround thin = Ground(TerrainMap(), Thin, true);
            SimFix z = Wp23.Mm(3500);
            Assert.That(thin.CanStand(new SimVec3(SimFix.FromRaw(10 * S - R15), default, z)), Is.True);
            Assert.That(thin.CanStand(new SimVec3(SimFix.FromRaw(10 * S - R15 + 1), default, z)), Is.False);
        }

        [Test]
        public void S10_DeployGroundRefusesARadiusTheNavigationCannotProve()
        {
            WcMap map = TerrainMap();
            Assert.Throws<ArgumentOutOfRangeException>(() => Ground(map, SimFix.FromRaw(NavGrid.MaxClearRadiusRaw + 1), true));
            Assert.Throws<ArgumentOutOfRangeException>(() => Ground(map, SimFix.FromRaw(0), true));
        }

        [TestCase(MoverKind.InterimPath)]
        [TestCase(MoverKind.InterimCrowd)]
        public void S10_TheWorldIsBuiltWhereTheThinBodyStands(MoverKind mover)
        {
            var units = new[] { Wp23.Unit(7, 1, 9800, 3500) };
            Assert.DoesNotThrow(() => Session(mover, "0.15", units));
            ArgumentException e = Assert.Throws<ArgumentException>(() => Session(mover, null, units));
            Assert.That(e.Message, Does.Contain("7"));
        }

        // The match deploys reserves with the pack's radius: a base 0.2 m west of a wall takes a reserve with r = 0.15 and refuses it by default.
        private static bool DeployedBesideAWall(string radius)
        {
            WcMap map = M3TestWorld.Boxes(80, 40, (6, 18, 1, 5));
            var anchors = new[] { new SpawnAnchor(1, new SimVec3(SimFix.Ratio(58, 10), default, SimFix.FromInt(20))), new SpawnAnchor(2, M6World.P(74)) };
            var w = new M6World(M6World.RecipeOf(new[] { M6World.U(1, 1, reserve: true), M6World.U(2, 2, 60) },
                M6World.Pack(interval: 1, columns: 1, rows: 1, bases: anchors), Combat(radius)), WcMapWriter.Write(map));
            w.Step();
            return w.Has<Position>(1);
        }

        [Test]
        public void S10_TheMatchDeploysWhereTheThinBodyStands()
        {
            Assert.That(DeployedBesideAWall("0.15"), Is.True);
            Assert.That(DeployedBesideAWall(null), Is.False);
        }

        // ---- S11: the map gate ----

        private static string[] Gate(string radius)
        {
            string id = radius == null ? "hc1-gate-default" : "hc1-gate-thin";
            ContentPack pack = Ml3Content.Load(id, Ml3Content.Layer(id, "rwr-m6", "\"combat\": { " + Hc1ContentTests.BodyJson(radius) + " }"));
            E2Map m = new E2Map { Pack = id, Width = 16, Depth = 8 }.Wall(10, 2, 1, 3).Unit(1, 9800, 3500);
            return MapGate.Check(m.Bytes(), pack).Select(p => p.Code).ToArray();
        }

        [Test]
        public void S11_TheGateChecksUnitsAgainstThePacksRadius()
        {
            Assert.That(Gate(null), Is.EqualTo(new[] { "GATE_SPAWN_FOOTPRINT" }));
            Assert.That(Gate("0.25"), Is.EqualTo(new[] { "GATE_SPAWN_FOOTPRINT" }));
            Assert.That(Gate("0.15"), Is.Empty);
        }

        [Test]
        public void S11_AWorldWithoutABodyKeepsTheDefaultFootprint()
        {
            Assert.That(MapGate.FootprintRaw, Is.EqualTo(BodyRadius.DefaultRaw));
            ContentPack pack = Ml3Content.Load("hc1-gate-none", Ml3Content.Layer("hc1-gate-none", "rwr-m6", ""));
            Assert.That(pack.Body, Is.Null);
            E2Map m = new E2Map { Pack = "hc1-gate-none", Width = 16, Depth = 8 }.Wall(10, 2, 1, 3).Unit(1, 9800, 3500);
            Assert.That(MapGate.Check(m.Bytes(), pack).Select(p => p.Code).ToArray(), Is.EqualTo(new[] { "GATE_SPAWN_FOOTPRINT" }));
        }

        // ---- S13: the deployer's key and the saves ----

        private static ulong PolicyKey(M6World w) =>
            ((IUnitDeployer)typeof(ReinforceSystem).GetField("deployer", Private).GetValue(w.Reinforce)).PolicyKey;

        [Test]
        public void S13_TheDeployersPolicyKeyFollowsTheRadius()
        {
            ulong standard = PolicyKey(Battle(null)), thin = PolicyKey(Battle(Combat("0.15")));
            Assert.That(thin, Is.Not.EqualTo(PolicyKey(Battle(Combat(null)))));
            Assert.That(PolicyKey(Battle(Combat("0.15"))), Is.EqualTo(thin), "the same pack, the same key");
            Assert.That(standard, Is.EqualTo(BaselinePolicyKey), "no combat pack: the key of before HC-1");
            Assert.That(PolicyKey(Battle(Combat(null))), Is.EqualTo(standard), "a body of the default radius: the same key");
        }

        private const ulong BaselinePolicyKey = 304368653193324599UL;

        private static SimSession Lineup(string radius)
        {
            ContentPack pack = Ml3Content.Load("hc1w", Ml3Content.Layer("hc1w", "rwr-stance", "\"combat\": { " + Hc1ContentTests.BodyJson(radius) + " }"));
            int weapon = pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1);
            var units = new[] { new UnitSpawn(1, 0, 1, 5000, 5500, 6000, 1, weapon), new UnitSpawn(2, 0, 2, 6500, 5500, 6000, 1, weapon) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 9000, 5500) };
            SimSession s = SimSession.Create(new WorldRecipe(41, MoverKind.InterimCrowd, units, script).WithCombat(pack.BuildCombat()), Scale2aDemo.MapBytes());
            for (int i = 0; i < 20; i++) s.StepOnce();
            return s;
        }

        [Test]
        public void S13_ASaveOfAThinBodyIsRefusedByAPackOfAnotherRadius()
        {
            byte[] thinSave = M3TestWorld.Sim(Lineup("0.15")).SaveRetainedSnapshot();
            byte[] standardSave = M3TestWorld.Sim(Lineup(null)).SaveRetainedSnapshot();
            Assert.That(thinSave, Is.Not.EqualTo(standardSave));
            Assert.Catch<InvalidOperationException>(() => M3TestWorld.Sim(Lineup(null)).RestoreRetainedSnapshot(thinSave));
            Assert.Catch<InvalidOperationException>(() => M3TestWorld.Sim(Lineup("0.15")).RestoreRetainedSnapshot(standardSave));
            Assert.DoesNotThrow(() => M3TestWorld.Sim(Lineup("0.15")).RestoreRetainedSnapshot(thinSave), "the same pack restores it");
            Assert.DoesNotThrow(() => M3TestWorld.Sim(Lineup(null)).RestoreRetainedSnapshot(standardSave));
        }

        // The default body's world, byte for byte as before HC-1 (the same 20 ticks and the same save, measured on the code before).
        [Test]
        public void S13_TheDefaultWorldsStateAndSaveAreUnchanged()
        {
            SimSession s = SimSession.Create(Scale2aDemo.Recipe(), Scale2aDemo.MapBytes());
            for (int i = 0; i < 20; i++) s.StepOnce();
            Assert.That(s.StateHash, Is.EqualTo(BaselineHash));
            byte[] save = M3TestWorld.Sim(s).SaveRetainedSnapshot();
            Assert.That(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(save)).ToLowerInvariant(), Is.EqualTo(BaselineSave));
        }

        private const ulong BaselineHash = 8692015661068015648UL;
        private const string BaselineSave = "f4a2d031a15570072a6e9bda639845410655ce51745d27a367351f882255801f";

        // ---- S14: the picture ----

        [TestCase("0.15", R15)]
        [TestCase("0.05", 3276L)]
        [TestCase(null, Max)]
        public void S14_TheDrawnRadiusIsThePacksRadius(string radius, long raw)
        {
            BodySizes sizes = BodySizes.Of(Lineup(radius));
            Assert.That(sizes.Radius, Is.EqualTo(raw / 65536f));
            Assert.That(BodySizes.RadiusRaw, Is.EqualTo(BodyRadius.DefaultRaw));
        }

        // The drawn body is as wide as the collider: the picture and the simulation read the same number.
        [Test]
        public void S14_ThePictureAndTheColliderAgree()
        {
            SimSession s = Lineup("0.15");
            Assert.That(BodySizes.Of(s).Radius * 65536f, Is.EqualTo((float)RadiusOf(s)));
        }
    }
}
