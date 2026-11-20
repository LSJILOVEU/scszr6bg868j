using System;
using NUnit.Framework;
using WarCore.Presentation;
using static WarCore.Tests.Ed6aFixtures;

namespace WarCore.Tests
{
    // Acceptance 4: squads in the 1 m formation, offsets worked out by hand.
    public sealed class Ed6aSquadTests
    {
        private const long Cx = 20000, Cz = 15000;

        // Columns ceil(sqrt n), rows ceil(n / columns); offset ((2 (rank % c) - (c - 1)) / 2, (2 (rank / c) - (r - 1)) / 2) m.
        private static readonly long[][] Ten =
        {
            new long[] { -1500, -1000 }, new long[] { -500, -1000 }, new long[] { 500, -1000 }, new long[] { 1500, -1000 },
            new long[] { -1500, 0 }, new long[] { -500, 0 }, new long[] { 500, 0 }, new long[] { 1500, 0 },
            new long[] { -1500, 1000 }, new long[] { -500, 1000 },
        };

        private static readonly long[][] Four =
        {
            new long[] { -500, -500 }, new long[] { 500, -500 }, new long[] { -500, 500 }, new long[] { 500, 500 },
        };

        private static void Check(MapDocument doc, int first, long[][] offsets)
        {
            for (int i = 0; i < offsets.Length; i++)
            {
                MapUnitInfo u = doc.Units()[first + i];
                Assert.That(u.XMm - Cx, Is.EqualTo(offsets[i][0]), "rank " + i);
                Assert.That(u.ZMm - Cz, Is.EqualTo(offsets[i][1]), "rank " + i);
            }
        }

        [Test]
        public void OneSoldierStandsOnTheCentre()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30).WithSquad("rwr.soldier-1", 1, 1, Cx, Cz, 1, false);
            Assert.That(doc.Units().Count, Is.EqualTo(1));
            Check(doc, 0, new[] { new long[] { 0, 0 } });
        }

        [Test]
        public void FourSoldiersStandInTwoByTwo()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30).WithSquad("rwr.soldier-1", 1, 1, Cx, Cz, 4, false);
            Check(doc, 0, Four);
        }

        // The M6 squads: 4500..7500 by 19000..21000 around (6000, 20000).
        [Test]
        public void TenSoldiersStandInFourColumnsAndThreeRows()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30).WithSquad("rwr.soldier-1", 1, 1, Cx, Cz, 10, false);
            Assert.That(doc.Units().Count, Is.EqualTo(10));
            Check(doc, 0, Ten);
        }

        [Test]
        public void HundredSoldiersStandInTenByTen()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30).WithSquad("rwr.soldier-1", 1, 1, Cx, Cz, 100, true);
            Assert.That(doc.Units().Count, Is.EqualTo(100));
            for (int rank = 0; rank < 100; rank++)
            {
                MapUnitInfo u = doc.Units()[rank];
                Assert.That(u.XMm - Cx, Is.EqualTo(-4500 + 1000 * (rank % 10)), "rank " + rank);
                Assert.That(u.ZMm - Cz, Is.EqualTo(-4500 + 1000 * (rank / 10)), "rank " + rank);
                Assert.That(u.Key, Is.EqualTo((ulong)rank + 1));
                Assert.That(u.Reserve, Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(101)]
        [TestCase(-1)]
        public void CountOutsideOneToHundredIsRefused(int count)
        {
            MapDocument doc = Empty("rwr-m6", 40, 30);
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithSquad("rwr.soldier-1", 1, 1, Cx, Cz, count, false));
        }

        // team 1, squad 7, so a swap of the two shows.
        [Test]
        public void TeamSquadTypeAndKeysAreKept()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30).WithUnitAdded(Unit(1000, 1000));
            doc = doc.WithSquad("rwr.soldier-2", 1, 7, Cx, Cz, 10, false);
            Assert.That(doc.Units().Count, Is.EqualTo(11));
            for (int i = 1; i <= 10; i++)
            {
                MapUnitInfo u = doc.Units()[i];
                Assert.That(u.Team, Is.EqualTo(1));
                Assert.That(u.Squad, Is.EqualTo(7));
                Assert.That(u.Type, Is.EqualTo("rwr.soldier-2"));
                Assert.That(u.Key, Is.EqualTo((ulong)i + 1));
                Assert.That(u.Reserve, Is.False);
                Assert.That(u.HasPlate, Is.False);
                Assert.That(u.Owner, Is.EqualTo("hand"));
            }
            Assert.That(doc.NextId("unit"), Is.EqualTo(12UL));
        }

        [Test]
        public void SquadReachingOffTheMapIsRefusedWhole()
        {
            MapDocument doc = Empty("rwr-m6", 40, 30);
            byte[] before = doc.Save();
            Assert.Throws<ArgumentOutOfRangeException>(() => doc.WithSquad("rwr.soldier-1", 1, 1, 500, 15000, 10, false));
            Assert.That(doc.Save(), Is.EqualTo(before));
        }
    }
}
