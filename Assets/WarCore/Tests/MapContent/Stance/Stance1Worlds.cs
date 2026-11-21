using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Battle;
using WarCore.Content.Rwr;
using WarCore.Interim;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Stance
{
    // STANCE-1 fixtures. Worlds are built by the real WorldBuilder (crowd mover, combat pack of a content layer or made here) on
    // a flat 40 x 16 m map whose walls are boxes given in millimetres, from the floor up to a top. Seed 41.
    internal static class Stance1Worlds
    {
        internal const long M = SimFix.Scale;
        internal const string Layer = "rwr-stance";
        internal const int Width = 40, Depth = 16;

        // A wall box over [x0, x1] x [z0, z1] (mm), from the floor to top (raw).
        internal readonly struct Box
        {
            internal readonly long X0, Z0, X1, Z1, TopRaw;
            internal Box(long x0, long z0, long x1, long z1, long topRaw) { X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; TopRaw = topRaw; }
        }

        internal static CombatPack Pack(string layer = Layer) => RwrPack.Load(layer).BuildCombat();

        // A deterministic pack: one no-spread weapon (Proj1World.Weapon), FalloffDamage, the shipped body and stances (or the
        // given ones); speed > 0 makes the weapon fire projectiles.
        internal static CombatPack Exact(int speed = 0, StanceDef[] stances = null)
        {
            CombatPack shipped = Pack();
            return new CombatPack(new[] { WarCore.Tests.Projectile.Proj1World.Weapon() }, new WarCore.Tests.Projectile.FalloffDamage(), new NearestVisibleEnemy(), 100 * M,
                body: shipped.Body, projectileSpeeds: speed == 0 ? null : new[] { speed }, stances: stances ?? shipped.Stances.ToArray());
        }

        internal static byte[] Map(params Box[] boxes)
        {
            var walls = new WcMapWall[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                Box b = boxes[i];
                walls[i] = new WcMapWall(0, (ulong)(i + 1), "wall", "walls",
                    new SimVec3(SimFix.Ratio(b.X0 + b.X1, 2000), SimFix.FromRaw(b.TopRaw / 2), SimFix.Ratio(b.Z0 + b.Z1, 2000)),
                    SimFix.Ratio(b.X1 - b.X0, 2000), SimFix.FromRaw(b.TopRaw), SimFix.Ratio(b.Z1 - b.Z0, 2000));
            }
            var bits = new byte[(Width * Depth + 7) / 8];
            for (int z = 0; z < Depth; z++)
                for (int x = 0; x < Width; x++)
                {
                    bool blocked = false;
                    foreach (Box b in boxes) blocked |= x * 1000 < b.X1 && (x + 1) * 1000 > b.X0 && z * 1000 < b.Z1 && (z + 1) * 1000 > b.Z0;
                    if (!blocked) { int k = z * Width + x; bits[k / 8] |= (byte)(1 << (k % 8)); }
                }
            var map = new WcMap("stance1", "none", default, new SimVec3(SimFix.FromInt(Width), SimFix.FromInt(6), SimFix.FromInt(Depth)),
                SimFix.FromInt(Width), default, Width, Depth, new[] { new WcMapLayer("walls", 1) }, walls, bits);
            return WcMapWriter.Write(map);
        }

        internal static UnitSpawn Soldier(ulong key, int squad, long xMm, long zMm, int team, int weapon = 0, long speed = 6000) =>
            new UnitSpawn(key, 0, squad, xMm, zMm, speed, team, weapon);

        internal static SimSession World(CombatPack pack, UnitSpawn[] units, byte[] map, BridgeCommand[] script = null,
            Func<WorldRecipe, WorldRecipe> open = null, MoverKind mover = MoverKind.InterimCrowd)
        {
            var recipe = new WorldRecipe(41, mover, units, script ?? Array.Empty<BridgeCommand>()).WithCombat(pack);
            return WorldBuilder.Build(open == null ? recipe : open(recipe), map, false);
        }

        // A world with unit key under player control: player control needs a match, here one that never ends, with its point
        // at (30, 14) m and the bases in two corners.
        internal static Func<WorldRecipe, WorldRecipe> Player(ulong key) => r => r
            .WithMatch(M6World.Pack(new[] { new ControlPointDef(7, SimVec3.Metres(30, 0, 14), SimFix.FromInt(1)) },
                bases: new[] { new SpawnAnchor(1, SimVec3.Metres(2, 0, 2)), new SpawnAnchor(2, SimVec3.Metres(38, 0, 2)) }))
            .WithRuleSet(MatchMode.TestNeverEndsId).WithPlayer(key);

        internal static Simulation SimOf(SimSession s) => M3TestWorld.Sim(s);
        internal static EntityId Unit(SimSession s, ulong key) => M3TestWorld.Unit(s, key);
        internal static StanceSystem Stances(SimSession s) => s.Stances;
        internal static CombatSystem Combat(SimSession s) => s.Stances?.Gate == null ? null : CombatOf(s);
        internal static CombatSystem CombatOf(SimSession s)
        {
            var field = typeof(SimSession).GetField("combat", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (CombatSystem)field.GetValue(s);
        }

        // Puts unit key in level at once (before the first step or between steps).
        internal static void Force(SimSession s, ulong key, int level)
        {
            if (!s.Stances.ForceStance(Unit(s, key), level)) throw new InvalidOperationException("ForceStance refused unit " + key);
        }

        // The fire records of the next n steps.
        internal static List<FireRecord> Fires(SimSession s, int n)
        {
            var list = new List<FireRecord>();
            CombatSystem combat = CombatOf(s);
            for (int i = 0; i < n; i++)
            {
                s.StepOnce();
                foreach (FireRecord f in combat.Fires) list.Add(f);
            }
            return list;
        }

        // A fixture system that runs an action during the step of one tick, in a phase at an order (before the first step).
        internal sealed class AtTick : IRetainedGameplaySystem
        {
            private readonly long tick; private readonly int order; private readonly Action<SimContext> action;
            internal AtTick(long tick, int order, Action<SimContext> action) { this.tick = tick; this.order = order; this.action = action; }
            public int Order => order;
            public string StateTypeId => "test.stance1-at-" + order;
            public int StateVersion => 1;
            public void Step(SimContext c) { if (c.Clock.Tick == tick) action(c); }
            public void SaveState(SimStateWriter w) { }
            public void LoadState(SimStateReader r) { }
            public void WriteState(StateHash h) { }
        }

        internal static void Run(SimSession s, long tick, SimSystemPhase phase, int order, Action<SimContext> action) =>
            SimOf(s).RegisterGameplay("test.stance1-at-" + order, new AtTick(tick, order, action), phase);

        // Kills the units in the step of tick (Queries phase, order 45).
        internal static void Kill(SimSession s, long tick, params ulong[] keys) => Run(s, tick, SimSystemPhase.Queries, 45, c =>
        {
            Simulation sim = SimOf(s);
            foreach (ulong key in keys)
            {
                StableId id = sim.Entities.StableOf(Unit(s, key));
                c.Damage(sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)c.Clock.Tick, 0, id, (ulong)c.Clock.Tick, 0), 1, 1000 * M);
            }
        });

        internal static void StepTo(SimSession s, long tick) { while (s.Tick < tick) s.StepOnce(); }

        internal static int Hurt(IEnumerable<FireRecord> fires)
        {
            int hurt = 0;
            foreach (FireRecord f in fires)
                if (f.Outcome == FireOutcome.Hit && f.DamageRaw > 0) hurt++;
            return hurt;
        }
    }
}
