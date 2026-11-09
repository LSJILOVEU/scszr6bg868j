using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // P3 fixtures, built on P1's and P2's (their files are not touched): A2's map with its wall, the same cast and walk, but
    // a fast rifle with effect 0 (5-tick interval, 10 rounds, 60-tick reload, 10..30 m, no spread) so that a burst and its
    // pause fit in a short run. The combat pack is written out with the values of P2Fixture.cs (ally-check miss 0, ally
    // hold 30000 milli-ticks). Per-tick rows reuse P2.Shots.
    internal static class P3
    {
        internal const ulong KeyA = P1.KeyA, KeyB = P1.KeyB, KeyC = P1.KeyC, KeyA2 = P1.KeyA2;
        internal const int Interval = 5, Magazine = 10, Reload = 60;
        internal const int AllyMissRaw = 0;
        internal const long AllyHoldMilli = 30000;

        internal static WeaponDef Rifle() => new WeaponDef(5000, 10, 60000, SimFix.FromInt(10), SimFix.FromInt(30), 0);

        internal static CombatPack Combat() =>
            new CombatPack(new[] { Rifle() }, new RwrHitResolver(), new SightLimited(new NearestVisibleEnemy(), A2.Sight), 100 * A2.S,
                friendlyFire: false, friendlyFireLocked: false, allyCheckMissRaw: AllyMissRaw, allyHoldMilli: AllyHoldMilli);

        // The ai section with only the given fields, in the order reactionTicks, senseTicks, burstShots, burstPauseTicks.
        internal static string Ai(int reaction, int? sense = null, int? burst = null, int? pause = null)
        {
            var parts = new List<string> { "\"reactionTicks\": " + reaction };
            if (sense != null) parts.Add("\"senseTicks\": " + sense.Value);
            if (burst != null) parts.Add("\"burstShots\": " + burst.Value);
            if (pause != null) parts.Add("\"burstPauseTicks\": " + pause.Value);
            return "{ " + string.Join(", ", parts) + " }";
        }

        internal static WorldRecipe Recipe(UnitSpawn[] units, string ai, params IWorldOption[] extensions)
        {
            WorldRecipe r = A3.Recipe(units, A3.Load(P1.PackJson(ai))).WithCombat(Combat());
            foreach (IWorldOption e in extensions) r = r.With(e);
            return r;
        }
        internal static M6World World(UnitSpawn[] units, string ai, bool reverse = false, params IWorldOption[] extensions) =>
            new M6World(Recipe(units, ai, extensions), WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);

        internal static M6World RunTo(string ai, long last, bool reverse = false, UnitSpawn[] units = null, params IWorldOption[] extensions)
        {
            M6World w = World(units ?? P2.Cast(), ai, reverse, extensions);
            for (long tick = 1; tick <= last; tick++) w.Sim.Step(P2.Drive(w, tick, P1.Walk));
            return w;
        }

        internal sealed class Trace
        {
            internal M6World W;
            internal readonly P2.Shots A = new P2.Shots(), A2 = new P2.Shots();
            internal readonly List<ulong> Hashes = new List<ulong>();
            internal readonly List<int> Counts = new List<int>();
            internal ulong Id(ulong key) => P2.Id(W, key).Value;
        }

        private static void Record(M6World w, ulong key, P2.Shots s, long tick, bool present)
        {
            if (!present) { s.Sel.Add(0); s.Target.Add(0); s.Fire.Add(false); s.Due.Add(0); s.Ammo.Add(0); s.At.Add(default); s.Origin.Add(default); s.End.Add(default); return; }
            s.Sel.Add(P1.Selected(w, key));
            Combatant unit = w.Read<Combatant>(key);
            s.Due.Add(unit.DueMilli); s.Ammo.Add(unit.Ammo); s.At.Add(w.Read<Position>(key).Value);
            bool fired = false; ulong hit = 0; SimVec3 origin = default, end = default;
            if (tick > 0)
            {
                StableId id = P2.Id(w, key);
                foreach (FireRecord f in w.Combat.Fires)
                    if (f.Shooter.Equals(id)) { fired = true; hit = f.Target.Value; origin = f.Origin; end = f.End; }
            }
            s.Fire.Add(fired); s.Target.Add(hit); s.Origin.Add(origin); s.End.Add(end);
        }

        internal static Trace Run(UnitSpawn[] units, P1.Move[] script, string ai, long last, bool reverse = false, params IWorldOption[] extensions)
        {
            var r = new Trace { W = World(units, ai, reverse, extensions) };
            bool twin = Array.Exists(units, u => u.Key == KeyA2);
            bool counted = P2.Systems(r.W).Contains("battle.ai-fire");
            for (long tick = 0; tick <= last; tick++)
            {
                if (tick > 0) r.W.Sim.Step(P2.Drive(r.W, tick, script));
                Record(r.W, KeyA, r.A, tick, true);
                Record(r.W, KeyA2, r.A2, tick, twin);
                r.Hashes.Add(r.W.Hash);
                r.Counts.Add(counted ? P2.Count(r.W) : -1);
            }
            return r;
        }

        // The rhythm model on the fixture side: from the first shot, a shot whenever the weapon is ready (the previous shot
        // plus the interval, plus the reload when it emptied the magazine) and the burst allows it (after the B-th shot of a
        // burst the next shot comes no earlier than that shot's tick plus Q; the shot that empties the magazine resets the
        // count and adds no pause). B = 0 is continuous fire.
        internal static List<long> Expect(long first, int interval, int magazine, int reload, int burst, int pause, long upTo)
        {
            var fires = new List<long>();
            long ready = first, allowed = first; int ammo = magazine, count = 0;
            for (long tick = first; tick <= upTo; tick++)
            {
                if (tick < ready || tick < allowed) continue;
                fires.Add(tick);
                ammo--;
                if (ammo == 0) { ammo = magazine; ready = tick + reload; count = 0; continue; }
                ready = tick + interval;
                count++;
                if (burst > 0 && count == burst) { allowed = tick + pause; count = 0; }
            }
            return fires;
        }
        internal static List<long> Expect(long first, int burst, int pause, long upTo) => Expect(first, Interval, Magazine, Reload, burst, pause, upTo);

        internal static void NoFire(P2.Shots s, long from, long to, string what)
        {
            for (long t = from; t <= to; t++) Assert.That(s.Fire[(int)t], Is.False, what + ", tick " + t);
        }

        // The hash of the simulation's random stream (SimRandom.WriteState), to see whether a call drew a number.
        internal static ulong Rng(M6World w)
        {
            var hash = new StateHash();
            w.Sim.Rng.WriteState(hash);
            return hash.Value;
        }

        // SaveState called directly (the framework writes the type and version around it).
        internal static byte[] Payload(ReactionFire fire)
        {
            var writer = new SimStateWriter();
            fire.SaveState(writer);
            return writer.ToArray();
        }

        // The version 3 payload read back in binary order: R, the entries, K, B, Q, the burst records.
        internal sealed class Saved
        {
            internal int R, K, B, Q, Count;
            internal (ulong Shooter, ulong Target, long First, long Last)[] Entries;
            internal (int Shots, long LastShot)[] Bursts;
            internal int RemainingBeforeK, RemainingBeforeBursts, RemainingAtEnd;
        }
        internal static Saved Parse(byte[] bytes)
        {
            var reader = new SimStateReader(bytes);
            var s = new Saved { R = reader.Int32() };
            s.Count = reader.Int32();
            s.Entries = new (ulong, ulong, long, long)[s.Count];
            for (int i = 0; i < s.Count; i++) s.Entries[i] = (reader.UInt64(), reader.UInt64(), reader.Int64(), reader.Int64());
            s.RemainingBeforeK = reader.Remaining;
            s.K = reader.Int32(); s.B = reader.Int32(); s.Q = reader.Int32();
            s.RemainingBeforeBursts = reader.Remaining;
            s.Bursts = new (int, long)[s.Count];
            for (int i = 0; i < s.Count; i++) s.Bursts[i] = (reader.Int32(), reader.Int64());
            s.RemainingAtEnd = reader.Remaining;
            return s;
        }
        // The burst record of the one entry a hand-driven fire decision holds.
        internal static (int Shots, long LastShot) Burst(ReactionFire fire)
        {
            Saved s = Parse(Payload(fire));
            Assert.That(s.Count, Is.EqualTo(1));
            return s.Bursts[0];
        }

        internal static ulong Digest(byte[] bytes) => A3.Digest(bytes);
        internal static string Systems(M6World w) => P1.Systems(w);
    }
}
