using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.MapTools;
using WarCore.Sim;
using WarCore.Tests.Projectile;

namespace WarCore.Tests
{
    // HC-5: each bound whose copy now names its definition is checked on both sides: exactly the bound is accepted, one
    // step past it is refused. The numbers are written out here so a changed definition shows up as a failing test.
    public sealed class Hc5BoundaryTests
    {
        private const long M = SimFix.Scale;
        private const long WorldRaw = 16384 * M, ColliderHeightRaw = 32768 * M;

        // A one-cell map without walls whose west edge is at minX metres and whose ceiling is maxYRaw.
        private static WcMap Map(long minX, long maxYRaw)
        {
            WcMap s = M2MapFixture.Model(false);
            return new WcMap(s.MapId, s.ContentId, SimVec3.Metres(minX, 0, 0),
                new SimVec3(SimFix.FromInt(minX + 1), SimFix.FromRaw(maxYRaw), SimFix.FromInt(1)), SimFix.FromInt(1), default,
                1, 1, s.Layers.ToArray(), new WcMapWall[0], new byte[] { 1 });
        }

        private static string Code(WcMap map) => Assert.Throws<InvalidDataException>(() => WcMapValidation.Validate(map)).Message;

        [Test]
        public void MapCorners_AcceptTheWorldLimit_AndRefuseOneMore()
        {
            Assert.DoesNotThrow(() => WcMapValidation.Validate(Map(16384 - 1, M)), "east edge on the limit");
            Assert.DoesNotThrow(() => WcMapValidation.Validate(Map(-16384, M)), "west edge on the limit");
            Assert.DoesNotThrow(() => WcMapValidation.Validate(Map(0, WorldRaw)), "ceiling on the limit");
            Assert.That(Code(Map(16384, M)), Is.EqualTo("WCMAP_BOUNDS"), "east edge one metre past");
            Assert.That(Code(Map(-16385, M)), Is.EqualTo("WCMAP_BOUNDS"), "west edge one metre past");
            Assert.That(Code(Map(0, WorldRaw + 1)), Is.EqualTo("WCMAP_BOUNDS"), "ceiling one raw past");
        }

        private static string DescError(int originX, int originZ)
        {
            byte[] bytes = E1aSamples.Utf8(E3Fixtures.Map(originX: originX, originZ: originZ));
            return Assert.Throws<InvalidDataException>(() => MapDescJson.Read(bytes)).Message;
        }

        [Test]
        public void MapDescOrigin_AcceptsTheWorldLimit_AndRefusesOneMore()
        {
            Assert.DoesNotThrow(() => E3Fixtures.Read(E3Fixtures.Map(originX: 16384 - E3Fixtures.W, originZ: -16384)));
            Assert.DoesNotThrow(() => E3Fixtures.Read(E3Fixtures.Map(originX: -16384, originZ: 16384 - E3Fixtures.D)));
            Assert.That(DescError(16384 - E3Fixtures.W + 1, 0), Does.StartWith(MapDescReader.SizeCode));
            Assert.That(DescError(-16385, 0), Does.StartWith(MapDescReader.SizeCode));
            Assert.That(DescError(0, 16384 - E3Fixtures.D + 1), Does.StartWith(MapDescReader.SizeCode));
            Assert.That(DescError(0, -16385), Does.StartWith(MapDescReader.SizeCode));
        }

        [Test]
        public void ColliderHeight_AcceptsTheLimit_AndRefusesOneRawMore()
        {
            SimFix at = SimFix.FromRaw(ColliderHeightRaw), over = SimFix.FromRaw(ColliderHeightRaw + 1), one = SimFix.FromInt(1);
            Collider built = default;
            Assert.DoesNotThrow(() => built = new Collider(one, at), "capsule on the limit");
            Assert.That(built.Height, Is.EqualTo(at));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Collider(one, over));
            Assert.DoesNotThrow(() => built = Collider.Box(one, at, one, default), "box on the limit");
            Assert.That(built.Height, Is.EqualTo(at));
            Assert.Throws<ArgumentOutOfRangeException>(() => Collider.Box(one, over, one, default));
            Collider capsule = new Collider(one, SimFix.FromInt(2));
            Assert.DoesNotThrow(() => built = capsule.WithHeight(at), "WithHeight on the limit");
            Assert.That(built.Height, Is.EqualTo(at));
            Assert.Throws<ArgumentOutOfRangeException>(() => capsule.WithHeight(over));
        }

        [Test]
        public void PostureHeight_AcceptsTheColliderLimit_AndRefusesOneRawMore()
        {
            SimFix one = SimFix.FromInt(1);
            PostureDef def = default;
            Assert.DoesNotThrow(() => def = new PostureDef(1, SimFix.FromRaw(ColliderHeightRaw), one, default, one), "on the limit");
            Assert.That(def.Height.Raw, Is.EqualTo(ColliderHeightRaw));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PostureDef(1, SimFix.FromRaw(ColliderHeightRaw + 1), one, default, one));
        }

        // A saved projectile step may be twice the fastest per-tick step: 2 * 1 000 000 mm/s * 65536 / 30000 = 4 369 066 raw.
        private const long MaxStepRaw = 4_369_066;

        private static byte[] Save(Proj1World w, StableId launcher, long stepX)
        {
            var x = new SimStateWriter();
            x.UInt64(w.UsedPack.PolicyDigest);
            x.Int32(1);
            x.UInt64(launcher.Value); x.Int32(1); x.Int32(0); x.Int64(w.Tick);
            x.Int64(0); x.Int64(98304); x.Int64(0);
            x.Int64(stepX); x.Int64(0); x.Int64(0);
            x.Int32(w.UsedPack.Projectiles[0].Flights);
            return x.ToArray();
        }

        [Test]
        public void SavedProjectileStep_AcceptsTheBound_AndRefusesOneRawMore()
        {
            WeaponDef gun = Proj1World.Weapon(intervalMilli: 3_000_000);
            var w = new Proj1World(Proj1World.Pack(new[] { gun }, new[] { 30000 }, new RollDamage()),
                new[] { Proj1World.U(0, 0, 1), Proj1World.U(20 * M, 0, 2, -1) });
            w.Step();
            StableId id = w.Id(0);
            Assert.DoesNotThrow(() => w.Projectiles.LoadState(new SimStateReader(Save(w, id, MaxStepRaw))));
            Assert.That(w.Projectiles.Count, Is.EqualTo(1));
            Assert.DoesNotThrow(() => w.Projectiles.LoadState(new SimStateReader(Save(w, id, -MaxStepRaw))));
            Assert.Throws<SimSnapshotException>(() => w.Projectiles.LoadState(new SimStateReader(Save(w, id, MaxStepRaw + 1))));
            Assert.Throws<SimSnapshotException>(() => w.Projectiles.LoadState(new SimStateReader(Save(w, id, -MaxStepRaw - 1))));
        }
    }
}
