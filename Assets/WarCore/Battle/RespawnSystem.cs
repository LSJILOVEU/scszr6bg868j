using System;
using WarCore.Combat;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Battle
{
    // Player respawn: RespawnTicks after the controlled unit died, the first reserve of its team (in Capture() order) is deployed
    // at the team's owned, uncontested spawn point nearest the body (else the nearest team base), and control switches to it only
    // once that deployment succeeded. The anchor is chosen again on every tick until then, so a failed deployment retries the same
    // anchor unless a point became contested or free meanwhile. No state of its own: the death tick lives in the Sim, the current
    // target in the control system, and the delay in the pack digest.
    public sealed class RespawnSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly CombatSystem combat;
        private readonly MatchPack pack;
        private readonly CaptureSystem capture;
        private readonly IUnitDeployer deployer;
        private readonly IControlTarget controlled;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;

        public RespawnSystem(Simulation sim, CombatSystem combat, MatchPack pack, CaptureSystem capture, IUnitDeployer deployer, IControlTarget controlled)
        {
            this.sim = sim; this.combat = combat; this.pack = pack; this.capture = capture; this.deployer = deployer; this.controlled = controlled;
            stage = new EntityStage(combat.Entities); positions = combat.Entities.Resolve<Position>(); combatants = combat.Entities.Resolve<Combatant>();
        }
        public int Order => 4;
        public string StateTypeId => "battle.respawn";
        public int StateVersion => 1;

        public void Step(SimContext c)
        {
            EntityId player = controlled.Current;
            if (!combat.Entities.IsAlive(player)) return;
            GameplayLifeView life = sim.GameplayLife(combat.Entities.StableOf(player));
            if (life.State != GameplayLifeState.Dead || c.Clock.Tick < life.EnteredTick + pack.RespawnTicks) return;
            int team = combat.Unit(player).Team;
            EntityId reserve = default; bool found = false;
            foreach (EntityId entity in stage.Capture())
                if (combat.Entities.TryGet(combatants, entity, out Combatant unit) && unit.Team == team
                    && !combat.Entities.TryGet(positions, entity, out _) && !combat.IsDead(entity))
                { reserve = entity; found = true; break; }
            // A dead unit keeps its Position (death cleanup removes only the move intent), so the body is the distance origin.
            if (!found || !TryAnchor(team, combat.Point(player), out SimVec3 anchor)) return;
            int cells = pack.GridColumns * pack.GridRows;
            for (int slot = 0; slot < cells; slot++)
            {
                SimVec3 offset = new SimVec3(SimFix.Ratio(2 * (slot % pack.GridColumns) - pack.GridColumns + 1, 2) * pack.GridSpacing,
                    default, SimFix.Ratio(2 * (slot / pack.GridColumns) - pack.GridRows + 1, 2) * pack.GridSpacing);
                if (!deployer.TryDeploy(reserve, anchor + offset)) continue;
                controlled.Switch(c, reserve);
                return;
            }
        }

        // The team's owned, uncontested spawn point nearest the body, ties to the lower index; with none, the nearest team base the
        // same way (bases are never contested). With neither there is no anchor and the switch waits.
        private bool TryAnchor(int team, SimVec3 origin, out SimVec3 anchor)
        {
            anchor = default; SimWide best = default; bool found = false;
            for (int i = 0; i < pack.Points.Length; i++)
                if (capture.Points[i].Owner == team && pack.Points[i].HasSpawn && !Contested(team, pack.Points[i])) Closer(pack.Points[i].Spawn, origin, ref anchor, ref best, ref found);
            if (found) return true;
            foreach (var spawn in pack.Bases) if (spawn.Team == team) Closer(spawn.Point, origin, ref anchor, ref best, ref found);
            return found;
        }

        // Contested: a living unit of the other team with a position stands inside the point's capture circle (centre and radius,
        // horizontal only, boundary included), counted like CaptureSystem counts units. Read before this tick's movement.
        // PointState.Capturing is not used: under InterruptMode.Pause it outlives the enemy that set it.
        private bool Contested(int team, in ControlPointDef point)
        {
            foreach (EntityId entity in stage.Capture())
                if (combat.Entities.TryGet(combatants, entity, out Combatant unit) && unit.Team == 3 - team
                    && combat.Entities.TryGet(positions, entity, out Position at) && !combat.IsDead(entity) && CaptureSystem.Inside(at.Value, point))
                    return true;
            return false;
        }

        private static void Closer(SimVec3 point, SimVec3 origin, ref SimVec3 anchor, ref SimWide best, ref bool found)
        {
            SimWide distance = CombatSystem.Horizontal(point - origin).LengthSquared;
            if (found && distance >= best) return;
            anchor = point; best = distance; found = true;
        }

        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.RespawnTicks); }
    }
}
