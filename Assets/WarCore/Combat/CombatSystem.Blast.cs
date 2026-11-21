using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // BLAST-1: the combat side of explosions. BlastSystem owns the queue; how one blast lands is judged here, with combat's own
    // sight line, body centre and damage guard. SubmitDamage is the damage entry point that later packages route every hit through.
    public sealed partial class CombatSystem
    {
        private BlastSystem blastSystem;
        // Scratch for one blast: the sphere query's hits, then the units by StableId. Sized like the combat candidates.
        private SpatialHit[] blastHits;
        private ulong[] blastOrder;
        private EntityId[] blastUnits;

        // A blast point is lifted this much before its line to a body centre is tested, so a blast lying on the ground does not
        // graze the ground itself (0.25 m).
        internal const long BlastLiftRaw = SimFix.Scale / 4;

        // The key's action ordinal of a blast hit: shots 0, melee 1, projectiles 2.
        internal const uint BlastActionOrdinal = 3;

        // Bound once by the BlastSystem constructor; a restored world binds a fresh one when it is assembled.
        internal void AttachBlasts(BlastSystem system)
        {
            if (!pack.HasBlasts) throw new InvalidOperationException("The combat pack has no blasts.");
            if (blastSystem != null) throw new InvalidOperationException("A blast system is already attached.");
            blastSystem = system;
            blastHits = new SpatialHit[candidates.Length];
            blastOrder = new ulong[candidates.Length];
            blastUnits = new EntityId[candidates.Length];
        }

        public BlastSystem Blasts => blastSystem;

        // One blast of the given kind at point (BLAST-1 C3). Units within the outer radius of the sphere query, in StableId
        // order and each once; skipped: dead, team 0, and the source's team when the kind is not friendly. Distance: 3D from the
        // point to the body centre; at or beyond the outer radius nothing. With walls, a blocked line from the point lifted
        // 0.25 m to the body centre stops it. A damage of 0 issues nothing. Writes each hit into the arrays from offset on and
        // returns how many it wrote. No random numbers are drawn.
        internal int Detonate(SimContext c, in BlastSpec spec, SimVec3 point, StableId source, int team, long serial, long tick,
            StableId[] victims, long[] damages, long[] distances, int offset)
        {
            var result = sim.SpatialQuery.AllCapped(SpatialQueries.Sphere(point, SimFix.FromRaw(spec.OuterRaw), unitLayerMask), blastHits.Length, blastHits);
            if (result.Truncated) throw new InvalidOperationException("Combat unit capacity exceeded; a blast's victims would be incomplete.");
            int n = result.Written;
            for (int i = 0; i < n; i++)
            {
                blastUnits[i] = blastHits[i].Entity;
                blastOrder[i] = Entities.StableOf(blastUnits[i]).Value;
            }
            Array.Sort(blastOrder, blastUnits, 0, n);
            var key = new GameplayApplyKey(source, (ulong)serial, 0, source, (ulong)serial, BlastActionOrdinal);
            SimVec3 from = Lift(point, BlastLiftRaw);
            int written = 0;
            for (int i = 0; i < n; i++)
            {
                if (i > 0 && blastOrder[i] == blastOrder[i - 1]) continue;
                EntityId entity = blastUnits[i];
                if (!Entities.TryGet(combatants, entity, out Combatant unit) || unit.Team == 0 || IsDead(entity)) continue;
                if (!spec.Friendly && unit.Team == team) continue;
                SimVec3 centre = Center(Point(entity));
                long distance = (centre - point).Length.Raw;
                long damage = spec.DamageAt(distance);
                if (damage == 0) continue;
                if (spec.Walls && !LineClear(from, centre)) continue;
                StableId victim = Entities.StableOf(entity);
                long applied = SubmitDamage(c, victim, key, damage, tick);
                victims[offset + written] = victim; damages[offset + written] = applied; distances[offset + written] = distance;
                written++;
            }
            return written;
        }

        // The one damage entry of the combat layer (BLAST-1 C3.7): zero while the victim is shielded, then the damage command.
        // Only blasts use it so far; the other damage sites move here with the package that rewrites damage (downed, gear).
        internal long SubmitDamage(SimContext c, StableId victim, GameplayApplyKey key, long damageRaw, long tick)
        {
            if (Shielded(victim, tick)) damageRaw = 0;
            c.Damage(sim.GameplayTarget(victim), key, 1, damageRaw);
            return damageRaw;
        }
    }
}
