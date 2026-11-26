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
    /// RC2-6a: a member whose place the leader stands on stands a step aside of it for as long as the leader's body is on
    /// that place; a leader that walks past sends nobody aside.
    /// Deterministic and allocation-free after construction. Saved: leader, mode, anchor, and per member its key, its
    /// spot (fixed when it joins) and whether its last walk order was refused. The places are derived.</summary>
    public sealed class LedSquad : IRetainedGameplaySystem
    {
        // Places kept per member: its own and three spares for when a place cannot be used.
        internal const int PlacesPerMember = 4;
        // The first ring of the lattice: its six steps are the sides a member can stand aside to.
        internal const int Sides = 6;
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
        private readonly ComponentType<Collider> colliders;
        private readonly SimWide recruitSquared, shiftSquared;
        // The formation: lattice offsets from the anchor, ring by ring. A member's spot is an index below rules.Size.
        private readonly SimVec3[] offsets;
        // Half of each of the first ring's steps.
        private readonly SimVec3[] halves = new SimVec3[Sides];
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
        // Derived at every step: where the leader stands, its body's radius and half height (0 without a body), and whether
        // it walks: its input asks for a step, whether or not the step can be taken (as the movers have it).
        private SimVec3 leaderAt;
        private SimFix leaderRadius;
        private long leaderHalf;
        private bool leaderWalks;

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
            colliders = combat.Entities.Resolve<Collider>();
            for (int side = 0; side < Sides; side++)
                halves[side] = new SimVec3(SimFix.FromRaw(offsets[side].X.Raw / 2), default, SimFix.FromRaw(offsets[side].Z.Raw / 2));
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

        // Where a member is to stand now: its place in the formation, or a step aside of it while the leader's body is on that
        // place (Stand); false, with no point, for a unit that is not a member and for a member with nowhere to stand. The
        // leader is where it stood at the last step; whether it walks is not asked here (Upkeep leaves a member it walks past).
        public bool TryPlace(StableId unit, out SimVec3 place)
        {
            place = default;
            int i = IndexOf(unit.Value);
            if (i < 0 || !placed[i] || restored) return false;
            // A member that is not on the field any more is asked as if it stood on its place without a walk order.
            bool alive = Alive(members[i], out _, out SimVec3 here);
            SimVec3 target = default; int layer = 0;
            bool walking = alive && moves.TryGetOrder(unit, out target, out layer) && layer == anchorLayer;
            return Stand(i, Radius(unit), Half(unit), alive ? here : places[i], walking, target, 0, out place, out _);
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
                leaderAt = here.Value; leaderRadius = Radius(player); leaderHalf = Half(player);
                leaderWalks = c.Input.Move.X.Raw != 0 || c.Input.Move.Z.Raw != 0;
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
                placed[i] = Usable(anchor + offsets[spots[i]], out places[i]);
                if (placed[i]) taken[spots[i]] = true;
            }
            for (int i = 0; i < count; i++)
            {
                if (placed[i]) continue;
                for (int o = 0; o < offsets.Length; o++)
                {
                    if (taken[o] || !Usable(anchor + offsets[o], out places[i])) continue;
                    placed[i] = true; taken[o] = true;
                    break;
                }
            }
        }

        private bool Usable(SimVec3 point, out SimVec3 grounded) =>
            moves.TryStandAt(point, anchorLayer, out grounded) && combat.Sees(anchor, grounded);

        private SimFix Radius(EntityId unit) => combat.Entities.TryGet(colliders, unit, out Collider body) ? body.Radius : default;
        private SimFix Radius(StableId unit) => combat.Entities.TryResolve(unit, out EntityRef found) ? Radius((EntityId)found) : default;
        // Half a body's height, as the movers take it.
        private long Half(EntityId unit) => combat.Entities.TryGet(colliders, unit, out Collider body) ? body.Height.Raw / 2 : 0;
        private long Half(StableId unit) => combat.Entities.TryResolve(unit, out EntityRef found) ? Half((EntityId)found) : 0;

        private static bool Same(SimVec3 a, SimVec3 b) => a.X.Raw == b.X.Raw && a.Z.Raw == b.Z.Raw;

        // RC2-6a: a body of that radius and half height standing at the point does not overlap the leader's body: the two are
        // apart by their radii, or, on a map with floors, one stands above the other's head (bodies stand on their feet; the
        // movers judge two bodies the same way, so the layers themselves are not asked).
        private bool Clear(SimFix radius, long half, SimVec3 point)
        {
            if (CombatSystem.Horizontal(point - leaderAt).LengthSquared >= new SimVec3(leaderRadius + radius, default, default).LengthSquared) return true;
            long rise = point.Y.Raw - leaderAt.Y.Raw;
            return layers != null && (rise >= 2 * leaderHalf || -rise >= 2 * half);
        }

        // RC2-6a: the step from a place to its side: half a lattice step, or, when that is shorter than the two radii, the
        // shortest step along the same line that is not. A step's parts are whole raw units, so its length is asked, not
        // assumed. A side of the lattice is never a point, so the search ends: only at a spacing of one raw unit would the
        // rounding put a place on the origin, and there the lattice is not made of complete rings, which Formation refuses.
        private SimVec3 Aside(int side, SimFix radius)
        {
            SimFix apart = leaderRadius + radius;
            SimWide clear = new SimVec3(apart, default, default).LengthSquared;
            if (halves[side].LengthSquared >= clear) return halves[side];
            for (long reach = apart.Raw; ; reach++)
            {
                var step = new SimVec3(SimFix.FromRaw(offsets[side].X.Raw * reach / spacingRaw), default, SimFix.FromRaw(offsets[side].Z.Raw * reach / spacingRaw));
                if (step.LengthSquared >= clear) return step;
            }
        }

        // RC2-6a: where member i (with a place) is to stand now: its place, or, while the leader's body is on that place, a step
        // aside of it. Of the sides that are usable and clear of the leader (and not in skip): the one the member already
        // stands at or walks to, so that it does not change sides while the leader shifts about; else the one that points
        // most away from the leader (the lower side when two point away alike), so that it does not cross the leader's body
        // to get there. False, with no point, while there is none. A function of the places, of where the leader and the
        // member stand and of the member's walk order, so nothing more is saved.
        private bool Stand(int i, SimFix radius, long half, SimVec3 from, bool walking, SimVec3 target, int skip, out SimVec3 at, out int side)
        {
            at = places[i]; side = -1;
            if (Clear(radius, half, at)) return true;
            SimVec3 toLeader = CombatSystem.Horizontal(leaderAt - places[i]);
            long least = 0;
            for (int s = 0; s < Sides; s++)
            {
                if ((skip & 1 << s) != 0) continue;
                SimVec3 step = Aside(s, radius);
                if (!Usable(places[i] + step, out SimVec3 point) || !Clear(radius, half, point)) continue;
                if (walking ? Same(target, point) : SimpleAiSystem.Arrived(from, point)) { at = point; side = s; return true; }
                long toward = step.X.Raw * toLeader.X.Raw + step.Z.Raw * toLeader.Z.Raw;
                if (side >= 0 && toward >= least) continue;
                at = point; side = s; least = toward;
            }
            if (side < 0) at = default;
            return side >= 0;
        }

        // RC2-6a: a walking leader sends nobody aside. A member it walks past keeps standing where it stands: on its place, or,
        // with the leader's body still on that place, a step aside of it.
        private bool Passed(int i, SimFix radius, long half, SimVec3 at)
        {
            if (SimpleAiSystem.Arrived(at, places[i])) return true;
            if (Clear(radius, half, places[i])) return false;
            for (int s = 0; s < Sides; s++)
                if (Usable(places[i] + Aside(s, radius), out SimVec3 point) && SimpleAiSystem.Arrived(at, point)) return true;
            return false;
        }

        // Every tick: a member with nowhere to stand stands; a member the walking leader passes stands (Passed); a member where
        // it is to stand (its place, or aside of it, Stand) and clear of the leader's body stands; any other member is walked
        // there. A walk order to its place that was refused is not given again until the places change, and such a member is
        // not sent aside either. A refused order to stand aside goes to the next side at once; when every side was refused
        // the member is left alone in the same way.
        private void Upkeep()
        {
            for (int i = 0; i < count; i++)
            {
                var id = new StableId(members[i]);
                bool walking = moves.TryGetOrder(id, out SimVec3 target, out int targetLayer);
                if (!placed[i]) { if (walking) moves.Stop(id); continue; }
                if (!Alive(members[i], out EntityId entity, out SimVec3 at)) continue;
                SimFix body = Radius(entity); long half = Half(entity);
                if (leaderWalks && !walking && LayerOf(entity) == anchorLayer && Passed(i, body, half, at)) continue;
                for (int skip = 0; ;)
                {
                    bool headed = walking && targetLayer == anchorLayer;
                    if (!Stand(i, body, half, at, headed, target, skip, out SimVec3 stand, out int side))
                    {
                        if (skip != 0) refused[i] = true; else if (walking) moves.Stop(id);
                        break;
                    }
                    if (LayerOf(entity) == anchorLayer && SimpleAiSystem.Arrived(at, stand) && Clear(body, half, at)) { if (walking) moves.Stop(id); break; }
                    if (refused[i] || headed && Same(target, stand)) break;
                    moves.Order(id, stand, anchorLayer);
                    walking = moves.TryGetOrder(id, out target, out targetLayer);
                    if (walking) break;
                    if (side < 0) { refused[i] = true; break; }
                    skip |= 1 << side;
                }
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
