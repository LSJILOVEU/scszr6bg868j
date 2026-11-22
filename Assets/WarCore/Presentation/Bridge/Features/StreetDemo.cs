using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // SEE-1: a street for seeing through walls. Flat 64 x 40 m ground; a street 12 m wide along x (z 14..26); on the camera's side
    // of it (z 8..14) two solid buildings, 6 m high (x 8..22) and 9 m high (x 26..42); on the far side a 1.2 m sandbag line
    // (x 30..36, z 26..27). Walls only, no plates. The player (key 1, armed) starts at the west end of the street; team mates
    // stand behind each building and in the open, enemies behind each building and at the east end. Nobody but the player moves.
    // PLACEHOLDER demo values.
    public static class StreetDemo
    {
        public const ulong PlayerKey = 1;
        private const long M = SimFix.Scale;
        // The demo walks at a soldier's speed over PaceDivisor: the scripted ticks for pictures are laid out for that pace.
        private const long PaceDivisor = 2;

        public static MapDesc Desc()
        {
            var walls = new[]
            {
                Wall(1, 8, 8, 14, 6, 6), Wall(2, 26, 8, 16, 6, 9), Wall(3, 30, 26, 6, 1, 1.2),
            };
            var units = new MapUnit[0];
            return new MapDesc("street-see-demo", "none", "rwr-m6", new MapXZ(64, 40), new MapXZ(0, 0), 0, 16 * M,
                new[] { new MapLayer("walls", 1) }, null, null, walls,
                new[]
                {
                    new MapPoint(1, new MapMm(6000, 33000), 3000, 0, new MapMm(6000, 33000)),
                    new MapPoint(2, new MapMm(58000, 33000), 3000, 0, new MapMm(58000, 33000)),
                },
                new[] { new MapBase(1, 1, new MapMm(2000, 20000)), new MapBase(2, 2, new MapMm(62000, 20000)) },
                units, new MapRun(41, "teamIdle"), new[] { MapEdit.Hand }, null, null, new MapStamp[0], new MapSrc[0],
                new MapNext(4, 3, 3, 1));
        }

        public static byte[] DescBytes() => MapDescJson.Write(Desc());

        // The baked .wcmap bytes.
        public static byte[] MapBytes() => MapBake.Run(DescBytes()).WcMapBytes;

        public static WorldRecipe Recipe()
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            int weapon = content.WeaponOfUnitType("rwr.soldier-1");
            long speed = content.SpeedOfUnitType("rwr.soldier-1") / PaceDivisor;
            var units = new[]
            {
                new UnitSpawn(PlayerKey, 0, 1, 4500, 19000, speed, 1, weapon),
                new UnitSpawn(2, 0, 2, 15500, 16000, speed, 1),
                new UnitSpawn(3, 0, 2, 34500, 17000, speed, 1),
                new UnitSpawn(4, 0, 2, 46000, 22000, speed, 1),
                new UnitSpawn(10, 0, 3, 16500, 16500, speed, 2),
                new UnitSpawn(11, 0, 3, 36000, 16000, speed, 2),
                new UnitSpawn(12, 0, 3, 56000, 19000, speed, 2),
            };
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(6, 0, 33), SimFix.FromInt(2), 1),
                new ControlPointDef(2, SimVec3.Metres(58, 0, 33), SimFix.FromInt(2), 2),
            };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(2, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(62, 0, 20)) };
            var match = new MatchPack(points, bases, 1, 1, true, 300, InterruptMode.Reset, 1, 1000000, 1000000, 108000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), 300);
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(match).WithCrowdPass(CrowdPassKind.TeamIdle).WithPlayer(PlayerKey);
        }

        // The scripted walk for pictures: east along the street from tick 1, past the 6 m building (x 8..22, about ticks 35 to
        // 175), then the 9 m one (x 26..42, about ticks 215 to 375), and stand at tick 480 (x about 52). A new array each call.
        public static (long AtTick, PlayerInput Input)[] Script() => new[]
        {
            (1L, new PlayerInput(1000, 0)),
            (480L, new PlayerInput(0, 0)),
        };

        private static MapWall Wall(ulong id, int x, int z, int w, int d, double height) =>
            new MapWall(id, "wall", "walls", new MapXZ(x, z), new MapXZ(w, d), (long)Math.Round(height * M / 2) * 2);
    }
}
