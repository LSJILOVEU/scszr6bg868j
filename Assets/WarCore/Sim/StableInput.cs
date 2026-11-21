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
            return new InputFrame(Payload.Move,Payload.Aim,Payload.PrimaryAttack,Payload.Block,Payload.Throw,Payload.SkillSlot,squad,Payload.GameplayTick,Payload.SkillCommandArray,Payload.SkillCommandCount,Payload.AimLayer,Payload.Actions);
        }
        // layers: the world carries layer numbers (Sim.EnableInputLayers). Without them the bytes are the original ones; with them
        // the squad command's TargetLayer follows its link and the AimLayer comes last. actions: the world carries action bits
        // (Sim.EnableInputActions); then one UInt32 follows everything else.
        internal void Save(SimStateWriter writer,bool layers,bool actions = false)
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
            if (actions) writer.Int32(unchecked((int)Payload.Actions)); // four bytes, read back as uint
        }
        internal static StableInputFrame Read(SimStateReader reader,bool layers,bool actions = false)
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
            uint bits = actions ? Actions(reader) : 0;
            return new StableInputFrame(new InputFrame(move,aim,attack,block,throwing,skill,squad,aimLayer:aimLayer,actions:bits),target);
        }
        private static int Layer(SimStateReader reader)
        {
            int layer = reader.Int32();
            SimStateReader.Require(layer >= 0 && layer <= PlateField.MaxPlates,"Invalid input layer.");
            return layer;
        }
        private static uint Actions(SimStateReader reader)
        {
            uint bits = unchecked((uint)reader.Int32());
            SimStateReader.Require(InputActions.IsValid(bits),"Invalid input action bits.");
            return bits;
        }
    }

    public sealed partial class Sim
    {
        private StableInputFrame stableInput;
        private bool inputLayers;
        private bool inputActions;
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
        private void CheckInputActions(in InputFrame input)
        {
            if (!inputActions && input.Actions != 0)
                throw new SimBoundaryException("This world's input carries no action bits.");
        }
        // Lets the input carry action bits (InputFrame.Actions): turned on before warmup by the code that builds the world, every
        // time (the switch is not saved). Without it a frame with non-zero bits is refused and the input bytes are the original
        // ones; with it every saved and hashed input ends with one UInt32, after the layer numbers.
        public void EnableInputActions()
        {
            Entities.RequireStableWorld();
            if (warmed) throw new InvalidOperationException("Input actions are enabled before warmup.");
            inputActions = true;
        }
        public void StepStable(StableInputFrame input)
        {
            Entities.RequireStableWorld();
            RequireActivationBoundary();
            CheckInputLayers(input.Payload);
            CheckInputActions(input.Payload);
            InputFrame bound = input.Bind(Entities,RequestDiagnostics);
            stableInput = input;
            StepCore(bound);
        }
    }
}
