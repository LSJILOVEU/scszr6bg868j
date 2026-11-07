using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // Combat: the combatant component, each unit's gameplay binding and combatant state, the combat system with the units'
    // melee and block specs, and the cleanup of dead units' move intents. On when the recipe carries a combat pack.
    internal sealed class CombatFeature : WorldStageFeature
    {
        internal static readonly CombatFeature Instance = new CombatFeature();

        public override string OptionId => "world.combat";

        public override bool IsActive(WorldAssembly a) => a.Recipe.Combat != null;

        public override void CompleteComponents(WorldAssembly a) =>
            a.Combatants = a.Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());

        public override void CompleteUnit(WorldAssembly a, int index, EntityId unit)
        {
            UnitSpawn spawn = a.Recipe.Units[index];
            a.Sim.BindGameplay(unit, 1);
            int ammo = spawn.Weapon < 0 ? 0 : a.Recipe.Combat.Weapons[spawn.Weapon].Magazine;
            a.Sim.Entities.Set(a.Combatants, unit, new Combatant(spawn.Team, spawn.Weapon, ammo, 0));
        }

        // Runs after the player feature: the combat system keeps the player control it is given.
        public override void Connect(WorldAssembly a)
        {
            Simulation sim = a.Sim;
            WorldRecipe recipe = a.Recipe;
            var combat = new CombatSystem(sim, a.Positions, a.Combatants, Covered(a), a.WallMask, WorldAssembly.MoverLayer, recipe.Units.Length, a.Controlled);
            sim.RegisterGameplay("combat.system", combat, SimSystemPhase.Queries);
            // The combat system registers the melee component, so the units get their specs only now.
            for (int i = 0; i < recipe.Units.Length; i++)
                if (recipe.Units[i].Melee >= 0) combat.SetMelee(a.Units[i], recipe.Units[i].Melee);
            for (int i = 0; i < recipe.Units.Length; i++)
                if (recipe.Units[i].Block >= 0) combat.SetBlock(a.Units[i], recipe.Units[i].Block);
            sim.RegisterGameplay("bridge.dead-cleanup", new DeadCleanup(sim, a.Combatants, a.Intents), SimSystemPhase.Requests);
            a.Combat = combat;
        }

        // The recipe's combat pack, with its selector wrapped in ConcealedSight when some cell of a terrain map stands on a surface
        // with cover. Otherwise (flat world, no surface table, or every cell at factor 1000) it is the recipe's pack itself.
        private static CombatPack Covered(WorldAssembly a)
        {
            CombatPack pack = a.Recipe.Combat;
            TerrainTables terrain = a.Terrain;
            if (terrain == null || terrain.SurfaceCount == 0) return pack;
            var bySurface = new short[terrain.SurfaceCount];
            for (int i = 0; i < bySurface.Length; i++)
            {
                // TerrainDerivation already refused a map naming a surface the rules lack.
                a.Recipe.TerrainPack.Terrain.TryFind(terrain.SurfaceName(i), out SurfaceDef def);
                bySurface[i] = (short)def.ConcealMilli;
            }
            var cells = new short[terrain.Nav.Count];
            bool any = false;
            for (int key = 0; key < cells.Length; key++)
            {
                cells[key] = bySurface[terrain.SurfaceIndexAt(key)];
                any |= cells[key] != SurfaceDef.MaxConcealMilli;
            }
            if (!any) return pack;
            return pack.WithSelector(new ConcealedSight(pack.Selector, terrain.Nav, cells));
        }
    }

    internal sealed class DeadCleanup : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly EntityStage stage;
        private readonly ComponentType<Combatant> combatants;
        private readonly ComponentType<MoveIntent> intents;
        public DeadCleanup(Simulation sim, ComponentType<Combatant> combatants, ComponentType<MoveIntent> intents)
        { this.sim = sim; this.combatants = combatants; this.intents = intents; stage = new EntityStage(sim.Entities); }
        public int Order => 5;
        public string StateTypeId => "bridge.dead-cleanup";
        public int StateVersion => 1;
        public void Step(SimContext context)
        {
            foreach (EntityId unit in stage.Capture())
                if (sim.Entities.TryGet(combatants, unit, out _) && sim.Entities.TryGet(intents, unit, out _)
                    && sim.GameplayLife(sim.Entities.StableOf(unit)).State == GameplayLifeState.Dead)
                    sim.Entities.Remove(intents, unit);
        }
        public void SaveState(SimStateWriter writer) { }
        public void LoadState(SimStateReader reader) { }
        public void WriteState(StateHash hash) { hash.Add(StateTypeId); hash.Add(StateVersion); }
    }
}
