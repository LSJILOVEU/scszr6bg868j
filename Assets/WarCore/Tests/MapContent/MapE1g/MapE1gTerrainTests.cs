using System.IO;
using System.Linq;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // MAP-E1g T0a: a 160 x 160 map takes the Emmental tile as its ground, and the tile in the repository is the one signed off.
    public sealed class MapE1gTerrainTests
    {
        private static MapTerrainResult Result()
        {
            MapTerrainResult r = null;
            Assert.DoesNotThrow(() => r = MapE1gBuild.Ground(MapE1gBuild.GenText(), MapE1gBuild.ReadDem()));
            return r;
        }

        [Test]
        public void TheTileIsTheSignedOffFile()
        {
            byte[] bytes = File.ReadAllBytes(MapE1gBuild.DemPath);
            Assert.That(bytes.LongLength, Is.EqualTo(MapE1gBuild.DemFileBytes));
            Assert.That(E1aRecordTests.Sha(bytes).ToLowerInvariant(), Is.EqualTo(MapE1gBuild.DemFileSha), "whole file, credit text included");
            Assert.That(MapDemTiles.Fingerprint(MapE1gBuild.ReadDem()), Is.EqualTo(MapE1gBuild.DemFingerprint), "heights only");
            Assert.That(MapDemTiles.Credit(MapE1gBuild.ReadDem()).Count, Is.EqualTo(6));
            string meta = File.ReadAllText(MapE1gBuild.DemPath + ".meta");
            Assert.That(meta, Does.Contain("guid: " + MapE1gBuild.DemMetaGuid + "\n"));
        }

        [Test]
        public void TheGenerationSpecIsCanonicalAndAsTheCardSays()
        {
            string file = File.ReadAllText(MapE1gBuild.GenPath);
            Assert.That(file, Is.EqualTo(MapE1gBuild.GenText()), "the committed file equals its own canonical output");
            Assert.That(MapGenSpec.Read(file).Write(), Is.EqualTo(file));
            MapGenSpec spec = MapGenSpec.Read(file);
            Assert.That((spec.Width, spec.Depth, spec.Pack, spec.Seed, spec.BaseMm, spec.Ground), Is.EqualTo((160, 160, "rwr-field", 0UL, (int?)0, "grass")));
            Assert.That(spec.Stamps.Count, Is.EqualTo(0));
            MapGenBase b = spec.Base;
            Assert.That(b.Tile, Is.EqualTo("emmental.dem.json"));
            Assert.That(b.Sha256, Is.EqualTo(MapE1gBuild.DemFingerprint));
            Assert.That(b.X0Mm, Is.EqualTo(296000));
            Assert.That(b.Z0Mm, Is.EqualTo(346000));
            Assert.That(b.ScaleMilli, Is.EqualTo(1000));
            Assert.That(b.ExaggerationMilli, Is.EqualTo(1000));
            Assert.That(b.DatumMm, Is.EqualTo(60));
        }

        [Test]
        public void TheTileLaysOnA160By160MapWithNothingSkipped()
        {
            MapTerrainResult r = Result();
            Assert.That((r.HeightsSkipped, r.SurfacesSkipped), Is.EqualTo((0, 0)));
            MapDesc d = r.Document.Desc;
            Assert.That((d.Size.X, d.Size.Z), Is.EqualTo((160, 160)));
            var h = d.Heights;
            Assert.That((h.Count, h[0].Count), Is.EqualTo((161, 161)), "161 rows (z) of 161 points (x)");
            // The card names points as (x, z); the table is h[z][x].
            Assert.That(new[] { h[0][0], h[0][160], h[160][0], h[160][160], h[142][121], h[8][98], h[150][118], h[52][101], h[48][40] },
                Is.EqualTo(new[] { 842792, 1412300, 865075, 469237, 0, 91750, 9830, 68812, 701235 }));
            Assert.That(h.Sum(row => row.Sum(v => (long)v)), Is.EqualTo(12168889680L));
            Assert.That(h.Min(row => row.Min()), Is.EqualTo(0));
            Assert.That(h.Max(row => row.Max()), Is.EqualTo(1412300));
        }

        [Test]
        public void NoStepIsSteepEnoughForTheSlopeFilterToTouch()
        {
            var h = Result().Document.Desc.Heights;
            int step = 0;
            for (int z = 0; z < h.Count; z++)
                for (int x = 0; x < h[z].Count; x++)
                {
                    if (x > 0) step = System.Math.Max(step, System.Math.Abs(h[z][x] - h[z][x - 1]));
                    if (z > 0) step = System.Math.Max(step, System.Math.Abs(h[z][x] - h[z - 1][x]));
                }
            Assert.That(step, Is.EqualTo(59638));
            Assert.That(step, Is.LessThan(92681), "the filler leaves a step this small alone");
        }
    }
}
