using WarCore.Content;
using static WarCore.Content.Rwr.RwrPack;

namespace WarCore.Presentation
{
    // Shared by the Unity demo and the headless tests so both run the same script.
    public static class DemoRecipes
    {
        public static byte[] M2bMapBytes() => M2DemoMap.Bytes();

        public static byte[] M6MapBytes() => M6DemoMap.Bytes();

        // Which weapon a team's soldiers carry comes from the unit types in the content data (team 1, team 2).
        private static int[] TeamWeapons(ContentPack pack)
        {
            return new[] { pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam1), pack.WeaponOfUnitType(WarCore.Content.Rwr.RwrPack.SoldierTeam2) };
        }

        public static WorldRecipe M6() => M6(WarCore.Content.Rwr.RwrPack.Load(WarCore.Content.Rwr.RwrPack.M6Layer));

        // One loaded pack feeds the unit weapons, the combat pack and the match rules.
        public static WorldRecipe M6(ContentPack content)
        {
            int[] weapons = TeamWeapons(content);
            var units = new UnitSpawn[80];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 40 ? 1 : 2, rank = i % 40;
                var offset = WorldBuilder.FormationOffsetMm(rank % 10, 10);
                units[i] = new UnitSpawn((ulong)i + 1, 0, team,
                    (team == 1 ? 6000 : 74000) + offset.X, 20000 + offset.Z,
                    6000, team, weapons[team - 1], rank >= 10); // PLACEHOLDER: M5 speed and radius.
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(WarCore.Content.Rwr.RwrMatch.CreateM6(content)).WithCrowdPass(CrowdPassKind.TeamIdle);
        }

        // WP-20: two swordsmen face to face on the plain 80 x 40 m map; no match rules, so nothing ends the world and the fight
        // plays out until one of them dies. Units hold their ground; a swing starts when an enemy is within the weapon's engage range.
        private const string MeleeLayer = "rwr-melee", Swordsman = "rwr.swordsman";

        public static WorldRecipe MeleeDuel()
        {
            ContentPack content = WarCore.Content.Rwr.RwrPack.Load(MeleeLayer);
            int sword = content.MeleeOfUnitType(Swordsman);
            var units = new[]
            {
                new UnitSpawn(1, 0, 1, 30000, 20000, 3000, 1, content.WeaponOfUnitType(Swordsman), false, sword),
                new UnitSpawn(2, 0, 2, 31500, 20000, 3000, 2, content.WeaponOfUnitType(Swordsman), false, sword),
            };
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>()).WithCombat(content.BuildCombat());
        }

        public static byte[] M7MapBytes() => M7DemoMap.Bytes();
        public static WorldRecipe MpDemo()
        {
            WorldRecipe recipe = M7();
            // WorldRecipe sorts Units by Key; reserves must never become the initial player.
            foreach (UnitSpawn unit in recipe.Units)
                if (unit.Team == 1 && !unit.Reserve) return recipe.WithPlayer(unit.Key);
            throw new System.InvalidOperationException("MP requires a deployed team 1 unit.");
        }

        // F5u: the MP demo with one block spec that only the player's unit carries, so the HUD can show a held block.
        // Every other field of the recipe and its combat pack is copied from MpDemo; no block AI is added.
        public static WorldRecipe MpBlockDemo() =>
            WithPlayerBlock(MpDemo(), new WarCore.Combat.BlockSpec(500, 1000)); // PLACEHOLDER: front +-60 degrees, stops all of the blow.

        // C2: the MP block demo with player respawn on, so a death shows the countdown and play goes on in a reserve.
        public static WorldRecipe MpRespawnDemo() => WithRespawn(MpBlockDemo(), 300); // PLACEHOLDER: 10 s at 30 Hz.

        // The recipe with its match rules rebuilt from every field of the old ones and only the respawn delay replaced.
        internal static WorldRecipe WithRespawn(WorldRecipe source, int ticks)
        {
            WarCore.Battle.MatchPack m = source.Match ?? throw new System.InvalidOperationException("Respawn needs match rules.");
            return source.WithMatch(new WarCore.Battle.MatchPack(m.Points.ToArray(), m.Bases.ToArray(), m.Num, m.Den, m.Strict,
                m.CaptureTicks, m.Interrupt, m.DecayPerTick, m.IntervalTicks, m.ThinkInterval, m.MaxTicks, m.Sight,
                m.GridColumns, m.GridRows, m.GridSpacing, ticks));
        }

        // Appends spec to the recipe's combat pack and gives it to the player's unit only. Every other recipe and pack field is copied.
        internal static WorldRecipe WithPlayerBlock(WorldRecipe source, WarCore.Combat.BlockSpec block)
        {
            WarCore.Combat.CombatPack old = source.Combat;
            var blocks = new WarCore.Combat.BlockSpec[old.Block.Length + 1];
            old.Block.CopyTo(blocks);
            int spec = old.Block.Length;
            blocks[spec] = block;
            var pack = new WarCore.Combat.CombatPack(old.Weapons.ToArray(), old.Resolver, old.Selector, old.MaxHealthRaw,
                old.FriendlyFire, old.FriendlyFireLocked, old.AllyCheckMissRaw, old.AllyHoldMilli, old.Melee.ToArray(), blocks);
            ulong player = source.PlayerKey.Value;
            UnitSpawn[] units = source.Units.ToArray();
            for (int i = 0; i < units.Length; i++)
            {
                UnitSpawn u = units[i];
                if (u.Key != player) continue;
                units[i] = new UnitSpawn(u.Key, u.Archetype, u.Squad, u.XMm, u.ZMm, u.SpeedMmPerSec, u.Team, u.Weapon, u.Reserve, u.Melee, spec);
            }
            WorldRecipe recipe = new WorldRecipe(source.Seed, source.Mover, units, source.Script.ToArray())
                .WithCombat(pack).WithCrowdPass(source.CrowdPass).WithRuleSet(source.RuleSetId).WithPlayer(player);
            if (source.Match != null) recipe = recipe.WithMatch(source.Match);
            if (source.TerrainPack != null) recipe = recipe.WithTerrainRules(source.TerrainPack);
            if (source.BlockAi) recipe = recipe.WithBlockAi();
            foreach (IWorldOption option in source.Options) recipe = recipe.With(option);
            return recipe;
        }

        public static WorldRecipe M7() => M7(WarCore.Content.Rwr.RwrPack.Load(WarCore.Content.Rwr.RwrPack.M6Layer));

        public static WorldRecipe M7(ContentPack content)
        {
            int[] weapons = TeamWeapons(content);
            var units = new UnitSpawn[300];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 150 ? 1 : 2, rank = i % 150;
                // PLACEHOLDER: two 10x10 formations, 2 m spacing; reserves have no position until deployed.
                int x = team == 1 ? 6000 + rank % 10 * 2000 : 154000 - rank % 10 * 2000;
                int z = 41000 + rank % 100 / 10 * 2000;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, z, 6000, team, weapons[team - 1], rank >= 100);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(WarCore.Content.Rwr.RwrMatch.CreateM7(content)).WithCrowdPass(CrowdPassKind.TeamIdle);
        }

        // L6: the WP-22 demo terrain (TerrainDemo.MapBytes) as a full match. 20 per team: 10 deployed in the M6 formation around
        // the team anchor on the road, 10 reserves. Weapons and rules from the RWR M6 layer; surfaces, slope and ai rules from the
        // terrain demo layer. PLACEHOLDER: unit counts and speed (M5 speed, as M6/M7).
        public static WorldRecipe TerrainMatch() =>
            TerrainMatch(WarCore.Content.Rwr.RwrPack.Load(WarCore.Content.Rwr.RwrPack.M6Layer), WarCore.Content.Rwr.RwrPack.Load(TerrainDemoMap.PackId));

        public static WorldRecipe TerrainMatch(ContentPack content, ContentPack terrain)
        {
            int[] weapons = TeamWeapons(content);
            var units = new UnitSpawn[40];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 20 ? 1 : 2, rank = i % 20;
                var offset = WorldBuilder.FormationOffsetMm(rank % 10, 10);
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, (team == 1 ? 3500 : 44500) + offset.X, 16500 + offset.Z,
                    6000, team, weapons[team - 1], rank >= 10);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(TerrainDemoMap.BuildMatch(content))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(terrain);
        }

        public static WorldRecipe M5()
        {
            WorldRecipe original = M4();
            ContentPack content = WarCore.Content.Rwr.RwrPack.Load(WarCore.Content.Rwr.RwrPack.M5Layer);
            int[] weapons = TeamWeapons(content);
            var units = original.Units.ToArray();
            for (int i = 0; i < units.Length; i++)
            {
                UnitSpawn u = units[i];
                units[i] = new UnitSpawn(u.Key, u.Archetype, u.Squad, u.XMm, u.ZMm, u.SpeedMmPerSec,
                    i < 10 ? 1 : 2, weapons[i < 10 ? 0 : 1]);
            }
            return new WorldRecipe(41, original.Mover, units, original.Script.ToArray()).WithCombat(content.BuildCombat());
        }

        public static WorldRecipe M4()
        {
            var units = new UnitSpawn[20];
            for (int i = 0; i < units.Length; i++)
            {
                var offset = WorldBuilder.FormationOffsetMm(i % 10, 10);
                units[i] = new UnitSpawn((ulong)i + 1, 0, i < 10 ? 1 : 2,
                    (i < 10 ? 6000 : 58000) + offset.X,
                    (i < 10 ? 10000 : 8000) + offset.Z, 6000);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units,
                new[] { BridgeCommand.MoveTo(1, 1, 44000, 22000), BridgeCommand.MoveTo(2, 2, 56000, 22000) });
        }

        public static WorldRecipe M3() => new WorldRecipe(41, MoverKind.InterimPath,
            new[] { new UnitSpawn(1, 0, 1, 4000, 12000, 6000), new UnitSpawn(2, 0, 2, 4000, 28000, 6000) },
            new[] { BridgeCommand.MoveTo(1, 1, 60000, 12000), BridgeCommand.MoveTo(2, 2, 60000, 28000) });

        // 64 x 32 m M2 demo map, 3 m/s. Unit 1's lane hits the long wall; unit 2's lane is clear.
        public static WorldRecipe M2b() => new WorldRecipe(41, MoverKind.Interim,
            new[] { new UnitSpawn(1, 0, 1, 4000, 12000, 3000), new UnitSpawn(2, 0, 2, 4000, 28000, 3000) },
            new[] { BridgeCommand.MoveTo(1, 1, 60000, 12000), BridgeCommand.MoveTo(2, 2, 60000, 28000) });
    }
}
