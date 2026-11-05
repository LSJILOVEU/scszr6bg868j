using System;

namespace WarCore.Sim
{
    internal readonly struct SpatialPositionRequest : IStateHash
    {
        internal readonly SimVec3 Target;
        internal SpatialPositionRequest(SimVec3 target) => Target = target;
        public void WriteState(StateHash hash) => Target.WriteState(hash);
    }

    internal readonly struct SpatialAttachment : IStateHash
    {
        internal readonly EntityId Host;
        internal readonly SimVec3 Offset;
        internal readonly EntityLink HostLink;
        internal SpatialAttachment(EntityId host, SimVec3 offset, EntityLink hostLink = default)
        {
            Host = host;
            HostLink = hostLink;
            Offset = offset;
        }
        public void WriteState(StateHash hash)
        {
            Host.WriteState(hash);
            Offset.WriteState(hash);
        }
    }

    internal sealed class SpatialPositionRequestSerializer : IStableComponentSerializer<SpatialPositionRequest>
    {
        public int TypeId => 12;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SpatialPositionRequest value) => SnapshotValues.Vector(writer, value.Target);
        public SpatialPositionRequest Read(SimStateReader reader)
        {
            SimVec3 target = SnapshotValues.Vector(reader);
            target.ValidateWorld();
            return new SpatialPositionRequest(target);
        }
    }

    internal sealed class SpatialAttachmentSerializer : IStableComponentSerializer<SpatialAttachment>, IStableReferenceCleanup<SpatialAttachment>
    {
        public ReferenceCleanup ClearRetiredReference(ref SpatialAttachment value, EntityLink retired, out StableRequestKind kind)
        {
            kind = StableRequestKind.FollowHost;
            return value.HostLink.Equals(retired) ? ReferenceCleanup.Removed : ReferenceCleanup.None;
        }
        public int TypeId => 13;
        public int Version => 1;
        public void Write(SimStateWriter writer, in SpatialAttachment value)
        {
            if (writer.IsStable)
                writer.Link(value.HostLink);
            else
                SnapshotValues.Entity(writer, value.Host);
            SnapshotValues.Vector(writer, value.Offset);
        }
        public SpatialAttachment Read(SimStateReader reader)
        {
            EntityLink link = reader.IsStable ? reader.Link() : default;
            EntityId host = reader.IsStable ? reader.Entities.ResolveLogical(link) : SnapshotValues.Entity(reader);
            SimVec3 offset = SnapshotValues.Vector(reader);
            offset.ValidateWorld();
            return new SpatialAttachment(host, offset, link);
        }
    }

    public sealed partial class Sim
    {
        private ComponentType<SpatialPositionRequest> positionRequests;
        private ComponentType<SpatialAttachment> attachments;
        private EntityId[] attachmentStack;
        private byte[] attachmentState;

        private void ConfigureSpatialMotion()
        {
            positionStage = new EntityStage(Entities);
            // Commands and relationships are authority, unlike the derived grid and temporary outputs.
            positionRequests = Entities.Register<SpatialPositionRequest>("warcore.position-request", new SpatialPositionRequestSerializer());
            attachments = Entities.Register<SpatialAttachment>("warcore.spatial-attachment", new SpatialAttachmentSerializer());
            staticRequestOwners = new EntityId[Entities.Capacity];
            staticRequestFlags = new byte[Entities.Capacity];
            attachmentStack = new EntityId[Entities.Capacity];
            attachmentState = new byte[Entities.Capacity];
        }

        public void RequestPosition(EntityId entity, SimVec3 target)
        {
            RequireLegacyMotion();
            RequireSpatialEntity(entity);
            target.ValidateWorld();
            // A later request replaces an earlier one for the same entity; settlement is by slot.
            Entities.Set(positionRequests, entity, new SpatialPositionRequest(target));
        }

        public void Attach(EntityId passenger, EntityId host, SimVec3 offset)
        {
            RequireLegacyMotion();
            RequireSpatialEntity(passenger);
            RequireSpatialEntity(host);
            offset.ValidateWorld();
            // Relationship edits request following at the next attachment phase, never an immediate position write.
            Entities.Set(attachments, passenger, new SpatialAttachment(host, offset));
        }

        public void Detach(EntityId passenger)
        {
            RequireLegacyMotion();
            RequireSpatialEntity(passenger);
            Entities.Remove(attachments, passenger);
        }

        private void RequireLegacyMotion()
        {
            if (Entities.IdentityMode != IdentityMode.Legacy)
                throw new SimBoundaryException("StableWorld requires a stable motion request key.");
        }

        private void RequireSpatialEntity(EntityId entity)
        {
            if (spatialIndex == null || !Entities.TryGet(spatialPositions, entity, out _))
                throw new SimBoundaryException("Spatial motion requires a configured index and a live entity with Position.");
        }

        private void SettlePositionRequests()
        {
            if (spatialIndex == null)
                return;
            foreach (EntityId entity in positionStage.Capture())
            {
                if (!Entities.TryGet(positionRequests, entity, out SpatialPositionRequest request))
                    continue;
                Entities.Set(spatialPositions, entity, new Position(request.Target));
                Entities.Remove(positionRequests, entity);
            }
        }

        private EntityId AttachmentHost(in SpatialAttachment relation)
            => Entities.IdentityMode == IdentityMode.StableWorld ? Entities.Resolve(relation.HostLink) : relation.Host;

        private void FollowAttachments()
        {
            if (spatialIndex == null)
                return;
            Array.Clear(attachmentState, 0, attachmentState.Length);
            foreach (EntityId entity in Entities)
            {
                EntityId current = entity;
                int depth = 0;
                while (attachmentState[current.Id] != 2)
                {
                    if (attachmentState[current.Id] == 1)
                        throw new SimBoundaryException("Cyclic spatial attachment.");
                    attachmentState[current.Id] = 1;
                    attachmentStack[depth++] = current;
                    if (!Entities.TryGet(attachments, current, out SpatialAttachment relation))
                        break;
                    bool stable = Entities.IdentityMode == IdentityMode.StableWorld;
                    EntityRef resolved = default;
                    bool live = stable ? Entities.TryResolve(relation.HostLink.Id,out resolved) : Entities.IsAlive(relation.Host);
                    if (!live)
                    {
                        Entities.Remove(attachments,current);
                        if (stable)
                            RequestDiagnostics.Record(StableRequestKind.FollowHost,Entities.Link(current),relation.HostLink);
                        break;
                    }
                    EntityId host = stable ? (EntityId)resolved : relation.Host;
                    RequireSpatialEntity(host);
                    current = host;
                }
                // Unwinding is host-first even when creation order puts passengers first.
                while (depth > 0)
                {
                    current = attachmentStack[--depth];
                    if (Entities.TryGet(attachments, current, out SpatialAttachment relation))
                    {
                        SimVec3 host = Entities.Read(spatialPositions, AttachmentHost(in relation)).Value;
                        var next = new SimVec3(SimFix.FromRaw(SimChecked.Add(host.X.Raw, relation.Offset.X.Raw)),
                            SimFix.FromRaw(SimChecked.Add(host.Y.Raw, relation.Offset.Y.Raw)),
                            SimFix.FromRaw(SimChecked.Add(host.Z.Raw, relation.Offset.Z.Raw)));
                        Entities.Set(spatialPositions, current, new Position(next));
                    }
                    attachmentState[current.Id] = 2;
                }
            }
        }
    }
}
