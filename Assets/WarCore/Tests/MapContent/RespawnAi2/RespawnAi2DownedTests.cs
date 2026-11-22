using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Interim;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // RESPAWN-AI-2 A7: a downed unit is not dead. No shipped pack lets a unit go down yet (CombatPack.BuildSchema), so this
    // world is built by hand with the same schema except that a unit at 0 health is downed for 100 ticks and then dies.
    // Team 1: keys 1 and 2 on the field, reserves 11 and 12; team 2: key 4 on the field. One base each, one neutral point.
    public sealed class RespawnAi2DownedTests
    {
        private const int Reentry = 60, DownedTicks = 100;
        private const long Hit = 10;

        private sealed class World
        {
            internal readonly Simulation Sim;
            internal readonly CombatSystem Combat;
            internal readonly ReinforceSystem Reinforce;
            internal readonly ComponentType<Position> Positions;
            private readonly ComponentType<Collider> colliders;
            private static readonly (ulong key, int team, bool reserve)[] Units = { (1, 1, false), (2, 1, false), (11, 1, true), (12, 1, true), (4, 2, false) };

            internal World()
            {
                CombatPack pack = RespawnWorld.Combat();
                int n = Units.Length;
                var content = new StableContentManifest(1);
                var entries = new List<PlacedCatalogEntry>();
                foreach (var u in Units) entries.Add(new PlacedCatalogEntry(1, u.key));
                var schema = new GameplaySchema(
                    new[] { new GameplayResourceDef(1, "health", 0, pack.MaxHealthRaw, pack.MaxHealthRaw, isLife: true) },
                    new[] { new GameplayDamageTypeDef(1, "hit", 1, 1) },
                    new[] { new GameplayPipelineDef(1, new GameplayDamageSpec(0, GameplayDamageStages.None)) },
                    new[] { new GameplayArchetypeDef(1, new[] { 1UL }) },
                    new GameplayCapacities(n, n, n, 2 * n), "combat.critical",
                    lifeRules: new[] { new GameplayLifeRuleDef(1, 1, true, DownedTicks) });
                Sim = Simulation.CreateRetainedWorld(41, new PlacedCatalog(content, entries.ToArray()), pack.BuildConfig(n, n), content: content, gameplaySchema: schema);
                Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
                colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
                var combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
                Combat = new CombatSystem(Sim, Positions, combatants, pack, 0, 1, n);
                var match = new MatchPack(new[] { new ControlPointDef(5, SimVec3.Metres(40, 0, 20), SimFix.FromInt(4)) },
                    new[] { new SpawnAnchor(1, SimVec3.Metres(6, 0, 20)), new SpawnAnchor(2, SimVec3.Metres(74, 0, 20)) }, 2, 1, false, 8000, InterruptMode.Reset, 1,
                    30, 1000000, 100000, SimFix.FromInt(35), 2, 2, SimFix.FromInt(2), reentryTicks: Reentry);
                var capture = new CaptureSystem(Combat, match);
                Reinforce = new ReinforceSystem(Combat, match, capture, new Deployer(this), sim: Sim);
                Sim.RegisterGameplay("battle.reinforce", Reinforce, SimSystemPhase.Requests);
                Sim.RegisterGameplay("test.hit", new HitAt(this, Hit, 2), SimSystemPhase.Queries);
                Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
                Sim.RegisterGameplay("battle.capture", capture, SimSystemPhase.Queries);
                Sim.ConfigureSpatial(Positions, colliders, SimFix.FromInt(2));
                for (int i = 0; i < n; i++)
                {
                    EntityId id = Sim.Entities.Create(StableEntityKey.Placed(1, Units[i].key));
                    if (!Units[i].reserve) Place(id, SimVec3.Metres(10 + 4 * i, 0, 36));
                    Sim.BindGameplay(id, 1);
                    Sim.Entities.Set(combatants, id, new Combatant(Units[i].team, -1, 0, 0));
                }
                Sim.CompleteSpatialBoundary();
            }
            internal void Place(EntityId unit, SimVec3 at)
            {
                Sim.Entities.Set(Positions, unit, new Position(at));
                Sim.Entities.Set(colliders, unit, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
            }
            internal EntityId Unit(ulong key) => Sim.Entities.Resolve(new EntityLink(StableEntityKey.Placed(1, key).Id));
            internal GameplayLifeView Life(ulong key) => Sim.GameplayLife(Sim.Entities.StableOf(Unit(key)));
            internal bool Fielded(ulong key) => Sim.Entities.TryGet(Positions, Unit(key), out _);
        }

        private sealed class Deployer : IUnitDeployer
        {
            private readonly World world;
            internal Deployer(World world) { this.world = world; }
            public ulong PolicyKey => 7;
            public bool TryDeploy(EntityId unit, SimVec3 at) { world.Place(unit, at); return true; }
        }

        // Takes the unit's whole health at one tick (Queries, order 27).
        private sealed class HitAt : IRetainedGameplaySystem
        {
            private readonly World world;
            private readonly long tick;
            private readonly ulong key;
            internal HitAt(World world, long tick, ulong key) { this.world = world; this.tick = tick; this.key = key; }
            public int Order => 27;
            public string StateTypeId => "test.hit";
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                if (c.Clock.Tick != tick) return;
                StableId id = world.Sim.Entities.StableOf(world.Unit(key));
                c.Damage(world.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, 0), 1, RespawnWorld.Combat().MaxHealthRaw);
            }
            public void SaveState(SimStateWriter w) { w.Int64(tick); w.UInt64(key); }
            public void LoadState(SimStateReader r) { SimStateReader.Require(r.Int64() == tick && r.UInt64() == key, "hit"); }
            public void WriteState(StateHash h) { h.Add(tick); h.Add(key); }
        }

        private static void Go(World w) => Assert.DoesNotThrow(() => w.Sim.Step(default));

        [Test]
        public void A7_ADownedUnitIsNotOwed_UntilItDies()
        {
            var w = new World();
            while (w.Sim.Clock.Tick < Hit) Go(w);
            Assert.That((w.Life(2).State, w.Life(2).EnteredTick), Is.EqualTo((GameplayLifeState.Downed, Hit)), "downed at the hit");
            long died = Hit + DownedTicks;
            while (w.Sim.Clock.Tick < died - 1)
            {
                Go(w);
                Assert.That((w.Life(2).State, w.Reinforce.Due(1), w.Reinforce.Entered(1), w.Reinforce.Owed(1)), Is.EqualTo((GameplayLifeState.Downed, 0L, 0L, 0L)),
                    "downed, not owed, tick " + w.Sim.Clock.Tick);
            }
            Assert.That(w.Sim.Clock.Tick, Is.GreaterThan(Hit + Reentry), "the wait of a death has passed while it was down");
            Go(w);
            Assert.That((w.Life(2).State, w.Life(2).EnteredTick), Is.EqualTo((GameplayLifeState.Dead, died)), "dead when the downed time ran out");
            while (w.Sim.Clock.Tick < died + Reentry - 1)
            {
                Go(w);
                Assert.That((w.Reinforce.Due(1), w.Reinforce.Entered(1)), Is.EqualTo((0L, 0L)), "counted from the death, tick " + w.Sim.Clock.Tick);
            }
            Go(w);
            Assert.That((w.Sim.Clock.Tick, w.Reinforce.Due(1), w.Reinforce.Entered(1), w.Reinforce.Owed(1)), Is.EqualTo((died + Reentry, 1L, 1L, 0L)));
            Assert.That((w.Fielded(11) ? 1 : 0) + (w.Fielded(12) ? 1 : 0), Is.EqualTo(1), "one reserve entered");
            Assert.That(w.Reinforce.Entered(2), Is.Zero);
        }
    }
}
