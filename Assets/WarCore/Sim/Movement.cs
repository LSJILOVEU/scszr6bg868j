using System;

namespace WarCore.Sim
{
    public enum MoveMode
    {
        Ground,
        Flying,
        Water
    }

    public readonly struct Movement : IStateHash
    {
        public MoveMode Mode { get; }
        // Metres per second. One tick truncates velocity / TickRate toward zero in raw units.
        public SimVec3 Velocity { get; }
        public Movement(MoveMode mode, SimVec3 velocity)
        {
            if (mode < MoveMode.Ground || mode > MoveMode.Water)
                throw new ArgumentOutOfRangeException(nameof(mode));
            Mode = mode;
            Velocity = velocity;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add((int)Mode);
            Velocity.WriteState(hash);
        }
    }

    public sealed class MovementSerializer : IStableComponentSerializer<Movement>
    {
        public int TypeId => 5;
        public int Version => 1;
        public void Write(SimStateWriter writer, in Movement value)
        {
            writer.Int32((int)value.Mode);
            SnapshotValues.Vector(writer, value.Velocity);
        }

        public Movement Read(SimStateReader reader) => new Movement((MoveMode)reader.Int32(), SnapshotValues.Vector(reader));
    }

    public interface INavProvider : IStateHash, IStableStateSerializer
    {
        SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight);
    }

    public sealed class FlatGroundNavProvider : INavProvider
    {
        private SimFix surfaceHeight;
        public string StateTypeId => "warcore.flat-ground";
        public int StateVersion => 1;
        public FlatGroundNavProvider(SimFix surfaceHeight = default)
        {
            new SimVec3(default, surfaceHeight, default).ValidateWorld();
            this.surfaceHeight = surfaceHeight;
        }

        public SimVec3 Advance(SimVec3 position, SimVec3 displacement, SimFix halfHeight)
        {
            if (halfHeight.Raw < 0)
                throw new ArgumentOutOfRangeException(nameof(halfHeight));
            // Position remains the baseline capsule centre, so terrain contact includes half its height.
            var result = new SimVec3(SimFix.FromRaw(SimChecked.Add(position.X.Raw, displacement.X.Raw)),
                SimFix.FromRaw(SimChecked.Add(surfaceHeight.Raw, halfHeight.Raw)),
                SimFix.FromRaw(SimChecked.Add(position.Z.Raw, displacement.Z.Raw)));
            result.ValidateWorld();
            return result;
        }

        public void WriteState(StateHash hash) => surfaceHeight.WriteState(hash);
        public void SaveState(SimStateWriter writer) => writer.Int64(surfaceHeight.Raw);
        public void LoadState(SimStateReader reader)
        {
            SimFix height = SimFix.FromRaw(reader.Int64());
            new SimVec3(default, height, default).ValidateWorld();
            surfaceHeight = height;
        }
    }

    public sealed class MovementSystem : ISimSystem, ISimElapsedEntitySystem, IStableStateSerializer, ISimPhaseSystem
    {
        private readonly EntityStore entities;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Movement> movements;
        private readonly ComponentType<Collider> colliders;
        private readonly NavProviderRegistry navigation;
        private readonly PostureGeometry postures;
        private readonly byte[] stateTypeBytes;
        public int Order { get; }
        public SimSystemPhase Phase => SimSystemPhase.Movement;
        public string StateTypeId { get; }
        public int StateVersion => 1;
        public MovementSystem(EntityStore entities, ComponentType<Position> positions, ComponentType<Movement> movements,
            NavProviderRegistry navigation, int order, ComponentType<Collider> colliders = default, PostureGeometry postures = null)
        {
            if (entities == null || positions.Owner != entities || movements.Owner != entities
                || (colliders.Owner != null && colliders.Owner != entities) || (postures != null && postures.Entities != entities))
                throw new ArgumentException("Movement handles must belong to the supplied store.");
            this.entities = entities;
            stage = new EntityStage(entities);
            this.positions = positions;
            this.movements = movements;
            this.colliders = colliders;
            this.postures = postures;
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            navigation.Freeze();
            StateTypeId = "warcore.movement/" + positions.Storage.Name + "/" + movements.Storage.Name
                + "/" + (colliders.Owner == null ? "none" : colliders.Storage.Name) + "/" + navigation.ManifestId
                + (postures == null ? "" : "/posture/" + postures.ComponentId);
            stateTypeBytes = System.Text.Encoding.UTF8.GetBytes(StateTypeId);
            Order = order;
        }

        public void Step(SimContext context)
        {
            foreach (EntityId entity in stage.Capture())
                StepEntity(context, entity);
        }

        public void StepEntity(SimContext context, EntityId entity) => StepEntity(context, entity, 1);

        public void StepEntity(SimContext context, EntityId entity, long elapsedTicks)
        {
            if (elapsedTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedTicks));
            if (context.Entities != entities)
                throw new InvalidOperationException("Movement system belongs to another simulation.");
            if (!entities.TryGet(movements, entity, out Movement movement) || !entities.TryGet(positions, entity, out Position position))
                return;
            // The positive constant divisor cannot be zero or -1, even for MinValue velocity.
            SimVec3 velocity = movement.Velocity;
            if (postures != null)
            {
                SimFix factor = postures.SpeedFactor(entity);
                velocity = new SimVec3(PostureScale.Apply(velocity.X, factor), PostureScale.Apply(velocity.Y, factor), PostureScale.Apply(velocity.Z, factor));
            }

            var delta = new SimVec3(SimFix.FromRaw(velocity.X.Raw / SimClock.TickRate),
                SimFix.FromRaw(velocity.Y.Raw / SimClock.TickRate), SimFix.FromRaw(velocity.Z.Raw / SimClock.TickRate));
            SimVec3 next = position.Value;
            SimFix halfHeight = default;
            if (colliders.Owner != null && entities.TryGet(colliders, entity, out Collider collider))
                halfHeight = SimFix.FromRaw(collider.Height.Raw / 2);
            INavProvider provider = movement.Mode == MoveMode.Flying ? null : navigation.For(movement.Mode);
            // Retain each Full tick's truncation and navigation check; multiplying a rounded total changes both.
            for (long remaining = elapsedTicks; remaining > 0; remaining--)
            {
                if (provider == null)
                    next = new SimVec3(SimFix.FromRaw(SimChecked.Add(next.X.Raw, delta.X.Raw)),
                        SimFix.FromRaw(SimChecked.Add(next.Y.Raw, delta.Y.Raw)),
                        SimFix.FromRaw(SimChecked.Add(next.Z.Raw, delta.Z.Raw)));
                else
                    next = provider.Advance(next, delta, halfHeight);
                next.ValidateWorld();
            }

            if (!context.BufferMovement(entity, next))
                entities.Get(positions, entity).Value = next;
        }

        public void WriteState(StateHash hash)
        {
            hash.AddUtf8(stateTypeBytes);
            navigation.WriteState(hash);
            if (postures != null)
                postures.Catalog.WriteState(hash);
        }

        public void SaveState(SimStateWriter writer)
        {
            navigation.SaveState(writer);
            postures?.Catalog.SaveState(writer);
        }

        public void LoadState(SimStateReader reader)
        {
            navigation.LoadState(reader);
            postures?.Catalog.LoadState(reader);
        }
    }
}
