using System;
namespace WarCore.Sim
{
    public readonly struct GameplayInputCommand : IComparable<GameplayInputCommand>
    {
        public readonly GameplayStamp Actor;
        public readonly uint CommandOrdinal;
        public readonly GameplayInputKind Kind;
        public readonly ulong SlotKey;
        public readonly GameplayTargetMode TargetMode;
        public readonly bool HasExplicitTarget;
        public readonly GameplayStamp ExplicitTarget;
        public readonly SimVec3 AimDirection, TargetPoint;
        public GameplayInputCommand(GameplayStamp actor, uint commandOrdinal, GameplayInputKind kind, ulong slotKey,
            GameplayTargetMode targetMode = GameplayTargetMode.Self, GameplayStamp? explicitTarget = null,
            SimVec3 aimDirection = default, SimVec3 targetPoint = default)
        {
            Actor = actor;
            CommandOrdinal = commandOrdinal;
            Kind = kind;
            SlotKey = slotKey;
            TargetMode = targetMode;
            HasExplicitTarget = explicitTarget.HasValue;
            ExplicitTarget = explicitTarget.GetValueOrDefault();
            AimDirection = aimDirection;
            TargetPoint = targetPoint;
        }
        public int CompareTo(GameplayInputCommand other)
        {
            int order = Actor.Id.CompareTo(other.Actor.Id);
            return order != 0 ? order : CommandOrdinal.CompareTo(other.CommandOrdinal);
        }
    }
    // A frozen local authority is configuration, never a claim supplied by the input sender.
    public sealed class GameplayInputAuthority
    {
        private readonly StableId[] actors;
        public ulong Key
        {
            get;
        }
        public int ActorCount => actors.Length;
        public StableId ActorAt(int index) => actors[index];
        public GameplayInputAuthority(ulong key, StableId[] actors)
        {
            if (actors == null || actors.Length > GameplayMath.MaxPackets)
                throw new SimBoundaryException("M0C_INPUT_AUTHORITY");
            Key = key;
            this.actors = (StableId[])actors.Clone();
            Array.Sort(this.actors);
            for (int i = 1; i < this.actors.Length; i++)
                if (this.actors[i - 1].Equals(this.actors[i]))
                    throw new SimBoundaryException("M0C_INPUT_AUTHORITY_DUPLICATE");
        }
        public bool Allows(StableId actor)
        {
            int low = 0, high = actors.Length - 1;
            while (low <= high)
            {
                int mid = low + (high - low) / 2;
                int c = actors[mid].CompareTo(actor);
                if (c == 0)
                    return true;
                if (c < 0)
                    low = mid + 1;
                else
                    high = mid - 1;
            }
            return false;
        }
    }
}
