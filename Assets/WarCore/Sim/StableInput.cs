using System;

namespace WarCore.Sim
{
    public readonly struct StableInputFrame
    {
        public InputFrame Payload { get; }
        public EntityLink SquadTarget { get; }
        public StableInputFrame(InputFrame payload, EntityLink squadTarget = default)
        {
            if ((!payload.Squad.HasValue && squadTarget.HasValue)
                || (payload.Squad.HasValue && payload.Squad.Value.TargetEntity.Generation != 0))
                throw new SimBoundaryException("Stable input must express entity targets with EntityLink.");
            Payload = payload;
            SquadTarget = squadTarget;
        }
        internal InputFrame Bind(EntityStore entities, RequestDiagnostics diagnostics = null)
        {
            entities.ValidateLink(SquadTarget,false);
            SquadCommand? squad = null;
            if (Payload.Squad.HasValue)
            {
                SquadCommand value = Payload.Squad.Value;
                EntityId target = default;
                if (SquadTarget.HasValue && entities.TryResolve(SquadTarget.Id,out EntityRef resolved))
                    target = resolved;
                else if (SquadTarget.HasValue)
                    diagnostics?.Record(StableRequestKind.InputTarget,SquadTarget);
                squad = new SquadCommand(value.Kind,value.Squad,value.TargetPoint,target);
            }
            return new InputFrame(Payload.Move,Payload.Aim,Payload.PrimaryAttack,Payload.Block,Payload.Throw,Payload.SkillSlot,squad,Payload.GameplayTick,Payload.SkillCommandArray,Payload.SkillCommandCount);
        }
        internal void Save(SimStateWriter writer)
        {
            SnapshotValues.Vector(writer,Payload.Move);
            SnapshotValues.Vector(writer,Payload.Aim);
            writer.Bool(Payload.PrimaryAttack);
            writer.Bool(Payload.Block);
            writer.Bool(Payload.Throw);
            writer.Int32(Payload.SkillSlot);
            writer.Bool(Payload.Squad.HasValue);
            if (Payload.Squad.HasValue)
            {
                SquadCommand value = Payload.Squad.Value;
                writer.Int32((int)value.Kind);
                writer.Int32(value.Squad);
                SnapshotValues.Vector(writer,value.TargetPoint);
                writer.Link(SquadTarget);
            }
        }
        internal static StableInputFrame Read(SimStateReader reader)
        {
            SimVec3 move = SnapshotValues.Vector(reader);
            SimVec3 aim = SnapshotValues.Vector(reader);
            bool attack = reader.Bool();
            bool block = reader.Bool();
            bool throwing = reader.Bool();
            int skill = reader.Int32();
            SquadCommand? squad = null;
            EntityLink target = default;
            if (reader.Bool())
            {
                squad = new SquadCommand((SquadCommandKind)reader.Int32(),reader.Int32(),SnapshotValues.Vector(reader),default);
                target = reader.Link();
            }
            return new StableInputFrame(new InputFrame(move,aim,attack,block,throwing,skill,squad),target);
        }
    }

    public sealed partial class Sim
    {
        private StableInputFrame stableInput;
        public void StepStable(StableInputFrame input)
        {
            Entities.RequireStableWorld();
            RequireActivationBoundary();
            InputFrame bound = input.Bind(Entities,RequestDiagnostics);
            stableInput = input;
            StepCore(bound);
        }
    }
}
