using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;

namespace WarCore.Tests
{
    // BODY-1: the layers "rwr-body" (rwr-m6 plus a soldier body) and "rwr-field" (rwr-surfaces plus the same body).
    // The raw numbers are written out here, never read back from the layers: 1.75 m * 65536 = 114688, 1.5 m * 65536 = 98304.
    public sealed class Body1RulesTests
    {
        internal const string FlatLayer = "rwr-body", FieldLayer = "rwr-field";
        internal static readonly long[] BodyRaw = { 114688, 98304, 98304 };

        private static long[] Raw(CombatBody? body) => new[] { body.Value.Height.Raw, body.Value.Eye.Raw, body.Value.Muzzle.Raw };

        // B1: both layers load and carry the same body (height, eye, muzzle).
        [TestCase(FlatLayer)]
        [TestCase(FieldLayer)]
        public void Layer_CarriesTheSoldierBody(string layer)
        {
            ContentPack pack = RwrPack.Load(layer);
            Assert.That(pack.Body.HasValue, Is.True, layer);
            Assert.That(Raw(pack.Body), Is.EqualTo(BodyRaw), layer);
            Assert.That(Raw(pack.BuildCombat().Body), Is.EqualTo(BodyRaw), layer + " combat pack");
        }

        [Test]
        public void TheTwoLayersHaveTheSameBody() =>
            Assert.That(Raw(RwrPack.Load(FlatLayer).Body), Is.EqualTo(Raw(RwrPack.Load(FieldLayer).Body)));

        // B1: rwr-body is rwr-m6 plus the body, nothing else.
        [Test]
        public void FlatLayer_IsTheM6LayerPlusTheBody()
        {
            ContentPack parent = RwrPack.Load("rwr-m6"), child = RwrPack.Load(FlatLayer);
            SameRules(parent, child);
            Assert.That(child.Terrain, Is.Null);
            Assert.That(parent.Terrain, Is.Null);
        }

        // B1: rwr-field is rwr-surfaces plus the body: the same ten surfaces and slope rule, the same rules otherwise.
        [Test]
        public void FieldLayer_IsTheSurfacesLayerPlusTheBody()
        {
            ContentPack parent = RwrPack.Load("rwr-surfaces"), child = RwrPack.Load(FieldLayer);
            SameRules(parent, child);
            TerrainRules a = parent.Terrain, b = child.Terrain;
            Assert.That(b.SurfaceCount, Is.EqualTo(10));
            Assert.That(b.SurfaceCount, Is.EqualTo(a.SurfaceCount));
            for (int i = 0; i < a.SurfaceCount; i++)
            {
                SurfaceDef x = a.Surface(i), y = b.Surface(i);
                Assert.That((y.Id, y.Passable, y.SpeedMilli, y.Cost, y.ConcealMilli), Is.EqualTo((x.Id, x.Passable, x.SpeedMilli, x.Cost, x.ConcealMilli)), x.Id);
            }
            Assert.That((b.MaxWalkableNum, b.MaxWalkableDen), Is.EqualTo((a.MaxWalkableNum, a.MaxWalkableDen)));
        }

        // The body adds one segment to the pack digest, so each new layer's digest differs from its parent's.
        [TestCase(FlatLayer, "rwr-m6")]
        [TestCase(FieldLayer, "rwr-surfaces")]
        public void PackDigest_DiffersFromTheParents(string layer, string parent) =>
            Assert.That(RwrPack.Load(layer).BuildCombat().PolicyDigest, Is.Not.EqualTo(RwrPack.Load(parent).BuildCombat().PolicyDigest), layer);

        // B2: no older layer has a body, and their files are untouched (the SURF-1 test pins rwr-terrain-demo, rwr-ai-playtest
        // and editor.json; here the rest).
        [TestCase("rwr")]
        [TestCase("rwr-m6")]
        [TestCase("rwr-m7-playtest")]
        [TestCase("rwr-melee")]
        [TestCase("rwr-terrain-demo")]
        [TestCase("rwr-ai-playtest")]
        [TestCase("rwr-surfaces")]
        public void OlderLayers_HaveNoBody(string layer)
        {
            Assert.That(RwrPack.Load(layer).Body.HasValue, Is.False, layer);
            Assert.That(RwrPack.Load(layer).BuildCombat().Body.HasValue, Is.False, layer);
        }

        [Test]
        public void AncientLayers_HaveNoBody()
        {
            foreach (string layer in new[] { "ancient", "ancient-field" })
                Assert.That(ContentLoader.Load(new FileContentSource("Assets/WarCore/Content/Ancient/Data"), layer).Body.HasValue, Is.False, layer);
        }

        [TestCase("rwr.json", "ab899d19a791ef55a441e58ed54aad3e9ede096938279f9434e39bc67bf2e640")]
        [TestCase("rwr-m6.json", "96b95c08ead27c47bb0145f36352e349b2501194d0d7791afcc4825e2981cb28")]
        [TestCase("rwr-m7-playtest.json", "30f3ac68b7a7068a2559f989deebca779e81274b04ec34ba1c0e9dd2dcbb79cf")]
        [TestCase("rwr-melee.json", "8ee908b4a9c7e1b2498a8f5d7ac6d7607f1f48d510591faea6205db77f373347")]
        [TestCase("rwr-surfaces.json", "7b805e67ceda488ed21a23d9e5cf991940895e2fcc1cf96b0de3c780f5ba9b68")]
        public void OldLayerFiles_AreByteIdentical(string file, string sha256) => Assert.That(Sha(file), Is.EqualTo(sha256), file);

        private static string Sha(string file)
        {
            string text = File.ReadAllText(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Content/Rwr/Data", file)).Replace("\r\n", "\n");
            return BitConverter.ToString(SHA256.Create().ComputeHash(new UTF8Encoding(false).GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        // Everything a layer sets except its body and surfaces: weapons, unit types, match numbers, rule ids, selector, resolver,
        // health, friendly fire, melee and block.
        internal static void SameRules(ContentPack a, ContentPack b)
        {
            Assert.That(b.WeaponCount, Is.EqualTo(a.WeaponCount));
            for (int i = 0; i < a.WeaponCount; i++) Assert.That(b.WeaponId(i), Is.EqualTo(a.WeaponId(i)));
            Assert.That(b.UnitTypeCount, Is.EqualTo(a.UnitTypeCount));
            for (int i = 0; i < a.UnitTypeCount; i++)
            {
                string id = a.UnitTypeId(i);
                Assert.That(b.UnitTypeId(i), Is.EqualTo(id));
                Assert.That((b.WeaponOfUnitType(id), b.MeleeOfUnitType(id)), Is.EqualTo((a.WeaponOfUnitType(id), a.MeleeOfUnitType(id))), id);
            }
            Assert.That(b.HasMatch, Is.EqualTo(a.HasMatch));
            if (a.HasMatch) Assert.That(b.Match, Is.EqualTo(a.Match));
            CombatPack x = a.BuildCombat(), y = b.BuildCombat();
            Assert.That(y.Weapons.Length, Is.EqualTo(x.Weapons.Length));
            for (int i = 0; i < x.Weapons.Length; i++) Assert.That(y.Weapons[i], Is.EqualTo(x.Weapons[i]), "weapon " + i);
            Assert.That(y.Selector.PolicyKey, Is.EqualTo(x.Selector.PolicyKey));
            Assert.That(y.Resolver.PolicyKey, Is.EqualTo(x.Resolver.PolicyKey));
            Assert.That(new object[] { y.MaxHealthRaw, y.FriendlyFire, y.FriendlyFireLocked, y.AllyCheckMissRaw, y.AllyHoldMilli, y.Melee.Length, y.Block.Length },
                Is.EqualTo(new object[] { x.MaxHealthRaw, x.FriendlyFire, x.FriendlyFireLocked, x.AllyCheckMissRaw, x.AllyHoldMilli, x.Melee.Length, x.Block.Length }));
        }
    }
}
