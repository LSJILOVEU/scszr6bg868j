using System;
using WarCore.Battle;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // ML-10: the one public way to open a map with floor plates: the ML-8 demo house (PlateDemoMap) with the player (key 1,
    // armed) outside its door, two unarmed team mates on the first floor and two unarmed enemies far off on the ground. No AI
    // rules: nobody but the player moves. The runtime still refuses other maps with plates (WorldBuilder). PLACEHOLDER demo values.
    public static class PlateDemo
    {
        public const ulong PlayerKey = 1;
        // The demo walks at a soldier's speed over PaceDivisor: the scripted ticks for pictures are laid out for that pace.
        private const long PaceDivisor = 2;

        public static byte[] MapBytes() => PlateDemoMap.Bytes();

        public static WorldRecipe Recipe()
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            int weapon = content.WeaponOfUnitType("rwr.soldier-1");
            long speed = content.SpeedOfUnitType("rwr.soldier-1") / PaceDivisor;
            var units = new[]
            {
                new UnitSpawn(PlayerKey, 0, 1, 4500, 8500, speed, 1, weapon),
                new UnitSpawn(2, 0, 2, 11500, 7500, speed, 1, layer: 2),
                new UnitSpawn(3, 0, 2, 12500, 6500, speed, 1, layer: 2),
                new UnitSpawn(10, 0, 3, 20500, 3500, speed, 2),
                new UnitSpawn(11, 0, 3, 20500, 12500, speed, 2),
            };
            foreach (UnitSpawn unit in units) MapRecipe.CheckStep(content, "rwr.soldier-1", unit);
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(5, 0, 13), SimFix.FromInt(2), 1),
                new ControlPointDef(2, SimVec3.Metres(19, 0, 13), SimFix.FromInt(2), 2),
            };
            var bases = new[] { new SpawnAnchor(1, SimVec3.Metres(2, 0, 8)), new SpawnAnchor(2, SimVec3.Metres(22, 0, 8)) };
            var match = new MatchPack(points, bases, 1, 1, true, 300, InterruptMode.Reset, 1, 1000000, 1000000, 108000,
                SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), 300);
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(match).WithCrowdPass(CrowdPassKind.TeamIdle)
                .WithPlayer(PlayerKey).WithPlatesPreview();
        }

        // The scripted walk for pictures: in through the west door (tick 1), stand under the first floor (51), up the stair
        // (160, 170), onto the floor (260), across it (320) and stand (400). A new array each call.
        public static (long AtTick, PlayerInput Input)[] Script() => new[]
        {
            (1L, new PlayerInput(1000, 0)),
            (51L, new PlayerInput(0, 0)),
            (160L, new PlayerInput(0, 1000)),
            (170L, new PlayerInput(1000, 0)),
            (260L, new PlayerInput(0, -1000)),
            (280L, new PlayerInput(0, 0)),
            (320L, new PlayerInput(-1000, 0)),
            (400L, new PlayerInput(0, 0)),
        };
    }
}
