using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // T1a-2a acceptance 10 (manifest, snapshots, serialisation) and the multiplier cap.
    public sealed class Wp23StateTests
    {
        private const long S = Wp23.S;

        private static WcMap Terrain(int bump = 0, bool shifted = false) =>
            Wp23.Make(16, 8, 0, 6 * S, new[] { (8, 2, 1, 3) }, 4 * S,
                (i, j) => (int)(i * (S / 16)) + (shifted ? 3 : 0) + (i == 5 && j == 4 ? bump : 0), Wp23.Names, (x, z) => (byte)(x < 5 ? 0 : x < 10 ? 1 : 2));

        private static SimSession World(WcMap map, MoverKind mover, ContentPack pack = null)
        {
            var units = new[] { Wp23.Unit(1, 1, 1500, 1500, 3000), Wp23.Unit(2, 2, 1500, 5500, 4000) };
            var script = new[] { BridgeCommand.MoveTo(1, 1, 14500, 1500), BridgeCommand.MoveTo(2, 2, 14500, 6500) };
            return Wp23.Session(map, mover, units, script, pack ?? Wp23.Pack());
        }

        private static readonly MoverKind[] Movers = { MoverKind.InterimPath, MoverKind.InterimCrowd };

        [Test]
        public void ManifestCarriesTheTerrainDigestUnderTheAgreedKey()
        {
            WcMap map = Terrain();
            TerrainTables tables = TerrainDerivation.Derive(map, Wp23.Pack());
            SimSession s = World(map, MoverKind.InterimPath);
            Assert.That(M3TestWorld.Sim(s).StableContent.Digest,
                Is.EqualTo(new StableContentManifest(1, new StableContentKey("terrain.tables", tables.Digest)).Digest));
            Assert.That(M3TestWorld.Sim(s).StableContent.Digest,
                Is.Not.EqualTo(new StableContentManifest(1, new StableContentKey("terrain.table", tables.Digest)).Digest));
            SimSession flat = Wp23.Session(Wp23.Make(16, 8, 0, 6 * S, null, 4 * S), MoverKind.InterimPath, new[] { Wp23.Unit(1, 1, 1500, 1500) });
            Assert.That(M3TestWorld.Sim(flat).StableContent.Digest, Is.EqualTo(new StableContentManifest(1).Digest));
        }

        [TestCaseSource(nameof(Movers))]
        public void SnapshotsRefuseAnyOtherTerrain_AndResumeUnderTheSame(MoverKind mover)
        {
            SimSession a = World(Terrain(), mover);
            for (int i = 0; i < 40; i++) a.StepOnce();
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            string demoJson = TerrainDemoMap.PackJson;
            var others = new Dictionary<string, SimSession>
            {
                ["one height point"] = World(Terrain(bump: 1), mover),
                ["same cells, other heights"] = World(Terrain(shifted: true), mover),
                ["speed only"] = World(Terrain(), mover, Wp23.Pack(demoJson.Replace("\"speedMilli\": 500", "\"speedMilli\": 400"))),
                ["cost only"] = World(Terrain(), mover, Wp23.Pack(demoJson.Replace("\"cost\": 3", "\"cost\": 4"))),
            };
            foreach (var pair in others)
            {
                Assert.That(pair.Value.StateHash, Is.Not.EqualTo(World(Terrain(), mover).StateHash), pair.Key + " changes the hash");
                Assert.Throws<SimSnapshotException>(() => M3TestWorld.Sim(pair.Value).RestoreRetainedSnapshot(snapshot), pair.Key);
            }
            SimSession fresh = World(Terrain(), mover);
            M3TestWorld.Sim(fresh).RestoreRetainedSnapshot(snapshot);
            for (int i = 0; i < 100; i++)
            {
                a.StepOnce(); fresh.StepOnce();
                Assert.That(fresh.StateHash, Is.EqualTo(a.StateHash), $"tick +{i + 1}");
            }
            Assert.That(Wp23.At(fresh, 1).X.Raw, Is.GreaterThan(Wp23.Mm(4000).Raw), "it really walked");
        }

        [TestCaseSource(nameof(Movers))]
        public void SavedLocomotionBytes_HaveVersionPlus100_AndTheDigestAppended(MoverKind mover)
        {
            WcMap map = Terrain();
            TerrainTables tables = TerrainDerivation.Derive(map, Wp23.Pack());
            InterimLocomotion terrain = M3TestWorld.Locomotion(World(map, mover));
            // The same walls and cells with no height field and no surface table: the flat world of that shape.
            WcMap flatMap = Wp23.Make(16, 8, 0, 6 * S, new[] { (8, 2, 1, 3) }, 4 * S);
            InterimLocomotion flat = M3TestWorld.Locomotion(Wp23.Session(flatMap, mover, new[] { Wp23.Unit(1, 1, 1500, 1500) }));
            int legacy = mover == MoverKind.InterimCrowd ? 4 : 3;
            Assert.That(flat.StateVersion, Is.EqualTo(legacy));
            Assert.That(terrain.StateVersion, Is.EqualTo(legacy + 100));
            var tw = new SimStateWriter(); terrain.SaveState(tw);
            var fw = new SimStateWriter(); flat.SaveState(fw);
            byte[] t = tw.ToArray(), f = fw.ToArray();
            Assert.That(t.Length, Is.EqualTo(f.Length + 8));
            Assert.That(t[..f.Length], Is.EqualTo(f), "the old fields come first, unchanged");
            Assert.That(BitConverter.ToUInt64(t, f.Length), Is.EqualTo(tables.Digest));
            var hash = new StateHash(); terrain.WriteState(hash);
            var again = new StateHash(); M3TestWorld.Locomotion(World(map, mover)).WriteState(again);
            Assert.That(again.Value, Is.EqualTo(hash.Value));
        }

        // MoveProgress rides in the snapshot: a restore in the middle of a stall resumes the very same counters.
        [TestCaseSource(nameof(Movers))]
        public void MoveProgress_SurvivesASnapshotRoundTrip(MoverKind mover)
        {
            WcMap map = Wp23Fx.Map((x, z) => x == 0 && z == 1);
            SimSession a = Wp23Fx.World(mover, map, Wp23.Unit(1, 1, 1500, 1500, Wp23Fx.Speed));
            Wp23Fx.Order(a, 1, 1100, 1500);
            for (int i = 0; i < 100; i++) a.StepOnce();
            MoveProgress before = Wp23Fx.Progress(a, 1);
            Assert.That(before.Stall + before.Retries * 60, Is.GreaterThan(0), "the unit is mid-stall");
            byte[] snapshot = M3TestWorld.Sim(a).SaveRetainedSnapshot();
            SimSession b = Wp23Fx.World(mover, map, Wp23.Unit(1, 1, 1500, 1500, Wp23Fx.Speed));
            M3TestWorld.Sim(b).RestoreRetainedSnapshot(snapshot);
            MoveProgress after = Wp23Fx.Progress(b, 1);
            Assert.That((after.BestDist2, after.Stall, after.Retries, after.Failed, after.OrderBest, after.OrderStall),
                Is.EqualTo((before.BestDist2, before.Stall, before.Retries, before.Failed, before.OrderBest, before.OrderStall)));
            int failA = -1, failB = -1;
            for (int i = 1; i <= 300; i++)
            {
                a.StepOnce(); b.StepOnce();
                if (failA < 0 && Wp23Fx.Failed(a, 1)) failA = i;
                if (failB < 0 && Wp23Fx.Failed(b, 1)) failB = i;
                Assert.That(b.StateHash, Is.EqualTo(a.StateHash));
            }
            Assert.That(failA, Is.GreaterThan(0));
            Assert.That(failB, Is.EqualTo(failA));
        }

        [Test]
        public void SpeedMultiplierCap_LivesInSimAndMatchesContent()
        {
            Assert.That(TerrainTables.MaxSpeedMilli, Is.EqualTo(SurfaceDef.MaxSpeedMilli));
            NavGrid nav = new NavGrid(2, 1, default, default, new byte[] { 3 });
            TerrainTables Make(ushort speed) => new TerrainTables(null, nav, new byte[] { 1, 1 }, new ushort[] { 1000, speed },
                Array.Empty<string>(), Array.Empty<byte>(), Array.Empty<byte>());
            Assert.DoesNotThrow(() => Make(2000));
            Assert.Throws<ArgumentException>(() => Make(2001));
            Assert.Throws<ArgumentException>(() => Make(65535));
        }

        // The locomotion's own hash and byte stream name the terrain. A flat world's hash is what the baseline commit 4ba1795
        // produced (the probe there printed these two values for the UMap fixture).
        [Test]
        public void LocomotionHash_NamesTheTerrain_AndAFlatWorldKeepsTheBaselineValue()
        {
            string Hash(InterimLocomotion l) { var h = new StateHash(); l.WriteState(h); return h.Value.ToString("x16"); }
            Assert.That(Hash(M3TestWorld.Locomotion(Flat(MoverKind.InterimPath))), Is.EqualTo("dc4f998101f36b13"));
            Assert.That(Hash(M3TestWorld.Locomotion(Flat(MoverKind.InterimCrowd))), Is.EqualTo("6b035fcf0fc9b888"));
            foreach (MoverKind mover in Movers)
            {
                string a = Hash(M3TestWorld.Locomotion(World(Terrain(), mover)));
                Assert.That(Hash(M3TestWorld.Locomotion(World(Terrain(), mover))), Is.EqualTo(a));
                Assert.That(Hash(M3TestWorld.Locomotion(World(Terrain(bump: 1), mover))), Is.Not.EqualTo(a), "other heights");
                Assert.That(Hash(M3TestWorld.Locomotion(World(Terrain(), mover, Wp23.Pack(TerrainDemoMap.PackJson.Replace("\"cost\": 3", "\"cost\": 4"))))),
                    Is.Not.EqualTo(a), "other rules");
            }
        }

        private static SimSession Flat(MoverKind mover)
        {
            byte[] bytes = WcMapWriter.Write(M3TestWorld.UMap());
            return SimSession.Create(new WorldRecipe(41, mover, new[] { new UnitSpawn(1, 0, 1, 1500, 1500, 6000), new UnitSpawn(2, 0, 1, 2500, 5500, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 12500, 4500), BridgeCommand.MoveTo(2, 2, 12500, 12500) }), bytes);
        }

        // The locomotion's own stream carries the digest and loading refuses a different one (the snapshot test above is
        // already stopped by the manifest, so this reaches the system's own check).
        [TestCaseSource(nameof(Movers))]
        public void LocomotionLoad_RefusesAnotherTerrainDigest(MoverKind mover)
        {
            var writer = new SimStateWriter();
            M3TestWorld.Locomotion(World(Terrain(), mover)).SaveState(writer);
            byte[] bytes = writer.ToArray();
            Assert.DoesNotThrow(() => M3TestWorld.Locomotion(World(Terrain(), mover)).LoadState(new SimStateReader(bytes)));
            Assert.Throws<InvalidOperationException>(() => M3TestWorld.Locomotion(World(Terrain(bump: 1), mover)).LoadState(new SimStateReader(bytes)));
        }
    }
}
