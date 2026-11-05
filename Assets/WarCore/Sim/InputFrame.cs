using System;

namespace WarCore.Sim
{
    public enum SquadCommandKind
    {
        MoveTo,
        Hold,
        Follow
    }

    public readonly struct SquadCommand : IStateHash
    {
        public SquadCommandKind Kind
        {
            get;
        }

        public int Squad
        {
            get;
        }

        public SimVec3 TargetPoint
        {
            get;
        }

        public EntityId TargetEntity
        {
            get;
        }

        public SquadCommand(SquadCommandKind kind, int squad, SimVec3 targetPoint, EntityId targetEntity)
        {
            if (kind < SquadCommandKind.MoveTo || kind > SquadCommandKind.Follow)
                throw new ArgumentOutOfRangeException(nameof(kind));
            targetPoint.ValidateWorld();
            Kind = kind;
            Squad = squad;
            TargetPoint = targetPoint;
            TargetEntity = targetEntity;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add((int)Kind);
            hash.Add(Squad);
            TargetPoint.WriteState(hash);
            TargetEntity.WriteState(hash);
        }
    }

    public readonly struct SquadId : IStateHash
    {
        public int Value
        {
            get;
        }

        public SquadId(int value)
        {
            Value = value;
        }

        public void WriteState(StateHash hash)
        {
            hash.Add(Value);
        }
    }

    public readonly struct InputFrame : IStateHash
    {
        public SimVec3 Move
        {
            get;
        }

        public SimVec3 Aim
        {
            get;
        }

        public bool PrimaryAttack
        {
            get;
        }

        public bool Block
        {
            get;
        }

        public bool Throw
        {
            get;
        }

        // Zero means no skill; positive values identify content-defined slots.
        public int SkillSlot
        {
            get;
        }

        public SquadCommand? Squad
        {
            get;
        }

        private readonly GameplayInputCommand[] skillCommands;
        internal GameplayInputCommand[] SkillCommandArray => skillCommands;
        public long GameplayTick { get; }
        public int SkillCommandCount { get; }
        public ReadOnlySpan<GameplayInputCommand> SkillCommands => skillCommands == null ? ReadOnlySpan<GameplayInputCommand>.Empty : skillCommands.AsSpan(0, SkillCommandCount);
        internal bool HasValidSkillCommandShape => SkillCommandCount >= 0 && (skillCommands != null ? SkillCommandCount <= skillCommands.Length : SkillCommandCount == 0);
        internal GameplayInputCommand SkillCommandAt(int index) => skillCommands[index];

        // The frame borrows its bounded input block until Step starts; Step copies it before running systems.
        public InputFrame(SimVec3 move, SimVec3 aim, bool primaryAttack = false, bool block = false, bool @throw = false, int skillSlot = 0, SquadCommand? squad = null,
            long gameplayTick = 0, GameplayInputCommand[] skillCommands = null, int skillCommandCount = -1)
        {
            if (skillSlot < 0)
                throw new ArgumentOutOfRangeException(nameof(skillSlot));
            Move = move;
            Aim = aim;
            PrimaryAttack = primaryAttack;
            Block = block;
            Throw = @throw;
            SkillSlot = skillSlot;
            Squad = squad;
            GameplayTick = gameplayTick;
            this.skillCommands = skillCommands;
            SkillCommandCount = skillCommandCount == -1 ? (skillCommands == null ? 0 : skillCommands.Length) : skillCommandCount;
        }

        public void WriteState(StateHash hash)
        {
            Move.WriteState(hash);
            Aim.WriteState(hash);
            hash.Add(PrimaryAttack);
            hash.Add(Block);
            hash.Add(Throw);
            hash.Add(SkillSlot);
            hash.Add(Squad.HasValue);
            if (Squad.HasValue)
                Squad.Value.WriteState(hash);
        }
    }
}
