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
        private static string Hex(byte[] bytes) => BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();

        private static string Sha256Hex(byte[] bytes)
        {
            Func<SHA256> factory = SHA256.Create;
            using SHA256 sha = factory();
            return Hex(sha.ComputeHash(bytes));
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
            Assert.That(hex, Is.EqualTo(Hex(Bake(file).DescHash)));
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
        // IReadOnlyList<T> of an allowed T, and other types of the home assembly (Presentation) whose own public members,
        // inherited ones included, follow the same rule. Members only object, ValueType or Enum declare are skipped.
        internal static List<string> SurfaceViolations(Type root, Assembly home, out int membersChecked)
        {
            var problems = new List<string>();
            var seen = new HashSet<Type>();
            int count = 0;
            bool Universal(Type declaring) => declaring == typeof(object) || declaring == typeof(ValueType) || declaring == typeof(Enum);
            void CheckType(Type t, string where)
            {
                if (t.IsByRef) t = t.GetElementType();
                if (t == typeof(void) || t.IsPrimitive || t == typeof(string) || t == typeof(byte[]) || t == typeof(SimSession)) return;
                if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)) { CheckType(t.GetGenericArguments()[0], where); return; }
                if (t.Assembly == home && !t.IsArray && !t.IsGenericType) { CheckMembers(t); return; }
                problems.Add(where + ": " + t.FullName);
            }
            void CheckMembers(Type t)
            {
                if (!seen.Add(t)) return;
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
                foreach (MethodInfo m in t.GetMethods(flags))
                {
                    if (Universal(m.GetBaseDefinition().DeclaringType)) continue;
                    count++;
                    CheckType(m.ReturnType, t.Name + "." + m.Name + " return");
                    foreach (ParameterInfo p in m.GetParameters()) CheckType(p.ParameterType, t.Name + "." + m.Name + " " + p.Name);
                }
                foreach (ConstructorInfo c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                {
                    count++;
                    foreach (ParameterInfo p in c.GetParameters()) CheckType(p.ParameterType, t.Name + ".ctor " + p.Name);
                }
                foreach (PropertyInfo p in t.GetProperties(flags))
                {
                    if (Universal(p.DeclaringType)) continue;
                    count++; CheckType(p.PropertyType, t.Name + "." + p.Name);
                    foreach (ParameterInfo i in p.GetIndexParameters()) CheckType(i.ParameterType, t.Name + "." + p.Name + " " + i.Name);
                }
                foreach (FieldInfo f in t.GetFields(flags))
                {
                    if (Universal(f.DeclaringType)) continue;
                    count++; CheckType(f.FieldType, t.Name + "." + f.Name);
                }
                foreach (EventInfo e in t.GetEvents(flags)) { count++; CheckType(e.EventHandlerType, t.Name + "." + e.Name); }
            }
            CheckMembers(root);
            membersChecked = count;
            return problems;
        }

        [Test]
        public void MapDocumentPublicSurfaceNamesNoContentOrSimulationType()
        {
            List<string> problems = SurfaceViolations(typeof(MapDocument), typeof(MapDocument).Assembly, out int members);
            Assert.That(problems, Is.Empty);
            // Open, Save, Start, the DescHashHex and PackId getters, and the two properties.
            Assert.That(members, Is.GreaterThanOrEqualTo(7));
        }

        // The walker itself: WorldRecipe exposes Combat, Content and Battle types, and each one is reported.
        [Test]
        public void SurfaceWalkerReportsContentAndCombatTypes()
        {
            List<string> problems = SurfaceViolations(typeof(WorldRecipe), typeof(WorldRecipe).Assembly, out _);
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Combat.CombatPack"));
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Content.ContentPack"));
            Assert.That(problems, Has.Some.EndsWith(": WarCore.Battle.MatchPack"));
        }

        // A home-assembly type that leaks a Content type only through inherited members (indexer, Add, enumerator).
        public sealed class LeakList : List<WarCore.Content.ContentPack> { }
        public sealed class LeakDocument
        {
            public LeakList Items => null;
            public IReadOnlyList<WarCore.Content.ContentPack> Packs => null;
            public List<int> Numbers => null;
            public IReadOnlyList<int> Allowed => null;
        }

        [Test]
        public void SurfaceWalkerReportsInheritedMembers()
        {
            List<string> problems = SurfaceViolations(typeof(LeakDocument), typeof(LeakDocument).Assembly, out _);
            Assert.That(problems, Has.Some.EqualTo("LeakList.Add item: WarCore.Content.ContentPack"));
            Assert.That(problems, Has.Some.EqualTo("LeakList.Item: WarCore.Content.ContentPack"));
            // The element type of IReadOnlyList<T> is checked, and no other generic type is let through.
            Assert.That(problems, Has.Some.EqualTo("LeakDocument.Packs: WarCore.Content.ContentPack"));
            Assert.That(problems, Has.Some.StartsWith("LeakDocument.Numbers: System.Collections.Generic.List`1"));
            Assert.That(problems, Has.None.StartsWith("LeakDocument.Allowed"));
        }

        // A description that is valid but not canonical: Save returns the canonical bytes and the hash is taken over them.
        [Test]
        public void NonCanonicalDescriptionSavesCanonicalBytes()
        {
            byte[] input = Utf8(E1bSamples.TerrainCase(true, true));
            byte[] canonical = MapDescJson.Write(MapDescJson.Read(input));
            Assert.That(canonical, Is.Not.EqualTo(input));
            MapDocument doc = MapDocument.Open(input);
            Assert.That(doc.Save(), Is.EqualTo(canonical));
            Assert.That(doc.DescHashHex, Is.EqualTo(Sha256Hex(canonical)));
            Assert.That(doc.DescHashHex, Is.Not.EqualTo(Sha256Hex(input)));
        }
    }
}
