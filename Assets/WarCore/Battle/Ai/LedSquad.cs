using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // RC2-1: the two numbers of a led squad (both or none in a content pack): how many soldiers the controlled unit may lead,
    // and from how far away they join.
    public readonly struct LedRules
    {
        // The formation table holds complete lattice rings only up to six of them (GuardSlots.Build), which is 126 places;
        // a squad keeps four places per member so that a blocked place has a substitute.
        public const int MaxSize = 30;
        public readonly int Size, RecruitRadiusMm;
        public LedRules(int size, int recruitRadiusMm)
        {
            if (size < 1 || size > MaxSize) throw new ArgumentOutOfRangeException(nameof(size));
            if (recruitRadiusMm < 1) throw new ArgumentOutOfRangeException(nameof(recruitRadiusMm));
            Size = size; RecruitRadiusMm = recruitRadiusMm;
        }
    }

    public enum LedMode { None, Follow, Hold }

    /// <summary>RC2-1: the squad the controlled unit leads. A command addressed to SquadCommand.Led makes the controlled unit
    /// a leader: the nearest team mates join, each gets a place in a formation around an anchor, and this system keeps
    /// walking them to their places. Follow moves the anchor with the leader, Hold fixes it where the leader stands, MoveTo
    /// fixes it at the commanded point, Dismiss breaks the squad up. A member counts as commanded (IMoveOrders.IsCommanded
    /// through the bridge), so the AI squads and the AI scheduler leave it alone; it still fires and swings on its own.
    /// Deterministic and allocation-free after construction. Saved: leader, mode, anchor, and per member its key, its
    /// spot (fixed when it joins) and whether its last walk order was refused. The places are derived.</summary>
    public sealed class LedSquad : IRetainedGameplaySystem
    {
        // Places kept per member: its own and three spares for when a place cannot be used.
        internal const int PlacesPerMember = 4;
        private const ulong RecruitSalt = 0x6c65642d73717561UL;

        private readonly CombatSystem combat;
        private readonly IMoveOrders moves;
        private readonly IControlTarget controlled;
        private readonly LedRules rules;
        private readonly long spacingRaw;
        private readonly IUnitLayers layers;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly SimWide recruitSquared, shiftSquared;
        // The formation: lattice offsets from the anchor, ring by ring. A member's spot is an index below rules.Size.
        private readonly SimVec3[] offsets;
        // Saved. Members are in key order.
        private ulong leader;
        private LedMode mode;
        private SimVec3 anchor;
        private int anchorLayer, count;
        private readonly ulong[] members;
        private readonly int[] spots;
        private readonly bool[] refused;
        // Derived from the saved state and the static world: where each member stands, if anywhere.
        private readonly bool[] placed, taken;
        private readonly SimVec3[] places;
        private bool dirty, restored;

        public LedSquad(CombatSystem combat, IMoveOrders moves, IControlTarget controlled, LedRules rules, long spacingRaw, IUnitLayers layers = null)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
            this.controlled = controlled ?? throw new ArgumentNullException(nameof(controlled));
            this.rules = rules; this.spacingRaw = spacingRaw; this.layers = layers;
            // Formation refuses rules that were never set (a size of 0) and a spacing that is not positive.
            offsets = Formation(rules.Size, spacingRaw);
            stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            recruitSquared = new SimVec3(SimFix.Ratio(rules.RecruitRadiusMm, 1000), default, default).LengthSquared;
            shiftSquared = new SimVec3(SimFix.FromRaw(spacingRaw / 2), default, default).LengthSquared;
            members = new ulong[rules.Size]; spots = new int[rules.Size]; refused = new bool[rules.Size];
            placed = new bool[rules.Size]; places = new SimVec3[rules.Size]; taken = new bool[offsets.Length];
        }

        /// <summary>The offsets of a formation for that many members: the lattice rings around the origin, nearest ring
        /// first, without the origin itself; the smallest number of complete rings that holds PlacesPerMember per member.</summary>
        public static SimVec3[] Formation(int size, long spacingRaw)
        {
            if (size < 1 || size > LedRules.MaxSize) throw new ArgumentOutOfRangeException(nameof(size));
            int rings = 1;
            while (3 * rings * (rings + 1) < PlacesPerMember * size) rings++;
            SimVec3[] lattice = GuardSlots.Build(default, SimFix.FromRaw(rings * spacingRaw), spacingRaw);
            if (lattice.Length != 1 + 3 * rings * (rings + 1)) throw new InvalidOperationException("The formation lattice is not made of complete rings.");
            var result = new SimVec3[lattice.Length - 1];
            Array.Copy(lattice, 1, result, 0, result.Length);
            return result;
        }

        public int Order => 1;
        public string StateTypeId => "battle.led-squad";
        public int StateVersion => 1;
        public LedRules Rules => rules;
        // 0 when the controlled unit leads nobody.
        public ulong Leader => leader;
        public LedMode Mode => mode;
        public int MemberCount => count;
        public SimVec3 Anchor => anchor;
        public int AnchorLayer => anchorLayer;
        public bool IsMember(StableId unit) => IndexOf(unit.Value) >= 0;
        // The squad's leader for the leader itself and for a member; 0 for any other unit.
        public ulong LeaderOf(StableId unit) => unit.Value == leader || IsMember(unit) ? leader : 0;
        public ulong MemberAt(int index) => index >= 0 && index < count ? members[index] : throw new ArgumentOutOfRangeException(nameof(index));
        // The spot a member got when it joined; -1 for a unit that is not a member.
        public int SpotOf(StableId unit) { int i = IndexOf(unit.Value); return i < 0 ? -1 : spots[i]; }

        // Where a member stands in the formation now; false for a unit that is not a member and for a member without a place.
        public bool TryPlace(StableId unit, out SimVec3 place)
        {
            int i = IndexOf(unit.Value);
            if (i < 0 || !placed[i] || restored) { place = default; return false; }
            place = places[i];
            return true;
        }

        private int IndexOf(ulong key) => Array.BinarySearch(members, 0, count, key);

        public void Step(SimContext c)
        {
            EntityStore store = combat.Entities;
            EntityId player = controlled.Current;
            Position here = default; Combatant me = default;
            bool playing = store.IsAlive(player) && store.TryGet(combatants, player, out me) && me.Team >= 1
                && store.TryGet(positions, player, out here) && !combat.IsDead(player);
            ulong key = playing ? store.StableOf(player).Value : 0;
            Drop();
            if (leader != 0 && leader != key) Disband();
            if (playing && c.Input.Squad.HasValue && c.Input.Squad.Value.Squad == SquadCommand.Led)
                Command(c.Input.Squad.Value, key, here.Value, LayerOf(player));
            if (leader != 0)
            {
                Recruit(player, me.Team, here.Value);
                if (mode == LedMode.Follow) Trail(here.Value, LayerOf(player));
                // A restored squad gets its places back without forgetting which walk orders were refused: they are saved.
                if (dirty) Place(true); else if (restored) Place(false);
                Upkeep();
            }
            dirty = false; restored = false;
        }

        private int LayerOf(EntityId unit) => layers == null ? 0 : layers.LayerOf(unit);

        private void Command(SquadCommand command, ulong key, SimVec3 here, int layer)
        {
            switch (command.Kind)
            {
                case SquadCommandKind.Dismiss: Disband(); return;
                case SquadCommandKind.Follow: Lead(key, LedMode.Follow, here, layer); return;
                case SquadCommandKind.Hold: Lead(key, LedMode.Hold, here, layer); return;
                default:
                    // A point nobody can stand on voids the whole command: the squad stays as it was.
                    if (moves.TryStandAt(command.TargetPoint, command.TargetLayer, out SimVec3 grounded)) Lead(key, LedMode.Hold, grounded, command.TargetLayer);
                    return;
            }
        }

        private void Lead(ulong key, LedMode next, SimVec3 at, int layer)
        {
            leader = key; mode = next; anchor = at; anchorLayer = layer; dirty = true;
        }

        private void Disband()
        {
            for (int i = 0; i < count; i++) moves.Stop(new StableId(members[i]));
            leader = 0; mode = LedMode.None; anchor = default; anchorLayer = 0; count = 0;
        }

        private bool Alive(ulong key, out EntityId entity, out SimVec3 at)
        {
            entity = default; at = default;
            EntityStore store = combat.Entities;
            if (!store.TryResolve(new StableId(key), out EntityRef found)) return false;
            entity = (EntityId)found;
            if (combat.IsDead(entity) || !store.TryGet(positions, entity, out Position position)) return false;
            at = position.Value;
            return true;
        }

        // Members who died or left the field go; the others keep their spots. Whenever the list changes (here and in Join) the
        // places are worked out again, which forgets every refused walk order, so the refused marks are not moved along.
        private void Drop()
        {
            int kept = 0;
            for (int i = 0; i < count; i++)
            {
                if (!Alive(members[i], out _, out _)) { dirty = true; continue; }
                members[kept] = members[i]; spots[kept] = spots[i]; kept++;
            }
            count = kept;
        }

        // While the squad is short: the nearest living team mate within the radius that nobody commands joins. Equal
        // distances go by a mix of the key (one to one, so two units never tie); never by the entity index.
        private void Recruit(EntityId player, int team, SimVec3 leaderAt)
        {
            EntityStore store = combat.Entities;
            while (count < rules.Size)
            {
                bool any = false; ulong best = 0, bestMix = 0; SimWide bestDistance = default; SimVec3 bestAt = default;
                foreach (EntityId entity in stage.Capture())
                {
                    if (entity.Equals(player) || !store.TryGet(combatants, entity, out Combatant unit) || unit.Team != team
                        || !store.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) continue;
                    StableId id = store.StableOf(entity);
                    if (IndexOf(id.Value) >= 0 || moves.IsCommanded(id)) continue;
                    SimWide distance = CombatSystem.Horizontal(position.Value - leaderAt).LengthSquared;
                    if (distance > recruitSquared) continue;
                    ulong mix = AiParts.Mix(id.Value ^ RecruitSalt);
                    if (any && (distance > bestDistance || distance == bestDistance && mix > bestMix)) continue;
                    any = true; best = id.Value; bestMix = mix; bestDistance = distance; bestAt = position.Value;
                }
                if (!any) return;
                Join(best, bestAt);
            }
        }

        // The new member takes the free spot whose place is nearest to where it stands (the lower spot when two are equally
        // near) and keeps it for as long as it is in the squad.
        private void Join(ulong key, SimVec3 at)
        {
            int spot = -1; SimWide nearest = default;
            for (int s = 0; s < rules.Size; s++)
            {
                bool free = true;
                for (int i = 0; i < count && free; i++) free = spots[i] != s;
                if (!free) continue;
                SimWide distance = CombatSystem.Horizontal(anchor + offsets[s] - at).LengthSquared;
                if (spot < 0 || distance < nearest) { spot = s; nearest = distance; }
            }
            int index = count;
            while (index > 0 && members[index - 1] > key)
            {
                members[index] = members[index - 1]; spots[index] = spots[index - 1]; index--;
            }
            members[index] = key; spots[index] = spot; count++; dirty = true;
        }

        // Following: the anchor moves once the leader has changed layer or walked more than half a spacing away from it,
        // so the members are not sent a new place on every tick.
        private void Trail(SimVec3 here, int layer)
        {
            if (layer == anchorLayer && CombatSystem.Horizontal(here - anchor).LengthSquared <= shiftSquared) return;
            anchor = here; anchorLayer = layer; dirty = true;
        }

        // A place can be used when a unit can stand there on the anchor's layer and the anchor sees it (so a member is not
        // sent to the far side of a wall). First every member tries its own spot, in key order; then the members left over
        // take, in key order, the first usable place nobody has. A member may end up without a place.
        private void Place(bool forget)
        {
            for (int o = 0; o < taken.Length; o++) taken[o] = false;
            for (int i = 0; i < count; i++)
            {
                if (forget) refused[i] = false;
                placed[i] = Usable(spots[i], out places[i]);
                if (placed[i]) taken[spots[i]] = true;
            }
            for (int i = 0; i < count; i++)
            {
                if (placed[i]) continue;
                for (int o = 0; o < offsets.Length; o++)
                {
                    if (taken[o] || !Usable(o, out places[i])) continue;
                    placed[i] = true; taken[o] = true;
                    break;
                }
            }
        }

        private bool Usable(int offset, out SimVec3 grounded) =>
            moves.TryStandAt(anchor + offsets[offset], anchorLayer, out grounded) && combat.Sees(anchor, grounded);

        // Every tick: a member without a place stands; a member on its place stands; any other member is walked to its
        // place. A walk order that was refused is not given again until the places change.
        private void Upkeep()
        {
            EntityStore store = combat.Entities;
            for (int i = 0; i < count; i++)
            {
                var id = new StableId(members[i]);
                bool walking = moves.TryGetOrder(id, out SimVec3 target, out int targetLayer);
                if (!placed[i]) { if (walking) moves.Stop(id); continue; }
                if (!Alive(members[i], out EntityId entity, out SimVec3 at)) continue;
                if (LayerOf(entity) == anchorLayer && SimpleAiSystem.Arrived(at, places[i])) { if (walking) moves.Stop(id); continue; }
                if (refused[i]) continue;
                if (walking && targetLayer == anchorLayer && target.X.Raw == places[i].X.Raw && target.Z.Raw == places[i].Z.Raw) continue;
                moves.Order(id, places[i], anchorLayer);
                if (!moves.TryGetOrder(id, out _, out _)) refused[i] = true;
            }
        }

        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(rules.Size); writer.Int32(rules.RecruitRadiusMm); writer.Int64(spacingRaw);
            writer.UInt64(leader); writer.Int32((int)mode);
            writer.Int64(anchor.X.Raw); writer.Int64(anchor.Y.Raw); writer.Int64(anchor.Z.Raw); writer.Int32(anchorLayer);
            writer.Int32(count);
            for (int i = 0; i < count; i++) { writer.UInt64(members[i]); writer.Int32(spots[i]); writer.Bool(refused[i]); }
        }

        public void LoadState(SimStateReader reader)
        {
            bool same = reader.Int32() == rules.Size;
            same &= reader.Int32() == rules.RecruitRadiusMm; same &= reader.Int64() == spacingRaw;
            SimStateReader.Require(same, "Led squad rules differ.");
            leader = reader.UInt64();
            int kind = reader.Int32();
            SimStateReader.Require(kind >= (int)LedMode.None && kind <= (int)LedMode.Hold, "Led squad mode is out of range.");
            mode = (LedMode)kind;
            SimStateReader.Require(leader == 0 == (mode == LedMode.None), "Led squad has a leader without a mode, or a mode without a leader.");
            anchor = new SimVec3(SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()), SimFix.FromRaw(reader.Int64()));
            anchorLayer = reader.Int32();
            SimStateReader.Require(anchorLayer >= 0 && (leader != 0 || anchorLayer == 0 && anchor.X.Raw == 0 && anchor.Y.Raw == 0 && anchor.Z.Raw == 0), "Led squad anchor is out of range.");
            count = reader.Count(rules.Size);
            SimStateReader.Require(leader != 0 || count == 0, "Led squad has members without a leader.");
            ulong previous = 0;
            for (int i = 0; i < count; i++)
            {
                ulong member = reader.UInt64();
                SimStateReader.Require(member > previous && member != leader, "Led squad members are not in strictly increasing order, or one is the leader.");
                previous = member;
                int spot = reader.Int32();
                SimStateReader.Require(spot >= 0 && spot < rules.Size, "Led squad spot is out of range.");
                for (int j = 0; j < i; j++) SimStateReader.Require(spots[j] != spot, "Two led squad members share a spot.");
                members[i] = member; spots[i] = spot; refused[i] = reader.Bool();
            }
            restored = true;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion);
            hash.Add(rules.Size); hash.Add(rules.RecruitRadiusMm); hash.Add(spacingRaw);
            hash.Add(leader); hash.Add((int)mode);
            hash.Add(anchor.X.Raw); hash.Add(anchor.Y.Raw); hash.Add(anchor.Z.Raw); hash.Add(anchorLayer);
            hash.Add(count);
            for (int i = 0; i < count; i++) { hash.Add(members[i]); hash.Add(spots[i]); hash.Add(refused[i]); }
        }
    }
}
