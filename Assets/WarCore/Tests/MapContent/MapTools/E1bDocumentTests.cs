using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Content.MapTools;
using WarCore.Presentation;
using static WarCore.Tests.E1aSamples;

namespace WarCore.Tests
{
    // The MapDocument facade: round trip of the repository descriptions, hash text, errors, and its public surface.
    public sealed class E1bDocumentTests
    {
        private static string Sha256Hex(byte[] bytes)
        {
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            return Convert.ToHexString(sha.ComputeHash(bytes)).ToLowerInvariant();
        }

        [TestCase(M6Path, "rwr-m6")]
        [TestCase(TerrainPath, "rwr-terrain-demo")]
        public void RepositoryDescriptionRoundTripsByteForByte(string path, string pack)
        {
            byte[] file = File.ReadAllBytes(path);
            MapDocument doc = MapDocument.Open(file);
            Assert.That(doc.Save(), Is.EqualTo(file));
            Assert.That(doc.PackId, Is.EqualTo(pack));
            string hex = doc.DescHashHex;
            Assert.That(hex.Length, Is.EqualTo(64));
            Assert.That(hex, Is.EqualTo(Sha256Hex(file)));
            Assert.That(hex, Is.EqualTo(Convert.ToHexString(Bake(file).DescHash).ToLowerInvariant()));
        }

        [Test]
        public void BadDescriptionThrowsTheReaderError()
        {
            byte[] bad = Utf8(Edit(Sample, "\"version\": 1,", "\"version\": 2,"));
            string expected = Assert.Throws<InvalidDataException>(() => MapDescJson.Read(bad)).Message;
            Assert.That(expected, Does.StartWith("MAPDESC_"));
            InvalidDataException e = Assert.Throws<InvalidDataException>(() => MapDocument.Open(bad));
            Assert.That(e.Message, Is.EqualTo(expected));
        }

        [Test]
        public void MapDocumentIsDeclaredPartial()
        {
            string source = File.ReadAllText(Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Presentation/Maps/MapDocument.cs"));
            Assert.That(source, Does.Contain("public sealed partial class MapDocument"));
        }

        // Allowed in a public signature: primitives, string, byte[], void, SimSession (its surface is M1BoundaryTests' job),
        // IReadOnlyList<T> of an allowed T, and other Presentation types whose own public members follow the same rule.
        internal static List<string> SurfaceViolations(Type root, out int membersChecked)
        {
            var problems = new List<string>();
            var seen = new HashSet<Type>();
            int count = 0;
            void CheckType(Type t, string where)
            {
                if (t.IsByRef) t = t.GetElementType();
                if (t == typeof(void) || t.IsPrimitive || t == typeof(string) || t == typeof(byte[]) || t == typeof(SimSession)) return;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) { CheckType(t.GetGenericArguments()[0], where); return; }
                if (t.Assembly == typeof(MapDocument).Assembly && !t.IsArray && !t.IsGenericType) { CheckMembers(t); return; }
                problems.Add(where + ": " + t.FullName);
            }
            void CheckMembers(Type t)
            {
                if (!seen.Add(t)) return;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MethodInfo m in t.GetMethods(flags))
                {
                    if (m.GetBaseDefinition().DeclaringType != t) continue; // overrides of object members
                    count++;
                    CheckType(m.ReturnType, t.Name + "." + m.Name + " return");
                    foreach (ParameterInfo p in m.GetParameters()) CheckType(p.ParameterType, t.Name + "." + m.Name + " " + p.Name);
                }
                foreach (ConstructorInfo c in t.GetConstructors(flags))
                {
                    count++;
                    foreach (ParameterInfo p in c.GetParameters()) CheckType(p.ParameterType, t.Name + ".ctor " + p.Name);
                }
                foreach (PropertyInfo p in t.GetProperties(flags)) { count++; CheckType(p.PropertyType, t.Name + "." + p.Name); }
                foreach (FieldInfo f in t.GetFields(flags)) { count++; CheckType(f.FieldType, t.Name + "." + f.Name); }
            }
            CheckMembers(root);
            membersChecked = count;
            return problems;
        }

        [Test]
        public void MapDocumentPublicSurfaceNamesNoContentOrSimulationType()
        {
            List<string> problems = SurfaceViolations(typeof(MapDocument), out int members);
            Assert.That(problems, Is.Empty);
            // Open, Save, Start, the DescHashHex and PackId getters, and the two properties.
            Assert.That(members, Is.GreaterThanOrEqualTo(7));
        }

        // The walker itself: WorldRecipe exposes Combat, Content and Battle types, and each one is reported.
        [Test]
        public void SurfaceWalkerReportsContentAndCombatTypes()
        {
            List<string> problems = SurfaceViolations(typeof(WorldRecipe), out _);
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Combat.CombatPack"));
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Content.ContentPack"));
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Battle.MatchPack"));
        }
    }
}
