using System;
using System.Collections.Generic;
using WarCore.Content;
using WarCore.Combat;
using WarCore.Battle;

namespace WarCore.Presentation
{
    public enum MoverKind { Interim = 1, InterimPath = 2, InterimCrowd = 3 }

    // Which crowd pass rule a crowd world uses. None keeps the movement layer exactly as it was.
    public enum CrowdPassKind { None = 0, TeamIdle = 1 }

    public readonly struct UnitSpawn
    {
        public ulong Key { get; }
        public int Archetype { get; }
        public int Squad { get; }
        public int Team { get; }
        public int Weapon { get; }
        public bool Reserve { get; }
        // Index into CombatPack.Melee, or -1 for a unit without a melee spec.
        public int Melee { get; }
        public long XMm { get; }
        public long ZMm { get; }
        public long SpeedMmPerSec { get; }
        // Index into CombatPack.Block, or -1 for a unit that cannot block.
        public int Block { get; }

        // Placeholder bound: ten metres per tick at 30 ticks per second. A step this long is still swept in full by the movers.
        public const long MaxSpeedMmPerSec = ContentPack.MaxUnitSpeedMmPerSec;

        public static UnitSpawn OfType(ulong key, ContentPack pack, string unitTypeId, int squad, int team, long xMm, long zMm, bool reserve = false)
        {
            var type = pack.SpawnOfUnitType(unitTypeId);
            return new UnitSpawn(key, type.Archetype, squad, xMm, zMm, type.SpeedMmPerSec, team, type.Weapon, reserve, type.Melee, type.Block);
        }

        public UnitSpawn(ulong key, int archetype, int squad, long xMm, long zMm, long speedMmPerSec, int team = 0, int weapon = -1, bool reserve = false, int melee = -1, int block = -1)
        {
            if (melee < -1) throw new ArgumentOutOfRangeException(nameof(melee));
            if (block < -1) throw new ArgumentOutOfRangeException(nameof(block));
            Block = block;
            Melee = melee;
            if (speedMmPerSec <= 0)
                throw new ArgumentOutOfRangeException(nameof(speedMmPerSec));
            if (speedMmPerSec > MaxSpeedMmPerSec)
                throw new ArgumentOutOfRangeException(nameof(speedMmPerSec), "Unit " + key + " field speedMmPerSec is " + speedMmPerSec
                    + ", above the " + MaxSpeedMmPerSec + " that the movers sweep.");
            Key = key;
            Team = team;
            Weapon = weapon;
            Reserve = reserve;
            Archetype = archetype;
            Squad = squad;
            XMm = xMm;
            ZMm = zMm;
            SpeedMmPerSec = speedMmPerSec;
        }
    }

    public sealed class WorldRecipe
    {
        private readonly UnitSpawn[] units;
        private readonly BridgeCommand[] script;
        private readonly List<IWorldOption> options = new List<IWorldOption>();

        public CombatPack Combat { get; private set; }
        public MatchPack Match { get; private set; }
        public ulong? PlayerKey { get; private set; }
        public CrowdPassKind CrowdPass { get; private set; }
        // Opens the per-unit facing component. Off by default: a world without it is byte-identical to one built before it existed.
        public bool Facing => HasOption(WorldOptions.Facing.OptionId);
        // Opens the per-unit motion-limits component, same rule: off by default, and a world without it is unchanged.
        public bool Motion => HasOption(WorldOptions.Motion.OptionId);
        // Opens the per-unit standing-order component (hold, follow, move in progress), same rule: off by default, and a world without it is unchanged.
        public bool Standing => HasOption(WorldOptions.Standing.OptionId);
        // Adds the block AI (units raise a block against incoming swings, by their block spec's chance). Needs block specs and facing.
        public bool BlockAi { get; private set; }
        // The rules a map with terrain is derived under; null for flat maps. Not part of the recipe's seed or rule-set id.
        public ContentPack TerrainPack { get; private set; }
        public ContentPack AiPack { get; private set; }
        // Opaque rule-set identity: hashed from tick 0 and checked on restore. Never derived from the rule contents.
        public string RuleSetId { get; private set; } = MatchMode.RwrRuleSetId;
        public ulong Seed { get; }
        public MoverKind Mover { get; }
        public ReadOnlySpan<UnitSpawn> Units => units;
        public ReadOnlySpan<BridgeCommand> Script => script;
        // The switches this recipe carries, in the order they were added. Features read them when the world is assembled.
        internal IReadOnlyList<IWorldOption> Options => options;

        public WorldRecipe(ulong seed, MoverKind mover, UnitSpawn[] units, BridgeCommand[] script)
        {
            if (units == null)
                throw new ArgumentNullException(nameof(units));
            if (script == null)
                throw new ArgumentNullException(nameof(script));
            Seed = seed;
            Mover = mover;
            this.units = (UnitSpawn[])units.Clone();
            this.script = (BridgeCommand[])script.Clone();
            Array.Sort(this.units, (a, b) => a.Key.CompareTo(b.Key));
            for (int i = 1; i < this.units.Length; i++)
                if (this.units[i - 1].Key == this.units[i].Key)
                    throw new ArgumentException("Unit keys must be unique.", nameof(units));
        }

        private WorldRecipe Copy()
        {
            var copy = new WorldRecipe(Seed, Mover, units, script);
            copy.Combat = Combat; copy.Match = Match; copy.PlayerKey = PlayerKey; copy.RuleSetId = RuleSetId; copy.CrowdPass = CrowdPass; copy.TerrainPack = TerrainPack; copy.BlockAi = BlockAi; copy.options.AddRange(options);
            copy.AiPack = AiPack;
            return copy;
        }

        public WorldRecipe WithPlayer(ulong unitKey)
        {
            var copy = Copy(); copy.PlayerKey = unitKey;
            return copy;
        }

        public WorldRecipe WithMatch(MatchPack match)
        {
            var copy = Copy(); copy.Match = match;
            return copy;
        }

        // A method, not a constructor overload: the Unity assembly sees only Presentation and calls the constructor,
        // so no constructor may name a Combat type.
        public WorldRecipe WithCombat(CombatPack combat)
        {
            var copy = Copy(); copy.Combat = combat;
            return copy;
        }

        public WorldRecipe WithRuleSet(string ruleSetId)
        {
            if (string.IsNullOrEmpty(ruleSetId)) throw new ArgumentException("A rule set id is required.", nameof(ruleSetId));
            var copy = Copy(); copy.RuleSetId = ruleSetId;
            return copy;
        }

        // A map with a height field or surface table needs the pack whose terrain rules it is run under.
        public WorldRecipe WithTerrainRules(ContentPack pack)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            var copy = Copy(); copy.TerrainPack = pack;
            return copy;
        }

        // Flat maps opt in; terrain maps keep using their terrain pack's AI rules.
        public WorldRecipe WithAiRules(ContentPack pack)
        {
            var copy = Copy(); copy.AiPack = pack;
            return copy;
        }

        // Turns one world feature on. A switch is identified by its id, so adding it again changes nothing.
        public WorldRecipe With(IWorldOption option)
        {
            if (option == null) throw new ArgumentNullException(nameof(option));
            if (string.IsNullOrEmpty(option.OptionId)) throw new ArgumentException("An option id is required.", nameof(option));
            var copy = Copy();
            if (!HasOption(option.OptionId)) copy.options.Add(option);
            return copy;
        }

        private bool HasOption(string id)
        {
            foreach (IWorldOption option in options)
                if (option.OptionId == id) return true;
            return false;
        }

        public WorldRecipe WithFacing() => With(WorldOptions.Facing);

        public WorldRecipe WithMotion() => With(WorldOptions.Motion);

        public WorldRecipe WithBlockAi()
        {
            var copy = Copy(); copy.BlockAi = true;
            return copy;
        }

        public WorldRecipe WithStandingOrders() => With(WorldOptions.Standing);

        public WorldRecipe WithCrowdPass(CrowdPassKind kind)
        {
            if (!Enum.IsDefined(typeof(CrowdPassKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            var copy = Copy(); copy.CrowdPass = kind;
            return copy;
        }
    }
}
