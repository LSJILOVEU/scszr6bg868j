using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WarCore.Replay
{
    // Picks up to three stretches of an export worth watching when squads have tempers or regroup: a rash squad that keeps
    // walking with the enemy in range, a steady squad that stands meanwhile, and a squad that goes back and comes out again.
    // The same tape always gives the same clips.
    public static class Clips
    {
        // A steady squad needs this many standing frames inside the rash clip to be shown beside it; every clip gets
        // this many ticks before and after.
        public const int MinStanding = 10, PadTicks = 60;
        public const int Rash = 2, Steady = 3, Regrouping = 1, Waiting = 2;
        // Row.Step: no previous frame to compare with, moved at most Metrics.MovedDistance since it, moved further.
        public const int Unknown = 0, Stood = 1, Walked = 2;

        // One squad in one recorded frame, named by its leader's unit index. Near: the leader is calm and has an enemy
        // in sight (within Metrics.SightRange, no wall between them).
        public readonly struct Row
        {
            public readonly int Leader, Team, Mood, Rally, Step;
            public readonly bool Near;
            public Row(int leader, int team, int mood, int rally, bool near, int step)
            { Leader = leader; Team = team; Mood = mood; Rally = rally; Near = near; Step = step; }
        }

        // The rows of every recorded frame, in frame order.
        public sealed class Tape
        {
            internal readonly List<long> Ticks = new List<long>();
            internal readonly List<int> Starts = new List<int>();
            internal readonly List<Row> Rows = new List<Row>();
            public void Begin(long tick) { Ticks.Add(tick); Starts.Add(Rows.Count); }
            public void Add(Row row) => Rows.Add(row);
            internal int End(int frame) => frame + 1 < Starts.Count ? Starts[frame + 1] : Rows.Count;
            internal int Leaders { get { int top = 0; foreach (Row row in Rows) top = Math.Max(top, row.Leader + 1); return top; } }
        }

        // Kind: "rash", "steady" or "regroup". Note: for steady 1 = picked inside the rash stretch, 0 = another stretch;
        // for regroup 1 = ended by filling up, 0 = ended by waiting too long; 1 for rash.
        public readonly struct Clip
        {
            public readonly string Kind; public readonly int Team, Leader, Note; public readonly long From, To;
            public Clip(string kind, int team, int leader, long from, long to, int note)
            { Kind = kind; Team = team; Leader = leader; From = from; To = to; Note = note; }
        }

        private static bool Walks(Row row) => row.Mood == Rash && row.Rally != Regrouping && row.Near && row.Step == Walked;
        private static bool Stands(Row row) => row.Mood == Steady && row.Rally != Regrouping && row.Near && row.Step == Stood;

        public static List<Clip> Pick(Tape tape)
        {
            var clips = new List<Clip>();
            if (tape.Ticks.Count == 0) return clips;
            int leader, team, first, last;
            bool rash = Longest(tape, Walks, out int rashLeader, out int rashTeam, out int rashFirst, out int rashLast);
            if (rash) clips.Add(Padded(tape, "rash", rashTeam, rashLeader, rashFirst, rashLast, 1));
            // "Meanwhile" is the rash clip as it is shown, its padding included.
            if (rash && Most(tape, Frame(tape, clips[0].From, true), Frame(tape, clips[0].To, false), out leader, out team))
                clips.Add(new Clip("steady", team, leader, clips[0].From, clips[0].To, 1));
            else if (Longest(tape, Stands, out leader, out team, out first, out last))
                clips.Add(Padded(tape, "steady", team, leader, first, last, 0));
            if (Episode(tape, out leader, out team, out first, out last, out int filled))
                clips.Add(Padded(tape, "regroup", team, leader, first, last, filled));
            return clips;
        }

        private static Clip Padded(Tape tape, string kind, int team, int leader, int first, int last, int note) => new Clip(kind, team, leader,
            Math.Max(tape.Ticks[0], tape.Ticks[first] - PadTicks), Math.Min(tape.Ticks[tape.Ticks.Count - 1], tape.Ticks[last] + PadTicks), note);

        // The first frame at or after a tick, or the last at or before it.
        private static int Frame(Tape tape, long tick, bool first)
        {
            int frame = first ? 0 : tape.Ticks.Count - 1;
            while (first ? tape.Ticks[frame] < tick : tape.Ticks[frame] > tick) frame += first ? 1 : -1;
            return frame;
        }

        // The longest run of consecutive frames in which one squad is wanted; equal runs: the earlier start, then the
        // lower leader.
        private static bool Longest(Tape tape, Func<Row, bool> wanted, out int leader, out int team, out int first, out int last)
        {
            int count = tape.Leaders; var start = new int[count]; var seen = new int[count];
            for (int i = 0; i < count; i++) seen[i] = -2;
            leader = team = first = last = -1; int best = 0;
            for (int frame = 0; frame < tape.Ticks.Count; frame++)
                for (int r = tape.Starts[frame]; r < tape.End(frame); r++)
                {
                    Row row = tape.Rows[r];
                    if (!wanted(row)) continue;
                    if (seen[row.Leader] != frame - 1) start[row.Leader] = frame;
                    seen[row.Leader] = frame;
                    int length = frame - start[row.Leader] + 1, from = start[row.Leader];
                    if (length < best || (length == best && (from > first || (from == first && row.Leader >= leader)))) continue;
                    best = length; leader = row.Leader; team = row.Team; first = from; last = frame;
                }
            return best > 0;
        }

        // The steady squad with the most standing frames between two frames (inclusive), at least MinStanding; equal
        // counts: the lower leader.
        private static bool Most(Tape tape, int first, int last, out int leader, out int team)
        {
            var frames = new int[tape.Leaders]; var teams = new int[tape.Leaders];
            for (int frame = first; frame <= last; frame++)
                for (int r = tape.Starts[frame]; r < tape.End(frame); r++)
                    if (Stands(tape.Rows[r])) { frames[tape.Rows[r].Leader]++; teams[tape.Rows[r].Leader] = tape.Rows[r].Team; }
            leader = -1; team = 0; int best = MinStanding - 1;
            for (int i = 0; i < frames.Length; i++) if (frames[i] > best) { best = frames[i]; leader = i; team = teams[i]; }
            return leader >= 0;
        }

        // The first regrouping that ended with the squad filled up (regrouping, then no mark), from the frame it began to
        // the frame it ended; when no squad ever filled up, the first that ended by waiting too long (regrouping, then
        // waiting). Several in one frame: the lower leader.
        private static bool Episode(Tape tape, out int leader, out int team, out int first, out int last, out int filled)
        {
            int count = tape.Leaders; var began = new int[count]; var seen = new int[count]; var was = new int[count];
            for (int i = 0; i < count; i++) seen[i] = -2;
            leader = team = first = last = -1; filled = 0;
            for (int frame = 0; frame < tape.Ticks.Count; frame++)
                for (int r = tape.Starts[frame]; r < tape.End(frame); r++)
                {
                    Row row = tape.Rows[r];
                    bool went = seen[row.Leader] == frame - 1 && was[row.Leader] == Regrouping;
                    if (row.Rally == Regrouping && !went) began[row.Leader] = frame;
                    seen[row.Leader] = frame; was[row.Leader] = row.Rally;
                    if (!went || row.Rally == Regrouping) continue;
                    if (row.Rally != Waiting)
                    { leader = row.Leader; team = row.Team; first = began[row.Leader]; last = frame; filled = 1; return true; }
                    if (leader < 0) { leader = row.Leader; team = row.Team; first = began[row.Leader]; last = frame; }
                }
            return leader >= 0;
        }

        // ,"clips":[{"k":"rash","team":1,"leader":12,"from":300,"to":640,"note":1}, ...]
        public static void Write(StringBuilder sb, List<Clip> clips)
        {
            sb.Append(",\"clips\":[");
            for (int i = 0; i < clips.Count; i++)
            {
                Clip clip = clips[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"k\":\"").Append(clip.Kind).Append("\",\"team\":").Append(clip.Team).Append(",\"leader\":").Append(clip.Leader)
                  .Append(",\"from\":").Append(clip.From.ToString(CultureInfo.InvariantCulture)).Append(",\"to\":").Append(clip.To.ToString(CultureInfo.InvariantCulture))
                  .Append(",\"note\":").Append(clip.Note).Append('}');
            }
            sb.Append(']');
        }
    }
}
