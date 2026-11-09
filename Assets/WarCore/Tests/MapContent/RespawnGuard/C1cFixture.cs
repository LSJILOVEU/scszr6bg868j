using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    // C1c fixtures: the C1 respawn world (RespawnWorld) with a lethal gun. Everything is written here; the match pack is built
    // literally (no shipped default). K = 1, D = 5: the switch happens on tick 6 and the new unit stands at (23, 19).
    internal static partial class C1c
    {
        internal const int K = 1, D = 5, Switch = K + D;
        internal const ulong Reserve = 3, Reserve2 = 4, Shooter = 11, Shooter2 = 12;

        // One gun (index 0): a shot every tick, 30 rounds, falloff 10..20 m (range 20 m), no spread; every hit is lethal.
        internal static CombatPack Lethal(IHitResolver resolver = null) =>
            new CombatPack(new[] { new WeaponDef(1000, 30, 60000, SimFix.FromInt(10), SimFix.FromInt(20), 32768) },
                resolver ?? new LethalResolver(), new NearestVisibleEnemy(), 100 * SimFix.Scale);

        // Shooter 11 (team 2, gun 0) at (40, 20): 17.03 m from the new unit at (23, 19), 26.9 m from the ally at (20, 38), 20 m from the player.
        internal static List<UnitSpawn> ShooterUnits(params UnitSpawn[] extra)
        {
            var units = Units(U(Reserve, 0, 0, reserve: true), U(Shooter, 40, 20, team: 2, weapon: 0));
            units.AddRange(extra);
            return units;
        }

        internal static ControlPointDef[] PointA() => Points(Spawn(1, 24, 20));
        internal static SpawnAnchor[] BaseA() => Bases(new SpawnAnchor(1, P(5, 38)));

        internal static WorldRecipe Recipe(IEnumerable<UnitSpawn> units, MatchPack pack, CombatPack combat) =>
            new WorldRecipe(Seed, MoverKind.InterimCrowd, new List<UnitSpawn>(units).ToArray(), System.Array.Empty<BridgeCommand>())
                .WithMatch(pack).WithCombat(combat).WithCrowdPass(CrowdPassKind.TeamIdle).WithPlayer(Player);

        internal static M6World World(IEnumerable<UnitSpawn> units, MatchPack pack, CombatPack combat = null, long[] kills = null, bool reverse = false)
        {
            var w = new M6World(Recipe(units, pack, combat ?? Lethal()), reverse: reverse);
            w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, kills ?? new long[] { K }), SimSystemPhase.Queries);
            return w;
        }

        internal static ulong Fnv(ulong fold, ulong value) => (fold ^ value) * 1099511628211UL;

        internal static ulong FoldFires(ulong fold, M6World w)
        {
            foreach (FireRecord f in w.Combat.Fires)
            {
                fold = Fnv(fold, f.Shooter.Value); fold = Fnv(fold, f.Target.Value); fold = Fnv(fold, (ulong)f.DamageRaw);
                fold = Fnv(fold, (ulong)f.Outcome); fold = Fnv(fold, (ulong)f.End.X.Raw); fold = Fnv(fold, (ulong)f.End.Z.Raw);
            }
            return fold;
        }

        internal static string Sha256(byte[] bytes)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return System.BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
        }
    }
}
