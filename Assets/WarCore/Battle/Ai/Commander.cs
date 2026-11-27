using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    // The commander's numbers: the four of CommanderQuotaRules and the threat radius (a pack writes all five or none).
    public readonly struct CommanderRules
    {
        public readonly CommanderQuotaRules Quota;
        // Enemies counted near a point: standing units of other teams within this horizontal distance of its centre.
        public readonly int ThreatRadiusMm;
        // The threat radius is at most the map side limit (HeightField.MaxSide metres; a cell is one metre).
        public const int MaxThreatRadiusMm = HeightField.MaxSide * 1000;
        public CommanderRules(CommanderQuotaRules quota, int threatRadiusMm)
        {
            if (threatRadiusMm < 1) throw new ArgumentOutOfRangeException(nameof(threatRadiusMm));
            if (threatRadiusMm > MaxThreatRadiusMm) throw new ArgumentOutOfRangeException(nameof(threatRadiusMm));
            Quota = quota; ThreatRadiusMm = threatRadiusMm;
        }
    }

    /// <summary>A commander per team that remembers which point it sent each unit to. It dispatches groups: an
    /// AI squad (key: the leader's key; place: the leader's; size: the leader and its living members) or a unit in no squad
    /// (its own key); after the decision every unit of a group remembers the group's point (one row per unit). Every tick, in
    /// the queries phase after the capture (order 41), for every team: count the enemies near every point, ask
    /// CommanderQuota.Plan which points to attack and how many soldiers each target wants, keep every order whose point is
    /// still a target the group can walk to, and send the groups without an order to the targets. Orders only ever name a
    /// target; no group is sent to an own point. An order that still serves is never changed; a point a group is on its way
    /// to stays a candidate target even when it no longer borders an own point. The AI reads the rows on
    /// the next tick through the "commander" dispatch part (Dispatch); squad members ask with their leader's key
    /// (SquadDispatch). Saved: the rules, the attack flags per team and point, and the rows (unit key, point) in key order.
    /// Allocation-free after construction.</summary>
    public sealed class Commander : IRetainedGameplaySystem
    {
        private readonly CombatSystem combat;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly IMoveOrders moves;
        private readonly IControlTarget controlled;
        private readonly Squads squads;
        private readonly IUnitLayers layers;
        private readonly CommanderRules rules;
        private readonly AiField field;
        private readonly PointPresence presence;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly SimWide threatSquared;
        private readonly int teams, points;
        // Static: the point centres and which points are neighbours (CommanderQuota.Adjacency), found once.
        private readonly SimVec3[] centers;
        private readonly bool[] adjacent;
        // Saved, per (team - 1) * points + point: the attack flags of the last plan; and the rows (unit key, point) in key
        // order. The *Next buffers take the decision of this tick.
        private bool[] attack, attackNext;
        private ulong[] orderKeys, nextKeys;
        private int[] orderPoints, nextPoints;
        private int orderCount, nextCount;
        // Scratch, rebuilt every Step: per team and point the enemies near it and the soldiers it wants; the owners now.
        private readonly int[] enemies, desired, near, owners;
        // Scratch for one team's plan (planRank: the order CommanderQuota.Plan picked the targets in).
        private readonly int[] planEnemies, planDesired, planRank, deficit, groupsAt;
        private readonly bool[] planPrevious, planAttack, blocked, underWay;
        // Scratch: the units the commander may dispatch (in key order after the sort), their group, and the groups.
        private readonly ulong[] unitKeys, unitGroups, foundKeys, foundGroups, sortKeys;
        private readonly int[] unitTeams, unitLayers, unitGroupIndex, foundTeams, foundLayers, from, sortIndex;
        private readonly SimVec3[] unitGroupAt, foundGroupAt;
        private int units, found;
        private readonly ulong[] groupKeys;
        private readonly int[] groupTeams, groupCounts, groupLayers, groupOrders;
        private readonly SimVec3[] groupAt;
        private readonly bool[] leaderRow;
        private int groups;

        public Commander(CombatSystem combat, MatchPack pack, CaptureSystem capture, IMoveOrders moves, IControlTarget controlled, Squads squads,
            CommanderRules rules, IPointReach reach = null, IUnitLayers layers = null)
        {
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.pack = pack ?? throw new ArgumentNullException(nameof(pack));
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.moves = moves ?? throw new ArgumentNullException(nameof(moves));
            this.controlled = controlled; this.squads = squads; this.layers = layers; this.rules = rules;
            if (rules.ThreatRadiusMm < 1) throw new ArgumentOutOfRangeException(nameof(rules));
            field = new AiField(pack, capture, reach);
            presence = new PointPresence(combat, pack.TeamCount, layers);
            stage = new EntityStage(combat.Entities);
            positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
            threatSquared = new SimVec3(SimFix.Ratio(rules.ThreatRadiusMm, 1000), default, default).LengthSquared;
            teams = pack.TeamCount; points = pack.Points.Length;
            centers = new SimVec3[points];
            for (int i = 0; i < points; i++) centers[i] = pack.Points[i].Center;
            adjacent = new bool[points * points];
            CommanderQuota.Adjacency(centers, points, adjacent);
            // A team's anchor may be its spawn anchor (R3): checked against the same coordinate limit before the match starts.
            foreach (SpawnAnchor anchor in pack.Bases) CommanderQuota.CheckBounds(anchor.Point);
            int flags = teams * points;
            attack = new bool[flags]; attackNext = new bool[flags];
            enemies = new int[flags]; desired = new int[flags]; near = new int[flags]; owners = new int[points];
            int p = points;
            planEnemies = new int[p]; planDesired = new int[p]; planRank = new int[p];
            deficit = new int[p]; groupsAt = new int[p];
            planPrevious = new bool[p]; planAttack = new bool[p]; blocked = new bool[p]; underWay = new bool[p];
            int capacity = combat.Entities.Capacity;
            orderKeys = new ulong[capacity]; nextKeys = new ulong[capacity];
            orderPoints = new int[capacity]; nextPoints = new int[capacity];
            unitKeys = new ulong[capacity]; unitGroups = new ulong[capacity]; foundKeys = new ulong[capacity]; foundGroups = new ulong[capacity];
            sortKeys = new ulong[capacity];
            unitTeams = new int[capacity]; unitLayers = new int[capacity]; unitGroupIndex = new int[capacity];
            foundTeams = new int[capacity]; foundLayers = new int[capacity];
            from = new int[capacity]; sortIndex = new int[capacity];
            unitGroupAt = new SimVec3[capacity]; foundGroupAt = new SimVec3[capacity];
            groupKeys = new ulong[capacity]; groupTeams = new int[capacity]; groupCounts = new int[capacity]; groupLayers = new int[capacity];
            groupOrders = new int[capacity]; groupAt = new SimVec3[capacity];
            leaderRow = new bool[capacity];
            Dispatch = new CommanderDispatch(this);
        }

        public int Order => 41;
        public string StateTypeId => "battle.ai-commander";
        // Version 1 is the layout in SaveState (five numbers, attack flags, a row per unit).
        public int StateVersion => 1;
        public CommanderRules Rules => rules;
        // The "commander" dispatch part: the point the unit remembers (a member asks with its leader's key), -1 without one.
        public IAiDispatch Dispatch { get; }
        public int PointCount => points;
        public int TeamCount => teams;
        public int OrderCount => orderCount;
        public ulong OrderKeyAt(int index) => index >= 0 && index < orderCount ? orderKeys[index] : throw new ArgumentOutOfRangeException(nameof(index));
        public int OrderPointAt(int index) => index >= 0 && index < orderCount ? orderPoints[index] : throw new ArgumentOutOfRangeException(nameof(index));
        // The point the unit with this key remembers; -1 without a row.
        public int CommandOf(ulong key)
        {
            int i = Array.BinarySearch(orderKeys, 0, orderCount, key);
            return i < 0 ? -1 : orderPoints[i];
        }
        // As the last Step saw them (scratch, not saved: stale right after a load until the next Step).
        public int EnemiesNear(int team, int point) => enemies[Flag(team, point)];
        public int Desired(int team, int point) => desired[Flag(team, point)];
        public bool Attacks(int team, int point) => attack[Flag(team, point)];
        // The key a unit is dispatched by: its leader's while its leader is alive, else its own.
        public ulong GroupOf(StableId unit) => squads != null && squads.TryLeader(unit, out StableId leader, out _) ? leader.Value : unit.Value;
        private int Flag(int team, int point)
        {
            if (team < 1 || team > teams) throw new ArgumentOutOfRangeException(nameof(team));
            if ((uint)point >= (uint)points) throw new ArgumentOutOfRangeException(nameof(point));
            return (team - 1) * points + point;
        }

        public void Step(SimContext c)
        {
            // The commander reads only TryBase and TrySpan, neither of which needs Begin; it is called to keep AiField's
            // contract (once per tick before the first read).
            field.Begin(c.Clock.Tick);
            Gather();
            Decide(nextKeys, nextPoints, out nextCount, attackNext);
            ulong[] keys = orderKeys; orderKeys = nextKeys; nextKeys = keys;
            int[] at = orderPoints; orderPoints = nextPoints; nextPoints = at;
            orderCount = nextCount;
            bool[] flags = attack; attack = attackNext; attackNext = flags;
        }

        // R0 and R2: the units the commander may dispatch, sorted by key, grouped by the key they are dispatched by; and the
        // standing units of every team near every point.
        private void Gather()
        {
            Array.Clear(near, 0, near.Length);
            for (int i = 0; i < points; i++) owners[i] = capture.Points[i].Owner;
            found = 0;
            foreach (EntityId entity in stage.Capture())
            {
                // R2: a standing unit counts at the one point whose centre is nearest (ties to the lower index), and only
                // when that centre is within the threat radius.
                if (presence.Stands(entity, out int standing, out SimVec3 where))
                {
                    int nearest = 0; SimWide best = CombatSystem.Horizontal(where - centers[0]).LengthSquared;
                    for (int i = 1; i < points; i++)
                    {
                        SimWide span = CombatSystem.Horizontal(where - centers[i]).LengthSquared;
                        if (span < best) { nearest = i; best = span; }
                    }
                    if (best <= threatSquared) near[(standing - 1) * points + nearest]++;
                }
                // R0: the units the computer steers (AiUnits.Steered), less the squads that regroup.
                if (!AiUnits.Steered(combat, combatants, positions, moves, controlled, entity, out StableId id, out int team, out SimVec3 here)) continue;
                ulong group = id.Value; SimVec3 groupHere = here;
                if (squads != null)
                {
                    // A squad on its way back (Squads.RegroupOf Active, and its members still follow a living leader) is not
                    // dispatched this round and its units keep no rows: SquadDispatch sends it to its rally point first.
                    bool body = squads.SizeOf(id) > 0;
                    if (squads.TryLeader(id, out StableId leader, out SimVec3 leaderHere)) { group = leader.Value; groupHere = leaderHere; body = true; }
                    if (body && squads.RegroupOf(id) == SquadRally.Active) continue;
                }
                foundKeys[found] = id.Value; foundGroups[found] = group; foundTeams[found] = team;
                foundGroupAt[found] = groupHere; foundLayers[found] = layers != null ? layers.LayerOf(entity) : 0; found++;
            }
            for (int t = 1; t <= teams; t++)
                for (int i = 0; i < points; i++)
                {
                    int count = 0;
                    for (int o = 1; o <= teams; o++) if (o != t) count += near[(o - 1) * points + i];
                    enemies[(t - 1) * points + i] = count;
                }
            units = found;
            for (int i = 0; i < units; i++) { sortKeys[i] = foundKeys[i]; sortIndex[i] = i; }
            // Keys are all different, so the order after the sort does not depend on the order the units were found in.
            Array.Sort(sortKeys, sortIndex, 0, units);
            for (int i = 0; i < units; i++)
            {
                int k = sortIndex[i];
                unitKeys[i] = foundKeys[k]; unitGroups[i] = foundGroups[k]; unitTeams[i] = foundTeams[k];
                unitGroupAt[i] = foundGroupAt[k]; unitLayers[i] = foundLayers[k];
            }
            // Groups in group-key order. Team, place and layer are read from the unit with the smallest key in the group. The
            // layer is that unit's own while the place is its leader's; the two could differ only on a map with floor plates,
            // and squads and the commander are both refused there (ContentPolicies).
            for (int i = 0; i < units; i++) { sortKeys[i] = unitGroups[i]; sortIndex[i] = i; }
            Array.Sort(sortKeys, sortIndex, 0, units);
            groups = 0;
            for (int i = 0; i < units; i++)
            {
                int u = sortIndex[i];
                if (groups == 0 || groupKeys[groups - 1] != sortKeys[i])
                {
                    groupKeys[groups] = sortKeys[i]; groupCounts[groups] = 0; from[groups] = u; groups++;
                }
                int g = groups - 1;
                groupCounts[g]++; unitGroupIndex[u] = g;
                if (u < from[g]) from[g] = u;
            }
            for (int g = 0; g < groups; g++)
            {
                int u = from[g];
                groupTeams[g] = unitTeams[u]; groupAt[g] = unitGroupAt[u]; groupLayers[g] = unitLayers[u];
            }
        }

        // R8: a group's old order is its leader's row; without one, the row of the group's unit with the smallest key that
        // has one; -1 if none has. Reads the given rows (the saved ones).
        private void OldOrders(ulong[] keys, int[] at, int count)
        {
            for (int g = 0; g < groups; g++) { groupOrders[g] = -1; leaderRow[g] = false; }
            for (int i = 0; i < units; i++)
            {
                int r = CommandIn(keys, at, count, unitKeys[i]);
                if (r < 0) continue;
                int g = unitGroupIndex[i];
                if (unitKeys[i] == groupKeys[g]) { groupOrders[g] = r; leaderRow[g] = true; }
                else if (!leaderRow[g] && groupOrders[g] < 0) groupOrders[g] = r;
            }
        }

        // R3 to R5 and R8 from the gathered units, the saved rows and attack flags; writes the new rows (in key order) and
        // attack flags into the given buffers, never into the saved ones it reads.
        private void Decide(ulong[] outKeys, int[] outPoints, out int outCount, bool[] outAttack)
        {
            OldOrders(orderKeys, orderPoints, orderCount);
            for (int t = 1; t <= teams; t++) Plan(t, outAttack);
            // R8: every unit of a group with an order remembers it; the units of a group without one have no row.
            outCount = 0;
            for (int i = 0; i < units; i++)
            {
                int p = groupOrders[unitGroupIndex[i]];
                if (p < 0) continue;
                outKeys[outCount] = unitKeys[i]; outPoints[outCount] = p; outCount++;
            }
        }

        private void Plan(int team, bool[] outAttack)
        {
            int row = (team - 1) * points;
            // R3: soldiers, anchor.
            int soldiers = 0;
            for (int g = 0; g < groups; g++)
                if (groupTeams[g] == team) soldiers += groupCounts[g];
            SimVec3 anchor = default;
            if (!field.TryBase(team, out anchor))
            {
                int own = -1;
                for (int i = 0; i < points && own < 0; i++) if (owners[i] == team) own = i;
                // A match has at least one point (MatchPack).
                anchor = own >= 0 ? centers[own] : centers[0];
            }
            for (int i = 0; i < points; i++) { planEnemies[i] = enemies[row + i]; planPrevious[i] = attack[row + i]; underWay[i] = false; }
            // The points this team's groups are on their way to (their old orders, before R4): Plan keeps them candidates.
            // Not saved: rebuilt every tick from the saved rows.
            for (int g = 0; g < groups; g++)
                if (groupTeams[g] == team && groupOrders[g] >= 0) underWay[groupOrders[g]] = true;
            CommanderQuota.Plan(team, points, owners, centers, planEnemies, adjacent, soldiers, anchor, planPrevious, rules.Quota, planDesired, planAttack,
                underWay, planRank);
            for (int i = 0; i < points; i++) { desired[row + i] = planDesired[i]; outAttack[row + i] = planAttack[i]; }

            // R4: which old orders stay. A point that is not a target this tick, or that the group cannot walk to, lets it
            // go; a target the group can walk to keeps it (nobody is moved sideways to even out the targets).
            for (int g = 0; g < groups; g++)
            {
                int p = groupOrders[g];
                if (groupTeams[g] != team || p < 0) continue;
                if (!planAttack[p] || !field.TrySpan(groupAt[g], groupLayers[g], p, out _)) groupOrders[g] = -1;
            }

            // R5: the groups without an order (deficit from the plan on the counts now).
            int idle = 0;
            for (int p = 0; p < points; p++) { deficit[p] = planDesired[p]; groupsAt[p] = 0; blocked[p] = false; }
            for (int g = 0; g < groups; g++)
            {
                if (groupTeams[g] != team) continue;
                int p = groupOrders[g];
                if (p < 0) { idle++; continue; }
                deficit[p] -= groupCounts[g]; groupsAt[p]++;
            }
            while (idle > 0)
            {
                int p = Next();
                if (p < 0) break;
                int g = Nearest(team, p);
                if (g < 0) { blocked[p] = true; continue; }
                groupOrders[g] = p; deficit[p] -= groupCounts[g]; groupsAt[p]++; idle--;
            }
        }

        // R5's choice of point among this tick's targets (ties to the target picked earlier, planRank; points no group could
        // reach are passed over): 1. a target nobody goes to, largest wanted; 2. the target with the largest deficit, even
        // below zero.
        private int Next()
        {
            int pick = -1;
            for (int i = 0; i < points; i++)
                if (planAttack[i] && groupsAt[i] == 0 && !blocked[i] && (pick < 0 || planDesired[i] > planDesired[pick]
                    || (planDesired[i] == planDesired[pick] && planRank[i] < planRank[pick]))) pick = i;
            if (pick >= 0) return pick;
            for (int i = 0; i < points; i++)
                if (planAttack[i] && !blocked[i] && (pick < 0 || deficit[i] > deficit[pick]
                    || (deficit[i] == deficit[pick] && planRank[i] < planRank[pick]))) pick = i;
            return pick;
        }

        // The team's group without an order nearest to point p that can walk to it (ties to the smaller key).
        private int Nearest(int team, int p)
        {
            int best = -1; SimWide bestSpan = default;
            for (int g = 0; g < groups; g++)
            {
                if (groupTeams[g] != team || groupOrders[g] >= 0) continue;
                if (!field.TrySpan(groupAt[g], groupLayers[g], p, out SimWide span)) continue;
                if (best < 0 || span < bestSpan) { best = g; bestSpan = span; }
            }
            return best;
        }

        private static int CommandIn(ulong[] keys, int[] at, int count, ulong key)
        {
            int i = Array.BinarySearch(keys, 0, count, key);
            return i < 0 ? -1 : at[i];
        }

        // R8: the five numbers, the attack flags, then the rows (unit key, point) in key order.
        public void SaveState(SimStateWriter writer)
        {
            WriteRules(writer);
            writer.Int32(attack.Length);
            for (int i = 0; i < teams * points; i++) writer.Bool(attack[i]);
            writer.Int32(orderCount);
            for (int i = 0; i < orderCount; i++) { writer.UInt64(orderKeys[i]); writer.Int32(orderPoints[i]); }
        }

        private void WriteRules(SimStateWriter writer)
        {
            CommanderQuotaRules q = rules.Quota;
            writer.Int32(q.MaxAttackTargets); writer.Int32(q.EnemyWeight); writer.Int32(q.DistanceWeightPerMetre); writer.Int32(q.Stickiness); writer.Int32(rules.ThreatRadiusMm);
        }

        public void LoadState(SimStateReader reader)
        {
            CommanderQuotaRules q = rules.Quota;
            bool same = reader.Int32() == q.MaxAttackTargets; same &= reader.Int32() == q.EnemyWeight;
            same &= reader.Int32() == q.DistanceWeightPerMetre; same &= reader.Int32() == q.Stickiness; same &= reader.Int32() == rules.ThreatRadiusMm;
            SimStateReader.Require(same, "Commander rules differ.");
            SimStateReader.Require(reader.Int32() == attack.Length, "Commander teams or points differ.");
            for (int i = 0; i < teams * points; i++) attack[i] = reader.Bool();
            orderCount = reader.Count(orderKeys.Length);
            for (int i = 0; i < orderCount; i++)
            {
                ulong key = reader.UInt64();
                SimStateReader.Require(i == 0 || key > orderKeys[i - 1], "Commander rows are not in strictly increasing key order.");
                int point = reader.Int32();
                SimStateReader.Require(point >= 0 && point < points, "Commander row point is out of range.");
                orderKeys[i] = key; orderPoints[i] = point;
            }
        }

        public void WriteState(StateHash hash)
        {
            CommanderQuotaRules q = rules.Quota;
            hash.Add(StateTypeId); hash.Add(StateVersion);
            hash.Add(q.MaxAttackTargets); hash.Add(q.EnemyWeight); hash.Add(q.DistanceWeightPerMetre); hash.Add(q.Stickiness); hash.Add(rules.ThreatRadiusMm);
            hash.Add(attack.Length);
            for (int i = 0; i < teams * points; i++) hash.Add(attack[i]);
            hash.Add(orderCount);
            for (int i = 0; i < orderCount; i++) { hash.Add(orderKeys[i]); hash.Add(orderPoints[i]); }
        }

        // R7: the dispatch part. Its answer depends on the unit's key and on the commander's saved orders.
        private sealed class CommanderDispatch : IAiDispatch
        {
            private readonly Commander commander;
            internal CommanderDispatch(Commander commander) { this.commander = commander; }

            public ulong Fold(ulong digest)
            {
                CommanderRules r = commander.rules; CommanderQuotaRules q = r.Quota;
                var hash = new StateHash(); hash.Add("battle.ai.commander.v1"); hash.Add(digest);
                hash.Add(q.MaxAttackTargets); hash.Add(q.EnemyWeight); hash.Add(q.DistanceWeightPerMetre); hash.Add(q.Stickiness); hash.Add(r.ThreatRadiusMm);
                return hash.Value;
            }

            public int Pick(in AiUnit unit, AiField field) => commander.CommandOf(unit.Id.Value);
        }
    }
}
