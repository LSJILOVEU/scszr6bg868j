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
        // Which point a unit goes for and where at it the unit wants to stand are parts; the rest of a think stays here.
        private readonly AiField field;
        private readonly IAiDispatch dispatch;
        private readonly IAiPlacement placement;
        // The placement's waypoint face, when it has one (found once here, not per think).
        private readonly IAiWaypoint waypoints;
        private readonly IAiEngage engage;
        private int intentCount, current;
        private long decisionTick;
        public SimpleAiSystem(CombatSystem combat, CombatPack weapons, MatchPack pack, CaptureSystem capture, IMoveOrders moves,
            IControlTarget controlled = null, FailedSlotMemory memory = null, TargetMemory contacts = null,
            TargetSearch search = null, TargetSuppress suppress = null, int meleeChaseRange = 0,
            bool fairDecisions = false, ulong decisionSeed = 0, AiParts parts = null)
        {
            this.combat = combat; this.weapons = weapons; this.pack = pack; this.capture = capture; this.moves = moves;
            this.controlled = controlled; this.memory = memory; this.contacts = contacts; this.search = search; this.suppress = suppress;
            this.meleeChaseRange = SimFix.FromInt(meleeChaseRange);
            this.fairDecisions = fairDecisions; seedSalt = AiParts.Mix(decisionSeed); policyDigest = pack.PolicyDigest;
            parts = parts ?? new AiParts();
            slots = new GuardSlots(pack.Points, parts.SlotSpacingRaw, parts.SlotRingRaw);
            if (fairDecisions)
            {
                intents = new Intent[combat.Entities.Capacity]; winners = new int[slots.Total];
                var hash = new StateHash(); hash.Add("battle.ai.fair-decisions.v1"); hash.Add(policyDigest); hash.Add(fairDecisions); hash.Add(decisionSeed);
                policyDigest = hash.Value;
            }
            field = new AiField(pack, capture); dispatch = parts.Dispatch; placement = parts.Placement; engage = parts.Engage;
            waypoints = placement as IAiWaypoint;
            // Fold order is fixed: fair decisions, dispatch, placement, then a slot spacing other than the default, then a slot ring,
            // then engage.
            policyDigest = placement.Fold(dispatch.Fold(policyDigest));
            if (parts.SlotSpacingRaw != GuardSlots.SpacingRaw)
            {
                var hash = new StateHash(); hash.Add("battle.ai.slot-spacing.v1"); hash.Add(policyDigest); hash.Add(parts.SlotSpacingRaw);
                policyDigest = hash.Value;
            }
            if (parts.SlotRingRaw != 0)
            {
                var hash = new StateHash(); hash.Add("battle.ai.slot-ring.v1"); hash.Add(policyDigest); hash.Add(parts.SlotRingRaw);
                policyDigest = hash.Value;
            }
            policyDigest = engage.Fold(policyDigest);
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
            field.Begin(c.Clock.Tick);
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
                if ((c.Clock.Tick + (long)((fairDecisions ? AiParts.Mix(id.Value ^ seedSalt) : id.Value) % (ulong)pack.ThinkInterval)) % pack.ThinkInterval != 0) continue;
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
                var thinker = new AiUnit(id, unit.Team, here);
                // An enemy in sight: the engage part says stand (Hold) or go on to the point below (Advance).
                bool advance = false;
                if (unit.Weapon >= 0 && combat.Pack.Selector.TrySelect(combat, entity, weapons.Weapons[unit.Weapon].FalloffEnd, out EntityId seen))
                {
                    contacts?.Saw(id, seen, c.Clock.Tick);
                    if (engage.Decide(in thinker, combat.Point(seen), field) == AiEngage.Hold) { if (moving) StopUnit(id, here, target); continue; }
                    advance = true;
                }
                // Nothing in sight: a remembered target may be suppressed (fire at where it was last seen) from this tick on.
                if (!advance && suppress != null && unit.Weapon >= 0) suppress.Decide(id, c.Clock.Tick);
                // Nothing in sight: a remembered target comes before the points (go to where it was last seen, stand, give up).
                if (!advance && search != null && unit.Weapon >= 0)
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
                int point = dispatch.Pick(in thinker, field);
                if (point < 0)
                { if (moving) StopUnit(id, here, target); continue; }
                // A waypoint first: walk there without a slot, ordered like a search (only X and Z tell the order).
                if (waypoints != null && waypoints.TryWaypoint(in thinker, point, field, out SimVec3 via) && GoesVia(here, via, point))
                {
                    if (!moving || target.X.Raw != via.X.Raw || target.Z.Raw != via.Z.Raw)
                    { OrderUnit(id, here, moving, target, via, -1); }
                    continue;
                }
                if (moving && KeepsSlot(id, target, point)) continue;
                int slot = Choose(id, placement.Anchor(in thinker, point, field), point, c.Clock.Tick);
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
        // Stateless: the unit goes via the waypoint while it is farther from the centre than the waypoint is. A waypoint
        // that is not standable, or that falls on a slot coordinate (it would read as a slot order), is skipped.
        private bool GoesVia(SimVec3 here, SimVec3 via, int point)
        {
            SimVec3 center = pack.Points[point].Center;
            return CombatSystem.Horizontal(here - center).LengthSquared > CombatSystem.Horizontal(via - center).LengthSquared
                && !slots.TryFind(via, -1, out _) && moves.CanStandAt(via);
        }
        private bool KeepsSlot(StableId id, SimVec3 target, int point) =>
            slots.TryFind(target, point, out int slot) && Available(slot, id) && moves.CanStandAt(target);
        // The available standable slot nearest to the anchor, ties to the lower slot number. A full circle returns -1: no
        // order, no stacking. A slot the memory says failed for this unit recently is skipped.
        private int Choose(StableId id, SimVec3 anchor, int point, long tick)
        {
            int chosen = -1; SimWide best = default;
            int first = slots.Start(point), end = first + slots.Count(point);
            for (int slot = first; slot < end; slot++)
            {
                if (!Available(slot, id)) continue;
                if (memory != null && memory.Skips(id, slots.Canonical(slot), tick)) continue;
                SimWide distance = CombatSystem.Horizontal(anchor - slots.At(slot)).LengthSquared;
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
        private ulong Priority(ulong id) => AiParts.Mix(id ^ seedSalt ^ unchecked((ulong)decisionTick * 0x9e3779b97f4a7c15UL));
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
