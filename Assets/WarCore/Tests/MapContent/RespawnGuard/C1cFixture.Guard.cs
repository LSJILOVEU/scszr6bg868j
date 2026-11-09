using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.RespawnWorld;

namespace WarCore.Tests
{
    internal static partial class C1c
    {
        // Point A (spawn at its centre (24, 20), radius 2 m), a team 1 base at (5, 38), D = 5, guard G. Capture and AI never act.
        internal static MatchPack Pack(int guard) => new MatchPack(PointA(), BaseA(), 2, 1, false, 8000, InterruptMode.Reset, 1,
            1000000, 1000000, 100000, SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), D, guard);

        internal static SimVec3 Spot => SimVec3.Metres(23, 0, 19);

        internal static bool Holds(M6World w, ulong key) => w.Has<Position>(key) && !w.Dead(key);

        // The hits of one shooter on one target recorded in the tick just stepped.
        internal static List<long> Hits(M6World w, ulong shooter, ulong target)
        {
            var hits = new List<long>();
            ulong s = w.Sim.Entities.StableOf(w.Unit(shooter)).Value, t = w.Sim.Entities.StableOf(w.Unit(target)).Value;
            foreach (FireRecord f in w.Combat.Fires)
                if (f.Shooter.Value == s && f.Target.Value == t && f.Outcome == FireOutcome.Hit) hits.Add(f.DamageRaw);
            return hits;
        }

        // Settlement facts of one tick for one unit: the damage amounts (raw) and the number of FinalLife facts.
        internal static (List<long> damage, int finalLife) Facts(M6World w, ulong key, long tick)
        {
            var damage = new List<long>(); int finalLife = 0;
            StableId id = w.Sim.Entities.StableOf(w.Unit(key));
            int count = w.Sim.Events.Read(tick).Length;
            for (int i = 0; i < count; i++)
            {
                StableSimEvent e = w.Sim.ReadStableEvent(tick, i);
                if (!e.Target.HasValue || !e.Target.Id.Equals(id)) continue;
                if (e.Type.Equals(GameplayEvents.Damage)) damage.Add(e.Payload.Magnitude.Raw);
                if (e.Type.Equals(GameplayEvents.FinalLife)) finalLife++;
            }
            return (damage, finalLife);
        }

        // Melee fixture (3): no guns; one melee spec (windup 3, active 1, recovery 2, radius 2 m, 60 degrees, damage 100 = lethal,
        // engage 1.8 m) and one block spec (60 degrees, stops all). Reserve 3 carries block spec 0; enemy 11 at (21.5, 19) swings.
        internal static CombatPack MeleePack() => new CombatPack(System.Array.Empty<WeaponDef>(), new ZeroResolver(), new NearestVisibleEnemy(),
            100 * SimFix.Scale, false, false, 0, 30000,
            new[] { new MeleeSpec(3, 1, 2, SimFix.FromInt(2), 500, SimFix.FromInt(1), SimFix.FromInt(100), 3, SimFix.Ratio(18, 10)) },
            new[] { new BlockSpec(500, 1000) });

        internal static List<UnitSpawn> MeleeUnits() =>
            Units(new UnitSpawn(Reserve, 0, 1, 0, 0, 1, 1, -1, true, -1, 0), new UnitSpawn(Shooter, 0, 2, 21500, 19000, 1, 2, -1, false, 0));
    }

    // Lethal like LethalResolver, and logs every roll it is asked about (C1c-14a/14b).
    internal sealed class LethalRollLog : IHitResolver
    {
        internal readonly List<int> Rolls = new List<int>();
        public ulong PolicyKey => 0x5445535400000C1CUL;
        public long DamageRaw(in WeaponDef weapon, long falloffRaw, int rollRaw, long maxHealthRaw) { Rolls.Add(rollRaw); return maxHealthRaw; }
    }
}
