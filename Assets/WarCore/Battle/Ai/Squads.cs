using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Squad numbers a content pack sets (all or none). Sizes count the members without the leader.
    public readonly struct SquadRules
    {
        public readonly int MinSize, MaxSize, SkirmisherPermille, RecruitRadiusMm, FormTicks, SpreadMinMm, SpreadMaxMm;
        public SquadRules(int minSize, int maxSize, int skirmisherPermille, int recruitRadiusMm, int formTicks, int spreadMinMm, int spreadMaxMm)
        {
            if (minSize < 1) throw new ArgumentOutOfRangeException(nameof(minSize));
            if (maxSize < minSize) throw new ArgumentOutOfRangeException(nameof(maxSize));
            if (skirmisherPermille < 0 || skirmisherPermille > 1000) throw new ArgumentOutOfRangeException(nameof(skirmisherPermille));
            if (recruitRadiusMm < 1) throw new ArgumentOutOfRangeException(nameof(recruitRadiusMm));
            if (formTicks < 1) throw new ArgumentOutOfRangeException(nameof(formTicks));
            if (spreadMinMm < 0) throw new ArgumentOutOfRangeException(nameof(spreadMinMm));
            if (spreadMaxMm < spreadMinMm) throw new ArgumentOutOfRangeException(nameof(spreadMaxMm));
            MinSize = minSize; MaxSize = maxSize; SkirmisherPermille = skirmisherPermille; RecruitRadiusMm = recruitRadiusMm;
            FormTicks = formTicks; SpreadMinMm = spreadMinMm; SpreadMaxMm = spreadMaxMm;
        }
    }

    // AI squads: who follows whom. A squad is a leader and one to its size in members; a unit in no squad is a
    // skirmisher. The system keeps the membership and nothing else: a member still thinks for itself (engage, pressure,
    // cover); the squad parts (SquadParts.cs) make it go for its leader's point and stand near its leader's place.
    //
    // Every tick, over the units the AI may steer (alive, not the player's, not under a squad command): a squad whose
    // leader is not among them is dissolved, a member that is not among them leaves, and a squad left without a member is
    // dissolved. Every formTicks ticks, per team: while fewer than the target (the units the AI may steer times the share
    // that is not skirmishers) are in squads, squads short of members recruit, largest gap first, the nearest skirmishers
    // within the recruit radius; then, while at least two places are left, the skirmisher that comes first by election
    // order leads a new squad if it finds a member. The election order is Mix(key ^ ElectSalt), never the key itself.
    //
    // Order 3, the first system of the tick: before respawns (4), the dead clean-up (5), the player's fallback (6) and
    // control switch (7), reinforcements (8), squad commands (10) and the AI (12). So a think never meets a squad whose
    // leader fell on the tick before, and a unit the player takes over or commands on this tick leaves its squad on the
    // next (the AI already leaves it alone on this one).
    public sealed partial class Squads : IRetainedGameplaySystem
    {
        private const ulong ElectSalt = 0x8f1bbcdc6ed9eba1UL, SizeSalt = 0xca62c1d6a953fd4eUL;

        private readonly CombatSystem combat;
        private readonly IMoveOrders moves;
        private readonly IControlTarget controlled;
        private readonly SquadRules rules;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly SimWide recruitSquared;
        // Saved: leader key -> size (the members it may have); member key -> leader key. Both in key order.
        private readonly SortedList<ulong, int> sizes;
        private readonly SortedList<ulong, ulong> leaderOf;
        // Scratch, rebuilt every Step: the units the AI may steer, in key order, and what Form knows about each.
        private readonly ulong[] keys, drop;
        private readonly int[] teams, counts, from, foundTeams;
        private readonly SimVec3[] at, foundAt;
        private readonly bool[] free, served;
        private int n;

        public Squads(CombatSystem combat, IMoveOrders moves, IControlTarget controlled, SquadRules rules)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
            this.controlled = controlled; this.rules = rules;
            stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            recruitSquared = new SimVec3(SimFix.Ratio(rules.RecruitRadiusMm, 1000), default, default).LengthSquared;
            int capacity = combat.Entities.Capacity;
            sizes = new SortedList<ulong, int>(capacity); leaderOf = new SortedList<ulong, ulong>(capacity);
            keys = new ulong[capacity]; drop = new ulong[capacity]; teams = new int[capacity]; counts = new int[capacity];
            at = new SimVec3[capacity]; free = new bool[capacity]; served = new bool[capacity];
            from = new int[capacity]; foundTeams = new int[capacity]; foundAt = new SimVec3[capacity];
        }

        public int Order => 3;
        public string StateTypeId => "battle.ai-squads";
        public int StateVersion => 1;
        public SquadRules Rules => rules;
        public int SquadCount => sizes.Count;
        public int MemberCount => leaderOf.Count;

        // The leader's key of the squad the unit is in (its own key for a leader); 0 for a skirmisher.
        public ulong SquadOf(StableId unit) =>
            leaderOf.TryGetValue(unit.Value, out ulong leader) ? leader : sizes.ContainsKey(unit.Value) ? unit.Value : 0;

        // How many members the unit's squad may have; 0 when the unit leads no squad.
        public int SizeOf(StableId leader) => sizes.TryGetValue(leader.Value, out int size) ? size : 0;

        // True for a member whose leader is on the field and alive: the leader and where it stands now. Looked up on every
        // call, so a leader that fell after this tick's Step is already gone for its members.
        public bool TryLeader(StableId unit, out StableId leader, out SimVec3 leaderHere)
        {
            leader = default; leaderHere = default;
            if (!leaderOf.TryGetValue(unit.Value, out ulong key)) return false;
            EntityStore store = combat.Entities;
            if (!store.TryResolve(new StableId(key), out EntityRef found) || combat.IsDead((EntityId)found)
                || !store.TryGet(positions, (EntityId)found, out Position position)) return false;
            leader = new StableId(key); leaderHere = position.Value;
            return true;
        }

        public void Step(SimContext c)
        {
            Gather();
            Clean();
            if (c.Clock.Tick % rules.FormTicks != 0) return;
            int top = 0;
            for (int i = 0; i < n; i++) if (teams[i] > top) top = teams[i];
            for (int team = 1; team <= top; team++) Form(team);
        }

        // The units the AI may steer, sorted by key.
        private void Gather()
        {
            n = 0;
            EntityStore store = combat.Entities;
            foreach (EntityId entity in stage.Capture())
            {
                if (controlled != null && entity.Equals(controlled.Current)) continue;
                if (!store.TryGet(combatants, entity, out Combatant unit) || unit.Team < 1
                    || !store.TryGet(positions, entity, out Position position) || combat.IsDead(entity)) continue;
                StableId id = store.StableOf(entity);
                if (moves.IsCommanded(id)) continue;
                keys[n] = id.Value; from[n] = n; foundTeams[n] = unit.Team; foundAt[n] = position.Value; n++;
            }
            // Keys are all different, so the order after the sort does not depend on the order the units were found in.
            Array.Sort(keys, from, 0, n);
            for (int i = 0; i < n; i++) { teams[i] = foundTeams[from[i]]; at[i] = foundAt[from[i]]; }
        }

        private int Find(ulong key) => Array.BinarySearch(keys, 0, n, key);

        // Squads whose leader is gone, members who are gone or whose squad is gone, then squads without a member.
        private void Clean()
        {
            int dropped = 0;
            IList<ulong> leaders = sizes.Keys;
            for (int i = 0; i < leaders.Count; i++) if (Find(leaders[i]) < 0) drop[dropped++] = leaders[i];
            for (int i = 0; i < dropped; i++) sizes.Remove(drop[i]);
            dropped = 0;
            IList<ulong> members = leaderOf.Keys, theirs = leaderOf.Values;
            for (int i = 0; i < members.Count; i++) if (Find(members[i]) < 0 || !sizes.ContainsKey(theirs[i])) drop[dropped++] = members[i];
            for (int i = 0; i < dropped; i++) leaderOf.Remove(drop[i]);
            Count();
            dropped = 0;
            for (int i = 0; i < leaders.Count; i++) if (counts[Find(leaders[i])] == 0) drop[dropped++] = leaders[i];
            for (int i = 0; i < dropped; i++) sizes.Remove(drop[i]);
        }

        // counts[i]: the members of the squad that unit i leads.
        private void Count()
        {
            for (int i = 0; i < n; i++) counts[i] = 0;
            IList<ulong> theirs = leaderOf.Values;
            for (int i = 0; i < theirs.Count; i++) counts[Find(theirs[i])]++;
        }

        private void Form(int team)
        {
            int foot = 0, bodies = 0;
            for (int i = 0; i < n; i++)
            {
                free[i] = false; served[i] = false;
                if (teams[i] != team) continue;
                foot++;
                if (sizes.ContainsKey(keys[i]) || leaderOf.ContainsKey(keys[i])) bodies++;
                else free[i] = true;
            }
            int target = (int)((long)foot * (1000 - rules.SkirmisherPermille) / 1000);
            // Squads short of members, largest gap first (equal gaps: election order), each served once.
            while (bodies < target)
            {
                int best = -1, gap = 0;
                for (int i = 0; i < n; i++)
                {
                    if (teams[i] != team || served[i] || !sizes.TryGetValue(keys[i], out int size)) continue;
                    int lacks = size - counts[i];
                    if (lacks > gap || (lacks == gap && best >= 0 && Before(keys[i], keys[best]))) { best = i; gap = lacks; }
                }
                if (best < 0) break;
                served[best] = true;
                bodies += Recruit(best, Math.Min(gap, target - bodies));
            }
            // New squads while two places are left: the free unit that comes first by election order leads, if it finds a
            // member. One that finds nobody leads nothing and is not tried again on this tick; nobody recruits it on this
            // tick either, since no free unit stands within its reach.
            while (target - bodies >= 2)
            {
                int leader = -1;
                for (int i = 0; i < n; i++)
                    if (free[i] && (leader < 0 || Before(keys[i], keys[leader]))) leader = i;
                if (leader < 0) break;
                free[leader] = false;
                int size = rules.MinSize + (int)(AiParts.Mix(keys[leader] ^ SizeSalt) % (ulong)(rules.MaxSize - rules.MinSize + 1));
                int got = Recruit(leader, Math.Min(size, target - bodies - 1));
                if (got == 0) continue;
                sizes.Add(keys[leader], size);
                bodies += 1 + got;
            }
        }

        // Adds up to max of the nearest free units within the recruit radius (equal distances: election order) to the
        // squad of unit leader; returns how many. Form marks only the team's own units free.
        private int Recruit(int leader, int max)
        {
            int added = 0;
            while (added < max)
            {
                int pick = -1; SimWide best = default;
                for (int i = 0; i < n; i++)
                {
                    if (!free[i]) continue;
                    SimWide distance = CombatSystem.Horizontal(at[i] - at[leader]).LengthSquared;
                    if (distance > recruitSquared) continue;
                    if (pick >= 0 && (distance > best || (distance == best && !Before(keys[i], keys[pick])))) continue;
                    pick = i; best = distance;
                }
                if (pick < 0) break;
                free[pick] = false; leaderOf.Add(keys[pick], keys[leader]); counts[leader]++; added++;
            }
            return added;
        }

        // Election order. Mix is one-to-one, so two different keys never tie.
        private static bool Before(ulong a, ulong b) => AiParts.Mix(a ^ ElectSalt) < AiParts.Mix(b ^ ElectSalt);

        // The seven numbers, then leader -> size and member -> leader, both in key order.
        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(rules.MinSize); writer.Int32(rules.MaxSize); writer.Int32(rules.SkirmisherPermille); writer.Int32(rules.RecruitRadiusMm);
            writer.Int32(rules.FormTicks); writer.Int32(rules.SpreadMinMm); writer.Int32(rules.SpreadMaxMm);
            IList<ulong> leaders = sizes.Keys, members = leaderOf.Keys, theirs = leaderOf.Values;
            IList<int> size = sizes.Values;
            writer.Int32(leaders.Count);
            for (int i = 0; i < leaders.Count; i++) { writer.UInt64(leaders[i]); writer.Int32(size[i]); }
            writer.Int32(members.Count);
            for (int i = 0; i < members.Count; i++) { writer.UInt64(members[i]); writer.UInt64(theirs[i]); }
        }

        public void LoadState(SimStateReader reader)
        {
            bool same = reader.Int32() == rules.MinSize;
            same &= reader.Int32() == rules.MaxSize; same &= reader.Int32() == rules.SkirmisherPermille;
            same &= reader.Int32() == rules.RecruitRadiusMm; same &= reader.Int32() == rules.FormTicks;
            same &= reader.Int32() == rules.SpreadMinMm; same &= reader.Int32() == rules.SpreadMaxMm;
            SimStateReader.Require(same, "AI squad rules differ.");
            sizes.Clear(); leaderOf.Clear();
            int squads = reader.Count(keys.Length);
            ulong previous = 0;
            for (int i = 0; i < squads; i++)
            {
                ulong leader = reader.UInt64();
                SimStateReader.Require(i == 0 || leader > previous, "AI squad leaders are not in strictly increasing order.");
                previous = leader;
                int size = reader.Int32();
                SimStateReader.Require(size >= rules.MinSize && size <= rules.MaxSize, "AI squad size is out of range.");
                sizes.Add(leader, size); counts[i] = 0;
            }
            int members = reader.Count(keys.Length);
            for (int i = 0; i < members; i++)
            {
                ulong member = reader.UInt64(), leader = reader.UInt64();
                SimStateReader.Require(i == 0 || member > previous, "AI squad members are not in strictly increasing order.");
                previous = member;
                int squad = sizes.IndexOfKey(leader);
                SimStateReader.Require(squad >= 0, "AI squad member follows a unit that leads no squad.");
                SimStateReader.Require(!sizes.ContainsKey(member), "AI squad member leads a squad.");
                leaderOf.Add(member, leader); counts[squad]++;
            }
            IList<int> room = sizes.Values;
            for (int i = 0; i < squads; i++)
                SimStateReader.Require(counts[i] >= 1 && counts[i] <= room[i], "AI squad has no member or more than its size.");
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion);
            hash.Add(rules.MinSize); hash.Add(rules.MaxSize); hash.Add(rules.SkirmisherPermille); hash.Add(rules.RecruitRadiusMm);
            hash.Add(rules.FormTicks); hash.Add(rules.SpreadMinMm); hash.Add(rules.SpreadMaxMm);
            IList<ulong> leaders = sizes.Keys, members = leaderOf.Keys, theirs = leaderOf.Values;
            IList<int> size = sizes.Values;
            hash.Add(leaders.Count);
            for (int i = 0; i < leaders.Count; i++) { hash.Add(leaders[i]); hash.Add(size[i]); }
            hash.Add(members.Count);
            for (int i = 0; i < members.Count; i++) { hash.Add(members[i]); hash.Add(theirs[i]); }
        }
    }
}
