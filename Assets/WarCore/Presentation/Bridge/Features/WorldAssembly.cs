using System.Collections.Generic;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Control;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Presentation
{
    // What the hooks of one build share: the recipe and map, the simulation once it exists, the component types the installers
    // registered, and the objects the systems are made from. Every field is written once, by the builder or the installer that
    // owns it (named next to it); every other installer and every extension only reads it.
    internal sealed class WorldAssembly
    {
        internal WorldAssembly(WorldRecipe recipe, byte[] mapBytes, bool reverseAllocation)
        {
            Recipe = recipe;
            MapBytes = mapBytes;
            ReverseAllocation = reverseAllocation;
            foreach (IWorldOption option in recipe.Options)
                if (option is IWorldFeature feature)
                    Extensions.Add(feature);
        }

        // The collision layer of every unit's body; walls must stay off it.
        internal const uint MoverLayer = 1u;

        internal WorldRecipe Recipe { get; }
        internal byte[] MapBytes { get; }
        internal bool ReverseAllocation { get; }
        // The features the recipe carries that are not part of the built-in tables, in the order they were added.
        internal List<IWorldFeature> Extensions { get; } = new List<IWorldFeature>();

        internal bool Crowd => Recipe.Mover == MoverKind.InterimCrowd;
        internal bool Path => Recipe.Mover == MoverKind.InterimPath || Crowd;

        // Facts of the map, written by the builder (the terrain tables by the terrain installer). The map is null on the
        // plain path.
        internal WcMap Map { get; set; }
        internal TerrainTables Terrain { get; set; }
        internal long GroundRaw { get; set; }
        internal uint WallMask { get; set; }
        // Entries of the stable content manifest that features contribute (the terrain digest, for one).
        internal List<StableContentKey> ContentKeys { get; } = new List<StableContentKey>();

        // Written by the builder.
        internal Simulation Sim { get; set; }
        internal EntityId[] Units { get; set; }

        // Component types, written by the installer that registers them. Default when the component is not registered.
        internal ComponentType<Position> Positions { get; set; }
        internal ComponentType<Collider> Colliders { get; set; }
        internal ComponentType<MoveIntent> Intents { get; set; }
        internal ComponentType<MoveWaypoint> Waypoints { get; set; }
        internal ComponentType<UnitFacing> Facings { get; set; }
        internal ComponentType<MotionLimits> MotionLimits { get; set; }
        internal ComponentType<SquadStanding>? Standing { get; set; }
        internal ComponentType<Combatant> Combatants { get; set; }

        // Written by the match installer before the simulation exists; the simulation owns it. Null without match rules.
        internal MatchMode Mode { get; set; }

        // Written by the Connect hook (player, combat, crowd pass rule, in that order), before the systems are registered:
        // the dependencies of the movement systems. Null when the feature is off.
        internal PlayerControl Controlled { get; set; }
        internal CombatSystem Combat { get; set; }
        internal ICrowdPass CrowdPass { get; set; }
        // Written by the movement installer when it registers its systems.
        internal InterimLocomotion Locomotion { get; set; }
        // Written by the match installer in the RegisterRules hook; null without match rules.
        internal CaptureSystem Capture { get; set; }
        internal ReinforceSystem Reinforce { get; set; }
        internal MatchSystem Match { get; set; }

        // Where a unit stands when it is placed: the map's flat floor, or the terrain surface where the map has a height field.
        internal SimVec3 SpawnPoint(in UnitSpawn spawn)
        {
            SimVec3 point = SimSession.Target(spawn.XMm, spawn.ZMm, GroundRaw);
            if (Terrain?.Ground != null && Terrain.Ground.TryHeightAt(point.X, point.Z, out SimFix groundY))
                point = new SimVec3(point.X, groundY, point.Z);
            return point;
        }

        internal static SimFix SpawnSpeed(in UnitSpawn spawn) => SimFix.Ratio(spawn.SpeedMmPerSec, 1000 * SimClock.TickRate);

        // A reserve unit of a battle has no body until it is deployed.
        internal bool IsDeployedAtStart(in UnitSpawn spawn) => Recipe.Match == null || !spawn.Reserve;
    }
}
