namespace WarCore.Tests
{
    // Guard outcomes recorded on the unmodified builder: "<case>|OK" or "<case>|<exception type>|<message>". Never edit by hand.
    internal static class AssemblyGuardGolden
    {
        internal static readonly string[] Lines =
        {
            "NullRecipe|System.ArgumentNullException|Value cannot be null. (Parameter 'recipe')",
            "UnsupportedMover|System.ArgumentOutOfRangeException|Unsupported mover. (Parameter 'recipe')",
            "UnsupportedMoverAndBattle|System.ArgumentOutOfRangeException|Unsupported mover. (Parameter 'recipe')",
            "PathWithoutMap|System.ArgumentException|Path and crowd movers require map bytes. (Parameter 'mapBytes')",
            "CrowdWithoutMap|System.ArgumentException|Path and crowd movers require map bytes. (Parameter 'mapBytes')",
            "PathWithoutMapAndBattle|System.ArgumentException|Path and crowd movers require map bytes. (Parameter 'mapBytes')",
            "MatchWithoutCombat|System.ArgumentException|Battle requires combat and crowd movement. (Parameter 'recipe')",
            "MatchWithoutCrowd|System.ArgumentException|Battle requires combat and crowd movement. (Parameter 'recipe')",
            "PlayerWithoutMatch|System.ArgumentException|Player control requires InterimCrowd, Combat and Match. (Parameter 'recipe')",
            "BattleAndPlayerBothWrong|System.ArgumentException|Battle requires combat and crowd movement. (Parameter 'recipe')",
            "TerrainNeedsPathMover|System.NotSupportedException|Terrain maps (height field or surface table) need a path or crowd mover.",
            "TerrainWithPlayerAndPack|System.NotSupportedException|Player control on terrain maps is not supported before T1c.",
            "TerrainWithPlayerAndNoPack|System.NotSupportedException|Player control on terrain maps is not supported before T1c.",
            "TerrainWithoutPack|System.ArgumentException|A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules). (Parameter 'recipe')",
            "TerrainPackMissingSurface|WarCore.Content.ContentException|content pack 'rwr-terrain-demo' terrain surfaces has no entry 'mud' named by map 'wp23' (surface table entry 1)",
            "WallLayerIsMoverLayer|System.InvalidOperationException|Wall layers must not share the mover layer.",
            "WallLayerIsMoverLayerAndNoPack|System.ArgumentException|A terrain map needs a content pack with terrain rules (WorldRecipe.WithTerrainRules). (Parameter 'recipe')",
            "SpawnNotStandable|System.ArgumentException|Unit spawn 1 is not on standable ground in a terrain world. (Parameter 'recipe')",
            "PlayerKeyUnknown|System.ArgumentException|Player key does not match a UnitSpawn.Key. (Parameter 'recipe')",
            "PlayerIsReserve|System.ArgumentException|Player key must name a deployed unit with Position, MoveIntent and Collider. (Parameter 'recipe')",
            "CrowdPassWithoutCombat|System.ArgumentException|A crowd pass rule requires crowd movement and combat. (Parameter 'recipe')",
            "CrowdPassWithoutCrowd|System.ArgumentException|A crowd pass rule requires crowd movement and combat. (Parameter 'recipe')",
            "CrowdPassAndBlockAiBothWrong|System.ArgumentException|A crowd pass rule requires crowd movement and combat. (Parameter 'recipe')",
            "BlockAiWithoutCombat|System.ArgumentException|The block AI needs combat with block specs and facing. (Parameter 'recipe')",
            "BlockAiWithoutBlockSpecs|System.ArgumentException|The block AI needs combat with block specs and facing. (Parameter 'recipe')",
            "BlockAiWithoutFacing|System.ArgumentException|The block AI needs combat with block specs and facing. (Parameter 'recipe')",
            "PlainCrowdWorldBuilds|OK",
            "TerrainWorldBuilds|OK",
        };
    }
}
