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
        private readonly HeightField ground;
        // ML-9: the map's floor plates, or null (no plates: every plate branch below is skipped and nothing changes).
        private readonly PlateField plates;
        // ML-3 body heights above the foot, in raw units, fixed at construction from the pack's body section (D4 of the
        // design): legacy values when the pack has none, so every formula below gives the bytes it gave before.
        private readonly bool bodied;
        private readonly long bodyHeightRaw;
        // eye: the viewpoint for sight, cover, explicit target shots and the melee wall line (legacy 0.5 m).
        // muzzle: the start of aimed and automatic shots (legacy 0, the foot).
        // aimEnd: how far above a ground point or a target's foot the aimed and automatic lines end (legacy 0, new = eye).
        // groundLift: the extra lift of a shot line tested against the ground (legacy 0.5 m, new 0: the line is the bullet's).
        // targetOrigin: the recorded start of an explicit target shot (legacy foot + 0.5 m, new = muzzle; never merged with muzzle).
        // center: the lift from a foot to its body centre for unit-layer queries (legacy 0, new floor(height / 2)).
        private readonly long eyeRaw, muzzleRaw, aimEndRaw, groundLiftRaw, targetOriginRaw, centerRaw;

        public CombatSystem(Simulation sim, ComponentType<Position> positions, ComponentType<Combatant> combatants,
            CombatPack pack, uint wallMask, uint unitLayerMask, int unitCount, IControlTarget controlled = null, HeightField ground = null,
            PlateField plates = null)
        {
            this.sim = sim; this.positions = positions; this.combatants = combatants; this.pack = pack;
            this.wallMask = wallMask; this.unitLayerMask = unitLayerMask;
            ValidateBody(sim.FootAnchoredLayers, pack.Body, wallMask, unitLayerMask);
            ValidatePlateBody(plates, pack.Body);
            bodied = pack.Body.HasValue;
            if (bodied)
            {
                CombatBody body = pack.Body.Value;
                bodyHeightRaw = body.Height.Raw;
                eyeRaw = body.Eye.Raw; muzzleRaw = body.Muzzle.Raw; aimEndRaw = body.Eye.Raw;
                groundLiftRaw = 0; targetOriginRaw = body.Muzzle.Raw; centerRaw = body.Height.Raw / 2;
            }
            else
            {
                eyeRaw = LegacyEyeRaw; muzzleRaw = 0; aimEndRaw = 0;
                groundLiftRaw = LegacyEyeRaw; targetOriginRaw = LegacyEyeRaw; centerRaw = 0;
            }
            stage = new EntityStage(sim.Entities);
            candidates = new SpatialHit[unitCount]; fires = new FireRecord[unitCount];
            hitStream = sim.Rng.Stream("combat.hit");
            spreadStream = sim.Rng.Stream("combat.spread");
            allyStream = sim.Rng.Stream("combat.ally-check");
            builtInAutoFire = autoFire = new DefaultAutoFire(sim, allyStream);
            colliders = Entities.Resolve<Collider>();
            liveUnits = new LiveUnit[Entities.Capacity];
            this.controlled = controlled;
            this.ground = ground;
            this.plates = plates;
            intents = new FireIntent[Entities.Capacity];
            intentTicks = new long[Entities.Capacity];
            intentSlots = new int[candidates.Length];
            InitMelee();
            InitBlock();
            InitAim();
        }

        private const long LegacyEyeRaw = SimFix.Scale / 2;

        // STANCE-1: the stances of the units, or null (a pack without stances: every height below is the pack's).
        private StanceSystem stances;
        public StanceSystem Stances => stances;

        // Bound once by the StanceSystem constructor; a restored world binds a fresh one when it is assembled.
        internal void AttachStances(StanceSystem system)
        {
            if (!pack.HasStances) throw new InvalidOperationException("The combat pack has no stances.");
            if (stances != null) throw new InvalidOperationException("A stance system is already attached.");
            stances = system;
        }

        // D2a: the pack's body is the only source. The spatial index must anchor the unit layer at the foot exactly when the
        // pack has a body (a Sim with no index yet counts as 0), and never a wall layer. A restored world rebuilds this system,
        // so the check runs again.
        private static void ValidateBody(uint footAnchored, CombatBody? body, uint wallMask, uint unitLayerMask)
        {
            if (body.HasValue && (footAnchored & unitLayerMask) != unitLayerMask)
                throw new InvalidOperationException("The combat pack has a body but the spatial index does not anchor the unit layer at the foot.");
            if (!body.HasValue && (footAnchored & unitLayerMask) != 0)
                throw new InvalidOperationException("The spatial index anchors the unit layer at the foot but the combat pack has no body.");
            if ((footAnchored & wallMask) != 0)
                throw new InvalidOperationException("The spatial index anchors a wall layer at the foot.");
        }

        // ML-9 (K11): a unit on a plate stands at the floored top, which on a slope can lie up to 1 raw inside the plate body.
        // With a body every line starts and ends at eye or muzzle height (the aim end is the eye), so both must be at least
        // 1 raw for every line to begin strictly above the continuous top. Without a body every line is already 0.5 m up.
        private static void ValidatePlateBody(PlateField plates, CombatBody? body)
        {
            if (plates != null && body.HasValue && (body.Value.Eye.Raw < 1 || body.Value.Muzzle.Raw < 1))
                throw new InvalidOperationException("A map with floor plates needs a combat body whose eye and muzzle are at least 1 raw above the foot.");
        }

        // ML-9: the map has floor plates.
        public bool HasPlates => plates != null;

        // ML-9 (K2): the distance used to pick, reach and weigh targets. With floor plates it is the 3D distance, otherwise the
        // horizontal one, as before.
        public SimWide SpanSquared(SimVec3 from, SimVec3 to) => plates != null ? (to - from).LengthSquared : Horizontal(to - from).LengthSquared;
        public SimFix Span(SimVec3 from, SimVec3 to) => plates != null ? (to - from).Length : Horizontal(to - from).Length;

        // ML-9: a target or point straight above or below may be shot along the vertical only on a map with plates and only when
        // the two feet are at different heights; elsewhere the old zero-direction rules stand.
        public bool Overhead(SimVec3 foot, SimVec3 other) => plates != null && foot.Y.Raw != other.Y.Raw;

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
                    if (bodied) RequireBodyHeight(shooter);
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
                    SimVec3 origin = Lift(position.Value, MuzzleOf(shooter)), direction;
                    if (hasIntent)
                    {
                        direction = Lift(intents[shooter.Id].Point, aimEndRaw) - origin;
                        if (Horizontal(direction).LengthSquared.Sign == 0
                            && !(Overhead(position.Value, intents[shooter.Id].Point) && direction.LengthSquared.Sign != 0)) continue;
                        // Positions are final here (after movement): an intent that avoids allies is declined for this tick
                        // when a living teammate is first on the line, with no shot, no ammo and no cooldown change.
                        if (intents[shooter.Id].AvoidAllies && AllyBlocks(shooter, origin, direction, weapon.FalloffEnd)) continue;
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
                    // AIM-1: the turn uses the spread line's angle; the weapon's spread state below keeps the weapon's angle.
                    int shot = ShotAngle(shooter, in unit, angle, tick);
                    if (shot > 0) direction = Rotate(direction, shot, sim.Rng.NextInt(spreadStream, 65536));
                    if (pack.IsProjectile(unit.Weapon)) LaunchRay(shooter, in unit, in weapon, tick, origin, direction, angle);
                    else FireRay(c, shooter, in unit, in position, in weapon, tick, origin, direction, angle);
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
