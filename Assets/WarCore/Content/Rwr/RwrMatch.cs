using WarCore.Battle;
using WarCore.Sim;

namespace WarCore.Content.Rwr
{
    // Rules come from the data layer; control points and spawn anchors are map data and stay here until the map tool owns them.
    public static class RwrMatch
    {
        public static SimFix Sight => RwrPack.Load(RwrPack.M6Layer).Match.Sight;
        public static MatchPack CreateM6() => CreateM6(RwrPack.Load(RwrPack.M6Layer));
        public static MatchPack CreateM7() => CreateM7(RwrPack.Load(RwrPack.M6Layer));
        // Pass the same loaded pack to BuildCombat, WeaponOfUnitType and these, so a layer that overrides rules and weapons applies to all.
        public static MatchPack CreateM6(ContentPack content) => content.BuildMatch(new[] {
            Point(1, 16), Point(2, 40), Point(3, 64)
        }, new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(74, 0, 20)) });
        // PLACEHOLDER: M7 coordinates only; all rules use the existing M6 values.
        public static MatchPack CreateM7(ContentPack content) => content.BuildMatch(new[] {
            M7Point(1, 24), M7Point(2, 52), M7Point(3, 80), M7Point(4, 108), M7Point(5, 136)
        }, new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 50)), new SpawnAnchor(2, SimVec3.Metres(154, 0, 50)) });
        private static ControlPointDef M7Point(int id, int x) => new ControlPointDef(id, SimVec3.Metres(x, 0, 50),
            SimFix.FromInt(6), hasSpawn: true, spawn: SimVec3.Metres(x, 0, 50));

        private static ControlPointDef Point(int id, int x) => new ControlPointDef(id, SimVec3.Metres(x, 0, 20),
            SimFix.FromInt(6), hasSpawn: true, spawn: SimVec3.Metres(x, 0, 20));
    }
}
