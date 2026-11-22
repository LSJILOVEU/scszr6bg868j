using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    // BLAST-1: one blast that went off this tick. Victims: VictimCount hits from VictimStart in BlastSystem.Hits.
    public readonly struct BlastRecord
    {
        public readonly SimVec3 Point;
        public readonly int Blast, Team;
        public readonly StableId Source;
        public readonly long Serial;
        public readonly int VictimStart, VictimCount;
        public BlastRecord(SimVec3 point, int blast, StableId source, int team, long serial, int victimStart, int victimCount)
        {
            Point = point; Blast = blast; Source = source; Team = team; Serial = serial; VictimStart = victimStart; VictimCount = victimCount;
        }
    }

    // BLAST-1: one damage command of a blast: the victim, the damage issued (0 while shielded) and the distance (raw) from the
    // blast point to the victim's body centre.
    public readonly struct BlastHit
    {
        public readonly StableId Victim;
        public readonly long DamageRaw, DistanceRaw;
        public BlastHit(StableId victim, long damageRaw, long distanceRaw) { Victim = victim; DamageRaw = damageRaw; DistanceRaw = distanceRaw; }
    }

    // BLAST-1: the blast queue (Order 28, Queries, before player fire 29 and combat 30). Registered only when the combat pack has
    // blasts, so a world without them keeps its systems, state bytes and hashes. First in, first out: each tick up to PerTick
    // queued blasts go off in queue order; the rest wait, in order, for the next ticks. A blast queued before Order 28 of a tick
    // goes off on that tick, one queued later on the next. Each queued blast gets the next serial (from 1), which names it in
    // its damage keys. The queue and the serial are saved and hashed; this tick's records are not.
    public sealed class BlastSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly CombatSystem combat;
        private readonly BlastRules rules;
        private readonly SimVec3[] points;
        private readonly int[] kinds, teams;
        private readonly StableId[] sources;
        private readonly long[] serials;
        private int head, count;
        private long serial;
        private readonly BlastRecord[] records;
        private int recordCount;
        private readonly StableId[] victims;
        private readonly long[] damages, distances;
        private int hitCount;

        // unitCount: the world's units (reserves included); one blast hits each of them at most once.
        public BlastSystem(Simulation sim, CombatSystem combat, int unitCount)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            if (combat == null) throw new ArgumentNullException(nameof(combat));
            if (unitCount < 0) throw new ArgumentOutOfRangeException(nameof(unitCount));
            rules = combat.Pack.Blasts ?? throw new InvalidOperationException("The combat pack has no blasts.");
            this.sim = sim; this.combat = combat;
            int queue = rules.Queue;
            points = new SimVec3[queue]; kinds = new int[queue]; teams = new int[queue]; sources = new StableId[queue]; serials = new long[queue];
            records = new BlastRecord[rules.PerTick];
            int hits = checked(rules.PerTick * unitCount);
            victims = new StableId[hits]; damages = new long[hits]; distances = new long[hits];
            combat.AttachBlasts(this);
        }

        public int Order => 28;
        public string StateTypeId => "combat.blasts";
        public int StateVersion => 1;
        public BlastRules Rules => rules;
        // Blasts waiting, and the serial the last queued blast got (0 before the first).
        public int Count => count;
        public long Serial => serial;
        // This tick's blasts in the order they went off, and their hits.
        public ReadOnlySpan<BlastRecord> Records => records.AsSpan(0, recordCount);
        public BlastHit Hit(int index)
        {
            if ((uint)index >= (uint)hitCount) throw new ArgumentOutOfRangeException(nameof(index));
            return new BlastHit(victims[index], damages[index], distances[index]);
        }
        public int HitCount => hitCount;

        // The i-th waiting blast (0 = the next to go off): point, kind, source, team, serial.
        public (SimVec3 Point, int Blast, StableId Source, int Team, long Serial) Waiting(int index)
        {
            if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
            int at = Slot(index);
            return (points[at], kinds[at], sources[at], teams[at], serials[at]);
        }

        private int Slot(int index) => (head + index) % points.Length;

        // Queues a blast of kind blast (an index into the pack's kinds) at point, for source (default = none, a scripted blast)
        // of team (0 = none). A full queue throws: the queue bound is the pack's, and a caller that fills it has a broken bound.
        public long Enqueue(SimVec3 point, int blast, StableId source, int team)
        {
            if ((uint)blast >= (uint)rules.Kinds.Length) throw new ArgumentOutOfRangeException(nameof(blast));
            if (team < 0) throw new ArgumentOutOfRangeException(nameof(team));
            point.ValidateWorld();
            if (count == points.Length) throw new InvalidOperationException("Blast queue capacity exceeded (" + points.Length + ").");
            int at = Slot(count++);
            points[at] = point; kinds[at] = blast; sources[at] = source; teams[at] = team; serials[at] = ++serial;
            return serial;
        }

        public void Step(SimContext c)
        {
            long tick = c.Clock.Tick;
            recordCount = 0; hitCount = 0;
            int go = Math.Min(count, rules.PerTick);
            for (int i = 0; i < go; i++)
            {
                int at = head;
                int written = combat.Detonate(c, in rules.Kinds[kinds[at]], points[at], sources[at], teams[at], serials[at], tick,
                    victims, damages, distances, hitCount);
                records[recordCount++] = new BlastRecord(points[at], kinds[at], sources[at], teams[at], serials[at], hitCount, written);
                hitCount += written;
                points[at] = default; kinds[at] = 0; sources[at] = default; teams[at] = 0; serials[at] = 0;
                head = (head + 1) % points.Length;
                count--;
            }
        }

        // Saved: the serial, the number waiting, then each waiting blast in queue order (point raw, kind, source, team, serial).
        public void SaveState(SimStateWriter writer)
        {
            writer.Int64(serial);
            writer.Int32(count);
            for (int i = 0; i < count; i++)
            {
                int at = Slot(i);
                writer.Int64(points[at].X.Raw); writer.Int64(points[at].Y.Raw); writer.Int64(points[at].Z.Raw);
                writer.Int32(kinds[at]); writer.UInt64(sources[at].Value); writer.Int32(teams[at]); writer.Int64(serials[at]);
            }
        }

        // A restore checks: at most Queue waiting, every kind of the pack, teams not negative, points inside the world, serials
        // strictly rising and none above the saved serial.
        public void LoadState(SimStateReader reader)
        {
            long savedSerial = reader.Int64();
            SimStateReader.Require(savedSerial >= 0, "Blast serial is negative.");
            int n = reader.Count(points.Length);
            var loaded = new (SimVec3 Point, int Kind, StableId Source, int Team, long Serial)[n];
            long last = 0;
            for (int i = 0; i < n; i++)
            {
                long x = reader.Int64(), y = reader.Int64(), z = reader.Int64();
                int kind = reader.Int32();
                var source = new StableId(reader.UInt64());
                int team = reader.Int32();
                long s = reader.Int64();
                SimStateReader.Require(x >= -MaxCoordinateRaw && x <= MaxCoordinateRaw && y >= -MaxCoordinateRaw && y <= MaxCoordinateRaw
                    && z >= -MaxCoordinateRaw && z <= MaxCoordinateRaw, "Blast point is out of range.");
                SimStateReader.Require(kind >= 0 && kind < rules.Kinds.Length, "Blast kind is not a kind of the pack.");
                SimStateReader.Require(team >= 0, "Blast team is negative.");
                SimStateReader.Require(s > last && s <= savedSerial, "Blast serials are not rising or exceed the saved serial.");
                last = s;
                loaded[i] = (new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z)), kind, source, team, s);
            }
            Array.Clear(points, 0, points.Length); Array.Clear(kinds, 0, kinds.Length); Array.Clear(sources, 0, sources.Length);
            Array.Clear(teams, 0, teams.Length); Array.Clear(serials, 0, serials.Length);
            for (int i = 0; i < n; i++)
            { points[i] = loaded[i].Point; kinds[i] = loaded[i].Kind; sources[i] = loaded[i].Source; teams[i] = loaded[i].Team; serials[i] = loaded[i].Serial; }
            head = 0; count = n; serial = savedSerial; recordCount = 0; hitCount = 0;
        }

        // Points stay within 2^32 raw (65536 m), as projectile points.
        private const long MaxCoordinateRaw = 1L << 32;

        public void WriteState(StateHash hash)
        {
            hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(serial); hash.Add(count);
            for (int i = 0; i < count; i++)
            {
                int at = Slot(i);
                hash.Add(points[at].X.Raw); hash.Add(points[at].Y.Raw); hash.Add(points[at].Z.Raw);
                hash.Add(kinds[at]); hash.Add(sources[at].Value); hash.Add(teams[at]); hash.Add(serials[at]);
            }
        }
    }
}
