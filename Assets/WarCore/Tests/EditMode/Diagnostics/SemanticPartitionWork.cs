using System.IO;
using WarCore.Sim;

namespace WarCore.Tests.Diagnostics
{
    internal static class SemanticPartitionWork
    {
        internal static void Compare(SemanticBenchScene scene, TextWriter output)
        {
            var store = new EntityStore(SemanticBenchScene.BodyCount);
            var p = store.Register<Position>("position");
            var c = store.Register<Collider>("collider");
            foreach (SpatialBody body in scene.Bodies)
            {
                EntityId id = store.Create();
                store.Set(p, id, new Position(body.Position));
                // Collapsing the partition bit reconstructs the prior single-table membership.
                store.Set(c, id, body.Collider.WithStatic(false));
            }
            var index = new GridSpatialIndex(store, p, c, SimFix.FromInt(3), SemanticBenchScene.BodyCount,
                SemanticBenchScene.BodyCount * 32, readLiveLayers: false);
            index.Rebuild();
            var single = new GridSpatialQuery(index, new GridQueryContext(SemanticBenchScene.BodyCount));
            var before = new SpatialQueryDiagnostics();
            var after = new SpatialQueryDiagnostics();
            single.Diagnostics = before;
            scene.Grid.Diagnostics = after;
            var a = new SpatialHit[512];
            var b = new SpatialHit[512];
            long oldMembers = 0;
            long newMembers = 0;
            long dynamicMembers = 0;
            foreach (SemanticQuery query in scene.Queries)
            {
                SemanticResult oldResult = query.Run(single, a);
                SemanticResult newResult = query.Run(scene.Grid, b);
                SemanticBenchScene.Require(oldResult.Signature(a) == newResult.Signature(b), "Partition workload output changed.");
                for (int i = 0; i < oldResult.Written; i++)
                    SemanticBenchScene.Require(a[i].Entity.Equals(b[i].Entity) && a[i].Distance.Raw == b[i].Distance.Raw, "Partition ordered output changed.");
                oldMembers += before.LeafMembers;
                newMembers += after.LeafMembers;
                dynamicMembers += after.UnitMembers;
            }
            output.WriteLine("S1 frozen semantic {0}: single_table_members={1}; partition_members={2}; unit_members={3}; selected={4:X16}; full={5:X16}; all original masks/modes/outputs retained.",
                scene.Label, oldMembers, newMembers, dynamicMembers, scene.OutputSignature, scene.OracleSignature);
            long sightBefore = 0;
            long sightAfter = 0;
            long sightUnits = 0;
            int count = 0;
            uint staticMask = scene.Content.LayerBits[1] | scene.Content.LayerBits[2];
            foreach (SemanticQuery query in scene.Queries)
            {
                if (query.Role != QueryRole.AiRay)
                    continue;
                SpatialRequest r = query.Request;
                var sight = SpatialQueries.Ray(r.Origin, r.Direction, r.Range, staticMask);
                bool oldHit = single.Nearest(in sight, out SpatialHit oldBest);
                bool newHit = scene.Grid.Nearest(in sight, out SpatialHit newBest);
                SemanticBenchScene.Require(oldHit == newHit && oldBest.Entity.Equals(newBest.Entity)
                    && oldBest.Distance.Raw == newBest.Distance.Raw, "Static-only sight result changed.");
                var oracle = SemanticBenchOracle.Hits(scene, sight);
                SemanticBenchScene.Require(newHit == (oracle.Count != 0), "Static sight oracle presence changed.");
                if (newHit)
                    SemanticBenchScene.Require(newBest.Entity.Equals(oracle[0].Entity) && newBest.Distance.Raw == oracle[0].Distance.Raw, "Static sight oracle nearest changed.");
                sightBefore += before.LeafMembers;
                sightAfter += after.StaticMembers;
                sightUnits += after.UnitMembers;
                count++;
            }
            SemanticBenchScene.Require(count == 660 && sightBefore > 0 && sightAfter > 0 && sightUnits == 0, "Static partition skip must have nonempty controls.");
            output.WriteLine("S1 static-sight diagnostic {0}: seed=0xB30001; rays={1}; mask={2}; mode=Nearest; same legacy origins/directions/ranges; exclusions=none; single_table_members={3}; static_members={4}; unit_members={5}; oracle=itemwise; separate mask variant, frozen workload unchanged.",
                scene.Label, count, staticMask, sightBefore, sightAfter, sightUnits);
            scene.Grid.Diagnostics = null;
        }
    }
}
