using System;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Combat
{
    public sealed partial class CombatSystem : IRetainedGameplaySystem
    {
        private readonly Simulation sim;
        private readonly EntityStage stage;
        private readonly ComponentType<Position> positions;
        private readonly ComponentType<Combatant> combatants;
        private readonly CombatPack pack;
        private readonly uint wallMask, unitLayerMask;
        private readonly SpatialHit[] candidates;
        private readonly FireRecord[] fires;
        private readonly RandomStreamId hitStream, spreadStream, allyStream;
        private int fireCount;
        private readonly IControlTarget controlled;
        // Entity slots include walls; only the number of pending shooters is capped by unitCount.
        private readonly FireIntent[] intents;
        private readonly long[] intentTicks;
        private readonly int[] intentSlots;
        private int intentCount;
        private long intentTick = -1, lastStepTick = -1;
        private readonly ComponentType<Collider> colliders;
        private readonly LiveUnit[] liveUnits;
        private int liveCount;
        private bool cacheValid, exclusionDisabled, inStep;
        public int QueriesThisStep { get; private set; }
        private readonly IAutoFire builtInAutoFire;
        private IAutoFire autoFire;

        public CombatSystem(Simulation sim, ComponentType<Position> positions, ComponentType<Combatant> combatants,
            CombatPack pack, uint wallMask, uint unitLayerMask, int unitCount, IControlTarget controlled = null)
        {
            this.sim = sim; this.positions = positions; this.combatants = combatants; this.pack = pack;
            this.wallMask = wallMask; this.unitLayerMask = unitLayerMask;
            stage = new EntityStage(sim.Entities);
            candidates = new SpatialHit[unitCount]; fires = new FireRecord[unitCount];
            hitStream = sim.Rng.Stream("combat.hit");
            spreadStream = sim.Rng.Stream("combat.spread");
            allyStream = sim.Rng.Stream("combat.ally-check");
            builtInAutoFire = autoFire = new DefaultAutoFire(sim, allyStream);
            colliders = Entities.Resolve<Collider>();
            liveUnits = new LiveUnit[Entities.Capacity];
            this.controlled = controlled;
            intents = new FireIntent[Entities.Capacity];
            intentTicks = new long[Entities.Capacity];
            intentSlots = new int[candidates.Length];
            InitMelee();
            InitBlock();
        }

        public EntityStore Entities => sim.Entities;
        public int Order => 30;
        public string StateTypeId => "combat.system";
        public int StateVersion => 1;
        public ReadOnlySpan<FireRecord> Fires => fires.AsSpan(0, fireCount);

        // The fire decision for units nobody orders to shoot. The built-in default is used until a game layer swaps it; null restores it.
        public IAutoFire BuiltInAutoFire => builtInAutoFire;
        public void UseAutoFire(IAutoFire autoFire) => this.autoFire = autoFire ?? builtInAutoFire;
        public CombatPack Pack => pack;
        private bool IsPlayer(EntityId entity) => controlled != null && controlled.Current.Equals(entity);

        public void Submit(SimContext c, in FireIntent intent)
        {
            if (c == null || !ReferenceEquals(c, sim.Context))
                throw new InvalidOperationException("Fire intent requires its owning simulation context.");
            // RequireActive is internal to Sim. An empty batch uses its public active-tick guard
            // without publishing events, changing sequence numbers or allocating memory.
            c.EmitStableBatch(Span<StableProducedEvent>.Empty);
            long tick = c.Clock.Tick;
            if (lastStepTick == tick || inStep)
                throw new InvalidOperationException("Fire intent must precede this tick's combat step.");
            if (intent.Kind != FireIntentKind.Aimed && intent.Kind != FireIntentKind.AtTarget)
                throw new ArgumentException("Unknown fire intent kind.", nameof(intent));
            if (!Entities.TryResolve(intent.Shooter, out EntityRef shooter) || IsDead((EntityId)shooter))
                throw new ArgumentException("Fire intent shooter must be alive.", nameof(intent));
            if (intent.Kind == FireIntentKind.AtTarget
                && (!Entities.TryResolve(intent.Target, out EntityRef target) || IsDead((EntityId)target)))
                throw new ArgumentException("Fire intent target must be alive.", nameof(intent));
            int slot = ((EntityId)shooter).Id;
            if ((uint)slot >= (uint)intents.Length)
                throw new InvalidOperationException("Combat entity capacity exceeded.");
            if (intentTick != tick) { ClearIntents(); intentTick = tick; }
            if (intents[slot].Kind == 0)
            {
                if (intentCount == candidates.Length)
                    throw new InvalidOperationException("Combat intent capacity exceeded.");
                intentSlots[intentCount++] = slot;
            }
            intents[slot] = intent;
            intentTicks[slot] = tick;
        }

        private void ClearIntents()
        {
            for (int i = 0; i < intentCount; i++)
            {
                int slot = intentSlots[i];
                intents[slot] = default;
                intentTicks[slot] = 0;
                intentSlots[i] = 0;
            }
            intentCount = 0;
            intentTick = -1;
        }

        public void Step(SimContext c)
        {
            QueriesThisStep = 0; cacheValid = false; inStep = true;
            lastStepTick = c.Clock.Tick;
            try
            {
                BuildLiveCache();
                long tick = c.Clock.Tick;
                fireCount = 0; strikeCount = 0;
                foreach (EntityId shooter in stage.Capture())
                {
                    if (!Entities.TryGet(combatants, shooter, out Combatant unit)
                        || !Entities.TryGet(positions, shooter, out Position position)) continue;
                    if (IsDead(shooter))
                    {
                        if (pack.HasMelee) CancelMelee(shooter);
                        continue;
                    }
                    bool hasIntent = intentCount != 0 && (uint)shooter.Id < (uint)intents.Length && intents[shooter.Id].Kind != 0
                        && intentTicks[shooter.Id] == tick && intents[shooter.Id].Shooter.Equals(Entities.StableOf(shooter));
                    bool autoUnit = !hasIntent && !IsPlayer(shooter);
                    if (autoUnit) autoFire.Sense(this, shooter, in unit, in position, tick);
                    // A unit that carries melee may spend this tick on a swing; nothing below runs for it then.
                    if (pack.HasMelee && MeleeStep(c, shooter, in unit, in position, tick, hasIntent)) continue;
                    if (unit.Weapon < 0 || unit.DueMilli > tick * 1000) continue;
                    WeaponDef weapon = pack.Weapons[unit.Weapon];
                    if (hasIntent && intents[shooter.Id].Kind == FireIntentKind.AtTarget)
                    {
                        FireAtTarget(c, shooter, in unit, in position, in weapon, tick, in intents[shooter.Id]);
                        continue;
                    }
                    SimVec3 origin = position.Value, direction;
                    if (hasIntent)
                    {
                        direction = Horizontal(intents[shooter.Id].Point - origin);
                        if (direction.LengthSquared.Sign == 0) continue;
                    }
                    else
                    {
                        if (!autoUnit) continue;
                        AutoFireAction action = autoFire.TryAim(this, shooter, in unit, in position, in weapon, tick, out direction);
                        if (action == AutoFireAction.HoldFire)
                        {
                            Entities.Set(combatants, shooter, new Combatant(unit.Team, unit.Weapon, unit.Ammo,
                                tick * 1000 + pack.AllyHoldMilli, unit.SpreadStored, unit.SpreadLastMilli));
                            continue;
                        }
                        if (action != AutoFireAction.Fire) continue;
                    }
                    int angle = weapon.Spread.AngleAt(unit.SpreadStored, unit.SpreadLastMilli, tick * 1000);
                    if (angle > 0) direction = Rotate(direction, angle, sim.Rng.NextInt(spreadStream, 65536));
                    FireRay(c, shooter, in unit, in position, in weapon, tick, origin, direction, angle);
                }
            }
            finally { cacheValid = false; inStep = false; ClearIntents(); }
        }

        public void SaveState(SimStateWriter writer)
        { writer.UInt64(pack.PolicyDigest); writer.UInt64(wallMask); writer.UInt64(unitLayerMask); if (controlled != null) writer.Bool(true); }
        public void LoadState(SimStateReader reader)
        {
            cacheValid = false; ClearIntents(); lastStepTick = sim.Clock.Tick;
            if (reader.UInt64() != pack.PolicyDigest || reader.UInt64() != wallMask || reader.UInt64() != unitLayerMask)
                throw new InvalidOperationException("Combat policy or layer masks differ from the saved configuration.");
            if (controlled != null && !reader.Bool())
                throw new InvalidOperationException("Combat control mode differs from the saved configuration.");
            fireCount = 0; strikeCount = 0;
        }
        public void WriteState(StateHash hash)
        { hash.Add(StateTypeId); hash.Add(StateVersion); hash.Add(pack.PolicyDigest); hash.Add((ulong)wallMask); hash.Add((ulong)unitLayerMask); if (controlled != null) hash.Add(true); }
    }
}
