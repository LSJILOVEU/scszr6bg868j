using System;
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
        // The plates of a map with floor plates, written by the plate installer; null otherwise.
        internal PlateWorld Plates { get; set; }
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
        internal ComponentType<VerticalState> Verticals { get; set; }

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
        internal Pressure Pressure { get; set; }
        // TRV-7a: the walking costs to each point (null without plates or without a match) and the entry table built from them
        // once the walls are in the spatial index (null likewise).
        internal PointReach Reach { get; set; }
        internal PointEntryTable Entries { get; set; }
        internal Squads Squads { get; set; }
        // STANCE-1, written by the stance installer (a pack with stances only; null otherwise): the posture geometry the spatial
        // index takes, the stance system and the action gate of the world. The gate is made by the stance installer, or by the
        // downed installer (DOWN-1) in a world that keeps casualties but has no stances; null when neither is on.
        internal PostureGeometry Postures { get; set; }
        internal StanceSystem Stances { get; set; }
        internal ActionGate Gate { get; set; }
        // DOWN-1, written by the downed installer (a pack that keeps casualties only; default otherwise).
        internal ComponentType<Casualty> Casualties { get; set; }
        // Written by the movement installer: the player's step, or null without player control.
        internal PlayerStep PlayerStep { get; set; }

        // Where a unit stands when it is placed: the map's flat floor, or the terrain surface where the map has a height field;
        // on a plate (layer above 0, checked by the plate installer) the plate's floored surface.
        internal SimVec3 SpawnPoint(in UnitSpawn spawn)
        {
            SimVec3 point = SimSession.Target(spawn.XMm, spawn.ZMm, GroundRaw);
            if (spawn.Layer > 0)
            {
                if (!Plates.TryFloor(spawn.Layer, point.X, point.Z, true, out SimFix floor))
                    throw new InvalidOperationException("Unit spawn " + spawn.Key + " is not on its plate.");
                return new SimVec3(point.X, floor, point.Z);
            }
            if (Terrain?.Ground != null && Terrain.Ground.TryHeightAt(point.X, point.Z, out SimFix groundY))
                point = new SimVec3(point.X, groundY, point.Z);
            return point;
        }

        // ML-3: the unit collider height follows the combat pack's body (its height), else the legacy 1 m.
        internal static SimFix UnitHeight(CombatPack pack) => pack?.Body?.Height ?? SimFix.FromInt(1);
        // HC-1: the unit collider radius and walking clearance follow the pack's body (its radius), else the default body radius.
        internal static SimFix UnitRadius(CombatPack pack) => pack?.Body?.Radius ?? BodyRadius.Default;
        // ML-3: the spatial index anchors the unit layer at the foot exactly when the combat pack has a body.
        internal static uint FootAnchoredLayers(CombatPack pack) => pack?.Body != null ? MoverLayer : 0u;
        internal static SimFix SpawnSpeed(in UnitSpawn spawn) => SimFix.Ratio(spawn.SpeedMmPerSec, 1000 * SimClock.TickRate);

        // A reserve unit of a battle has no body until it is deployed.
        internal bool IsDeployedAtStart(in UnitSpawn spawn) => Recipe.Match == null || !spawn.Reserve;
    }
}
