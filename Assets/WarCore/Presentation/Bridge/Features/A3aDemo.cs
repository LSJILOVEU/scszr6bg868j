using System;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The A3a search game (the test GoesStandsAndGivesUp: A3a fixture, content layer with targetMemoryTicks 300, searchTicks 60,
    // grass concealing to 500), written out here so the Unity demo can show it: observer A (team 1, armed, effect 0) stands
    // on point 1; target B (team 2, unarmed, 6 m/s) holds from tick 1, is sent into the far corner at tick 20 and holds
    // again at tick 140, the tick the test's driver re-holds it once its move has ended. A sees B last at tick 36, chases
    // from 46, stands looking at 146, gives up at 206 and forgets at 336. The match never ends (rule set test.never-ends).
    // PLACEHOLDER: every value is a demo value; no production recipe uses this.
    public static class A3aDemo
    {
        private const long S = SimFix.Scale;
        private const int Width = 40, Depth = 30, Think = 10;
        private const string LayerId = "a3a-test";
        private const string LayerJson = "{ \"id\": \"a3a-test\", \"extends\": \"rwr\", "
            + "\"surfaces\": [ { \"id\": \"dirt\" }, { \"id\": \"grass\", \"speedMilli\": 800, \"concealMilli\": 500 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, "
            + "\"ai\": { \"targetMemoryTicks\": 300, \"searchTicks\": 60 } }";
        private static readonly string[] Names = { "dirt", "grass" };
        private static readonly (int x, int z, int w, int d)[] Boxes = { (15, 13, 1, 5) };

        // 40 x 30 cells, flat height field at 0, Min.Y 0, Max.Y 4 m, one 2 m wall box at cells x 15, z 13..17,
        // grass on every cell with z >= 20.
        public static byte[] MapBytes() => WcMapWriter.Write(Map());

        public static WorldRecipe Recipe()
        {
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 22500, 10500, 3000, 1, 0, false),
                new UnitSpawn(2, 0, 2, 22500, 17500, 6000, 2, -1, false),
            };
            var commands = new[] { BridgeCommand.Hold(1, 2), BridgeCommand.MoveTo(20, 2, 37500, 29500), BridgeCommand.Hold(140, 2) };
            var match = new MatchPack(
                new[] { new ControlPointDef(1, Cell(22, 10), SimFix.Ratio(1, 2), initialOwner: 1), new ControlPointDef(2, Cell(34, 4), SimFix.Ratio(1, 2)) },
                new[] { new SpawnAnchor(1, Cell(1, 10)), new SpawnAnchor(2, Cell(38, 10)) },
                2, 1, false, 100000, InterruptMode.Reset, 1, 90, Think, 9000, SimFix.FromInt(20), 4, 4, SimFix.Ratio(3, 2));
            var combat = new CombatPack(new[] { new WeaponDef(30000, 3, 60000, SimFix.FromInt(10), SimFix.FromInt(30), 0) },
                new RwrHitResolver(), new SightLimited(new NearestVisibleEnemy(), SimFix.FromInt(20)), 100 * S);
            ContentPack pack = ContentLoader.Load(new LayerSource(FileContentSource.Rwr()), LayerId);
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, commands)
                .WithMatch(match).WithCombat(combat).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithRuleSet(MatchMode.TestNeverEndsId).WithTerrainRules(pack).WithStandingOrders();
        }

        // Cell (x, z) has its centre at (x + 0.5, z + 0.5) m.
        private static SimVec3 Cell(int x, int z) => new SimVec3(SimFix.FromRaw(x * S + S / 2), default, SimFix.FromRaw(z * S + S / 2));

        private static WcMap Map()
        {
            const long wallRaw = 2 * S, maxY = 4 * S;
            var walls = new WcMapWall[Boxes.Length];
            for (int i = 0; i < Boxes.Length; i++)
            {
                var b = Boxes[i];
                walls[i] = new WcMapWall(0, (ulong)(i + 1), "wall", "walls",
                    new SimVec3(SimFix.FromInt(b.x) + SimFix.Ratio(b.w, 2), SimFix.FromRaw(wallRaw / 2), SimFix.FromInt(b.z) + SimFix.Ratio(b.d, 2)),
                    SimFix.Ratio(b.w, 2), SimFix.FromRaw(wallRaw), SimFix.Ratio(b.d, 2));
            }
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    bool blocked = false;
                    foreach (var b in Boxes) blocked |= x >= b.x && x < b.x + b.w && z >= b.z && z < b.z + b.d;
                    if (!blocked) { int k = z * Width + x; bits[k / 8] |= (byte)(1 << (k % 8)); }
                }
            var points = new int[(Width + 1) * (Depth + 1)];
            var cells = new byte[Width * Depth];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++) cells[z * Width + x] = z >= 20 ? (byte)1 : (byte)0;
            var min = new SimVec3(default, SimFix.FromRaw(0), default);
            var max = new SimVec3(SimFix.FromInt(Width), SimFix.FromRaw(maxY), SimFix.FromInt(Depth));
            return new WcMap("wp23", "none", min, max, SimFix.FromInt(Math.Max(Width, Depth)), SimFix.FromRaw(0),
                Width, Depth, new[] { new WcMapLayer("walls", 1) }, walls, bits, points, Names, cells);
        }

        // The demo layer in memory over the RWR data files: a3a-test is the literal above, every other layer (its base rwr)
        // is read from the files.
        private sealed class LayerSource : IContentSource
        {
            private readonly IContentSource files;
            internal LayerSource(IContentSource files) { this.files = files; }

            public bool TryRead(string layerId, out string text, out string location)
            {
                if (layerId != LayerId) return files.TryRead(layerId, out text, out location);
                text = LayerJson; location = "demo:" + LayerId; return true;
            }
        }
    }
}
