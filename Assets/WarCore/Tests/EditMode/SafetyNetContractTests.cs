using System;
using System.IO;
using System.Security.Cryptography;
using NUnit.Framework;

namespace WarCore.Tests
{
    public sealed class SafetyNetContractTests
    {
        [Test]
        public void IntegerParserPreservesLongEndpointsAndJsonLiterals()
        {
            VectorJson value = VectorJson.Parse("{\"numbers\":[-9223372036854775808,9223372036854775807,0,-1],\"flags\":[true,false,null],\"text\":\"quote\\\" slash\\\\ line\\n unicode\\u0041\"}");
            Assert.That(value["numbers"].Integers(), Is.EqualTo(new[]{long.MinValue, long.MaxValue, 0, -1}));
            Assert.That(value["flags"][0].Boolean, Is.True);
            Assert.That(value["flags"][1].Boolean, Is.False);
            Assert.That(value["flags"][2].IsNull, Is.True);
            Assert.That(value["text"].Text, Is.EqualTo("quote\" slash\\ line\n unicodeA"));
            Assert.Throws<OverflowException>(() => VectorJson.Parse("9223372036854775808"));
            Assert.Throws<OverflowException>(() => VectorJson.Parse("-9223372036854775809"));
        }

        [TestCase("[1,]")]
        [TestCase("{\"a\":1,\"a\":2}")]
        [TestCase("01")]
        [TestCase("1.5")]
        [TestCase("1e3")]
        [TestCase("- 1")]
        [TestCase("[true false]")]
        [TestCase("null trailing")]
        public void ParserRejectsMalformedOrInexactInputs(string input)
        {
            Assert.Throws<FormatException>(() => VectorJson.Parse(input));
        }

        private static string Sha256(string path)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
        }

        [TestCase("fixed-math-vectors.json", "aef92ad0b761da1465e9a2c0fb40b40bd432f8685705a6097ac441897113c219")]
        [TestCase("fixed-math-vectors-q32.json", "526d65f9dd5e9d57dfe9bc95aa46337cd723fca8413b9927d95efac7ce14d2e9")]
        [TestCase("spatial-narrowphase-vectors.json", "43c03c8c35149a985279dabf59a19db412bb1acca148b8bc0865684d7d3c25ad")]
        public void FixedVectorContractsKeepTheirOriginalBytes(string file, string checksum)
        {
            Assert.That(Sha256(VectorJson.PathFor(file)), Is.EqualTo(checksum));
        }

        // Pin the copied arithmetic, not the evolving product, to prevent both sides drifting together.
        [TestCase("WideInt.cs", "accbc48c51b61e861b6d84e10e259505b10d64f6246afc3fd3e5f192dae31b83")]
        [TestCase("SimWide.cs", "6af91c3cffa914d3deea68e1ab6f305a4e9eae59f526ee6803e5a3ce4d0f954e")]
        [TestCase("Q16AtanOracle.cs", "6ba77a078f572c1e5eef5de41e1c3781fdd414d0229fd8f4a893be13426a76c8")]
        public void FrozenOracleCopiesKeepTheirRecordedBytes(string file, string checksum)
        {
            string path = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Tests/EditMode/Oracle", file);
            Assert.That(Sha256(path), Is.EqualTo(checksum));
        }

        [Test]
        public void VectorInventoryIncludesEveryBoundaryCategory()
        {
            foreach (string file in new[]{"fixed-math-vectors.json", "fixed-math-vectors-q32.json"})
            {
                VectorJson document = VectorJson.Load(file);
                string[] groups = {"mul", "div", "ratio", "sqrt", "sin", "cos", "atan2", "vec3_wide", "midpoint"};
                int[] counts = {1476, 1376, 200, 277, 638, 638, 536, 127, 80};
                int total = 0;
                for (int i = 0; i < groups.Length; i++)
                {
                    Assert.That(document[groups[i]].Count, Is.EqualTo(counts[i]), file + " " + groups[i]);
                    total += document[groups[i]].Count;
                }

                Assert.That(total, Is.EqualTo(5348));
            }

            VectorJson rows = VectorJson.Load("spatial-narrowphase-vectors.json")["cases"];
            int near = 0;
            int tangent = 0;
            int wide = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                near += rows[i]["nearBoundary"].Boolean ? 1 : 0;
                tangent += rows[i]["exactTangent"].Boolean ? 1 : 0;
                wide += rows[i]["note"].Text.Contains("wide-operands") ? 1 : 0;
            }

            Assert.That(rows.Count, Is.EqualTo(733));
            Assert.That(near, Is.EqualTo(96));
            Assert.That(tangent, Is.EqualTo(51));
            Assert.That(wide, Is.EqualTo(50));
            TestContext.WriteLine("Spatial inventory per provider: total=733, nearBoundary=96, exactTangent=51, wide-operands=50; no category filters.");
        }
    }
}
