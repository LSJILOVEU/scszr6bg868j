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
                squad = new SquadCommand(value.Kind,value.Squad,value.TargetPoint,target,value.TargetLayer);
            }
            return new InputFrame(Payload.Move,Payload.Aim,Payload.PrimaryAttack,Payload.Block,Payload.Throw,Payload.SkillSlot,squad,Payload.GameplayTick,Payload.SkillCommandArray,Payload.SkillCommandCount,Payload.AimLayer);
        }
        // layers: the world carries layer numbers (Sim.EnableInputLayers). Without them the bytes are the original ones; with them
        // the squad command's TargetLayer follows its link and the AimLayer comes last.
        internal void Save(SimStateWriter writer,bool layers)
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
                if (layers) writer.Int32(value.TargetLayer);
            }
            if (layers) writer.Int32(Payload.AimLayer);
        }
        internal static StableInputFrame Read(SimStateReader reader,bool layers)
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
                var kind = (SquadCommandKind)reader.Int32();
                int squadId = reader.Int32();
                SimVec3 point = SnapshotValues.Vector(reader);
                target = reader.Link();
                squad = new SquadCommand(kind,squadId,point,default,layers ? Layer(reader) : 0);
            }
            int aimLayer = layers ? Layer(reader) : 0;
            return new StableInputFrame(new InputFrame(move,aim,attack,block,throwing,skill,squad,aimLayer:aimLayer),target);
        }
        private static int Layer(SimStateReader reader)
        {
            int layer = reader.Int32();
            SimStateReader.Require(layer >= 0 && layer <= PlateField.MaxPlates,"Invalid input layer.");
            return layer;
        }
    }

    public sealed partial class Sim
    {
        private StableInputFrame stableInput;
        private bool inputLayers;
        // Lets the input carry layer numbers (SquadCommand.TargetLayer, InputFrame.AimLayer): a world with floor plates turns it on
        // before warmup. Without it a frame with a non-zero layer is refused and the input bytes are the original ones.
        public void EnableInputLayers()
        {
            Entities.RequireStableWorld();
            if (warmed) throw new InvalidOperationException("Input layers are enabled before warmup.");
            inputLayers = true;
        }
        private void CheckInputLayers(in InputFrame input)
        {
            if (!inputLayers && input.HasLayers)
                throw new SimBoundaryException("This world's input carries no layer numbers.");
        }
        public void StepStable(StableInputFrame input)
        {
            Entities.RequireStableWorld();
            RequireActivationBoundary();
            CheckInputLayers(input.Payload);
            InputFrame bound = input.Bind(Entities,RequestDiagnostics);
            stableInput = input;
            StepCore(bound);
        }
    }
}
