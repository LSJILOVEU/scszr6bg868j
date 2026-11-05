using System;

namespace WarCore.Sim
{
    public sealed partial class Sim
    {
        private struct StableMotionCommand : IComparable<StableMotionCommand>, IEquatable<StableMotionCommand>
        {
            internal EntityLink Target;
            internal StableRequestKey Key;
            internal int Kind;
            internal SimVec3 Position;
            internal EntityLink Host;
            internal bool Flag;
            public int CompareTo(StableMotionCommand other) => CompareMotion(this,other);
            public bool Equals(StableMotionCommand other) => CompareTo(other) == 0;
            public override int GetHashCode() => SimInteger.Int32Bits(StableEntityKey.Mix(Target.Id.Value)
                ^ unchecked((uint)Key.GetHashCode()) ^ (ulong)Kind);
        }
        private StableMotionCommand[] stableMotion;
        private int stableMotionCount;
        private BoundedLookup<StableMotionCommand> motionKeys;
        private EntityStage positionStage;
        public void RequestPosition(EntityLink target, SimVec3 position, StableRequestKey key)
        {
            RequireSpatialEntity(Entities.Resolve(target));
            position.ValidateWorld();
            AddStableMotion(new StableMotionCommand { Target=target, Position=position, Key=key, Kind=0 });
        }
        public void Attach(EntityLink passenger, EntityLink host, SimVec3 offset, StableRequestKey key)
        {
            RequireSpatialEntity(Entities.Resolve(passenger));
            RequireSpatialEntity(Entities.Resolve(host));
            offset.ValidateWorld();
            AddStableMotion(new StableMotionCommand { Target=passenger, Host=host, Position=offset, Key=key, Kind=1, Flag=true });
        }
        public void Detach(EntityLink passenger, StableRequestKey key)
        {
            RequireSpatialEntity(Entities.Resolve(passenger));
            AddStableMotion(new StableMotionCommand { Target=passenger, Key=key, Kind=1 });
        }
        public void RequestStableColliderStatic(EntityLink target, bool isStatic, StableRequestKey key)
        {
            EntityId entity = Entities.Resolve(target);
            RequireSpatialEntity(entity);
            if (!Entities.TryGet(spatialColliders,entity,out _))
                throw new SimBoundaryException("Static changes require a live Collider.");
            AddStableMotion(new StableMotionCommand { Target=target, Flag=isStatic, Key=key, Kind=2 });
        }
        private void AddStableMotion(StableMotionCommand command)
        {
            Entities.RequireStableWorld();
            if (stableMotionCount == stableMotion.Length)
                throw new SimBoundaryException("Stable motion request capacity exhausted.");
            if (motionKeys.Find(command) >= 0)
                throw new SimBoundaryException("Duplicate stable motion request key.");
            motionKeys.Add(command,stableMotionCount);
            stableMotion[stableMotionCount++] = command;
        }
        private static int CompareMotion(StableMotionCommand a, StableMotionCommand b)
        {
            int c = a.Target.Id.CompareTo(b.Target.Id);
            if (c != 0)
                return c;
            c = a.Kind.CompareTo(b.Kind);
            return c != 0 ? c : a.Key.CompareTo(b.Key);
        }
        private void SortStableMotion() => ValueOrder.Sort(stableMotion.AsSpan(0,stableMotionCount));
        private bool LiveMotion(StableMotionCommand item)
        {
            if (!Entities.TryResolve(item.Target.Id,out _))
            {
                StableRequestKind kind = item.Kind == 0 ? StableRequestKind.Position : item.Kind == 2
                    ? StableRequestKind.ColliderStatic : item.Flag ? StableRequestKind.AttachTarget : StableRequestKind.Detach;
                RequestDiagnostics.Record(kind,item.Target);
                return false;
            }
            if (item.Kind == 1 && item.Flag && !Entities.TryResolve(item.Host.Id,out _))
            {
                RequestDiagnostics.Record(StableRequestKind.AttachHost,item.Target,item.Host);
                return false;
            }
            return true;
        }
        private void CommitStableMotion()
        {
            SortStableMotion();
            int retained = 0;
            for (int i = 0; i < stableMotionCount; i++)
                if (LiveMotion(stableMotion[i]))
                    stableMotion[retained++] = stableMotion[i];
            stableMotionCount = retained;
            for (int i = 0; i < stableMotionCount; i++)
            {
                StableMotionCommand item = stableMotion[i];
                if (i+1 < stableMotionCount && item.Target.Equals(stableMotion[i+1].Target) && item.Kind == stableMotion[i+1].Kind)
                    continue;
                EntityId target = Entities.Resolve(item.Target);
                if (item.Kind == 0)
                    Entities.Set(positionRequests,target,new SpatialPositionRequest(item.Position));
                else if (item.Kind == 1)
                {
                    if (item.Flag)
                        Entities.Set(attachments,target,new SpatialAttachment(Entities.Resolve(item.Host),item.Position,item.Host));
                    else
                        Entities.Remove(attachments,target);
                }
                else
                    RequestColliderStaticCore(target,item.Flag);
            }
            stableMotionCount = 0;
            motionKeys.Clear();
        }
        private void SaveStableMotion(SimStateWriter writer)
        {
            SortStableMotion();
            writer.Int32(stableMotionCount);
            for (int i = 0; i < stableMotionCount; i++)
            {
                StableMotionCommand item = stableMotion[i];
                writer.Link(item.Target);
                item.Key.Save(writer);
                writer.Int32(item.Kind);
                SnapshotValues.Vector(writer,item.Position);
                writer.Link(item.Host);
                writer.Bool(item.Flag);
            }
        }
        private void LoadStableMotion(SimStateReader reader)
        {
            int count = reader.Count(stableMotion.Length);
            stableMotionCount = 0;
            motionKeys.Clear();
            for (int i = 0; i < count; i++)
            {
                var item = new StableMotionCommand { Target=reader.Link(), Key=StableRequestKey.Read(reader), Kind=reader.Int32(),
                    Position=SnapshotValues.Vector(reader), Host=reader.Link(), Flag=reader.Bool() };
                SimStateReader.Require(item.Target.HasValue && item.Kind >= 0 && item.Kind <= 2
                    && (i == 0 || CompareMotion(stableMotion[i-1],item) < 0),"Invalid stable motion request.");
                item.Position.ValidateWorld();
                if (!Entities.UsesRetainedProtocol)
                {
                    Entities.ResolveLogical(item.Target);
                    if (item.Kind == 1 && item.Flag)
                        Entities.ResolveLogical(item.Host);
                }
                AddStableMotion(item);
            }
        }
    }
}
