using System;
using System.Collections.Generic;
using WarCore.Sim;
using WarCore.Tests.Diagnostics;
using WarCore.Tests.Oracle;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    internal sealed class Round4ChurnScene : ISimSystem, IStableStateSerializer
    {
        internal const int ChurnPerTick = SemanticBenchScene.BodyCount/20;
        internal readonly SemanticBenchScene Template;
        internal readonly Simulation Sim;
        internal readonly GridSpatialQuery Grid;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Collider> colliders;
        private readonly EntityId[] entities = new EntityId[SemanticBenchScene.BodyCount];
        private readonly SpatialHit[] hits = new SpatialHit[512];
        private readonly EntityId[] affected = new EntityId[512];
        private int cursor;
        internal readonly ReferenceChurnFixture References;
        internal int LastAffected;
        internal int LastRays;
        internal int LastQueries;
        internal long Created;
        internal ulong TraversalChecksum;
        public int Order => 1;
        internal Round4ChurnScene(bool dense, bool references = false, bool naive = false, bool placed = false, GameplaySchema gameplay = null)
        {
            Template = new SemanticBenchScene(dense,true);
            var config = new SimConfig(entityCapacity:SemanticBenchScene.BodyCount+ChurnPerTick,timerCapacity:ChurnPerTick,
                    eventRetainedTicks:4,eventInitialCapacity:references ? 4096 : 16,eventHardLimit:references ? 4096 : 16);
            if (placed)
            {
                var catalogEntries = new PlacedCatalogEntry[entities.Length];
                for (int i = 0; i < entities.Length; i++)
                    catalogEntries[i] = new PlacedCatalogEntry(1,(ulong)i,1);
                Sim = Simulation.CreateRetainedWorld(RealBenchWorkload.Seed,new PlacedCatalog(new StableContentManifest(0),catalogEntries),config,gameplaySchema:gameplay);
            }
            else
                Sim = Simulation.CreateRetainedWorld(RealBenchWorkload.Seed,config);
            positions = Sim.Entities.Register<Position>("position",new PositionSerializer());
            colliders = Sim.Entities.Register<Collider>("collider",new ColliderSerializer());
            for (int i = 0; i < entities.Length; i++)
                Create(i,StableEntityKey.Placed(1,(ulong)i));
            var view = (SpatialQueryView)Sim.ConfigureSpatial(positions,colliders,SimFix.FromInt(3),SemanticBenchScene.BodyCount*32);
            Grid = new GridSpatialQuery(Sim.SpatialIndex,view.QueryContext);
            Sim.CompleteSpatialBoundary();
            if (references)
            {
                References = new ReferenceChurnFixture(Sim.Entities,entities,naive);
                References.Prepare(cursor);
                References.VerifyPrepared();
                Sim.Register("reference-setup",new ReferenceSetup(this),SimSystemPhase.Requests);
            }
            Sim.Register("churn",this,SimSystemPhase.Requests);
            for (int i = 0; i < 4; i++)
                Sim.Register("consumer-"+i,new Consumer(this,i+2),SimSystemPhase.Movement);
            Sim.Register("queries",new Queries(this),SimSystemPhase.Queries);
        }
        public string StateTypeId => "bench.churn";
        public int StateVersion => 1;
        public void SaveState(SimStateWriter writer)
        {
            writer.Bool(Template.Dense);
            writer.Int32(cursor); writer.Int64(Created); writer.UInt64(TraversalChecksum);
            writer.Int32(LastAffected); writer.Int32(LastRays); writer.Int32(LastQueries);
            // Logical order is part of the workload; physical allocation order is not.
            writer.Int32(entities.Length);
            for (int i = 0; i < entities.Length; i++) writer.Link(Sim.Entities.Link(entities[i]));
            writer.Bool(References != null);
            References?.SaveState(writer);
            // VerifyOracle can inspect the retained explosion set at a boundary.
            for (int i = 0; i < LastAffected; i++) writer.Link(Sim.Entities.Link(affected[i]));
            // hits alone is scratch, overwritten before every read.
        }
        public void LoadState(SimStateReader reader)
        {
            SimStateReader.Require(reader.Bool() == Template.Dense,"Churn layout mismatch.");
            cursor = reader.Int32(); Created = reader.Int64(); TraversalChecksum = reader.UInt64();
            LastAffected = reader.Int32(); LastRays = reader.Int32(); LastQueries = reader.Int32();
            SimStateReader.Require(cursor >= 0 && cursor < entities.Length && Created >= 0
                && reader.Int32() == entities.Length,"Invalid churn state.");
            for (int i = 0; i < entities.Length; i++)
            {
                EntityLink link = reader.Link();
                entities[i] = Sim.Entities.Resolve(link);
            }
            SimStateReader.Require(reader.Bool() == (References != null),"Churn references mismatch.");
            References?.LoadState(reader);
            SimStateReader.Require(LastAffected >= 0 && LastAffected <= affected.Length,"Invalid retained explosion count.");
            Array.Clear(hits,0,hits.Length); Array.Clear(affected,0,affected.Length);
            for (int i = 0; i < LastAffected; i++) affected[i] = Sim.Entities.Resolve(reader.Link());
        }
        // These systems own only immutable dispatch configuration. All mutable shared state
        // is serialized exactly once by bench.churn above. EntityStage is a borrowed view.
        private sealed class ReferenceSetup : ISimSystem, IStableStateSerializer
        {
            private readonly Round4ChurnScene scene;
            public int Order => 0;
            internal ReferenceSetup(Round4ChurnScene scene) => this.scene = scene;
            public void Step(SimContext context) => scene.References.Prepare(scene.cursor);
            public string StateTypeId => "bench.churn.ReferenceSetup";
            public int StateVersion => 1;
            public void SaveState(SimStateWriter writer) => writer.Int32(Order);
            public void LoadState(SimStateReader reader) => SimStateReader.Require(reader.Int32() == Order,"Churn dispatch mismatch.");
            public void WriteState(StateHash hash) { }
        }
        internal ulong ReferenceStateHash()
        {
            var hash = new StateHash();
            var writer = new SimStateWriter(Sim.Entities,hash:hash);
            Sim.Entities.WriteEntitySet(writer);
            return hash.Value;
        }
        private void Create(int logical, StableEntityKey key)
        {
            EntityId entity = Sim.Entities.Create(key);
            entities[logical] = entity;
            Sim.Entities.Set(positions,entity,new Position(Template.Bodies[logical].Position));
            Sim.Entities.Set(colliders,entity,Template.Bodies[logical].Collider);
        }
        public void Step(SimContext context)
        {
            for (int i = 0; i < ChurnPerTick; i++)
            {
                int logical = (cursor+i)%SemanticBenchScene.BodyCount;
                EntityId entity = entities[logical];
                TaskKey key = Sim.Scheduler.ScheduleFor(context.Clock.Tick+1,0,Sim.Entities.StableOf(entity),1,new StableSimEvent(new EventTypeId(100)));
                Round4LongRun.Require(Sim.Scheduler.CancelStable(key),"Churn task cancellation failed.");
                Sim.Entities.Destroy(entity);
                Create(logical,StableEntityKey.World(100,context.Clock.Tick,(ulong)i));
                Created++;
            }
            cursor = (cursor+ChurnPerTick)%SemanticBenchScene.BodyCount;
        }
        public void WriteState(StateHash hash) => hash.Add(Created);
        private sealed class Consumer : ISimSystem, IStableStateSerializer
        {
            private readonly Round4ChurnScene scene;
            private readonly EntityStage stage;
            public int Order { get; }
            internal Consumer(Round4ChurnScene scene, int order)
            {
                this.scene = scene;
                stage = new EntityStage(scene.Sim.Entities);
                Order = order;
            }
            public void Step(SimContext context)
            {
                int count = 0;
                foreach (EntityId entity in stage.Capture())
                {
                    scene.TraversalChecksum = unchecked(scene.TraversalChecksum+context.Entities.StableOf(entity).Value);
                    count++;
                }
                Round4LongRun.Require(count == SemanticBenchScene.BodyCount,"Shared stage lost live bodies.");
            }
            public string StateTypeId => "bench.churn.Consumer";
            public int StateVersion => 1;
            public void SaveState(SimStateWriter writer) => writer.Int32(Order);
            public void LoadState(SimStateReader reader) => SimStateReader.Require(reader.Int32() == Order,"Churn dispatch mismatch.");
            public void WriteState(StateHash hash) { }
        }
        private sealed class Queries : ISimSystem, IStableStateSerializer
        {
            private readonly Round4ChurnScene scene;
            public int Order => 9;
            internal Queries(Round4ChurnScene scene) => this.scene = scene;
            public void Step(SimContext context) => scene.Query();
            public string StateTypeId => "bench.churn.Queries";
            public int StateVersion => 1;
            public void SaveState(SimStateWriter writer) => writer.Int32(Order);
            public void LoadState(SimStateReader reader) => SimStateReader.Require(reader.Int32() == Order,"Churn dispatch mismatch.");
            public void WriteState(StateHash hash) { }
        }
        private SemanticQuery Cover(EntityId target)
        {
            SimVec3 delta = Sim.Entities.Read(positions,target).Value-Template.ExplosionOrigin;
            SimFix range = SimMath.Sqrt(delta.X*delta.X+delta.Y*delta.Y+delta.Z*delta.Z);
            return new SemanticQuery(QueryRole.Cover,SpatialQueries.Ray(Template.ExplosionOrigin,delta,range,Template.Content.SightMask),SemanticMode.Any);
        }
        private void Query()
        {
            LastAffected = 0;
            LastRays = 0;
            LastQueries = 0;
            int covers = 0;
            for (int i = 0; i < Template.Queries.Length; i++)
            {
                SemanticQuery query = Template.Queries[i].Role == QueryRole.Cover ? Cover(affected[covers++]) : Template.Queries[i];
                SemanticResult result = query.Run(Grid,hits);
                if (query.Role != QueryRole.Cover)
                {
                    SemanticResult expected = Template.Expected[i].Result;
                    Round4LongRun.Require(result.Hit == expected.Hit && result.TotalHits == expected.TotalHits && result.Written == expected.Written,
                        "Churn changed query geometry or work.");
                }
                if (query.Role == QueryRole.Explosion)
                {
                    LastAffected = result.Written;
                    for (int j = 0; j < result.Written; j++)
                        affected[j] = hits[j].Entity;
                }
                if (query.Request.Kind == SpatialQueryKind.Ray)
                    LastRays++;
                LastQueries++;
            }
            Round4LongRun.Require(LastAffected == Template.Affected && covers == LastAffected
                && LastRays == 832+LastAffected && Sim.Entities.ActiveEntityCount == SemanticBenchScene.BodyCount,
                "Continuous peak work disappeared during churn.");
        }
        internal void VerifyOracle(int ordinal)
        {
            SemanticQuery query = Template.Queries[ordinal%Template.Queries.Length];
            if (query.Role == QueryRole.Cover)
                query = Cover(affected[ordinal%LastAffected]);
            var expected = new List<SpatialHit>();
            for (int i = 0; i < entities.Length; i++)
            {
                SpatialBody body = new SpatialBody(entities[i],Template.Bodies[i].Position,Template.Bodies[i].Collider);
                if ((body.Collider.Layer & query.Request.LayerMask) == 0
                    || !OracleSpatialGeometry.Intersects(body,query.Request,out SimFix distance))
                    continue;
                if (query.Request.Kind != SpatialQueryKind.Ray)
                    distance = SimFix.FromRaw(QueryModeOracle.Distance(body.Position,body.Collider.Radius.Raw,body.Collider.Height.Raw,query.Request.Origin));
                expected.Add(new SpatialHit(body.Entity,distance));
            }
            expected.Sort((a,b) => a.Distance.Raw != b.Distance.Raw ? a.Distance.Raw.CompareTo(b.Distance.Raw)
                : Sim.Entities.StableOf(a.Entity).CompareTo(Sim.Entities.StableOf(b.Entity)));
            SemanticResult result = query.Run(Grid,hits);
            Round4LongRun.Require(result.Hit == (expected.Count != 0),"Churn oracle hit mismatch.");
            int count = query.Mode == SemanticMode.Any ? 0 : query.Mode == SemanticMode.Nearest ? Math.Min(1,expected.Count) : Math.Min(query.Cap,expected.Count);
            Round4LongRun.Require(result.Written == count,"Churn oracle count mismatch.");
            if (query.Mode == SemanticMode.AllCapped)
                Round4LongRun.Require(result.TotalHits == expected.Count,"Churn oracle total mismatch.");
            for (int i = 0; i < count; i++)
                Round4LongRun.Require(hits[i].Entity.Equals(expected[i].Entity) && hits[i].Distance.Equals(expected[i].Distance),"Churn oracle selection mismatch.");
        }
    }
}
