using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content.MapTools;
using static WarCore.Tests.E3Fixtures;
using static WarCore.Tests.E3Plans;
using static WarCore.Tests.Ed1Fixtures;

namespace WarCore.Tests
{
    // ED-1 acceptance 2, 3 and 4: a batch is the same steps one by one, or nothing.
    public sealed class Ed1BatchTests
    {
        [Test]
        public void ABatchEqualsTheSameStepsOneByOne()
        {
            MapDesc start = Start();
            byte[] before = Bytes(start);
            int allDone = 0, refused = 0;
            var kinds = new HashSet<string>();
            for (ulong seed = 1; seed <= 200; seed++)
            {
                List<Ed1Op> ops = Sequence(seed, start);
                foreach (Ed1Op op in ops) kinds.Add(op.Kind);
                var one = new MapEdit(start);
                Exception oneError = OneByOne(one, ops);
                var batch = new MapEdit(start);
                Exception batchError = null;
                try { batch.Batch(b => { foreach (Ed1Op op in ops) op.Apply(b); }); }
                catch (Exception e) { batchError = e; }
                if (oneError == null)
                {
                    allDone++;
                    Assert.That(batchError, Is.Null, "seed " + seed);
                    Assert.That(Bytes(batch.Current), Is.EqualTo(Bytes(one.Current)), "seed " + seed);
                }
                else
                {
                    refused++;
                    Assert.That(batchError, Is.Not.Null, "seed " + seed);
                    Assert.That(batchError.GetType(), Is.EqualTo(oneError.GetType()), "seed " + seed);
                    Assert.That(Bytes(batch.Current), Is.EqualTo(before), "seed " + seed);
                }
            }
            Assert.That(allDone, Is.GreaterThanOrEqualTo(150));
            Assert.That(refused, Is.GreaterThanOrEqualTo(10));
            Assert.That(kinds, Is.SupersetOf(GoodKinds));
        }

        // The body catches every refusal: the batch still fails with the first refused step's exception and changes nothing.
        [Test]
        public void ARefusedStepFailsTheBatchEvenWhenTheBodyCatchesIt()
        {
            MapDesc start = Start();
            byte[] before = Bytes(start);
            int checkedSeeds = 0;
            for (ulong seed = 3; seed <= 200; seed += 8)
            {
                List<Ed1Op> ops = Sequence(seed, start);
                Exception oneError = OneByOne(new MapEdit(start), ops);
                if (oneError == null) continue;
                checkedSeeds++;
                var batch = new MapEdit(start);
                Exception first = null;
                Exception got = Assert.Catch(() => batch.Batch(b =>
                {
                    foreach (Ed1Op op in ops)
                    {
                        try { op.Apply(b); }
                        catch (Exception e) { first = first ?? e; }
                    }
                }), "seed " + seed);
                Assert.That(got, Is.SameAs(first), "seed " + seed);
                Assert.That(got.GetType(), Is.EqualTo(oneError.GetType()), "seed " + seed);
                Assert.That(Bytes(batch.Current), Is.EqualTo(before), "seed " + seed);
            }
            Assert.That(checkedSeeds, Is.GreaterThanOrEqualTo(10));
        }

        private static void Same(MapDesc start, Action<MapEdit> steps)
        {
            var one = new MapEdit(start);
            steps(one);
            var batch = new MapEdit(start);
            batch.Batch(steps);
            Assert.That(Bytes(batch.Current), Is.EqualTo(Bytes(one.Current)));
        }

        // C-e counterexample 1: the cutout's plate exists only in the batch's draft.
        [Test]
        public void ACutoutOnAPlateAddedEarlierInTheBatch() => Same(Start(), e =>
        {
            ulong plate = e.AddPlate(Ml4Maps.Flat(0, 6, 0, 2, 2, 3 * M));
            e.AddCutout(Ml4Maps.Cut(0, plate, 6, 0, 1, 1));
        });

        // C-e counterexample 2: the surface name exists only in the batch's draft.
        [Test]
        public void ASurfaceNameAddedEarlierInTheBatch() => Same(Start(), e =>
        {
            e.AddSurfaceName("sand");
            e.SetSurface(2, 2, "sand");
            e.AddSurfaceName("aaa");
            e.SetSurface(3, 3, "aaa");
        });

        // C-e counterexample 3: the plate's last cutout was removed earlier in the batch.
        [Test]
        public void APlateWhoseCutoutWasRemovedEarlierInTheBatch() => Same(Start(), e =>
        {
            e.Remove("cutout", 1);
            e.Remove("plate", 1);
        });

        // C-e counterexample 4: the stamp skips the point the hand wrote earlier in the batch.
        [Test]
        public void AStampSkipsAPointTheHandWroteEarlierInTheBatch()
        {
            MapDesc start = Read(Plain());
            Same(start, e =>
            {
                e.SetHeight(1, 1, 777);
                e.Stamp(S1());
            });
            var batch = new MapEdit(start);
            batch.Batch(e => { e.SetHeight(1, 1, 777); e.Stamp(S1()); });
            Assert.That(batch.Current.Heights[1][1], Is.EqualTo(777));
            Assert.That(batch.HeightOwner(1, 1), Is.EqualTo("hand"));
            Assert.That(batch.HeightOwner(2, 2), Is.EqualTo("stamp:s1"));
        }

        [Test]
        public void TheTurnOnStepsWorkInABatch()
        {
            MapDesc flat = MapNew.Desc(new MapNewSpec { Id = "flat", Pack = "rwr-terrain-demo", Width = 6, Depth = 4, CeilingRaw = 262144 });
            Same(flat, e =>
            {
                e.EnableHeights(100);
                e.SetHeight(1, 1, 200);
                e.EnableSurfaces("grass");
                e.AddSurfaceName("aaa");
                e.SetSurface(0, 0, "aaa");
            });
        }

        [Test]
        public void CurrentAndTheOwnerQueriesShowTheBatchSoFar()
        {
            var e = new MapEdit(Start());
            e.Batch(b =>
            {
                b.SetHeight(2, 2, 1234);
                Assert.That(b.Current.Heights[2][2], Is.EqualTo(1234));
                Assert.That(b.HeightOwner(2, 2), Is.EqualTo("hand"));
                b.SetSurface(0, 5, "road");
                Assert.That(b.SurfaceOwner(0, 5), Is.EqualTo("hand"));
                ulong wall = b.AddWall(WallAt(0, 5));
                Assert.That(b.EntryOwner("wall", wall), Is.EqualTo("hand"));
                b.Stamp(S2());
                Assert.That(b.HeightOwner(6, 4), Is.EqualTo("stamp:s2"));
            });
            Assert.That(e.Current.Heights[2][2], Is.EqualTo(1234));
        }

        [Test]
        public void WhenTheBodyThrowsNothingChangesAndTheSameExceptionComesOut()
        {
            MapDesc start = Start();
            var e = new MapEdit(start);
            var mine = new TimeoutException("body");
            Exception got = Assert.Throws<TimeoutException>(() => e.Batch(b =>
            {
                b.SetHeight(0, 0, 5);
                b.AddWall(WallAt(1, 1));
                b.AddSurfaceName("sand");
                throw mine;
            }));
            Assert.That(got, Is.SameAs(mine));
            Assert.That(Bytes(e.Current), Is.EqualTo(Bytes(start)));
            // The editor is usable afterwards and ids were not used up.
            Assert.That(e.AddWall(WallAt(1, 1)), Is.EqualTo(Start().Next.Wall));
        }

        [Test]
        public void AStepThatThrowsHalfWayIsNotKept()
        {
            // The stamp writes its height points before it finds the surface cell off the map.
            var plan = new MapStampPlan("half", Corners(0, 2, 0, 2, 900), new[] { new MapSurfaceWrite(W, 0, "mud") },
                new MapStampEntry<MapWall>[0], new MapStampEntry<MapPoint>[0], new MapStampEntry<MapBase>[0], new MapStampEntry<MapUnit>[0]);
            MapDesc start = Start();
            var e = new MapEdit(start);
            Exception caught = null;
            Exception got = Assert.Throws<ArgumentOutOfRangeException>(() => e.Batch(b =>
            {
                b.SetHeight(5, 5, 10);
                try { b.Stamp(plan); }
                catch (ArgumentOutOfRangeException x) { caught = x; }
                b.SetHeight(6, 5, 20);
            }));
            Assert.That(got, Is.SameAs(caught));
            Assert.That(Bytes(e.Current), Is.EqualTo(Bytes(start)));
        }

        [Test]
        public void TwoRefusedStepsEndWithTheFirstOnesException()
        {
            MapDesc start = Start();
            var e = new MapEdit(start);
            Exception first = null;
            Exception got = Assert.Throws<ArgumentException>(() => e.Batch(b =>
            {
                try { b.SetSurface(0, 0, "nope"); }
                catch (ArgumentException x) { first = x; }
                try { b.SetHeight(W + 1, 0, 0); }
                catch (ArgumentOutOfRangeException) { }
            }));
            Assert.That(got, Is.SameAs(first));
            Assert.That(Bytes(e.Current), Is.EqualTo(Bytes(start)));
        }

        [Test]
        public void ABatchInsideABatchFailsTheOuterOne()
        {
            MapDesc start = Start();
            var e = new MapEdit(start);
            var ex = Assert.Throws<InvalidOperationException>(() => e.Batch(b =>
            {
                b.SetHeight(0, 0, 5);
                b.Batch(c => c.SetHeight(1, 1, 6));
            }));
            StringAssert.StartsWith("MAPEDIT_BATCH", ex.Message);
            Assert.That(Bytes(e.Current), Is.EqualTo(Bytes(start)));

            InvalidOperationException inner = null;
            Exception got = Assert.Throws<InvalidOperationException>(() => e.Batch(b =>
            {
                try { b.Batch(c => { }); }
                catch (InvalidOperationException x) { inner = x; }
                b.SetHeight(0, 0, 5);
            }));
            Assert.That(got, Is.SameAs(inner));
            Assert.That(Bytes(e.Current), Is.EqualTo(Bytes(start)));

            e.Batch(b => b.SetHeight(0, 0, 5));
            Assert.That(e.Current.Heights[0][0], Is.EqualTo(5), "a later batch works");
        }

        [Test]
        public void ANullBodyIsRefused()
        {
            Assert.Throws<ArgumentNullException>(() => new MapEdit(Start()).Batch(null));
        }
    }
}
