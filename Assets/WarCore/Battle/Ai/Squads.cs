using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // Squad tempers (all four or none). A squad is rash when its temper number is below RashPermille and steady when it
    // is at or above 1000 - SteadyPermille (Squads.MoodOf); ShakenPermille is how far a squad short of members moves
    // toward steady; a rash squad stops going on once the enemy is nearer than RashMinRangeMm.
    public readonly struct SquadTemper
    {
        public readonly int RashPermille, SteadyPermille, ShakenPermille, RashMinRangeMm;
        public SquadTemper(int rashPermille, int steadyPermille, int shakenPermille, int rashMinRangeMm)
        {
            if (rashPermille < 0 || rashPermille > 1000) throw new ArgumentOutOfRangeException(nameof(rashPermille));
            if (steadyPermille < 0 || steadyPermille > 1000 - rashPermille) throw new ArgumentOutOfRangeException(nameof(steadyPermille));
            if (shakenPermille < 0 || shakenPermille > 1000) throw new ArgumentOutOfRangeException(nameof(shakenPermille));
            if (rashMinRangeMm < 0) throw new ArgumentOutOfRangeException(nameof(rashMinRangeMm));
            RashPermille = rashPermille; SteadyPermille = steadyPermille; ShakenPermille = shakenPermille; RashMinRangeMm = rashMinRangeMm;
        }
    }

    // Regrouping (all three or none): a squad whose strength falls below BelowPermille goes back to a point its team owns
    // until its strength is at least UntilPermille again, or MaxTicks ticks have passed.
    public readonly struct SquadRegroup
    {
        public readonly int BelowPermille, UntilPermille, MaxTicks;
        public SquadRegroup(int belowPermille, int untilPermille, int maxTicks)
        {
            if (belowPermille < 1 || belowPermille > 1000) throw new ArgumentOutOfRangeException(nameof(belowPermille));
            if (untilPermille < belowPermille || untilPermille > 1000) throw new ArgumentOutOfRangeException(nameof(untilPermille));
            if (maxTicks < 1) throw new ArgumentOutOfRangeException(nameof(maxTicks));
            BelowPermille = belowPermille; UntilPermille = untilPermille; MaxTicks = maxTicks;
        }
    }

    // A squad's temper; None for a skirmisher and in a battle without tempers.
    public enum SquadMood { None, Normal, Rash, Steady }

    // A squad's regroup mark; None for a skirmisher and in a battle without regrouping. Active: on its way back to a point
    // of its own. Idle: short of members and not regrouping (a new squad that was never whole, or one that gave up).
    public enum SquadRally { None, Active, Idle }

    // Squad numbers a content pack sets (the seven: all or none). Sizes count the members without the leader.
    public readonly struct SquadRules
    {
        public readonly int MinSize, MaxSize, SkirmisherPermille, RecruitRadiusMm, FormTicks, SpreadMinMm, SpreadMaxMm;
        public readonly SquadTemper? Temper;
        public readonly SquadRegroup? Regroup;
        public SquadRules(int minSize, int maxSize, int skirmisherPermille, int recruitRadiusMm, int formTicks, int spreadMinMm, int spreadMaxMm,
            SquadTemper? temper = null, SquadRegroup? regroup = null)
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
            Temper = temper; Regroup = regroup;
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
    //
    // Strength: (1 + members) * 1000 / (1 + size), whole thousandths. With tempers (SquadTemper) a squad has a mood,
    // worked out anew at every question and never saved: m = Permille(leader ^ TemperSalt) + (1000 - strength) *
    // shaken / 1000; rash when m < rash, steady when m >= 1000 - steady, normal between. So a squad that loses members
    // grows careful and is its old self again when it is whole.
    //
    // With regrouping (SquadRegroup) every squad has a saved mark: -1 not regrouping, a tick >= 0 regrouping since then,
    // -2 short of members and not regrouping. A new squad starts at -2: it was never whole, so being short is no loss.
    // On a form tick, after every team has recruited and formed, squad by squad in key order: -1 turns into the tick when
    // the strength is below Below; a tick turns into -1 when the strength is at least Until, else into -2 once MaxTicks
    // ticks have passed; -2 turns into -1 when the strength is at least Below.
    public sealed partial class Squads : IRetainedGameplaySystem
    {
        private const ulong ElectSalt = 0x8f1bbcdc6ed9eba1UL, SizeSalt = 0xca62c1d6a953fd4eUL, TemperSalt = 0xe6222af5c6534b75UL;
        private const long Settled = -1, Idle = -2;

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
        // Saved with the regroup rules only: leader key -> regroup mark. The same keys as sizes, so the same order.
        private readonly SortedList<ulong, long> marks;
        private readonly bool tempered, regrouping;
        private readonly SquadTemper temper;
        private readonly SquadRegroup regroup;
        private readonly SimWide rashRangeSquared;
        // heads[i]: the members of the i-th squad of sizes. Rebuilt at the end of every Step and of LoadState, so the
        // questions below are answered right after a restore as well.
        private readonly int[] heads;
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
            marks = new SortedList<ulong, long>(capacity); heads = new int[capacity];
            tempered = rules.Temper.HasValue; temper = rules.Temper.GetValueOrDefault();
            regrouping = rules.Regroup.HasValue; regroup = rules.Regroup.GetValueOrDefault();
            rashRangeSquared = new SimVec3(SimFix.Ratio(temper.RashMinRangeMm, 1000), default, default).LengthSquared;
            keys = new ulong[capacity]; drop = new ulong[capacity]; teams = new int[capacity]; counts = new int[capacity];
            at = new SimVec3[capacity]; free = new bool[capacity]; served = new bool[capacity];
            from = new int[capacity]; foundTeams = new int[capacity]; foundAt = new SimVec3[capacity];
        }

        public int Order => 3;
        public string StateTypeId => "battle.ai-squads";
        // 2 with the regroup rules (the marks are saved), 1 without: a battle without them saves what it always did.
        public int StateVersion => regrouping ? 2 : 1;
        public SquadRules Rules => rules;
        public int SquadCount => sizes.Count;
        public int MemberCount => leaderOf.Count;

        // The leader's key of the squad the unit is in (its own key for a leader); 0 for a skirmisher.
        public ulong SquadOf(StableId unit) =>
            leaderOf.TryGetValue(unit.Value, out ulong leader) ? leader : sizes.ContainsKey(unit.Value) ? unit.Value : 0;

        // How many members the unit's squad may have; 0 when the unit leads no squad.
        public int SizeOf(StableId leader) => sizes.TryGetValue(leader.Value, out int size) ? size : 0;

        // The squad's temper, the same for the leader and every member.
        public SquadMood MoodOf(StableId unit)
        {
            int squad = tempered ? IndexOf(unit.Value) : -1;
            return squad < 0 ? SquadMood.None : Mood(squad);
        }

        public SquadRally RegroupOf(StableId unit)
        {
            int squad = regrouping ? IndexOf(unit.Value) : -1;
            if (squad < 0) return SquadRally.None;
            long mark = marks.Values[squad];
            return mark >= 0 ? SquadRally.Active : mark == Idle ? SquadRally.Idle : SquadRally.None;
        }

        // (1 + members) * 1000 / (1 + size) of the unit's squad; 0 for a skirmisher.
        public int StrengthPermille(StableId unit)
        {
            int squad = IndexOf(unit.Value);
            return squad < 0 ? 0 : Strength(squad);
        }

        // The place in sizes of the squad the unit leads or is a member of; negative for a skirmisher.
        private int IndexOf(ulong unit)
        {
            int squad = sizes.IndexOfKey(unit);
            return squad >= 0 || !leaderOf.TryGetValue(unit, out ulong leader) ? squad : sizes.IndexOfKey(leader);
        }

        private int Strength(int squad) => (1 + heads[squad]) * 1000 / (1 + sizes.Values[squad]);

        private SquadMood Mood(int squad)
        {
            int m = AiParts.Permille(sizes.Keys[squad], TemperSalt) + (1000 - Strength(squad)) * temper.ShakenPermille / 1000;
            return m < temper.RashPermille ? SquadMood.Rash : m >= 1000 - temper.SteadyPermille ? SquadMood.Steady : SquadMood.Normal;
        }

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
            bool forms = c.Clock.Tick % rules.FormTicks == 0;
            if (forms)
            {
                int top = 0;
                for (int i = 0; i < n; i++) if (teams[i] > top) top = teams[i];
                for (int team = 1; team <= top; team++) Form(team);
            }
            Recount();
            if (forms && regrouping) Judge(c.Clock.Tick);
        }

        private void Recount()
        {
            for (int i = 0; i < sizes.Count; i++) heads[i] = 0;
            IList<ulong> theirs = leaderOf.Values;
            for (int i = 0; i < theirs.Count; i++) heads[sizes.IndexOfKey(theirs[i])]++;
        }

        // The regroup marks, on a form tick after every team has recruited and formed; squads formed on this tick as well.
        private void Judge(long tick)
        {
            IList<ulong> leaders = marks.Keys;
            IList<long> all = marks.Values;
            for (int i = 0; i < leaders.Count; i++)
            {
                int strength = Strength(i);
                long mark = all[i], next = mark;
                if (mark == Settled) { if (strength < regroup.BelowPermille) next = tick; }
                else if (mark == Idle) { if (strength >= regroup.BelowPermille) next = Settled; }
                else if (strength >= regroup.UntilPermille) next = Settled;
                else if (tick - mark >= regroup.MaxTicks) next = Idle;
                if (next != mark) marks[leaders[i]] = next;
            }
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
            for (int i = 0; i < dropped; i++) Dissolve(drop[i]);
            dropped = 0;
            IList<ulong> members = leaderOf.Keys, theirs = leaderOf.Values;
            for (int i = 0; i < members.Count; i++) if (Find(members[i]) < 0 || !sizes.ContainsKey(theirs[i])) drop[dropped++] = members[i];
            for (int i = 0; i < dropped; i++) leaderOf.Remove(drop[i]);
            Count();
            dropped = 0;
            for (int i = 0; i < leaders.Count; i++) if (counts[Find(leaders[i])] == 0) drop[dropped++] = leaders[i];
            for (int i = 0; i < dropped; i++) Dissolve(drop[i]);
        }

        private void Dissolve(ulong leader) { sizes.Remove(leader); if (regrouping) marks.Remove(leader); }

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
                if (regrouping) marks.Add(keys[leader], Idle);
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

        // The seven numbers, then leader -> size and member -> leader, both in key order. With the regroup rules
        // (version 2): the three regroup numbers after the seven, and every squad's mark after its size.
        public void SaveState(SimStateWriter writer)
        {
            writer.Int32(rules.MinSize); writer.Int32(rules.MaxSize); writer.Int32(rules.SkirmisherPermille); writer.Int32(rules.RecruitRadiusMm);
            writer.Int32(rules.FormTicks); writer.Int32(rules.SpreadMinMm); writer.Int32(rules.SpreadMaxMm);
            if (regrouping) { writer.Int32(regroup.BelowPermille); writer.Int32(regroup.UntilPermille); writer.Int32(regroup.MaxTicks); }
            IList<ulong> leaders = sizes.Keys, members = leaderOf.Keys, theirs = leaderOf.Values;
            IList<int> size = sizes.Values;
            writer.Int32(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
            {
                writer.UInt64(leaders[i]); writer.Int32(size[i]);
                if (regrouping) writer.Int64(marks.Values[i]);
            }
            writer.Int32(members.Count);
            for (int i = 0; i < members.Count; i++) { writer.UInt64(members[i]); writer.UInt64(theirs[i]); }
        }

        public void LoadState(SimStateReader reader)
        {
            bool same = reader.Int32() == rules.MinSize;
            same &= reader.Int32() == rules.MaxSize; same &= reader.Int32() == rules.SkirmisherPermille;
            same &= reader.Int32() == rules.RecruitRadiusMm; same &= reader.Int32() == rules.FormTicks;
            same &= reader.Int32() == rules.SpreadMinMm; same &= reader.Int32() == rules.SpreadMaxMm;
            if (regrouping)
            {
                same &= reader.Int32() == regroup.BelowPermille; same &= reader.Int32() == regroup.UntilPermille;
                same &= reader.Int32() == regroup.MaxTicks;
            }
            SimStateReader.Require(same, "AI squad rules differ.");
            sizes.Clear(); leaderOf.Clear(); marks.Clear();
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
                if (!regrouping) continue;
                long mark = reader.Int64();
                SimStateReader.Require(mark >= Idle, "AI squad regroup mark is out of range.");
                marks.Add(leader, mark);
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
            Recount();
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion);
            hash.Add(rules.MinSize); hash.Add(rules.MaxSize); hash.Add(rules.SkirmisherPermille); hash.Add(rules.RecruitRadiusMm);
            hash.Add(rules.FormTicks); hash.Add(rules.SpreadMinMm); hash.Add(rules.SpreadMaxMm);
            if (regrouping) { hash.Add(regroup.BelowPermille); hash.Add(regroup.UntilPermille); hash.Add(regroup.MaxTicks); }
            IList<ulong> leaders = sizes.Keys, members = leaderOf.Keys, theirs = leaderOf.Values;
            IList<int> size = sizes.Values;
            hash.Add(leaders.Count);
            for (int i = 0; i < leaders.Count; i++)
            {
                hash.Add(leaders[i]); hash.Add(size[i]);
                if (regrouping) hash.Add(marks.Values[i]);
            }
            hash.Add(members.Count);
            for (int i = 0; i < members.Count; i++) { hash.Add(members[i]); hash.Add(theirs[i]); }
        }
    }
}
