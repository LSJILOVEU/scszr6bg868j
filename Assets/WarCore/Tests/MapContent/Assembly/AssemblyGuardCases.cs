using System;
using System.Collections.Generic;
using System.Linq;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // F2a: every guard of the world builder, one fixture each, plus fixtures that break two rules at once so that the order the
    // guards fire in is pinned. A case yields "OK" or "<exception type>|<full message>".
    internal static class AssemblyGuardCases
    {
        private const long S = Wp23.S;
        private static readonly BridgeCommand[] NoScript = new BridgeCommand[0];

        private static UnitSpawn[] One => new[] { new UnitSpawn(1, 0, 1, 2500, 4500, 1000) };

        private static WorldRecipe R(MoverKind mover = MoverKind.InterimCrowd, UnitSpawn[] units = null) =>
            new WorldRecipe(41, mover, units ?? One, NoScript);

        private static byte[] Flat() => WcMapWriter.Write(Wp23.Make(16, 8, 0, 6 * S, new[] { (10, 2, 1, 3) }, 4 * S));

        private static WcMap TerrainMap() =>
            Wp23.Make(16, 8, 0, 6 * S, new[] { (10, 2, 1, 3) }, 4 * S, (i, j) => (int)(i * (S / 16)));

        private static byte[] Terrain() => WcMapWriter.Write(TerrainMap());

        private static byte[] TerrainOnMoverLayer()
        {
            WcMap m = TerrainMap();
            var layers = new[] { new WcMapLayer("walls", 0) };
            return WcMapWriter.Write(new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth,
                layers, m.Walls.ToArray(), m.Passability.ToArray(), m.HeightPoints.ToArray(), null, null));
        }

        private static byte[] SurfacesNotInPack() => WcMapWriter.Write(Wp23.Make(16, 8, 0, 6 * S, null, 4 * S, null, Wp23.Names, (x, z) => (byte)(x < 8 ? 0 : 2)));

        private static ContentPack ThinPack() =>
            AssemblyWorlds.TerrainPack("{ \"id\": \"rwr-terrain-demo\", \"extends\": \"rwr\", \"surfaces\": [ { \"id\": \"grass\" } ], "
                + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 } }");

        private static MatchPack Match() => M6World.Pack();
        private static CombatPack Combat() => RwrPack.CreateM6();

        private static CombatPack Blocking() => AssemblyWorlds.BlockPack();

        private static UnitSpawn[] WithReserve => new[]
        {
            new UnitSpawn(1, 0, 1, 2500, 4500, 1000, 1, -1, false), new UnitSpawn(2, 0, 1, 2500, 5500, 1000, 1, -1, true),
        };

        private static WorldRecipe Battle(UnitSpawn[] units = null) => R(MoverKind.InterimCrowd, units).WithCombat(Combat()).WithMatch(Match());

        internal static readonly (string Name, Func<object> Run)[] Cases =
        {
            ("NullRecipe", () => WorldBuilder.Create(null, Flat())),
            ("UnsupportedMover", () => WorldBuilder.Create(R((MoverKind)99), Flat())),
            ("UnsupportedMoverAndBattle", () => WorldBuilder.Create(R((MoverKind)99).WithMatch(Match()), Flat())),
            ("PathWithoutMap", () => WorldBuilder.Create(R(MoverKind.InterimPath), null)),
            ("CrowdWithoutMap", () => WorldBuilder.Create(R(), null)),
            ("PathWithoutMapAndBattle", () => WorldBuilder.Create(R(MoverKind.InterimPath).WithMatch(Match()), null)),
            ("MatchWithoutCombat", () => WorldBuilder.Create(R().WithMatch(Match()), Flat())),
            ("MatchWithoutCrowd", () => WorldBuilder.Create(R(MoverKind.InterimPath).WithCombat(Combat()).WithMatch(Match()), Flat())),
            ("PlayerWithoutMatch", () => WorldBuilder.Create(R().WithCombat(Combat()).WithPlayer(1), Flat())),
            ("BattleAndPlayerBothWrong", () => WorldBuilder.Create(R().WithMatch(Match()).WithPlayer(1), Flat())),
            ("TerrainNeedsPathMover", () => WorldBuilder.Create(R(MoverKind.Interim).WithTerrainRules(AssemblyWorlds.TerrainPack()), Terrain())),
            ("TerrainWithPlayerAndPack", () => WorldBuilder.Create(Battle().WithPlayer(1).WithTerrainRules(AssemblyWorlds.TerrainPack()), Terrain())),
            ("TerrainWithPlayerAndNoPack", () => WorldBuilder.Create(Battle().WithPlayer(1), Terrain())),
            ("TerrainWithoutPack", () => WorldBuilder.Create(R(MoverKind.InterimPath), Terrain())),
            ("TerrainPackMissingSurface", () => WorldBuilder.Create(R(MoverKind.InterimPath).WithTerrainRules(ThinPack()), SurfacesNotInPack())),
            ("WallLayerIsMoverLayer", () => WorldBuilder.Create(R(MoverKind.Interim), WcMapWriter.Write(LayerZero()))),
            ("WallLayerIsMoverLayerAndNoPack", () => WorldBuilder.Create(R(MoverKind.InterimPath), TerrainOnMoverLayer())),
            ("SpawnNotStandable", () => WorldBuilder.Create(R(MoverKind.InterimPath, new[] { new UnitSpawn(1, 0, 1, 10500, 3500, 1000) })
                .WithTerrainRules(AssemblyWorlds.TerrainPack()), Terrain())),
            ("PlayerKeyUnknown", () => WorldBuilder.Create(Battle().WithPlayer(99), Flat())),
            ("PlayerIsReserve", () => WorldBuilder.Create(Battle(WithReserve).WithPlayer(2), Flat())),
            ("CrowdPassWithoutCombat", () => WorldBuilder.Create(R().WithCrowdPass(CrowdPassKind.TeamIdle), Flat())),
            ("CrowdPassWithoutCrowd", () => WorldBuilder.Create(R(MoverKind.InterimPath).WithCombat(Combat()).WithCrowdPass(CrowdPassKind.TeamIdle), Flat())),
            ("CrowdPassAndBlockAiBothWrong", () => WorldBuilder.Create(R().WithCrowdPass(CrowdPassKind.TeamIdle).WithFacing().WithBlockAi(), Flat())),
            ("BlockAiWithoutCombat", () => WorldBuilder.Create(R().WithFacing().WithBlockAi(), Flat())),
            ("BlockAiWithoutBlockSpecs", () => WorldBuilder.Create(R().WithCombat(Combat()).WithFacing().WithBlockAi(), Flat())),
            ("BlockAiWithoutFacing", () => WorldBuilder.Create(R().WithCombat(Blocking()).WithBlockAi(), Flat())),
            ("PlainCrowdWorldBuilds", () => WorldBuilder.Create(R(), Flat())),
            ("TerrainWorldBuilds", () => WorldBuilder.Create(R(MoverKind.InterimPath).WithTerrainRules(AssemblyWorlds.TerrainPack()), Terrain())),
        };

        // A plain map whose only wall sits on collision layer index 0, which is the movers' own layer.
        private static WcMap LayerZero()
        {
            WcMap m = M3TestWorld.Boxes(16, 8, (4, 3, 2, 2));
            return new WcMap(m.MapId, m.ContentId, m.Min, m.Max, m.ChunkSize, m.FlatHeight, m.Width, m.Depth,
                new[] { new WcMapLayer("walls", 0) }, m.Walls.ToArray(), m.Passability.ToArray());
        }

        internal static string Outcome(Func<object> run)
        {
            try
            {
                run();
                return "OK";
            }
            catch (Exception e)
            {
                return e.GetType().FullName + "|" + ExceptionText.Of(e).Replace("\r", "").Replace("\n", "\\n");
            }
        }
    }
}
