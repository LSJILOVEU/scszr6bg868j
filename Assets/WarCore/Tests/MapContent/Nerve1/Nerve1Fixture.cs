using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests.Nerve1
{
    // Fixtures of the NERVE-1 tests. Seed 41. Weapon angle 2000 (two degrees) unless a test says otherwise; no stance, no
    // move spread, so the status drags are the only factors of the spread line unless a test adds fixed ones.
    internal static class Nerve1Fixture
    {
        internal const long S = SimFix.Scale;
        internal const int Angle2000 = 2000;

        // A source of pressure levels the test sets per unit (a stand-in for the pressure system).
        internal sealed class Levels : INerveSource
        {
            private readonly Dictionary<ulong, int> levels = new Dictionary<ulong, int>();
            internal int Calls;
            internal void Set(StableId unit, int level) => levels[unit.Value] = level;
            public int NerveLevel(StableId unit) { Calls++; return levels.TryGetValue(unit.Value, out int level) ? level : 0; }
        }

        // A source that answers one fixed number for every unit (the out-of-range tests).
        internal sealed class Always : INerveSource
        {
            private readonly int level;
            internal Always(int level) { this.level = level; }
            public int NerveLevel(StableId unit) => level;
        }

        // The shipped weapons with their spread, the zero resolver (shots hurt nobody), health 100 unless given.
        internal static CombatPack Pack(CombatStatus status, int floor = 0, int ceil = 0, long maxHealthRaw = 100 * S) =>
            new CombatPack(RwrPack.Create().Weapons.ToArray(), new ZeroResolver(), new NearestVisibleEnemy(), maxHealthRaw,
                status: status, spreadDragFloorPermille: floor, spreadDragCeilPermille: ceil);

        internal static CombatStatus Pinned(int p = 1400) => new CombatStatus(p, 0, 0, 0);
        internal static CombatStatus Broken(int b = 1800) => new CombatStatus(0, b, 0, 0);
        internal static CombatStatus Nerve(int p = 1400, int b = 1800) => new CombatStatus(p, b, 0, 0);
        internal static CombatStatus Wounded(int below = 500, int w = 1250) => new CombatStatus(0, 0, below, w);
        internal static CombatStatus All(int p = 1400, int b = 1800, int below = 500, int w = 1250) => new CombatStatus(p, b, below, w);

        // Four shooters of team 1 (indices 0..3, keys 1..4, x = 0, z = 0, 2, 4, 6 m) and one unarmed target of team 2
        // (index 4, 20 m away; of team 1 too when enemy is false, so that nobody fires). Unit 0 is the player's when controlled.
        internal static Wp13TestWorld World(CombatPack pack, bool controlled = false, bool enemy = true) => new Wp13TestWorld(new[]
        {
            M5TestWorld.Spawn(1, 0, 0), M5TestWorld.Spawn(2, 0, 2000), M5TestWorld.Spawn(3, 0, 4000), M5TestWorld.Spawn(4, 0, 6000),
            M5TestWorld.Spawn(5, 20000, 3000, team: enemy ? 2 : 1, weapon: -1, squad: 2),
        }, pack, controlled: controlled);

        internal static int Angle(Wp13TestWorld w, int i, int weapon = Angle2000) => w.Combat.ShotAngle(w.Unit(i), w.State(i), weapon, w.Sim.Clock.Tick);

        // Deals the given raw damage to unit i on the next step (Wp13's After hook, Queries order 31).
        internal static void HurtNextStep(Wp13TestWorld w, params (int Index, long Raw)[] hurts)
        {
            long tick = w.Sim.Clock.Tick + 1;
            w.After = c =>
            {
                if (c.Clock.Tick != tick) return;
                foreach (var (index, raw) in hurts)
                {
                    StableId id = w.Id(index);
                    c.Damage(w.Sim.GameplayTarget(id), new GameplayApplyKey(id, (ulong)tick, 0, id, (ulong)tick, (uint)index), 1, raw);
                }
            };
        }

        internal static long Health(Wp13TestWorld w, int i) => w.Combat.HealthRaw(w.Id(i));

        // A fixed factor for the spread line.
        internal sealed class Fixed : ISpreadFactor
        {
            private readonly int permille;
            internal Fixed(int permille) { this.permille = permille; }
            public int SpreadPermille(EntityId unit) => permille;
        }

        // Content layers over the shipped ones.
        internal static ContentPack Load(string id, params (string Id, string Text)[] layers)
        {
            var map = new Dictionary<string, string>();
            foreach (var layer in layers) map[layer.Id] = layer.Text;
            return ContentLoader.Load(new MemoryContentSource(map, FileContentSource.Rwr()), id);
        }

        internal static (string, string) Layer(string id, string extends, string body) =>
            (id, "{ \"id\": \"" + id + "\", \"extends\": \"" + extends + "\"" + (body.Length == 0 ? "" : ", " + body) + " }");

        // No damage: the "damage" resolver with an effect of 0.
        internal const string Harmless = "\"weapons\": [ { \"id\": \"rwr.weapon-1\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-2\", \"effectRaw\": 0 }, "
            + "{ \"id\": \"rwr.weapon-3\", \"effectRaw\": 0 } ]";

        // (Id, order) of every registered system, and the type of every registered component, in registration order.
        internal static List<string> Systems(WarCore.Sim.Sim sim)
        {
            var list = new List<string>();
            var systems = (IEnumerable)typeof(WarCore.Sim.Sim).GetField("systems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim);
            foreach (object entry in systems)
            {
                Type type = entry.GetType();
                var id = (string)type.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                var system = (ISimSystem)type.GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                list.Add(id + "@" + system.Order);
            }
            return list;
        }

        internal static List<string> Components(WarCore.Sim.Sim sim)
        {
            var list = new List<string>();
            var storages = (IEnumerable)typeof(EntityStore).GetField("storages", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(sim.Entities);
            foreach (object storage in storages) list.Add(storage.GetType().GetGenericArguments()[0].FullName);
            return list;
        }

        internal static Pressure PressureOf(M6World w) => w.System<Pressure>();
    }
}
