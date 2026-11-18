using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    public sealed class SimpleAiSystem : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly CombatPack weapons;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly IMoveOrders moves;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<MeleeState> meleeStates;
        private readonly SimFix meleeChaseRange;
        private readonly IControlTarget controlled;
        private readonly FailedSlotMemory memory;
        private readonly TargetMemory contacts;
        private readonly TargetSearch search;
        private readonly TargetSuppress suppress;
        private readonly GuardSlots slots;
        // Rebuilt every Step, never persisted: who reserves a slot (active order to it) and who stands at it.
        private readonly bool[] reservedHas, standingHas;
        private readonly ulong[] reservedBy, standingBy;
        private struct Intent
        {
            internal StableId Id;
            internal SimVec3 Destination;
            internal int Kind, Slot, MemorySlot, Standing;
        }
        private readonly bool fairDecisions;
        private readonly ulong seedSalt, policyDigest;
        private readonly Intent[] intents;
        private readonly int[] winners;
        private readonly int holdSharePermille, attackSpreadPoints;
        private int intentCount, current, frontline1, frontline2;
        // Each team's first spawn anchor, the spread's reference when the team has no front line.
        private readonly bool hasBase1, hasBase2;
        private readonly SimVec3 base1, base2;
        private long decisionTick;
        public SimpleAiSystem(CombatSystem combat, CombatPack weapons, MatchPack pack, CaptureSystem capture, IMoveOrders moves,
            IControlTarget controlled = null, FailedSlotMemory memory = null, TargetMemory contacts = null,
            TargetSearch search = null, TargetSuppress suppress = null, int meleeChaseRange = 0,
            bool fairDecisions = false, ulong decisionSeed = 0, int holdSharePermille = 0, int attackSpreadPoints = 1)
        {
            this.combat = combat; this.weapons = weapons; this.pack = pack; this.capture = capture; this.moves = moves;
            this.controlled = controlled; this.memory = memory; this.contacts = contacts; this.search = search; this.suppress = suppress;
            this.meleeChaseRange = SimFix.FromInt(meleeChaseRange);
            this.fairDecisions = fairDecisions; seedSalt = Mix(decisionSeed); policyDigest = pack.PolicyDigest;
            slots = new GuardSlots(pack.Points);
            if (fairDecisions)
            {
                intents = new Intent[combat.Entities.Capacity]; winners = new int[slots.Total];
                var hash = new StateHash(); hash.Add("battle.ai.fair-decisions.v1"); hash.Add(policyDigest); hash.Add(fairDecisions); hash.Add(decisionSeed);
                policyDigest = hash.Value;
            }
            this.holdSharePermille = holdSharePermille;
            if (holdSharePermille > 0)
            {
                var hash = new StateHash(); hash.Add("battle.ai.hold-share.v1"); hash.Add(policyDigest); hash.Add(holdSharePermille);
                policyDigest = hash.Value;
            }
            this.attackSpreadPoints = attackSpreadPoints;
            foreach (SpawnAnchor anchor in pack.Bases)
            {
                if (anchor.Team == 1 && !hasBase1) { hasBase1 = true; base1 = anchor.Point; }
                if (anchor.Team == 2 && !hasBase2) { hasBase2 = true; base2 = anchor.Point; }
            }
            if (attackSpreadPoints > 1)
            {
                var hash = new StateHash(); hash.Add("battle.ai.attack-spread.v1"); hash.Add(policyDigest); hash.Add(attackSpreadPoints);
                policyDigest = hash.Value;
            }
            reservedHas = new bool[slots.Total]; standingHas = new bool[slots.Total];
            reservedBy = new ulong[slots.Total]; standingBy = new ulong[slots.Total];
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            if (weapons.HasMelee) meleeStates = combat.Entities.Resolve<MeleeState>();
        }
        public int Order => 12;
        public string StateTypeId => "battle.ai";
        public int StateVersion => fairDecisions ? 4 : 3;
        public void Step(SimContext c)
        {
            decisionTick = c.Clock.Tick; intentCount = 0;
            if (holdSharePermille > 0 || attackSpreadPoints > 1) { frontline1 = Frontline(1); frontline2 = Frontline(2); }
            Rebuild();
            foreach (EntityId entity in stage.Capture())
            {
                if (controlled != null && entity.Equals(controlled.Current)) { Forget(entity); continue; }
                if (!combat.Entities.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1 || unit.Team > 2
                    || !combat.Entities.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) { Forget(entity); continue; }
                StableId id = combat.Entities.StableOf(entity);
                if (moves.IsCommanded(id)) { memory?.Forget(id); continue; }
                if (fairDecisions) { current = intentCount++; intents[current] = new Intent { Id = id }; }
                memory?.Observe(id, c.Input);
                SimVec3 here = position.Value;
                bool moving = moves.TryGetOrder(id, out SimVec3 target);
                if (fairDecisions && moving && slots.TryFind(target, -1, out int retained))
                { intents[current] = new Intent { Id = id, Kind = 3, Slot = retained, Standing = slots.Nearest(here) }; }
                if (moving && Arrived(here, target) && slots.TryFind(target, -1, out _))
                { StopUnit(id, here, target); moving = false; }
                if ((c.Clock.Tick + (long)((fairDecisions ? Mix(id.Value ^ seedSalt) : id.Value) % (ulong)pack.ThinkInterval)) % pack.ThinkInterval != 0) continue;
                if (meleeChaseRange.Raw > 0 && unit.Weapon < 0 && weapons.HasMelee
                    && combat.Entities.TryGet(meleeStates, entity, out MeleeState melee) && melee.Spec >= 0
                    && TryMeleeTarget(entity, here, out SimVec3 enemy))
                {
                    SimFix engage = weapons.Melee[melee.Spec].EngageRange;
                    if (CombatSystem.Horizontal(enemy - here).LengthSquared <= new SimVec3(engage, default, default).LengthSquared)
                    { if (moving) StopUnit(id, here, target); }
                    else if (!moving || target.X.Raw != enemy.X.Raw || target.Z.Raw != enemy.Z.Raw)
                    { OrderUnit(id, here, moving, target, enemy, -2); }
                    continue;
                }
                if (unit.Weapon >= 0 && combat.Pack.Selector.TrySelect(combat, entity, weapons.Weapons[unit.Weapon].FalloffEnd, out EntityId seen))
                { contacts?.Saw(id, seen, c.Clock.Tick); if (moving) StopUnit(id, here, target); continue; }
                // Nothing in sight: a remembered target may be suppressed (fire at where it was last seen) from this tick on.
                if (suppress != null && unit.Weapon >= 0) suppress.Decide(id, c.Clock.Tick);
                // Nothing in sight: a remembered target comes before the points (go to where it was last seen, stand, give up).
                if (search != null && unit.Weapon >= 0)
                {
                    TargetSearch.Hunt hunt = search.Decide(id, here, c.Clock.Tick, out SimVec3 lastKnown);
                    if (hunt == TargetSearch.Hunt.Go)
                    {
                        // Orders are grounded (only Y changes), so X and Z tell whether the unit is already on its way.
                        if (!moving || target.X.Raw != lastKnown.X.Raw || target.Z.Raw != lastKnown.Z.Raw)
                        { OrderUnit(id, here, moving, target, lastKnown, -1); }
                        continue;
                    }
                    if (hunt == TargetSearch.Hunt.Hold)
                    { if (moving) StopUnit(id, here, target); continue; }
                }
                // A holder goes to its team's front-line point; without one it attacks like the rest.
                int nearest = holdSharePermille > 0 && Holds(id.Value, holdSharePermille) ? (unit.Team == 1 ? frontline1 : frontline2) : -1;
                bool attack = nearest < 0; SimWide best = default;
                if (attack && attackSpreadPoints > 1) { nearest = SpreadTarget(unit.Team, here, id.Value); attack = false; }
                for (int i = 0; attack && i < pack.Points.Length; i++)
                {
                    if (capture.Points[i].Owner == unit.Team) continue;
                    SimWide distance = CombatSystem.Horizontal(here - pack.Points[i].Center).LengthSquared;
                    if (nearest < 0 || distance < best) { nearest = i; best = distance; }
                }
                if (nearest < 0)
                { if (moving) StopUnit(id, here, target); continue; }
                if (moving && KeepsSlot(id, target, nearest)) continue;
                int slot = Choose(id, here, nearest, c.Clock.Tick);
                if (slot < 0)
                { if (moving) StopUnit(id, here, target); continue; }
                SimVec3 destination = slots.At(slot);
                if (Arrived(here, destination))
                { if (moving) StopUnit(id, here, target); continue; }
                if (!moving || !target.Equals(destination))
                { OrderUnit(id, here, moving, target, destination, slots.Canonical(slot)); }
            }
            if (fairDecisions) ApplyIntents();
        }
        private bool TryMeleeTarget(EntityId entity, SimVec3 here, out SimVec3 target)
        {
            target = default;
            bool found = false; SimWide best = default; StableId bestId = default;
            SimWide limit = new SimVec3(meleeChaseRange, default, default).LengthSquared;
            foreach (var hit in combat.Candidates(entity, meleeChaseRange))
            {
                EntityId candidate = hit.Entity;
                if (!combat.IsEnemy(entity, candidate)) continue;
                SimVec3 at = combat.Point(candidate);
                SimWide distance = CombatSystem.Horizontal(at - here).LengthSquared;
                if (distance > limit) continue;
                StableId id = combat.Entities.StableOf(candidate);
                if (found && (distance > best || (distance == best && id.CompareTo(bestId) >= 0))) continue;
                if (!combat.Visible(entity, candidate)) continue;
                target = at; best = distance; bestId = id; found = true;
            }
            return found;
        }
        private void Forget(EntityId entity)
        {
            if (memory == null && contacts == null) return;
            StableId id = combat.Entities.StableOf(entity);
            memory?.Forget(id); contacts?.Forget(id);
        }
        private void Rebuild()
        {
            Array.Clear(reservedHas, 0, reservedHas.Length); Array.Clear(standingHas, 0, standingHas.Length);
            if (fairDecisions) Array.Clear(winners, 0, winners.Length);
            foreach (EntityId entity in stage.Capture())
            {
                if (!combat.Entities.TryGet(combatants, entity, out Combatant _) || !combat.Entities.TryGet(positions, entity, out Position position)
                    || combat.IsDead(entity)) continue;
                StableId id = combat.Entities.StableOf(entity);
                if (moves.TryGetOrder(id, out SimVec3 target) && slots.TryFind(target, -1, out int reserved))
                {
                    Claim(reservedHas, reservedBy, reserved, id);
                    if (fairDecisions && moves.IsCommanded(id)) winners[reserved] = -1;
                }
                else
                {
                    int standing = slots.Nearest(position.Value);
                    if (standing >= 0) Claim(standingHas, standingBy, standing, id);
                }
            }
        }
        private bool Available(int slot, StableId id)
        {
            int record = slots.Canonical(slot);
            return (!reservedHas[record] || reservedBy[record] == id.Value) && (!standingHas[record] || standingBy[record] == id.Value);
        }
        private bool KeepsSlot(StableId id, SimVec3 target, int point) =>
            slots.TryFind(target, point, out int slot) && Available(slot, id) && moves.CanStandAt(target);
        // Nearest available standable slot, ties to the lower slot number. A full circle returns -1: no order, no stacking.
        // A slot the memory says failed for this unit recently is skipped.
        private int Choose(StableId id, SimVec3 here, int point, long tick)
        {
            int chosen = -1; SimWide best = default;
            int first = slots.Start(point), end = first + slots.Count(point);
            for (int slot = first; slot < end; slot++)
            {
                if (!Available(slot, id)) continue;
                if (memory != null && memory.Skips(id, slots.Canonical(slot), tick)) continue;
                SimWide distance = CombatSystem.Horizontal(here - slots.At(slot)).LengthSquared;
                if (chosen >= 0 && distance >= best) continue;
                if (!moves.CanStandAt(slots.At(slot))) continue;
                chosen = slot; best = distance;
            }
            return chosen;
        }
        private void OrderUnit(StableId id, SimVec3 here, bool moving, SimVec3 oldTarget, SimVec3 destination, int memorySlot)
        {
            if (fairDecisions)
            {
                intents[current] = new Intent { Id = id, Kind = 2, Destination = destination,
                    Slot = slots.TryFind(destination, -1, out int proposed) ? proposed : -1, MemorySlot = memorySlot, Standing = slots.Nearest(here) };
                return;
            }
            if (moving && slots.TryFind(oldTarget, -1, out int old)) Release(reservedHas, reservedBy, old, id);
            Release(standingHas, standingBy, slots.Nearest(here), id);
            moves.Order(id, destination);
            if (slots.TryFind(destination, -1, out int slot)) Claim(reservedHas, reservedBy, slot, id);
            if (memorySlot == -2) memory?.Forget(id); else memory?.Ordered(id, memorySlot);
        }
        private void StopUnit(StableId id, SimVec3 here, SimVec3 oldTarget)
        {
            if (fairDecisions) { intents[current] = new Intent { Id = id, Kind = 1, Slot = slots.Nearest(here) }; memory?.Forget(id); return; }
            if (slots.TryFind(oldTarget, -1, out int old)) Release(reservedHas, reservedBy, old, id);
            moves.Stop(id);
            memory?.Forget(id);
            int standing = slots.Nearest(here);
            if (standing >= 0) Claim(standingHas, standingBy, standing, id);
        }
        private void ApplyIntents()
        {
            for (int i = 0; i < intentCount; i++)
                if (intents[i].Kind == 1 && intents[i].Slot >= 0) winners[intents[i].Slot] = -1;
            for (int i = 0; i < intentCount; i++)
            {
                Intent proposal = intents[i];
                if ((proposal.Kind != 2 && proposal.Kind != 3) || proposal.Slot < 0) continue;
                if (proposal.Kind == 2 && proposal.MemorySlot == -1 && standingHas[proposal.Slot] && standingBy[proposal.Slot] != proposal.Id.Value) continue;
                if (proposal.Kind == 3 && standingHas[proposal.Slot] && combat.IsFriend(combat.Entities.Resolve(new EntityLink(proposal.Id)),
                    combat.Entities.Resolve(new EntityLink(new StableId(standingBy[proposal.Slot]))))) continue;
                if (winners[proposal.Slot] < 0) continue;
                int incumbent = winners[proposal.Slot] - 1;
                if (incumbent < 0 || Precedes(proposal.Id.Value, intents[incumbent].Id.Value)) winners[proposal.Slot] = i + 1;
            }
            bool changed;
            do
            {
                changed = false;
                for (int i = 0; i < intentCount; i++)
                {
                    Intent proposal = intents[i];
                    if ((proposal.Kind != 2 && proposal.Kind != 3) || proposal.Slot < 0 || winners[proposal.Slot] == i + 1) continue;
                    if (proposal.Standing < 0 || winners[proposal.Standing] < 0) continue;
                    winners[proposal.Standing] = -1; changed = true;
                }
                if (changed) continue;
                for (int i = 0; i < intentCount; i++)
                {
                    Intent proposal = intents[i];
                    if (proposal.Kind != 2 || (proposal.Slot >= 0 && winners[proposal.Slot] != i + 1)) continue;
                    moves.Order(proposal.Id, proposal.Destination);
                    if (proposal.MemorySlot == -2) memory?.Forget(proposal.Id); else memory?.Ordered(proposal.Id, proposal.MemorySlot);
                    intents[i].Kind = proposal.Slot < 0 ? 0 : 3;
                    if (moves.TryGetOrder(proposal.Id, out _) || proposal.Standing < 0) continue;
                    intents[i].Kind = 0;
                    changed = true; winners[proposal.Standing] = -1;
                }
            } while (changed);
            for (int i = 0; i < intentCount; i++)
            {
                Intent proposal = intents[i];
                if (proposal.Kind == 0 || (proposal.Kind == 3 && winners[proposal.Slot] == i + 1)) continue;
                moves.Stop(proposal.Id); memory?.Forget(proposal.Id);
            }
        }
        private bool Precedes(ulong a, ulong b)
        {
            if (!fairDecisions) return a < b;
            ulong x = Priority(a), y = Priority(b);
            return x < y || (x == y && a < b);
        }
        // Stateless split by unit key: the same unit is a holder on every tick and after a restore.
        private const ulong HoldSalt = 0x5a17c0de0b5e55edUL;
        private static bool Holds(ulong id, int permille) => Mix(id ^ HoldSalt) % 1000UL < (ulong)permille;
        // The team's owned point closest to any point it does not own (ties to the lower point index); -1 when it owns none or all.
        private int Frontline(int team)
        {
            int front = -1; SimWide best = default;
            for (int i = 0; i < pack.Points.Length; i++)
            {
                if (capture.Points[i].Owner != team) continue;
                for (int j = 0; j < pack.Points.Length; j++)
                {
                    if (capture.Points[j].Owner == team) continue;
                    SimWide distance = CombatSystem.Horizontal(pack.Points[i].Center - pack.Points[j].Center).LengthSquared;
                    if (front < 0 || distance < best) { front = i; best = distance; }
                }
            }
            return front;
        }
        // Stateless spread by unit key: rank k = Mix(key ^ salt) % min(spread, points not owned); the k-th nearest such point
        // (0 = nearest; equal distances to the lower point index); -1 when the team owns every point. Distances are measured
        // from the team's front-line point, so the choice holds while the unit walks and changes only when a point flips;
        // a team without a front line measures from its first spawn anchor, and without one from the unit itself.
        private const ulong SpreadSalt = 0x3c6ef372fe94f82bUL;
        private int SpreadTarget(int team, SimVec3 here, ulong key)
        {
            int front = team == 1 ? frontline1 : frontline2;
            SimVec3 from = front >= 0 ? pack.Points[front].Center : team == 1 ? (hasBase1 ? base1 : here) : (hasBase2 ? base2 : here);
            int count = 0;
            for (int i = 0; i < pack.Points.Length; i++) if (capture.Points[i].Owner != team) count++;
            if (count == 0) return -1;
            int rank = (int)(Mix(key ^ SpreadSalt) % (ulong)Math.Min(attackSpreadPoints, count));
            int last = -1; SimWide lastDistance = default;
            for (int r = 0; r <= rank; r++)
            {
                int pick = -1; SimWide best = default;
                for (int i = 0; i < pack.Points.Length; i++)
                {
                    if (capture.Points[i].Owner == team) continue;
                    SimWide distance = CombatSystem.Horizontal(from - pack.Points[i].Center).LengthSquared;
                    if (last >= 0 && (distance < lastDistance || (distance == lastDistance && i <= last))) continue;
                    if (pick < 0 || distance < best) { pick = i; best = distance; }
                }
                last = pick; lastDistance = best;
            }
            return last;
        }
        private ulong Priority(ulong id) => Mix(id ^ seedSalt ^ unchecked((ulong)decisionTick * 0x9e3779b97f4a7c15UL));
        private static ulong Mix(ulong x)
        {
            unchecked
            {
                x = (x ^ (x >> 30)) * 0xbf58476d1ce4e5b9UL;
                x = (x ^ (x >> 27)) * 0x94d049bb133111ebUL;
                return x ^ (x >> 31);
            }
        }
        private void Claim(bool[] has, ulong[] by, int record, StableId id)
        { if (!has[record] || Precedes(id.Value, by[record])) { has[record] = true; by[record] = id.Value; } }
        private static void Release(bool[] has, ulong[] by, int record, StableId id)
        { if (record >= 0 && has[record] && by[record] == id.Value) has[record] = false; }
        // PLACEHOLDER: a quarter of the body-overlap distance of two default capsules (0.5 m), so two units stopped at
        // neighbouring slots (1 m apart) never overlap. Tied to the lattice spacing.
        private static readonly SimWide ArrivalSquared = new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared;
        internal static bool Arrived(SimVec3 position, SimVec3 destination) =>
            CombatSystem.Horizontal(position - destination).LengthSquared <= ArrivalSquared;
        public void SaveState(SimStateWriter writer) { writer.UInt64(policyDigest); writer.UInt64(weapons.PolicyDigest); }
        public void LoadState(SimStateReader reader)
        { if (reader.UInt64() != policyDigest) throw new InvalidOperationException("Battle configuration differs from snapshot."); SimStateReader.Require(reader.UInt64() == weapons.PolicyDigest, "AI combat configuration differs."); }
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(policyDigest); hash.Add(weapons.PolicyDigest); }
    }
}
