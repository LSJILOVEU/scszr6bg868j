using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // Acceptance 1(a) and 9(d), recorded on the code before the suppression: A3's memory fixture (A armed on the point,
    // B unarmed walking into the grass on tick 20) with the memory on, with and without the search, and with the AI fire
    // state (R = 15, K = 3). The system table, the 600-tick run (64-bit FNV-1a over the per-tick hashes), A's fire ticks
    // and the retained snapshots at ticks 300 and 600 must stay the same after the change, and byte for byte the same with
    // "suppressTicks" written as 0.
    public sealed class A3bBaselineTests
    {
        internal const string MemoryAi = "{ \"targetMemoryTicks\": 300 }";
        internal const string SearchAi = "{ \"targetMemoryTicks\": 300, \"searchTicks\": 60 }";
        internal const string FireAi = "{ \"targetMemoryTicks\": 300, \"reactionTicks\": 15, \"senseTicks\": 3 }";
        internal const string MemorySystems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal const string SearchSystems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;battle.ai-search|0|17;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";
        internal const string FireSystems = "bridge.dead-cleanup|0|5;battle.reinforce|0|8;interim.orders|0|10;battle.ai|0|12;"
            + "battle.ai-contacts|0|13;battle.ai-fire|0|14;interim.locomotion|1|20;combat.system|3|30;battle.capture|3|40;battle.match|3|50";

        internal struct Recorded
        {
            internal string Systems; internal ulong Run600; internal long[] Fires;
            internal int Length300, Length600; internal ulong Digest300, Digest600;
        }
        internal static readonly Recorded Memory = new Recorded
        {
            Systems = MemorySystems, Run600 = 6450414499856491104UL, Fires = new long[] { 1, 31 },
            Length300 = 5498, Digest300 = 18025696330481666281UL, Length600 = 5450, Digest600 = 13212755508993669316UL
        };
        internal static readonly Recorded Search = new Recorded
        {
            Systems = SearchSystems, Run600 = 13006476089337611863UL,
            Fires = new long[] { 1, 31, 98, 158, 188, 218, 278, 308, 338, 398, 428, 458, 518, 548, 578 },
            Length300 = 5767, Digest300 = 10934660559477227877UL, Length600 = 5767, Digest600 = 18149043431913061792UL
        };
        // 9(d): the AI fire world (version 2 of battle.ai-fire with K > 1) at the end of tick 300.
        internal const int FireLength300 = 5658;
        internal const ulong FireDigest300 = 10696314328788025150UL;

        // The memory fixture's world and script, written here so this file compiles on the base code.
        internal static readonly SimVec3 BStart = A2.Cell(21, 18), BGrass = A2.Cell(21, 24);
        internal static M6World World(string ai, bool reverse = false) =>
            new M6World(A3.Recipe(new[] { A3.Observer(), A3.Target(A3.KeyB, 2, BStart) }, A3.Load(A3.PackJson(A3.Grass500, ai))),
                WcMapWriter.Write(A2.Map(A2.DWalls)), reverse);
        internal static InputFrame Script(long tick) =>
            tick == 1 ? A3.Command(SquadCommandKind.Hold, 2) : tick == 20 ? A3.Command(SquadCommandKind.MoveTo, 2, BGrass) : default;
        internal static void Step(M6World w, long tick) => w.Sim.Step(A3.Drive(w, Script(tick)));
        internal static bool AFired(M6World w)
        {
            StableId a = A3.Id(w, A3.KeyA);
            foreach (FireRecord f in w.Combat.Fires) if (f.Shooter.Equals(a)) return true;
            return false;
        }

        internal static void Check(string ai, Recorded expected)
        {
            M6World w = null;
            Assert.DoesNotThrow(() => w = World(ai));
            Assert.That(L4.Systems(w), Is.EqualTo(expected.Systems));
            Assert.That(A3.FirstThink(w, A3.KeyA), Is.EqualTo(6));
            ulong fold = 14695981039346656037UL;
            var fires = new List<long>();
            for (long tick = 0; tick <= 600; tick++)
            {
                if (tick > 0) { Step(w, tick); if (AFired(w)) fires.Add(tick); }
                fold = (fold ^ w.Hash) * 1099511628211UL;
                if (tick == 300 || tick == 600)
                {
                    byte[] snapshot = w.Sim.SaveRetainedSnapshot();
                    Assert.That(snapshot.Length, Is.EqualTo(tick == 300 ? expected.Length300 : expected.Length600), "length, tick " + tick);
                    Assert.That(A3.Digest(snapshot), Is.EqualTo(tick == 300 ? expected.Digest300 : expected.Digest600), "digest, tick " + tick);
                }
            }
            Assert.That(fires, Is.EqualTo(expected.Fires));
            Assert.That(fold, Is.EqualTo(expected.Run600));
        }

        [Test]
        public void MemoryWorld_IsRecorded() => Check(MemoryAi, Memory);

        [Test]
        public void SearchWorld_IsRecorded() => Check(SearchAi, Search);

        [Test]
        public void FireWorld_Snapshot300IsRecorded()
        {
            M6World w = World(FireAi);
            Assert.That(L4.Systems(w), Is.EqualTo(FireSystems));
            Assert.That(w.System<ReactionFire>().StateVersion, Is.EqualTo(2));
            for (long tick = 1; tick <= 300; tick++) Step(w, tick);
            byte[] snapshot = w.Sim.SaveRetainedSnapshot();
            Assert.That(snapshot.Length, Is.EqualTo(FireLength300));
            Assert.That(A3.Digest(snapshot), Is.EqualTo(FireDigest300));
        }
    }
}
