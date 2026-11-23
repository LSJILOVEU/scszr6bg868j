using System.Linq;
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

        // B2: no older layer has a body.
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
