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
        private readonly IControlTarget controlled;
        private readonly FailedSlotMemory memory;
        private readonly GuardSlots slots;
        // Rebuilt every Step, never persisted: who reserves a slot (active order to it) and who stands at it.
        private readonly bool[] reservedHas, standingHas;
        private readonly ulong[] reservedBy, standingBy;
        public SimpleAiSystem(CombatSystem combat, CombatPack weapons, MatchPack pack, CaptureSystem capture, IMoveOrders moves,
            IControlTarget controlled = null, FailedSlotMemory memory = null)
        {
            this.combat = combat; this.weapons = weapons; this.pack = pack; this.capture = capture; this.moves = moves;
            this.controlled = controlled; this.memory = memory;
            slots = new GuardSlots(pack.Points);
            reservedHas = new bool[slots.Total]; standingHas = new bool[slots.Total];
            reservedBy = new ulong[slots.Total]; standingBy = new ulong[slots.Total];
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
        }
        public int Order => 12;
        public string StateTypeId => "battle.ai";
        public int StateVersion => 3;
        public void Step(SimContext c)
        {
            Rebuild();
            foreach (EntityId entity in stage.Capture())
            {
                if (controlled != null && entity.Equals(controlled.Current)) { Forget(entity); continue; }
                if (!combat.Entities.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1 || unit.Team > 2
                    || !combat.Entities.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) { Forget(entity); continue; }
                StableId id = combat.Entities.StableOf(entity);
                if (moves.IsCommanded(id)) { memory?.Forget(id); continue; }
                memory?.Observe(id, c.Input);
                SimVec3 here = position.Value;
                bool moving = moves.TryGetOrder(id, out SimVec3 target);
                if (moving && Arrived(here, target) && slots.TryFind(target, -1, out _))
                { StopUnit(id, here, target); moving = false; }
                if ((c.Clock.Tick + (long)(id.Value % (ulong)pack.ThinkInterval)) % pack.ThinkInterval != 0) continue;
                if (unit.Weapon >= 0 && combat.Pack.Selector.TrySelect(combat, entity, weapons.Weapons[unit.Weapon].FalloffEnd, out _))
                { if (moving) StopUnit(id, here, target); continue; }
                int nearest = -1; SimWide best = default;
                for (int i = 0; i < pack.Points.Length; i++)
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
                { OrderUnit(id, here, moving, target, destination); memory?.Ordered(id, slots.Canonical(slot)); }
            }
        }
        private void Forget(EntityId entity) { if (memory != null) memory.Forget(combat.Entities.StableOf(entity)); }
        private void Rebuild()
        {
            Array.Clear(reservedHas, 0, reservedHas.Length); Array.Clear(standingHas, 0, standingHas.Length);
            foreach (EntityId entity in stage.Capture())
            {
                if (!combat.Entities.TryGet(combatants, entity, out Combatant _) || !combat.Entities.TryGet(positions, entity, out Position position)
                    || combat.IsDead(entity)) continue;
                StableId id = combat.Entities.StableOf(entity);
                if (moves.TryGetOrder(id, out SimVec3 target) && slots.TryFind(target, -1, out int reserved)) Claim(reservedHas, reservedBy, reserved, id);
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
        private void OrderUnit(StableId id, SimVec3 here, bool moving, SimVec3 oldTarget, SimVec3 destination)
        {
            if (moving && slots.TryFind(oldTarget, -1, out int old)) Release(reservedHas, reservedBy, old, id);
            Release(standingHas, standingBy, slots.Nearest(here), id);
            moves.Order(id, destination);
            if (slots.TryFind(destination, -1, out int slot)) Claim(reservedHas, reservedBy, slot, id);
        }
        private void StopUnit(StableId id, SimVec3 here, SimVec3 oldTarget)
        {
            if (slots.TryFind(oldTarget, -1, out int old)) Release(reservedHas, reservedBy, old, id);
            moves.Stop(id);
            memory?.Forget(id);
            int standing = slots.Nearest(here);
            if (standing >= 0) Claim(standingHas, standingBy, standing, id);
        }
        // The lowest stable identity holds a contested record.
        private static void Claim(bool[] has, ulong[] by, int record, StableId id)
        { if (!has[record] || by[record] > id.Value) { has[record] = true; by[record] = id.Value; } }
        private static void Release(bool[] has, ulong[] by, int record, StableId id)
        { if (record >= 0 && has[record] && by[record] == id.Value) has[record] = false; }
        // PLACEHOLDER: a quarter of the body-overlap distance of two default capsules (0.5 m), so two units stopped at
        // neighbouring slots (1 m apart) never overlap. Tied to the lattice spacing.
        private static readonly SimWide ArrivalSquared = new SimVec3(SimFix.Ratio(1, 4), default, default).LengthSquared;
        private static bool Arrived(SimVec3 position, SimVec3 destination) =>
            CombatSystem.Horizontal(position - destination).LengthSquared <= ArrivalSquared;
        public void SaveState(SimStateWriter writer) { writer.UInt64(pack.PolicyDigest); writer.UInt64(weapons.PolicyDigest); }
        public void LoadState(SimStateReader reader)
        { pack.Check(reader); SimStateReader.Require(reader.UInt64() == weapons.PolicyDigest, "AI combat configuration differs."); }
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add(weapons.PolicyDigest); }
    }
}
