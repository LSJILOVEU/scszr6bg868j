using System;
using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // AI-M1: the ancient cartridge on the ANC-2 field (96 x 64 m flat ground, three points on the middle line, no reserves),
    // 60 against 60 in six columns, shields in the two front columns and swords behind. PLACEHOLDER fixture values. The layer
    // "ancient" is the game as it was; "ancient-nerve" turns the pressure, the engage part "bound" and both AI-M1 fields on.
    internal static class AiM1Field
    {
        internal const string Plain = "ancient", Nerve = "ancient-nerve";
        internal const int Units = 120;

        internal const string FightField = "\"meleeNerveFightMm\": 4000";

        // The cartridge folder; fight (millimetres, 0 or more) replaces the fight range the nerve layer writes.
        private sealed class Source : IContentSource
        {
            private readonly IContentSource shipped = new FileContentSource(Anc2.ShippedDirectory);
            private readonly int fight;
            internal Source(int fight) { this.fight = fight; }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (!shipped.TryRead(layerId, out text, out location)) return false;
                if (fight < 0 || layerId != Nerve) return true;
                if (!text.Contains(FightField)) throw new InvalidOperationException("the nerve layer no longer writes " + FightField);
                text = text.Replace(FightField, "\"meleeNerveFightMm\": " + fight);
                return true;
            }
        }

        internal static ContentPack Load(string layer, int fight = -1) => ContentLoader.Load(new Source(fight), layer);

        // shift moves team 2 along z by so many metres: the two blocks then meet corner to corner instead of face to face.
        internal static M6World World(string layer, ulong seed = 41, int shift = 0, int fight = -1)
        {
            ContentPack pack = Load(layer, fight);
            var units = new UnitSpawn[Units];
            for (int i = 0; i < 60; i++)
            {
                int column = i % 6, row = i / 6;
                string type = column < 2 ? "ancient.guard" : "ancient.swordsman";
                long z = 22000 + 2000 * row;
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, type, 1, 1, 30000 - 1500 * column, z);
                units[i + 60] = UnitSpawn.OfType((ulong)i + 61, pack, type, 2, 2, 66000 + 1500 * column, z + 1000 * shift);
            }
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(48, 0, 12), SimFix.FromInt(4)),
                new ControlPointDef(2, SimVec3.Metres(48, 0, 32), SimFix.FromInt(4)),
                new ControlPointDef(3, SimVec3.Metres(48, 0, 52), SimFix.FromInt(4)),
            };
            var anchors = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 32)), new SpawnAnchor(2, SimVec3.Metres(92, 0, 32)) };
            return Anc2.Build(pack, units, points, anchors, seed);
        }

        // What one whole game showed. Ticks are counted from 1 (the tick the step ended on).
        internal sealed class Report
        {
            internal int Winner, EndTick, Dead1, Dead2;
            internal bool Finished;
            // The first tick a strike landed; landed strikes; the median, over the units that died, of ticks from Contact to death.
            internal int Contact = -1, Landed, MedianSurvival = -1;
            // Units that were ever broken; of those, the ones that left broken alive and later swung again.
            internal int Broke, Returned;
            // The largest share of living units broken at one tick: over the field while 20 or more are alive, and in one team
            // while 20 or more of that team are alive (thousandths).
            internal int PeakBrokenPermille, PeakTeamBrokenPermille, PeakPinnedPermille;
            // Idle: a living unit that stands where it stood a tick ago and has not swung for 30 ticks. From the first
            // landed strike to the end: the share of unit-ticks that were idle, the largest share at one tick while 20 or
            // more are alive (thousandths), and the number of such ticks at which more than half of the living were idle.
            internal int MeanIdlePermille, PeakIdlePermille, HalfIdleTicks;
            internal ulong Hash;

            // Everything but the hash, which carries the seed.
            internal (int, int, int, int, int, int, int) Fight => (Winner, EndTick, Dead1, Dead2, Contact, Landed, MedianSurvival);
            internal (int, int, int, int, int) Nerves => (Broke, Returned, PeakBrokenPermille, PeakTeamBrokenPermille, PeakPinnedPermille);
            internal (int, int, int) Idle => (MeanIdlePermille, PeakIdlePermille, HalfIdleTicks);

            public override string ToString() =>
                "winner " + Winner + " end " + EndTick + (Finished ? "" : " (not decided)") + " dead " + Dead1 + "/" + Dead2 + " contact " + Contact
                + " landed " + Landed + " survival " + MedianSurvival + " broke " + Broke + " returned " + Returned
                + " idle " + MeanIdlePermille + "/" + PeakIdlePermille + "/" + HalfIdleTicks + " peakBroken " + PeakBrokenPermille + " peakTeamBroken " + PeakTeamBrokenPermille + " peakPinned " + PeakPinnedPermille
                + " hash " + Hash.ToString("X16");
        }

        internal static Report Run(string layer, ulong seed = 41, int shift = 0, int fight = -1)
        {
            M6World w = World(layer, seed, shift, fight);
            Pressure pressure = w.System<Pressure>();
            var r = new Report();
            var died = new List<int>();
            // Per unit: 0 never broken, 1 broken now, 2 was broken and is not any more, 3 swung after that.
            var state = new byte[Units + 1];
            var dead = new bool[Units + 1];
            // Per unit: the tick of its last swing (any strike record it made) and where it stood a tick ago.
            var swung = new int[Units + 1];
            var stood = new SimVec3[Units + 1];
            long idleTicks = 0, aliveTicks = 0;
            for (int key = 0; key <= Units; key++) swung[key] = -1000;
            UnitSpawn[] units = new UnitSpawn[Units];
            var ids = new StableId[Units];
            for (int i = 0; i < Units; i++) { units[i] = w.Recipe.Units[i]; ids[i] = w.Sim.Entities.StableOf(w.Unit(units[i].Key)); }
            while (!w.Mode.IsFinished && w.Sim.Clock.Tick < w.Recipe.Match.MaxTicks)
            {
                w.Step();
                int tick = (int)w.Sim.Clock.Tick;
                foreach (StrikeRecord strike in w.Combat.Strikes)
                {
                    if (strike.Kind == StrikeKind.Landed) { r.Landed++; if (r.Contact < 0) r.Contact = tick; }
                    ulong striker = KeyOf(units, ids, strike.Striker);
                    if (striker != 0 && state[striker] == 2) state[striker] = 3;
                    if (striker != 0) swung[striker] = tick;
                }
                int alive = 0, broken = 0, pinned = 0, idle = 0;
                int[] teamAlive = new int[3], teamBroken = new int[3];
                for (int i = 0; i < Units; i++)
                {
                    UnitSpawn unit = units[i];
                    ulong key = unit.Key;
                    if (w.Dead(key))
                    {
                        if (!dead[key]) { dead[key] = true; if (r.Contact >= 0) died.Add(tick - r.Contact); }
                        continue;
                    }
                    alive++; teamAlive[unit.Team]++;
                    AiNerve nerve = pressure != null ? pressure.Level(ids[i]) : AiNerve.Calm;
                    if (nerve == AiNerve.Broken) { broken++; teamBroken[unit.Team]++; if (state[key] != 1) state[key] = 1; }
                    else if (state[key] == 1) state[key] = 2;
                    if (nerve == AiNerve.Pinned) pinned++;
                    SimVec3 at = w.Read<Position>(key).Value;
                    if (at.Equals(stood[key]) && tick - swung[key] > 30) idle++;
                    stood[key] = at;
                }
                if (r.Contact >= 0) { idleTicks += idle; aliveTicks += alive; }
                if (alive >= 20)
                {
                    if (r.Contact >= 0) { r.PeakIdlePermille = Math.Max(r.PeakIdlePermille, idle * 1000 / alive); if (idle * 2 > alive) r.HalfIdleTicks++; }
                    r.PeakBrokenPermille = Math.Max(r.PeakBrokenPermille, broken * 1000 / alive);
                    r.PeakPinnedPermille = Math.Max(r.PeakPinnedPermille, pinned * 1000 / alive);
                }
                for (int team = 1; team <= 2; team++)
                    if (teamAlive[team] >= 20) r.PeakTeamBrokenPermille = Math.Max(r.PeakTeamBrokenPermille, teamBroken[team] * 1000 / teamAlive[team]);
            }
            for (int key = 1; key <= Units; key++) { if (state[key] != 0) r.Broke++; if (state[key] == 3) r.Returned++; }
            died.Sort();
            if (died.Count > 0) r.MedianSurvival = died[died.Count / 2];
            if (aliveTicks > 0) r.MeanIdlePermille = (int)(idleTicks * 1000 / aliveTicks);
            r.Finished = w.Mode.IsFinished; r.Winner = w.Mode.Winner; r.EndTick = (int)w.Sim.Clock.Tick;
            r.Dead1 = Anc2.Deaths(w, 1); r.Dead2 = Anc2.Deaths(w, 2); r.Hash = w.Hash;
            return r;
        }

        private static ulong KeyOf(UnitSpawn[] units, StableId[] ids, StableId id)
        {
            for (int i = 0; i < Units; i++) if (ids[i].Equals(id)) return units[i].Key;
            return 0;
        }
    }
}
