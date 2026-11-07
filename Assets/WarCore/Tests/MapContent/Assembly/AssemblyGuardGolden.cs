namespace WarCore.Tests
{
    // Guard outcomes recorded on the unmodified builder: "<case>|OK" or "<case>|<exception type>|<message>". Never edit by hand.
    internal static class AssemblyGuardGolden
    {
        internal static readonly string[] Lines =
        {
            "NullRecipe|System.ArgumentNullException|Value cannot be null.|param=recipe",
            "UnsupportedMover|System.ArgumentOutOfRangeException|Unsupported mover.|param=recipe",
            "UnsupportedMoverAndBattle|System.ArgumentOutOfRangeException|Unsupported mover.|param=recipe",
            "PathWithoutMap|System.ArgumentException|Path and crowd movers require map bytes.|param=mapBytes",
            "CrowdWithoutMap|System.ArgumentException|Path and crowd movers require map bytes.|param=mapBytes",
            "PathWithoutMapAndBattle|System.ArgumentException|Path and crowd movers require map bytes.|param=mapBytes",
            "MatchWithoutCombat|System.ArgumentException|Battle requires combat and crowd movement.|param=recipe",
            "MatchWithoutCrowd|System.ArgumentException|Battle requires combat and crowd movement.|param=recipe",
            "PlayerWithoutMatch|System.ArgumentException|Player control requires InterimCrowd, Combat and Match.|param=recipe",
            "BattleAndPlayerBothWrong|System.ArgumentException|Battle requires combat and crowd movement.|param=recipe",
            "TerrainNeedsPathMover|System.NotSupportedException|Terrain maps (height field or surface table) need a path or crowd mover.",
            "TerrainWithPlayerAndPack|System.NotSupportedException|Player control on terrain maps is not supported before T1c.",
            "TerrainWithPlayerAndNoPack|System.NotSupportedException|Player control on terrain maps is not supported before T1c.",
            "TerrainWithoutPack|System.ArgumentException|A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules).|param=recipe",
            "TerrainPackMissingSurface|WarCore.Content.ContentException|content pack 'rwr-terrain-demo' terrain surfaces has no entry 'mud' named by map 'wp23' (surface table entry 1)",
            "WallLayerIsMoverLayer|System.InvalidOperationException|Wall layers must not share the mover layer.",
            "WallLayerIsMoverLayerAndNoPack|System.ArgumentException|A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules).|param=recipe",
            "SpawnNotStandable|System.ArgumentException|Unit spawn 1 is not on standable ground in a terrain world.|param=recipe",
            "PlayerKeyUnknown|System.ArgumentException|Player key does not match a UnitSpawn.Key.|param=recipe",
            "PlayerIsReserve|System.ArgumentException|Player key must name a deployed unit with Position, MoveIntent and Collider.|param=recipe",
            "CrowdPassWithoutCombat|System.ArgumentException|A crowd pass rule requires crowd movement and combat.|param=recipe",
            "CrowdPassWithoutCrowd|System.ArgumentException|A crowd pass rule requires crowd movement and combat.|param=recipe",
            "CrowdPassAndBlockAiBothWrong|System.ArgumentException|A crowd pass rule requires crowd movement and combat.|param=recipe",
            "BlockAiWithoutCombat|System.ArgumentException|The block AI needs combat with block specs and facing.|param=recipe",
            "BlockAiWithoutBlockSpecs|System.ArgumentException|The block AI needs combat with block specs and facing.|param=recipe",
            "BlockAiWithoutFacing|System.ArgumentException|The block AI needs combat with block specs and facing.|param=recipe",
            "PlainCrowdWorldBuilds|OK",
            "TerrainWorldBuilds|OK",
        };
    }
}
