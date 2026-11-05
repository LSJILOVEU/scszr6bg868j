using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Review conditions for block 3a: every shape-sensitive path outside the narrow phase.
    [Category("M0c3a")]
    public sealed class BoxContractTests
    {
        private static SimFix F(long raw) => SimFix.FromRaw(raw);

        // A long low wall: H < 2*hx would fail the capsule constructor if a copy went through it.
        private static Collider Wall(uint layer = 1u, bool isStatic = false)
            => Collider.Box(SimFix.FromInt(50), SimFix.FromInt(1), SimFix.Ratio(1, 2), BoxYaw.FromGenerator(2, 1), layer, isStatic);

        private static string Legacy(int count, RayHit[] hits)
            => string.Join(",", hits.Take(count).Select(h => h.Entity.Id + "@" + h.Distance.Raw).OrderBy(x => x, StringComparer.Ordinal));

        private static string Legacy(int count, EntityId[] entities)
            => string.Join(",", entities.Take(count).OrderBy(e => e.Id).ThenBy(e => e.Generation));

        [Test]
        public void WithLayerAndWithStaticKeepEveryBoxFieldAndChangeOnlyTheirBytes()
        {
            Collider wall = Wall();
            Collider copy = wall.WithLayer(0x80000000u).WithStatic(true);
            Assert.That((copy.ShapeKind, copy.Radius, copy.Height, copy.HalfDepth, copy.Yaw), Is.EqualTo((wall.ShapeKind, wall.Radius, wall.Height, wall.HalfDepth, wall.Yaw)));
            Assert.That((copy.Layer, copy.IsStatic), Is.EqualTo((0x80000000u, true)));
            Assert.That(Fixtures.Hash(copy), Is.EqualTo(Fixtures.Hash(Wall(0x80000000u, true))));
            Assert.That(Fixtures.Hash(copy.WithLayer(1u).WithStatic(false)), Is.EqualTo(Fixtures.Hash(wall)));
            Assert.That(copy.WorldHalfExtents, Is.EqualTo(wall.WorldHalfExtents));
            Collider lowered = wall.WithHeight(SimFix.Ratio(1, 2));
            Assert.That((lowered.ShapeKind, lowered.Radius, lowered.HalfDepth, lowered.Yaw, lowered.Height),
                Is.EqualTo((wall.ShapeKind, wall.Radius, wall.HalfDepth, wall.Yaw, SimFix.Ratio(1, 2))));
            Assert.Throws<ArgumentOutOfRangeException>(() => wall.WithHeight(F(-1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => wall.WithHeight(F(32768L * SimFix.Scale + 1)));
            var capsule = new Collider(SimFix.FromInt(1), SimFix.FromInt(3), 5u);
            Assert.That(Fixtures.Hash(capsule.WithLayer(9u).WithStatic(true)), Is.EqualTo(Fixtures.Hash(new Collider(SimFix.FromInt(1), SimFix.FromInt(3), 9u, ShapeKind.VerticalCapsule, true))));
            Assert.Throws<ArgumentOutOfRangeException>(() => capsule.WithHeight(F(2 * SimFix.Scale - 1)));
        }

        // The diagnostic replay must stage boxes like capsules: one Prefilter per ray/sweep candidate and one
        // Precise per narrow-phase entry, or profiles of box scenes report work the product never did.
        [Test]
        public void DiagnosticReplayCountsBoxStagesLikeTheProduct()
        {
            var store = new EntityStore(64);
            var positions = store.Register<Position>("position");
            var colliders = store.Register<Collider>("collider");
            var random = new Random(0x3AD1A6);
            for (int i = 0; i < 48; i++)
            {
                EntityId id = store.Create();
                store.Set(positions, id, new Position(SimVec3.Metres(random.Next(-20, 21), random.Next(-2, 3), random.Next(-20, 21))));
                Collider collider = i % 2 == 0
                    ? new Collider(SimFix.Ratio(1 + random.Next(4), 2), SimFix.FromInt(4))
                    : Collider.Box(SimFix.FromInt(1 + random.Next(6)), SimFix.FromInt(1 + random.Next(4)), SimFix.Ratio(1 + random.Next(6), 2),
                        BoxYaw.FromGenerator(1 + random.Next(9), random.Next(-9, 10)));
                store.Set(colliders, id, collider);
            }
            var grid = new GridSpatialQuery(store, positions, colliders, SimFix.FromInt(4), 64, 4096);
            var replay = new Diagnostics.ProfiledGridSpatialQuery(store, positions, colliders, SimFix.FromInt(4), 64, 4096);
            grid.Rebuild();
            replay.Rebuild();
            grid.Diagnostics = new SpatialQueryDiagnostics();
            replay.Diagnostics = new SpatialQueryDiagnostics();
            var expectedIds = new EntityId[64];
            var expectedRays = new RayHit[64];
            var ids = new EntityId[64];
            var rays = new RayHit[64];
            int narrowEntries = 0;
            for (int i = 0; i < 200; i++)
            {
                var kind = (SpatialQueryKind)(i % 5);
                SimVec3 origin = SimVec3.Metres(random.Next(-24, 25), random.Next(-2, 3), random.Next(-24, 25));
                SimVec3 direction = SimVec3.Metres(random.Next(-9, 10), random.Next(-1, 2), random.Next(-9, 10));
                if (direction.X.Raw == 0 && direction.Z.Raw == 0)
                    direction = SimVec3.Metres(1, 0, 0);
                SpatialRequest request = kind == SpatialQueryKind.Sphere ? SpatialQueries.Sphere(origin, SimFix.FromInt(6))
                    : kind == SpatialQueryKind.Cylinder ? SpatialQueries.Cylinder(origin, SimFix.FromInt(6), SimFix.FromInt(2))
                    : kind == SpatialQueryKind.Sector ? SpatialQueries.Sector(origin, SimFix.FromInt(12), new SimVec3(direction.X, default, direction.Z), 600, SimFix.FromInt(2))
                    : kind == SpatialQueryKind.Capsule ? SpatialQueries.Capsule(origin, origin + direction, SimFix.Ratio(1, 2))
                    : SpatialQueries.Ray(origin, direction, SimFix.FromInt(40));
                int expected = Diagnostics.QueryDiagnosticTests.Run(grid, request, expectedIds, expectedRays);
                for (int mode = 1; mode < 4; mode++)
                {
                    replay.Trace.Enabled = mode >= 2;
                    replay.Trace.DeepGeometry = mode != 3;
                    replay.Trace.Reset();
                    int actual = Diagnostics.QueryDiagnosticTests.Run(replay, request, ids, rays);
                    Diagnostics.RealSimBench.AssertOutput(kind == SpatialQueryKind.Ray ? 0 : 1, expected, actual, expectedIds, expectedRays, ids, rays);
                    Assert.That((replay.LastCandidateCount, replay.LastNarrowPhaseCount), Is.EqualTo((grid.LastCandidateCount, grid.LastNarrowPhaseCount)), "query " + i);
                    Assert.That(Diagnostics.RealSimBench.SameCounters(grid.Diagnostics, replay.Diagnostics), Is.True, "query " + i);
                    if (mode < 2)
                        continue;
                    Assert.That(replay.Trace.Calls[(int)Diagnostics.QueryStage.Precise], Is.EqualTo(replay.LastNarrowPhaseCount), "query " + i + " mode " + mode);
                    // Rays and sweeps pass the slab prefilter for both shapes. For area kinds only the deep capsule
                    // replay has a prefilter sub-stage; boxes have no profiled copy, so they stay one Precise stage.
                    long prefilter = replay.Trace.Calls[(int)Diagnostics.QueryStage.Prefilter];
                    if (kind == SpatialQueryKind.Ray || kind == SpatialQueryKind.Capsule)
                        Assert.That(prefilter, Is.EqualTo(replay.LastCandidateCount), "query " + i + " mode " + mode);
                    else
                        Assert.That(prefilter, Is.LessThanOrEqualTo(mode == 2 ? replay.LastCandidateCount : 0), "query " + i + " mode " + mode);
                }
                narrowEntries += grid.LastNarrowPhaseCount;
            }
            Assert.That(narrowEntries, Is.GreaterThan(200));
        }

        [Test]
        public void TickEndSynchronizeAfterSameTickDeathsKeepsSurvivingBoxes()
        {
            var fixture = new QueryModeFixture();
            EntityId wall = fixture.Add(SimVec3.Metres(0, 0, 0), Wall(0x80000001u));
            EntityId deadBox = fixture.Add(SimVec3.Metres(4, 0, 6), Collider.Box(SimFix.FromInt(2), SimFix.FromInt(1), SimFix.FromInt(3), BoxYaw.FromGenerator(4095, 1)));
            EntityId deadCapsule = fixture.Add(SimVec3.Metres(-6, 0, 2), SimFix.Scale / 2, 2 * SimFix.Scale);
            EntityId capsule = fixture.Add(SimVec3.Metres(8, 0, -3), SimFix.Scale / 2, 2 * SimFix.Scale);
            EntityId block = fixture.Add(SimVec3.Metres(-3, 0, -7), Collider.Box(SimFix.FromInt(3), SimFix.FromInt(4), SimFix.FromInt(1), BoxYaw.QuarterTurns(1)));
            fixture.Store.Destroy(deadBox);
            fixture.Store.Destroy(deadCapsule);
            // A layer change in the same tick takes the full copy path for every live body.
            fixture.Store.Get(fixture.Colliders, block) = fixture.Store.Get(fixture.Colliders, block).WithLayer(3u);
            fixture.Grid.Synchronize();
            var output = new SpatialHit[8];
            SpatialRequest sphere = SpatialQueries.Sphere(SimVec3.Metres(28, 0, -29), SimFix.FromInt(6));
            SpatialQueryResult result = fixture.Grid.AllCapped(in sphere, 8, output);
            Assert.That(result.Written, Is.EqualTo(1));
            Assert.That(output[0].Entity, Is.EqualTo(wall), "The yawed 50 m wall passes 4.5 m from (28, -29); its centre is 40.3 m away.");
            var naive = new NaiveSpatialQuery(fixture.Bodies.Where(b => fixture.Store.IsAlive(b.Entity))
                .Select(b => new SpatialBody(b.Entity, b.Position, fixture.Store.Get(fixture.Colliders, b.Entity))));
            for (int kind = 0; kind < 5; kind++)
                fixture.Check(QueryModeFixture.Request((SpatialQueryKind)kind), SimVec3.Metres(2, 1, -1), 8, 8);
            SimFix reach = SimFix.FromInt(16);
            SimVec3 forward = SimVec3.Metres(1, 0, 0);
            var expected = new EntityId[16];
            var actual = new EntityId[16];
            Assert.That(Legacy(fixture.Grid.Sphere(SimVec3.Metres(28, 0, -29), SimFix.FromInt(6), actual), actual),
                Is.EqualTo(Legacy(naive.Sphere(SimVec3.Metres(28, 0, -29), SimFix.FromInt(6), expected), expected)));
            Assert.That(Legacy(fixture.Grid.Sphere(default, reach, actual), actual), Is.EqualTo(Legacy(naive.Sphere(default, reach, expected), expected)));
            Assert.That(Legacy(fixture.Grid.Cylinder(default, reach, reach, actual), actual), Is.EqualTo(Legacy(naive.Cylinder(default, reach, reach, expected), expected)));
            Assert.That(Legacy(fixture.Grid.Sector(default, reach, forward, 600, reach, actual), actual), Is.EqualTo(Legacy(naive.Sector(default, reach, forward, 600, reach, expected), expected)));
            Assert.That(Legacy(fixture.Grid.Capsule(default, SimVec3.Metres(16, 0, 0), SimFix.FromInt(4), actual), actual),
                Is.EqualTo(Legacy(naive.Capsule(default, SimVec3.Metres(16, 0, 0), SimFix.FromInt(4), expected), expected)));
            var expectedRay = new RayHit[16];
            var actualRay = new RayHit[16];
            Assert.That(Legacy(fixture.Grid.Ray(default, forward, reach, actualRay), actualRay),
                Is.EqualTo(Legacy(naive.Ray(default, forward, reach, expectedRay), expectedRay)));
            Assert.That(fixture.Store.Get(fixture.Colliders, block).HalfDepth, Is.EqualTo(SimFix.FromInt(1)));
            Assert.That(fixture.Store.IsAlive(capsule), Is.True);
        }

        [Test]
        public void YawOrDepthOnlyChangesResynchronizeTheGrid()
        {
            // Synchronize refreshes only layers, flags and deaths; geometry reaches the grid through
            // Upsert and the Sim-owned SynchronizeBodies, which both compare bodies with SameBody.
            var fixture = new QueryModeFixture();
            Collider bar = Collider.Box(SimFix.FromInt(8), SimFix.FromInt(2), SimFix.Ratio(1, 2), BoxYaw.Identity);
            EntityId box = fixture.Add(default, bar);
            SpatialRequest probe = SpatialQueries.Sphere(SimVec3.Metres(0, 0, 6), SimFix.FromInt(1));
            Assert.That(fixture.Grid.Any(in probe), Is.False, "The bar runs along X and stays 5.5 m from the probe.");
            // Same centre, extents, height, layer and flag: only the yaw differs (Set upserts).
            fixture.Set(box, default, Collider.Box(SimFix.FromInt(8), SimFix.FromInt(2), SimFix.Ratio(1, 2), BoxYaw.QuarterTurns(1)));
            Assert.That(fixture.Grid.Any(in probe), Is.True, "A quarter turn lays the bar along Z through the probe.");
            fixture.Check(probe, default, 4, 4);
            // Only the half depth differs; the Sim-owned path finds the change itself.
            Replace(fixture, box, Collider.Box(SimFix.FromInt(8), SimFix.FromInt(2), SimFix.FromInt(6), BoxYaw.Identity));
            Assert.That(fixture.Grid.Any(in probe), Is.True, "A 6 m half depth reaches the probe.");
            fixture.Check(probe, default, 4, 4);
            Replace(fixture, box, bar);
            Assert.That(fixture.Grid.Any(in probe), Is.False);
            fixture.Check(probe, default, 4, 4);
        }

        private static void Replace(QueryModeFixture fixture, EntityId entity, Collider collider)
        {
            fixture.Store.Get(fixture.Colliders, entity) = collider;
            fixture.Bodies[entity.Id] = new SpatialBody(entity, fixture.Bodies[entity.Id].Position, collider);
            fixture.Grid.Index.SynchronizeBodies();
        }

        [Test]
        public void LegacyCompatibilityHashModesRejectBoxesButKeepCapsules()
        {
            var capsule = new Collider(SimFix.FromInt(1), SimFix.FromInt(3), 5u);
            foreach (StateHash legacy in new[] { new StateHash(ignoreColliderLayers: false), new StateHash(ignoreColliderLayers: true, ignoreColliderStatic: false), new StateHash(true) })
            {
                capsule.WriteState(legacy);
                Assert.That(Assert.Throws<SimBoundaryException>(() => Wall().WriteState(legacy)).Message, Is.EqualTo("M0C_SHAPE_LEGACY_HASH"));
            }
            Assert.DoesNotThrow(() => Wall().WriteState(new StateHash()));
        }

        [Test]
        public void NonDefaultPostureOnABoxFailsExplicitly()
        {
            var store = new EntityStore(4);
            var postures = store.Register<Posture>("posture");
            var geometry = new PostureGeometry(store, postures, PostureFixture.Definitions());
            EntityId entity = store.Create();
            SpatialBody standing = geometry.BodyFor(entity, SimVec3.Metres(1, 2, 3), Wall(), 0);
            Assert.That(standing.Collider.ShapeKind, Is.EqualTo(ShapeKind.OrientedBox));
            var error = Assert.Throws<SimBoundaryException>(() => geometry.BodyFor(entity, SimVec3.Metres(1, 2, 3), Wall(), 2));
            Assert.That(error.Message, Is.EqualTo("M0C_SHAPE_BOX_POSTURE"));
            Assert.That(geometry.BodyFor(entity, SimVec3.Metres(1, 2, 3), new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(3)), 2).Collider.Height, Is.EqualTo(SimFix.Ratio(3, 2)));
        }

        private static GameplayAbilityDef Ability(GameplayTargetSpec target)
            => new GameplayAbilityDef(1, "target", 1, 1, target, new[] { M0c2S3Fixture.Action(1, GameplayPrimitive.Damage, 1, 17) }, new[] { new GameplayCost(2, 3) }, 1, 4, 0);

        private static Simulation WithBoxTarget(GameplayTargetSpec spec, bool box)
        {
            Simulation sim = M0c2S3Fixture.CreateWith(M0c2S3Fixture.Schema(abilities: new[] { Ability(spec) }));
            if (box)
            {
                Assert.That(sim.Entities.TryResolve(M0c2S3Fixture.Target.Id, out EntityRef target), Is.True);
                sim.Entities.Set(sim.Entities.Resolve<Collider>(), (EntityId)target,
                    Collider.Box(SimFix.Ratio(1, 2), SimFix.FromInt(1), SimFix.Ratio(1, 4), BoxYaw.FromGenerator(2, 1), M0c2S3Fixture.UnitLayer));
            }
            return sim;
        }

        [Test]
        public void ExplicitAndOcclusionCheckedAbilityTargetsRejectBoxes()
        {
            var explicitSpec = new GameplayTargetSpec(GameplayTargetMode.Explicit, rangeRaw: 12 * M0c2S3Fixture.S, layerMask: M0c2S3Fixture.UnitLayer);
            var command = M0c2S3Fixture.Command(1, mode: GameplayTargetMode.Explicit, target: new GameplayStamp(M0c2S3Fixture.Target.Id));
            Simulation control = WithBoxTarget(explicitSpec, false);
            M0c2S3Fixture.Input(control, command);
            Assert.That(control.GameplayInputResultAt(0).Failure, Is.EqualTo(GameplayAbilityFailure.None));
            Assert.That(control.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(7983));
            Simulation boxed = WithBoxTarget(explicitSpec, true);
            M0c2S3Fixture.Input(boxed, command);
            Assert.That(boxed.GameplayInputResultAt(0).Failure, Is.EqualTo(GameplayAbilityFailure.InvalidTarget));
            Assert.That(boxed.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000));

            // An occlusion mask with no members: capsules pass the check, the box target is skipped.
            const uint emptyLayer = 0x40000000u;
            var area = new GameplayTargetSpec(GameplayTargetMode.Query, GameplayTargetShape.Sphere, GameplayTargetSelection.AllCapped,
                rangeRaw: 12 * M0c2S3Fixture.S, layerMask: M0c2S3Fixture.UnitLayer, maximumTargets: 4, radiusRaw: 5 * M0c2S3Fixture.S,
                useTargetPoint: true, excludeSelf: true, relation: GameplayRelation.OtherFaction, occlusionMask: emptyLayer);
            var sphere = new GameplayInputCommand(new GameplayStamp(M0c2S3Fixture.Actor.Id), 1, GameplayInputKind.Activate, 1, GameplayTargetMode.Query,
                aimDirection: SimVec3.Metres(1, 0, 0), targetPoint: SimVec3.Metres(6, 0, 0));
            Simulation areaControl = WithBoxTarget(area, false);
            M0c2S3Fixture.Input(areaControl, sphere);
            Assert.That(areaControl.GameplayInputResultAt(0).TotalHits, Is.EqualTo(3));
            Simulation areaBoxed = WithBoxTarget(area, true);
            M0c2S3Fixture.Input(areaBoxed, sphere);
            Assert.That(areaBoxed.GameplayInputResultAt(0).TotalHits, Is.EqualTo(2));
            Assert.That(areaBoxed.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(8000));
            Assert.That(areaBoxed.GameplayResource(M0c2S3Fixture.Target2.Id, 1), Is.EqualTo(7983));
            // Without an occlusion check a box is an ordinary area target.
            var open = new GameplayTargetSpec(GameplayTargetMode.Query, GameplayTargetShape.Sphere, GameplayTargetSelection.AllCapped,
                rangeRaw: 12 * M0c2S3Fixture.S, layerMask: M0c2S3Fixture.UnitLayer, maximumTargets: 4, radiusRaw: 5 * M0c2S3Fixture.S,
                useTargetPoint: true, excludeSelf: true, relation: GameplayRelation.OtherFaction);
            Simulation openBoxed = WithBoxTarget(open, true);
            M0c2S3Fixture.Input(openBoxed, sphere);
            Assert.That(openBoxed.GameplayInputResultAt(0).TotalHits, Is.EqualTo(3));
            Assert.That(openBoxed.GameplayResource(M0c2S3Fixture.Target.Id, 1), Is.EqualTo(7983));
        }

        [Test]
        public void SpatialBodyColumnsByteAccountMatchesTheLockedLayout()
        {
            int Size<T>() where T : struct => MemoryMarshal.AsBytes(new T[1].AsSpan()).Length;
            int slot = Size<EntityId>() + Size<SimVec3>() + Size<Collider>() + Size<SpatialBody>();
            Assert.That((Size<Collider>(), Size<SpatialBody>()), Is.EqualTo((32, 72)));
            Assert.That(new SpatialBodyColumns(10).AllocatedBytes, Is.EqualTo(10L * slot), "Per-slot bytes: EntityId + SimVec3 + Collider + SpatialBody.");
        }

        // Ruling 7 (docs/m0c/rulings-2026-10-26.md section 2) freezes these definitions until its section 2.2 thaw criteria are
        // met. The lock stays after the thaw: a change here then ships IL2CPP arm64 Release SimBench Light and semantic
        // ScatteredRegular before/after numbers in the same delivery.
        // Scope: the readonly struct modifier and the layout metadata (layout kind, pack, size); instance fields (readonly,
        // type, name, declaration order); no static constructor and the BeforeFieldInit flag; no nested types; every instance
        // constructor's visibility and parameters (in, ref and out told apart); every method declared on Collider or BoxYaw,
        // property getters included, whose return type or any parameter type mentions Collider or BoxYaw (by value, by ref, as
        // an array or Span element, or as a generic argument such as Nullable, a tuple or Action), with its visibility,
        // static-ness and parameters; no property setters (set or init); no static fields other than constants (a static
        // auto-property adds one); no methods with compiler-generated names starting with '<' (local functions); ShapeKind's
        // underlying type and members. Types are compared by namespace-qualified name, generic arguments expanded. Outside the lock:
        // method bodies (but lambdas, iterators and local functions in them add compiler-generated nested types or methods,
        // which the nested-type and compiler-generated-method pins catch), methods whose signature hides the two types (object,
        // an interface, a non-generic delegate, a type that contains a Collider such as SpatialBody, a method type parameter),
        // and producers declared on other types.
        [Test]
        public void ColliderBoxYawAndShapeKindDefinitionsAreFrozen()
        {
            const string Ruling = "Frozen by ruling 7 (docs/m0c/rulings-2026-10-26.md section 2): until the section 2.2 thaw a change needs a new ruling; after it a change ships IL2CPP arm64 Release SimBench Light and semantic ScatteredRegular before/after numbers in the same delivery.";
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            const BindingFlags Declared = Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            // Flags enums are not formatted with ToString: Mono prefixes the zero-valued names (AutoLayout, ReuseSlot).
            string Access(MethodBase m) => m.IsPublic ? "Public" : m.IsPrivate ? "Private" : m.IsAssembly ? "Assembly" : m.IsFamily ? "Family"
                : m.IsFamilyOrAssembly ? "FamORAssem" : m.IsFamilyAndAssembly ? "FamANDAssem" : "PrivateScope";
            bool CompilerReadOnly(ICustomAttributeProvider p) => p.GetCustomAttributes(false).Any(a => a.GetType().FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
            // IsIn is also set by [In] ref; only the compiler-emitted IsReadOnlyAttribute marks an in parameter.
            string Name(Type t) => t.IsByRef ? Name(t.GetElementType()) + "&" : t.IsPointer ? Name(t.GetElementType()) + "*"
                : t.IsArray ? Name(t.GetElementType()) + "[" + new string(',', t.GetArrayRank() - 1) + "]"
                : t.IsGenericParameter ? t.Name
                : t.IsGenericType ? t.GetGenericTypeDefinition().FullName + "[" + string.Join(", ", t.GetGenericArguments().Select(Name)) + "]" : t.FullName;
            string Parameters(MethodBase m) => string.Join(", ", m.GetParameters().Select(p => (p.IsOut ? "out " : !p.ParameterType.IsByRef ? "" : CompilerReadOnly(p) ? "in " : "ref ")
                + Name(p.ParameterType)));
            bool Mentions(Type t) => t == typeof(Collider) || t == typeof(BoxYaw) || (t.HasElementType && Mentions(t.GetElementType()))
                || (t.IsGenericType && t.GetGenericArguments().Any(Mentions));
            string Shape(Type type) => (CompilerReadOnly(type) ? "readonly" : "mutable")
                // An unset pack reads as 0 on CoreCLR and as 8 on Mono; both mean the default packing.
                + ", " + (type.IsExplicitLayout ? "ExplicitLayout" : type.IsLayoutSequential ? "SequentialLayout" : "AutoLayout") + ", " + (type.StructLayoutAttribute.Pack == 0 || type.StructLayoutAttribute.Pack == 8 ? "default pack" : "Pack=" + type.StructLayoutAttribute.Pack)
                + ", Size=" + type.StructLayoutAttribute.Size
                + ", " + (type.TypeInitializer == null ? "no static constructor" : "static constructor") + ", BeforeFieldInit=" + ((type.Attributes & TypeAttributes.BeforeFieldInit) != 0)
                + ", nested types: " + string.Join(" ", type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Select(t => t.Name).DefaultIfEmpty("none"))
                + ", setters: " + string.Join(" ", type.GetProperties(Declared).Where(p => p.SetMethod != null).Select(p => p.Name).DefaultIfEmpty("none"))
                + ", static fields: " + string.Join(" ", type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsLiteral).Select(f => f.Name).DefaultIfEmpty("none"))
                + ", compiler-generated methods: " + string.Join(" ", type.GetMethods(Declared).Where(m => m.Name.StartsWith("<")).Select(m => m.Name).DefaultIfEmpty("none"));
            string Fields(Type type) => string.Join(", ", type.GetFields(Instance).OrderBy(f => f.MetadataToken)
                .Select(f => (f.IsInitOnly ? "readonly " : "") + Name(f.FieldType) + " " + f.Name));
            string Constructors(Type type) => string.Join("; ", type.GetConstructors(Instance).OrderBy(c => c.MetadataToken)
                .Select(c => Access(c) + " (" + Parameters(c) + ")"));
            string Methods(Type type) => string.Join("; ", type.GetMethods(Declared)
                .Where(m => Mentions(m.ReturnType) || m.GetParameters().Any(p => Mentions(p.ParameterType))).OrderBy(m => m.MetadataToken)
                .Select(m => Access(m) + (m.IsStatic ? " static " : " ") + Name(m.ReturnType) + " " + m.Name + "(" + Parameters(m) + ")"));
            Assert.That(Shape(typeof(Collider)), Is.EqualTo("readonly, SequentialLayout, default pack, Size=0, no static constructor, BeforeFieldInit=True, nested types: none, "
                + "setters: none, static fields: none, compiler-generated methods: none"), Ruling);
            Assert.That(Fields(typeof(Collider)), Is.EqualTo("readonly System.UInt32 layerXorDefault, readonly System.Int32 halfDepthRaw, readonly WarCore.Sim.BoxYaw yaw, "
                + "readonly WarCore.Sim.ShapeKind shapeKind, readonly System.Boolean isStatic, readonly WarCore.Sim.SimFix radius, readonly WarCore.Sim.SimFix height"), Ruling);
            Assert.That(Constructors(typeof(Collider)), Is.EqualTo("Public (WarCore.Sim.SimFix, WarCore.Sim.SimFix, WarCore.Sim.ShapeKind); "
                + "Public (WarCore.Sim.SimFix, WarCore.Sim.SimFix, System.UInt32, WarCore.Sim.ShapeKind, System.Boolean); "
                + "Private (WarCore.Sim.SimFix, WarCore.Sim.SimFix, WarCore.Sim.SimFix, WarCore.Sim.BoxYaw, System.UInt32, System.Boolean); "
                + "Private (in WarCore.Sim.Collider&, System.UInt32, System.Boolean, WarCore.Sim.SimFix)"), Ruling);
            Assert.That(Methods(typeof(Collider)), Is.EqualTo("Public WarCore.Sim.BoxYaw get_Yaw(); "
                + "Public static WarCore.Sim.Collider Box(WarCore.Sim.SimFix, WarCore.Sim.SimFix, WarCore.Sim.SimFix, WarCore.Sim.BoxYaw, System.UInt32, System.Boolean); "
                + "Public WarCore.Sim.Collider WithLayer(System.UInt32); Public WarCore.Sim.Collider WithStatic(System.Boolean); Public WarCore.Sim.Collider WithHeight(WarCore.Sim.SimFix)"), Ruling);
            Assert.That(Shape(typeof(BoxYaw)), Is.EqualTo("readonly, SequentialLayout, default pack, Size=0, no static constructor, BeforeFieldInit=True, nested types: none, "
                + "setters: none, static fields: none, compiler-generated methods: none"), Ruling);
            Assert.That(Fields(typeof(BoxYaw)), Is.EqualTo("readonly System.Int16 mMinusOne, readonly System.Int16 n"), Ruling);
            Assert.That(Constructors(typeof(BoxYaw)), Is.EqualTo("Private (System.Int32, System.Int32)"), Ruling);
            Assert.That(Methods(typeof(BoxYaw)), Is.EqualTo("Public static WarCore.Sim.BoxYaw get_Identity(); Public static WarCore.Sim.BoxYaw FromGenerator(System.Int32, System.Int32); "
                + "Public static WarCore.Sim.BoxYaw QuarterTurns(System.Int32); Public System.Boolean Equals(WarCore.Sim.BoxYaw)"), Ruling);
            Assert.That(Enum.GetUnderlyingType(typeof(ShapeKind)), Is.EqualTo(typeof(byte)), Ruling);
            Assert.That(string.Join(", ", Enum.GetValues(typeof(ShapeKind)).Cast<ShapeKind>().Select(k => k + "=" + (byte)k)), Is.EqualTo("VerticalCapsule=0, OrientedBox=1"), Ruling);
        }

        // Every shape-sensitive read is locked per file; a new site must be reviewed and added here. Matching is by receiver
        // name; the headless ShapeReadSymbolTests resolves the same reads by symbol and requires the counts to agree.
        private static readonly Regex ShapeRead = new Regex(@"(\bCollider|\bcollider|\bbaseline|\bvalue)\.(Radius|Height|ShapeKind)\b|\.With(Layer|Static|Height)\(");
        internal static readonly string[] Roots = { "Assets/WarCore/Sim", "Assets/WarCore/Tests/EditMode/Oracle", "Assets/WarCore/Tests/EditMode/Diagnostics" };
        internal static readonly string[] Excluded = { "SnapshotValues.cs", "SimEvents.cs" };

        // Every file that reads Radius/Height/ShapeKind or rebuilds a collider. A new read must be reviewed for boxes here.
        private static readonly Dictionary<string, int> Locked = new Dictionary<string, int>
        {
            { "Diagnostics/ProductGeometrySteps.cs", 1 },
            { "Diagnostics/ProfiledGridSpatialQuery.cs", 12 },
            { "Diagnostics/ProfiledSpatialGeometry.cs", 7 },
            { "Diagnostics/ProfiledSpatialRay.cs", 9 },
            { "Diagnostics/QueryDiagnosticTests.cs", 2 },
            { "Diagnostics/SemanticBenchArtifacts.cs", 7 },
            { "Diagnostics/SemanticBenchScene.cs", 2 },
            { "Diagnostics/SemanticPartitionWork.cs", 1 },
            { "Oracle/ExactRayCapsule.cs", 4 },
            { "Oracle/OracleBox.cs", 2 },
            { "Oracle/OracleSpatialGeometry.cs", 3 },
            { "Oracle/QueryModeOracle.cs", 3 },
            { "Sim/CoreComponentSerializers.cs", 4 },
            { "Sim/GameplayWorld.cs", 1 },
            { "Sim/GridSpatialIndex.Modes.cs", 4 },
            { "Sim/GridSpatialIndex.cs", 8 },
            { "Sim/Movement.cs", 1 },
            { "Sim/PostureGeometry.cs", 4 },
            { "Sim/SpatialBounds.cs", 4 },
            { "Sim/SpatialBox.cs", 12 },
            { "Sim/SpatialGeometry.cs", 2 },
            { "Sim/SpatialGeometrySlow.cs", 7 },
            { "Sim/SpatialGridState.cs", 1 },
            { "Sim/SpatialModeMath.cs", 3 },
            { "Sim/SpatialPartitionRequests.cs", 1 },
            { "Sim/SpatialRay.cs", 9 },
            { "Sim/SpatialSector.cs", 2 },
        };

        internal static int Count(string source) => ShapeRead.Matches(SourceRules.StripComments(source)).Count;

        [Test]
        public void ShapeSensitiveReadsAreLockedPerFile()
        {
            var actual = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (string root in Roots)
                foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.TopDirectoryOnly))
                {
                    string name = Path.GetFileName(path);
                    if (Excluded.Contains(name))
                        continue;
                    int count = Count(File.ReadAllText(path));
                    if (count != 0)
                        actual[root.Substring(root.LastIndexOf('/') + 1) + "/" + name] = count;
                }
            string report = string.Join(", ", actual.Select(p => "{ \"" + p.Key + "\", " + p.Value + " }"));
            Assert.That(actual.Count, Is.EqualTo(Locked.Count), "Shape-read files changed: " + report);
            foreach (KeyValuePair<string, int> pair in actual)
                Assert.That(Locked.TryGetValue(pair.Key, out int expected) && expected == pair.Value, Is.True, "Unreviewed shape read in " + pair.Key + ": " + report);
            // Positive controls: a new read is counted, a comment is not.
            string sample = File.ReadAllText("Assets/WarCore/Sim/Movement.cs");
            Assert.That(Count(sample + "\nclass X { long r(Collider collider) => collider.Radius.Raw; }"), Is.EqualTo(Count(sample) + 1));
            Assert.That(Count(sample + "\n// collider.Radius and value.WithLayer(1)"), Is.EqualTo(Count(sample)));
            Assert.That(Count("x.WithStatic(true); baseline.Height; value.ShapeKind"), Is.EqualTo(3));
            TestContext.WriteLine("Shape-read guard: {0} files, {1} sites locked.", actual.Count, actual.Values.Sum());
        }
    }
}
