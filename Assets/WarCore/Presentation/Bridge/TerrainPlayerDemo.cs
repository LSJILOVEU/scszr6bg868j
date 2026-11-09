namespace WarCore.Presentation
{
    // L10: the demo terrain match with team 1's first unit (key 1) as the player, respawning in a reserve 300 ticks (10 s) after
    // death, and a fixed six-step input script for the scripted Unity demo. All values are PLACEHOLDER demo values.
    public static class TerrainPlayerDemo
    {
        public static WorldRecipe Recipe() => DemoRecipes.WithRespawn(DemoRecipes.TerrainMatch().WithPlayer(1), 300);

        // Up the road, stop and fire, walk into the cliff band, down the slope, back to flat ground, fire. A new array each call.
        public static (long AtTick, PlayerInput Input)[] Script() => new[]
        {
            (1L, new PlayerInput(1000, 0)),
            (100L, new PlayerInput(0, 0, 30000, 16500, primary: true)),
            (160L, new PlayerInput(0, 1000, 30000, 16500)),
            (220L, new PlayerInput(-1000, 0, 0, 21500)),
            (320L, new PlayerInput(0, -1000, 8500, 0)),
            (400L, new PlayerInput(0, 0, 40000, 16500, primary: true)),
        };
    }
}
