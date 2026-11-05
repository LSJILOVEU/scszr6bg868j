using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    // Independent world: exactly one CombatSystem, with an optional control target.
    internal sealed class Wp13TestWorld
    {
        internal const int Spread = 0;
        internal readonly Simulation Sim;
        internal readonly CombatSystem Combat;
        internal readonly CombatPack Pack;
        internal readonly Wp13Control Control;
        internal readonly ComponentType<Position> Positions;
        internal readonly ComponentType<Collider> Colliders;
        internal readonly ComponentType<Combatant> Combatants;
        internal readonly int Count;
        internal readonly uint WallMask;
        internal Action<SimContext> Produce, Requests, Move, After;

        internal Wp13TestWorld(UnitSpawn[] units = null, CombatPack pack = null, bool reverse = false,
            WcMap map = null, bool controlled = true, int? capacity = null, int spread = Spread)
        {
            if (spread != 0) throw new ArgumentOutOfRangeException(nameof(spread));
            units = units ?? M5TestWorld.Pair();
            Pack = pack ?? M5TestWorld.Pack(new ZeroResolver()); Count = units.Length;
            var content = new StableContentManifest(1);
            var entries = new List<PlacedCatalogEntry>();
            if (map != null) foreach (var wall in map.Walls)
            { entries.Add(new PlacedCatalogEntry(wall.Chunk, wall.Placement)); WallMask |= map.LayerMask(wall.LayerName); }
            foreach (var unit in units) entries.Add(new PlacedCatalogEntry(1, unit.Key));
            Sim = Simulation.CreateRetainedWorld(41, new PlacedCatalog(content, entries.ToArray()),
                Pack.BuildConfig(entries.Count, Count), content: content, gameplaySchema: Pack.BuildSchema(Count));
            Positions = Sim.Entities.Register<Position>("interim.position", new PositionSerializer());
            Colliders = Sim.Entities.Register<Collider>("interim.collider", new ColliderSerializer());
            Combatants = Sim.Entities.Register<Combatant>(Combatant.ComponentId, new CombatantSerializer());
            Control = new Wp13Control(Sim);
            Combat = new CombatSystem(Sim, Positions, Combatants, Pack, WallMask, 1, capacity ?? Count, controlled ? Control : null);
            Sim.RegisterGameplay("combat.system", Combat, SimSystemPhase.Queries);
            Sim.RegisterGameplay("wp13.producer", new Script(this, 29), SimSystemPhase.Queries);
            Sim.RegisterGameplay("wp13.requests", new Script(this, 10), SimSystemPhase.Requests);
            Sim.RegisterGameplay("wp13.move", new Script(this, 20), SimSystemPhase.Movement);
            Sim.RegisterGameplay("wp13.after", new Script(this, 31), SimSystemPhase.Queries);
            Sim.ConfigureSpatial(Positions, Colliders, SimFix.FromInt(2));
            if (map != null) for (int i = 0; i < map.Walls.Count; i++)
            {
                var wall = map.Walls[reverse ? map.Walls.Count - i - 1 : i];
                var id = Sim.Entities.Create(wall.Key);
                Sim.Entities.Set(Positions, id, new Position(new SimVec3(map.Min.X, map.FlatHeight, map.Min.Z) + wall.LocalCenter));
                Sim.Entities.Set(Colliders, id, Collider.Box(wall.HalfX, wall.Height, wall.HalfZ,
                    BoxYaw.Identity, layer: map.LayerMask(wall.LayerName), isStatic: true));
            }
            for (int i = 0; i < Count; i++)
            {
                var unit = units[reverse ? Count - i - 1 : i];
                var id = Sim.Entities.Create(StableEntityKey.Placed(1, unit.Key));
                Sim.Entities.Set(Positions, id, new Position(M3TestWorld.Point(unit.XMm, unit.ZMm)));
                Sim.Entities.Set(Colliders, id, new Collider(SimFix.Ratio(1, 4), SimFix.FromInt(1)));
                Sim.BindGameplay(id, 1);
                Sim.Entities.Set(Combatants, id, new Combatant(unit.Team, unit.Weapon,
                    unit.Weapon < 0 ? 0 : Pack.Weapons[unit.Weapon].Magazine, 0));
            }
            Control.Selected = Id(0);
            Sim.CompleteSpatialBoundary();
        }

        internal EntityId Unit(int i) => Sim.Entities.Resolve(new EntityLink(Id(i)));
        internal StableId Id(int i) => StableEntityKey.Placed(1, (ulong)i + 1).Id;
        internal Combatant State(int i) => Sim.Entities.Read(Combatants, Unit(i));
        internal SimVec3 Point(int i) => Sim.Entities.Read(Positions, Unit(i)).Value;
        internal bool Dead(int i) => Combat.IsDead(Unit(i));
        internal void Step() => Sim.Step(default);
        internal void Aim(SimContext c, int shooter, SimVec3 point) => Combat.Submit(c, FireIntent.Aimed(Id(shooter), point));
        internal void Target(SimContext c, int shooter, int target) => Combat.Submit(c, FireIntent.AtTarget(Id(shooter), Id(target)));
        internal void Hold(bool hit = true) => Produce = c => Aim(c, 0, hit ? Point(1) : Point(0) + SimVec3.Metres(0, 0, 1));
        internal void SetDue(int i, long due)
        { var u = State(i); Sim.Entities.Set(Combatants, Unit(i), new Combatant(u.Team, u.Weapon, u.Ammo, due)); }
        internal int EventCount(EventTypeId type)
        { int count = 0; foreach (var e in Sim.Events.Read(Sim.Clock.Tick)) if (e.Type.Equals(type)) count++; return count; }
        internal static ulong Hash(IStateHash value) { var h = new StateHash(); value.WriteState(h); return h.Value; }
        // Auto casts a ray from the shooter's position; an explicit AtTarget intent starts at chest height. Only the
        // Auto-versus-intent comparison (autoVsIntent) may ignore the two endpoints; there the origin must still agree horizontally.
        internal static void EqualCombat(Wp13TestWorld a, Wp13TestWorld b, bool autoVsIntent = false)
        {
            if (!autoVsIntent) Assert.That(b.Combat.Fires.ToArray(), Is.EqualTo(a.Combat.Fires.ToArray()), "all FireRecord fields");
            else
            {
                var x = a.Combat.Fires; var y = b.Combat.Fires;
                Assert.That(y.Length, Is.EqualTo(x.Length));
                for (int i = 0; i < x.Length; i++)
                {
                    Assert.That((y[i].Shooter, y[i].Target, y[i].Distance, y[i].DamageRaw, y[i].ShooterTeam, y[i].Outcome),
                        Is.EqualTo((x[i].Shooter, x[i].Target, x[i].Distance, x[i].DamageRaw, x[i].ShooterTeam, x[i].Outcome)));
                    Assert.That(y[i].Origin.X, Is.EqualTo(x[i].Origin.X)); Assert.That(y[i].Origin.Z, Is.EqualTo(x[i].Origin.Z));
                }
            }
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(b.State(i), Is.EqualTo(a.State(i)), "all Combatant fields");
                Assert.That(b.Sim.GameplayResource(b.Id(i), 1), Is.EqualTo(a.Sim.GameplayResource(a.Id(i), 1)));
                Assert.That(b.Sim.GameplayLife(b.Id(i)), Is.EqualTo(a.Sim.GameplayLife(a.Id(i))));
            }
            Assert.That(Hash(b.Sim.Rng), Is.EqualTo(Hash(a.Sim.Rng)), "RNG position");
            var ae = a.Sim.Events.Read(a.Sim.Clock.Tick); var be = b.Sim.Events.Read(b.Sim.Clock.Tick);
            Assert.That(be.Length, Is.EqualTo(ae.Length));
            for (int i = 0; i < ae.Length; i++)
            {
                var x = a.Sim.ReadStableEvent(a.Sim.Clock.Tick, i); var y = b.Sim.ReadStableEvent(b.Sim.Clock.Tick, i);
                Assert.That(y.Payload, Is.EqualTo(x.Payload)); Assert.That(y.Source, Is.EqualTo(x.Source)); Assert.That(y.Target, Is.EqualTo(x.Target));
            }
            Assert.That(b.Sim.Events.NextSequence, Is.EqualTo(a.Sim.Events.NextSequence));
            Assert.That(b.Sim.Events.EventsDroppedTotal, Is.EqualTo(a.Sim.Events.EventsDroppedTotal));
        }

        private sealed class Script : IRetainedGameplaySystem
        {
            private readonly Wp13TestWorld world;
            public int Order { get; }
            internal Script(Wp13TestWorld world, int order) { this.world = world; Order = order; }
            public string StateTypeId => "wp13.script." + Order;
            public int StateVersion => 1;
            public void Step(SimContext c)
            {
                if (Order == 29) world.Produce?.Invoke(c);
                else if (Order == 10) world.Requests?.Invoke(c);
                else if (Order == 20) world.Move?.Invoke(c);
                else world.After?.Invoke(c);
            }
            public void SaveState(SimStateWriter w) { if (Order == 29) w.UInt64(world.Control.Selected.Value); }
            public void LoadState(SimStateReader r) { if (Order == 29) world.Control.Selected = new StableId(r.UInt64()); }
            public void WriteState(StateHash h) { if (Order == 29) h.Add(world.Control.Selected.Value); }
        }
    }

    internal sealed class Wp13Control : IControlTarget
    {
        private readonly Simulation sim;
        internal StableId Selected;
        internal Wp13Control(Simulation sim) { this.sim = sim; }
        // Resolve each read, so reverse creation and restore epochs cannot leave a stale local handle.
        public EntityId Current => sim.Entities.TryResolve(Selected, out EntityRef id) ? (EntityId)id : default;
        public void Switch(SimContext c, EntityId target)
        {
            if (!ReferenceEquals(c, sim.Context)) throw new InvalidOperationException("Wrong simulation.");
            c.EmitStableBatch(Span<StableProducedEvent>.Empty);
            if (target.Equals(default(EntityId))) { Selected = default; return; }
            if (!sim.Entities.IsAlive(target)) throw new ArgumentException("Control target must be alive.");
            Selected = sim.Entities.StableOf(target);
        }
    }

    internal sealed class Wp13SelectorSpy : ITargetSelector
    {
        internal int Calls;
        private readonly ITargetSelector inner = new NearestVisibleEnemy();
        public ulong PolicyKey => inner.PolicyKey;
        public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target)
        { Calls++; return inner.TrySelect(world, shooter, range, out target); }
    }
}
