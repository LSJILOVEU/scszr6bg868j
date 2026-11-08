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
    // L6 fixture: the WP-22 demo terrain as a full match, frozen in the test. Seed 41, the content layer (no "extends" and no
    // fallback to the shipped files: every section and field is written here, all three weapons included because the combat
    // digest covers every weapon), the control points, the anchors and the 40-unit table are literals. The map is
    // TerrainDemoMap.Build(), whose bytes E1a already pins by SHA-256 (checked here). DemoRecipes.TerrainMatch() builds the same
    // layout from the shipped content; L6DemoRecipeTests compares the layout, not the content values.
    //
    // Known defect until L9: automatic fire is a horizontal ray from the unit's position, and a unit capsule is centred on its
    // position (height 1 m), so targets more than about 0.5 m higher or lower are not hit. On this map the match settles into a
    // stand-off between the ridge and the foot of the slope and ends on the time limit.
    internal static class L6
    {
        internal const long S = SimFix.Scale;
        internal const ulong Seed = 41;
        internal const long Speed = 6000;
        // The content's maxTicks is 9000; stepping stops here at the latest, and a world that is still running then fails.
        internal const int StepLimit = 9100;
        internal static readonly long[] HashTicks = { 300, 600, 900 };

        internal const string PackJson = "{ \"id\": \"l6-test\", "
            + "\"weapons\": [ "
            + "{ \"id\": \"rwr.weapon-1\", \"fireIntervalMilli\": 3390, \"magazine\": 30, \"reloadMilli\": 60000, \"falloffStart\": 34, \"falloffEnd\": 67, \"effectRaw\": 32768, \"spreadBase\": 1000, \"spreadGrowth\": 340, \"spreadRecoverPerSec\": 1400, \"spreadMax\": 3000 }, "
            + "{ \"id\": \"rwr.weapon-2\", \"fireIntervalMilli\": 3690, \"magazine\": 30, \"reloadMilli\": 60000, \"falloffStart\": 33, \"falloffEnd\": 68, \"effectRaw\": 36045, \"spreadBase\": 1000, \"spreadGrowth\": 340, \"spreadRecoverPerSec\": 1400, \"spreadMax\": 3000 }, "
            + "{ \"id\": \"rwr.weapon-3\", \"fireIntervalMilli\": 45000, \"magazine\": 10, \"reloadMilli\": 60000, \"falloffStart\": 99, \"falloffEnd\": 144, \"effectRaw\": 65536, \"spreadBase\": 1000, \"spreadGrowth\": 340, \"spreadRecoverPerSec\": 1400, \"spreadMax\": 3000 } ], "
            + "\"unitTypes\": [ { \"id\": \"rwr.soldier-1\", \"weapon\": \"rwr.weapon-1\" }, { \"id\": \"rwr.soldier-2\", \"weapon\": \"rwr.weapon-2\" } ], "
            + "\"combat\": { \"maxHealth\": 100, \"resolver\": \"rwr.hit\", \"selector\": \"sight-limited\", \"friendlyFire\": false, \"friendlyFireLocked\": false, \"allyCheckMissRate\": 0.25, \"allyHoldMilli\": 30000 }, "
            + "\"match\": { \"captureAdvantageNum\": 2, \"captureAdvantageDen\": 1, \"captureStrict\": false, \"captureTicks\": 300, \"interrupt\": \"reset\", \"decayPerTick\": 1, \"intervalTicks\": 90, \"thinkInterval\": 30, \"maxTicks\": 9000, \"sight\": 35, \"gridColumns\": 4, \"gridRows\": 4, \"gridSpacing\": 1.5, \"respawnTicks\": 0 }, "
            + "\"surfaces\": [ { \"id\": \"grass\", \"passable\": true, \"speedMilli\": 1000, \"cost\": 1, \"concealMilli\": 1000 }, { \"id\": \"mud\", \"passable\": true, \"speedMilli\": 500, \"cost\": 3, \"concealMilli\": 1000 }, { \"id\": \"road\", \"passable\": true, \"speedMilli\": 1250, \"cost\": 1, \"concealMilli\": 1000 } ], "
            + "\"slopeRule\": { \"maxWalkableNum\": 3, \"maxWalkableDen\": 4 }, "
            + "\"ai\": { \"failedSlotTicks\": 0, \"targetMemoryTicks\": 0, \"searchTicks\": 0, \"reactionTicks\": 0 } }";

        // No fallback source: a missing section or layer is an error instead of a silent read of the shipped files.
        internal static ContentPack Load(string id, string json) =>
            ContentLoader.Load(new MemoryContentSource(new Dictionary<string, string> { [id] = json }), id);

        internal static ContentPack Pack()
        {
            ContentPack pack = Load("l6-test", PackJson);
            Assert.That(pack.WeaponOfUnitType("rwr.soldier-1"), Is.EqualTo(0));
            Assert.That(pack.WeaponOfUnitType("rwr.soldier-2"), Is.EqualTo(1));
            return pack;
        }

        private static SimVec3 Raw(long x, long y, long z) => new SimVec3(SimFix.FromRaw(x), SimFix.FromRaw(y), SimFix.FromRaw(z));

        // Index 0 = id 1, the ridge crest (Y 5.75 m); index 1 = id 2, the mud patch. 3 m circles, reinforcement spawns at the centres.
        internal static ControlPointDef[] Points()
        {
            SimVec3 crest = Raw(22 * S + S / 2, 376832, 16 * S + S / 2), mud = Raw(38 * S + S / 2, 0, 8 * S + S / 2);
            return new[]
            {
                new ControlPointDef(1, crest, SimFix.FromInt(3), 0, true, crest),
                new ControlPointDef(2, mud, SimFix.FromInt(3), 0, true, mud),
            };
        }

        internal static SpawnAnchor[] Bases() => new[]
        {
            new SpawnAnchor(1, Raw(3 * S + S / 2, 0, 16 * S + S / 2)),
            new SpawnAnchor(2, Raw(44 * S + S / 2, 0, 16 * S + S / 2)),
        };

        // 20 per team in the M6 formation around the anchor (mm); keys 1-10 and 21-30 deployed, 11-20 and 31-40 in reserve.
        internal static UnitSpawn[] Units() => new[]
        {
            new UnitSpawn(1, 0, 1, 2000, 15500, Speed, 1, 0, false),
            new UnitSpawn(2, 0, 1, 3000, 15500, Speed, 1, 0, false),
            new UnitSpawn(3, 0, 1, 4000, 15500, Speed, 1, 0, false),
            new UnitSpawn(4, 0, 1, 5000, 15500, Speed, 1, 0, false),
            new UnitSpawn(5, 0, 1, 2000, 16500, Speed, 1, 0, false),
            new UnitSpawn(6, 0, 1, 3000, 16500, Speed, 1, 0, false),
            new UnitSpawn(7, 0, 1, 4000, 16500, Speed, 1, 0, false),
            new UnitSpawn(8, 0, 1, 5000, 16500, Speed, 1, 0, false),
            new UnitSpawn(9, 0, 1, 2000, 17500, Speed, 1, 0, false),
            new UnitSpawn(10, 0, 1, 3000, 17500, Speed, 1, 0, false),
            new UnitSpawn(11, 0, 1, 2000, 15500, Speed, 1, 0, true),
            new UnitSpawn(12, 0, 1, 3000, 15500, Speed, 1, 0, true),
            new UnitSpawn(13, 0, 1, 4000, 15500, Speed, 1, 0, true),
            new UnitSpawn(14, 0, 1, 5000, 15500, Speed, 1, 0, true),
            new UnitSpawn(15, 0, 1, 2000, 16500, Speed, 1, 0, true),
            new UnitSpawn(16, 0, 1, 3000, 16500, Speed, 1, 0, true),
            new UnitSpawn(17, 0, 1, 4000, 16500, Speed, 1, 0, true),
            new UnitSpawn(18, 0, 1, 5000, 16500, Speed, 1, 0, true),
            new UnitSpawn(19, 0, 1, 2000, 17500, Speed, 1, 0, true),
            new UnitSpawn(20, 0, 1, 3000, 17500, Speed, 1, 0, true),
            new UnitSpawn(21, 0, 2, 43000, 15500, Speed, 2, 1, false),
            new UnitSpawn(22, 0, 2, 44000, 15500, Speed, 2, 1, false),
            new UnitSpawn(23, 0, 2, 45000, 15500, Speed, 2, 1, false),
            new UnitSpawn(24, 0, 2, 46000, 15500, Speed, 2, 1, false),
            new UnitSpawn(25, 0, 2, 43000, 16500, Speed, 2, 1, false),
            new UnitSpawn(26, 0, 2, 44000, 16500, Speed, 2, 1, false),
            new UnitSpawn(27, 0, 2, 45000, 16500, Speed, 2, 1, false),
            new UnitSpawn(28, 0, 2, 46000, 16500, Speed, 2, 1, false),
            new UnitSpawn(29, 0, 2, 43000, 17500, Speed, 2, 1, false),
            new UnitSpawn(30, 0, 2, 44000, 17500, Speed, 2, 1, false),
            new UnitSpawn(31, 0, 2, 43000, 15500, Speed, 2, 1, true),
            new UnitSpawn(32, 0, 2, 44000, 15500, Speed, 2, 1, true),
            new UnitSpawn(33, 0, 2, 45000, 15500, Speed, 2, 1, true),
            new UnitSpawn(34, 0, 2, 46000, 15500, Speed, 2, 1, true),
            new UnitSpawn(35, 0, 2, 43000, 16500, Speed, 2, 1, true),
            new UnitSpawn(36, 0, 2, 44000, 16500, Speed, 2, 1, true),
            new UnitSpawn(37, 0, 2, 45000, 16500, Speed, 2, 1, true),
            new UnitSpawn(38, 0, 2, 46000, 16500, Speed, 2, 1, true),
            new UnitSpawn(39, 0, 2, 43000, 17500, Speed, 2, 1, true),
            new UnitSpawn(40, 0, 2, 44000, 17500, Speed, 2, 1, true),
        };

        internal static WorldRecipe Recipe()
        {
            ContentPack pack = Pack();
            return new WorldRecipe(Seed, MoverKind.InterimCrowd, Units(), Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(pack.BuildMatch(Points(), Bases()))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack);
        }

        internal static byte[] MapBytes()
        {
            byte[] bytes = WcMapWriter.Write(TerrainDemoMap.Build());
            Assert.That(E1aRecordTests.Sha(bytes), Is.EqualTo(E1aRecordTests.TerrainBytesSha), "the demo terrain bytes drifted");
            return bytes;
        }

        internal static M6World World(bool reverse = false) => new M6World(Recipe(), MapBytes(), reverse);

        // Steps the world until the match is decided (never past the end) and records what the baseline needs.
        internal static L6Trace RunToEnd(M6World w, Action<long> afterStep = null)
        {
            var trace = new L6Trace();
            while (w.Match.Winner == 0 && w.Sim.Clock.Tick < StepLimit)
            {
                w.Step();
                trace.Observe(w);
                afterStep?.Invoke(w.Sim.Clock.Tick);
            }
            Assert.That(w.Match.Winner, Is.Not.EqualTo(0), "the match must be decided by the content's time limit");
            return trace;
        }

        internal static int DeadOf(M6World w, int team)
        {
            int dead = 0;
            for (ulong key = 1; key <= 40; key++)
                if ((key <= 20 ? 1 : 2) == team && w.Has<Position>(key) && w.Dead(key)) dead++;
            return dead;
        }

        // seed,endTick,winner,reason,h300,h600,h900,hEnd,firstFire,firstFlip,flips,dead1,dead2,deployed1,deployed2,open,hit,wall,
        // friendBlocked,friendHit. A hash tick the match never reached is "-"; a missing event is -1.
        internal static string Line(M6World w, L6Trace trace)
        {
            var parts = new List<string> { w.Recipe.Seed.ToString(), w.Match.EndTick.ToString(), w.Match.Winner.ToString(), ((int)w.Match.EndReason).ToString() };
            foreach (long tick in HashTicks) parts.Add(trace.Hashes.TryGetValue(tick, out ulong h) ? h.ToString("X16") : "-");
            parts.Add(w.Hash.ToString("X16"));
            parts.Add(trace.FirstFire.ToString()); parts.Add(trace.FirstFlip.ToString()); parts.Add(trace.Flips.ToString());
            parts.Add(DeadOf(w, 1).ToString()); parts.Add(DeadOf(w, 2).ToString());
            parts.Add(w.Reinforce.Successes(1).ToString()); parts.Add(w.Reinforce.Successes(2).ToString());
            foreach (long count in trace.Outcomes) parts.Add(count.ToString());
            return string.Join(",", parts);
        }
    }

    internal sealed class L6Trace
    {
        internal long FirstFire = -1, FirstFlip = -1, FirstDeploy = -1;
        internal int Flips;
        internal readonly long[] Outcomes = new long[5];
        internal readonly bool[] TeamHit = new bool[3];
        internal readonly Dictionary<long, ulong> Hashes = new Dictionary<long, ulong>();
        internal CaptureRecord? FirstChange;

        internal void Observe(M6World w)
        {
            long tick = w.Sim.Clock.Tick;
            foreach (FireRecord fire in w.Combat.Fires)
            {
                Outcomes[(int)fire.Outcome]++;
                if (fire.Outcome == FireOutcome.Hit) TeamHit[fire.ShooterTeam] = true;
            }
            if (FirstFire < 0 && w.Combat.Fires.Length > 0) FirstFire = tick;
            if (w.Capture.Changes.Length > 0)
            {
                if (FirstFlip < 0) { FirstFlip = tick; FirstChange = w.Capture.Changes[0]; }
                Flips += w.Capture.Changes.Length;
            }
            if (FirstDeploy < 0 && w.Reinforce.Deployed.Length > 0) FirstDeploy = tick;
            if (Array.IndexOf(L6.HashTicks, tick) >= 0) Hashes[tick] = w.Hash;
        }
    }
}
