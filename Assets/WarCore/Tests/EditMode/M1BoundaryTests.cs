using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class M1BoundaryTests
    {
        [Test]
        public void M1_PresentationSource_ConfinesSimReferencesToTwoBridgeFiles()
        {
            string root = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Presentation");
            Assert.That(Directory.Exists(root), Is.True);
            string[] files = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty);
            var simFiles = new List<string>();
            foreach (string file in files)
            {
                string source = SourceRules.StripComments(File.ReadAllText(file));
                Assert.That(source, Does.Not.Match(@"\bUnityEngine\b"), file);
                if (UsesSimulationNamespace(source))
                    simFiles.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
            Assert.That(simFiles, Is.EquivalentTo(new[] { "Bridge/SimSession.cs", "Bridge/WorldBuilder.cs" }));
        }

        [TestCase("using WarCore.Sim;")]
        [TestCase("global::WarCore /* gap */ . Sim.Position position;")]
        [TestCase("Sim.Position position;")]
        [TestCase("using Alias = WarCore.Sim.Position;")]
        public void M1_SourceGuard_DetectsFullAndRelativeSimulationNamespaces(string source)
        {
            Assert.That(UsesSimulationNamespace(source), Is.True);
            Assert.That(UsesSimulationNamespace("// " + source), Is.False);
            Assert.That(UsesSimulationNamespace("SimSession session; SimulationView view;"), Is.False);
        }

        [Test]
        public void M1_PresentationAssembly_PublicApiDoesNotExposeSimOrInterimTypes()
        {
            Assembly assembly = typeof(SimSession).Assembly;
            Type[] exported = assembly.GetExportedTypes();
            Assert.That(exported, Is.Not.Empty);
            foreach (Type type in exported)
                foreach (Type signature in SignatureTypes(type))
                    Assert.That(ContainsSimulationType(signature), Is.False, type.FullName + " exposes " + signature);
            Assert.That(assembly.GetReferencedAssemblies().Any(a => a.Name.StartsWith("UnityEngine", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public void M1_ApiGuard_DetectsDirectWrappedAndByRefSimTypes()
        {
            Assert.That(ContainsSimulationType(typeof(SimVec3)), Is.True);
            Assert.That(ContainsSimulationType(typeof(List<Position[]>)), Is.True);
            Assert.That(ContainsSimulationType(typeof(SimVec3).MakeByRefType()), Is.True);
            Assert.That(ContainsSimulationType(typeof(ILocomotion)), Is.True);
            Assert.That(ContainsSimulationType(typeof(IReadOnlyList<ViewEntity>)), Is.False);
            Assert.That(ContainsSimulationType(typeof(ReadOnlySpan<ViewEntity>)), Is.False);
        }

        [Test]
        public void M1_PresentationAndInterimAsmdefs_DisableEngineReferences()
        {
            foreach (string layer in new[] { "Presentation", "Interim" })
            {
                string path = Path.Combine(ArchitectureTests.AssetRoot(), "WarCore", layer, "WarCore." + layer + ".asmdef");
                Assert.That(File.Exists(path), Is.True);
                Assert.That(Regex.IsMatch(File.ReadAllText(path), "\"noEngineReferences\"\\s*:\\s*true"), Is.True, path);
            }
        }

        [Test]
        public void M1_InterimRegistrations_UseInterimPrefixExceptAutomaticCoreRegistrations()
        {
            var content = new StableContentManifest(1);
            var baseline = Simulation.CreateRetainedWorld(1, new PlacedCatalog(content), content: content);
            var positions = baseline.Entities.Register<Position>("test.position", new PositionSerializer());
            var colliders = baseline.Entities.Register<Collider>("test.collider", new ColliderSerializer());
            baseline.ConfigureSpatial(positions, colliders, SimFix.FromInt(2));
            string[] automaticIds = ComponentNames(baseline.Entities)
                .Where(id => id != "test.position" && id != "test.collider").ToArray();
            Simulation sim = M1TestWorld.SimulationOf(SimSession.Create(M1TestWorld.Recipe()));
            string[] customIds = ComponentNames(sim.Entities).Except(automaticIds).ToArray();
            Assert.That(customIds, Does.Contain(MoveIntent.ComponentId));
            Assert.That(customIds.Length, Is.GreaterThanOrEqualTo(3));
            foreach (string id in customIds)
                Assert.That(id, Does.StartWith("interim."));
            int count = 0;
            foreach (object entry in (IEnumerable)ReadField(sim, "systems"))
            {
                object system = ReadField(entry, "System");
                if (system.GetType().Assembly != typeof(InterimLocomotion).Assembly)
                    continue;
                count++;
                Assert.That((string)ReadField(entry, "Id"), Does.StartWith("interim."));
                Assert.That(((IStableStateSerializer)system).StateTypeId, Does.StartWith("interim."));
            }
            Assert.That(count, Is.EqualTo(2));
            Assert.That(new MoveIntentSerializer().TypeId, Is.GreaterThan(0));
            Assert.That(new MoveIntentSerializer().Version, Is.GreaterThan(0));
        }

        private static IEnumerable<string> ComponentNames(EntityStore entities)
        {
            foreach (object storage in (IEnumerable)ReadField(entities, "storages"))
                yield return (string)storage.GetType().GetProperty("Name").GetValue(storage);
        }

        private static object ReadField(object owner, string name)
        {
            FieldInfo field = owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, owner.GetType().FullName + "." + name);
            return field.GetValue(owner);
        }

        private static bool ContainsSimulationType(Type type)
        {
            if (type == null)
                return false;
            if (type.Assembly == typeof(SimClock).Assembly || type.Assembly == typeof(ILocomotion).Assembly)
                return true;
            if (type.HasElementType)
                return ContainsSimulationType(type.GetElementType());
            return type.IsGenericType && type.GetGenericArguments().Any(ContainsSimulationType);
        }

        private static bool UsesSimulationNamespace(string source)
            => Regex.IsMatch(SourceRules.StripComments(source), @"\bSim\b");

        private static IEnumerable<Type> SignatureTypes(Type type)
        {
            yield return type.BaseType;
            foreach (Type contract in type.GetInterfaces())
                yield return contract;
            foreach (Type argument in type.GetGenericArguments())
                foreach (Type constraint in argument.GetGenericParameterConstraints())
                    yield return constraint;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (FieldInfo field in type.GetFields(flags))
                if (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly)
                    yield return field.FieldType;
            foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
            {
                if (!method.IsPublic && !method.IsFamily && !method.IsFamilyOrAssembly)
                    continue;
                if (method is MethodInfo info)
                    yield return info.ReturnType;
                foreach (ParameterInfo parameter in method.GetParameters())
                    yield return parameter.ParameterType;
                if (method.IsGenericMethod)
                    foreach (Type argument in method.GetGenericArguments())
                        foreach (Type constraint in argument.GetGenericParameterConstraints())
                            yield return constraint;
            }
        }
    }
}
