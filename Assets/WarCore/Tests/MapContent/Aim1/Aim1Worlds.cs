using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;
using WarCore.Sim;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests.Aim1
{
    // Fixtures of the AIM-1 tests. Seed 41, crowd mover, a flat walled 100 x 60 m map (walls 4 m high), the shipped layers
    // rwr-field and rwr-aim, and test layers on top of them. Weapons of the rwr packs: 0 = rwr.weapon-1, 1 = rwr.weapon-2,
    // 2 = rwr.weapon-3. One tick = 1/30 s; 1 m = 65536 raw.
    internal static class Aim1Worlds
    {
        internal const long S = SimFix.Scale;
        // Walking speed 3000 mm/s = 6553 raw per tick (band 500: threshold 1092), running 6000 mm/s = 13107 raw (band 4000: 8738).
        internal const long Walk = 3000, Run = 6000;

        // No damage: the "damage" resolver with an effect of 0, so every unit lives and keeps shooting.
        private const string Harmless = "\"combat\": { \"resolver\": \"damage\" }, \"weapons\": [ "
            + "{ \"id\": \"rwr.weapon-1\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-2\", \"effectRaw\": 0 }, { \"id\": \"rwr.weapon-3\", \"effectRaw\": 0 } ]";

        internal static readonly (string Id, string Text)[] TestLayers =
        {
            ("aim1-harmless", "{ \"id\": \"aim1-harmless\", \"extends\": \"rwr-aim\", " + Harmless + " }"),
            ("aim1-harmless-field", "{ \"id\": \"aim1-harmless-field\", \"extends\": \"rwr-field\", " + Harmless + " }"),
            // rwr-aim with a table of its own for rwr.weapon-3.
            ("aim1-by-weapon", "{ \"id\": \"aim1-by-weapon\", \"extends\": \"aim1-harmless\", \"combat\": { \"moveSpread\": { "
                + "\"bands\": [ { \"aboveMmPerSec\": 500, \"permille\": 1500 }, { \"aboveMmPerSec\": 4000, \"permille\": 3000 } ], "
                + "\"byWeapon\": { \"rwr.weapon-3\": [ { \"aboveMmPerSec\": 500, \"permille\": 2500 }, { \"aboveMmPerSec\": 4000, \"permille\": 4000 } ] } } } }"),
            // rwr.weapon-1 with a fixed spread of 2 thousandths of a degree (base = max = 2, no growth).
            ("aim1-tiny", "{ \"id\": \"aim1-tiny\", \"extends\": \"aim1-harmless\", \"weapons\": [ "
                + "{ \"id\": \"rwr.weapon-1\", \"spreadBase\": 2, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 2 } ] }"),
            ("aim1-tiny-none", "{ \"id\": \"aim1-tiny-none\", \"extends\": \"aim1-harmless\", \"weapons\": [ "
                + "{ \"id\": \"rwr.weapon-1\", \"spreadBase\": 0, \"spreadGrowth\": 0, \"spreadRecoverPerSec\": 0, \"spreadMax\": 0 } ] }"),
            // rwr-field with only the manual reload.
            ("aim1-reload-only", "{ \"id\": \"aim1-reload-only\", \"extends\": \"aim1-harmless-field\", \"combat\": { \"manualReload\": true } }"),
            // rwr-aim without the manual reload.
            ("aim1-no-reload", "{ \"id\": \"aim1-no-reload\", \"extends\": \"aim1-harmless\", \"combat\": { \"manualReload\": false } }"),
        };

        internal static ContentPack Load(string id, params (string Id, string Text)[] more)
        {
            var layers = new Dictionary<string, string>();
            foreach (var layer in TestLayers) layers[layer.Id] = layer.Text;
            foreach (var layer in more) layers[layer.Id] = layer.Text;
            return ContentLoader.Load(new MemoryContentSource(layers, FileContentSource.Rwr()), id);
        }

        internal static byte[] Map() => WcMapWriter.Write(Wp23.Make(100, 60, 0, 6 * S, null, 4 * S));

        internal static UnitSpawn Unit(ulong key, int squad, int team, long xMm, long zMm, long speed = Run, int weapon = 0) =>
            new UnitSpawn(key, 0, squad, xMm, zMm, speed, team, weapon);

        internal static SimSession World(ContentPack pack, UnitSpawn[] units, BridgeCommand[] script = null) =>
            WorldBuilder.Build(new WorldRecipe(41, MoverKind.InterimCrowd, units, script ?? Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()), Map(), false);

        internal static Simulation SimOf(SimSession s) => M3TestWorld.Sim(s);
        internal static EntityId Id(SimSession s, ulong key) => M3TestWorld.Unit(s, key);
        internal static CombatSystem CombatOf(SimSession s) =>
            (CombatSystem)typeof(SimSession).GetField("combat", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s);
        internal static Combatant State(SimSession s, ulong key) => CombatOf(s).Unit(Id(s, key));
        internal static void SetState(SimSession s, ulong key, Combatant value) =>
            SimOf(s).Entities.Set(SimOf(s).Entities.Resolve<Combatant>(), Id(s, key), value);

        // (id, order) of every registered system, in run order.
        internal static List<(string Id, int Order)> Systems(SimSession s)
        {
            var list = new List<(string, int)>();
            var systems = (IEnumerable)typeof(Simulation).GetField("systems", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(SimOf(s));
            foreach (object entry in systems)
            {
                Type type = entry.GetType();
                var id = (string)type.GetField("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                var system = (ISimSystem)type.GetField("System", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(entry);
                list.Add((id, system.Order));
            }
            return list;
        }

        internal static ulong RngHash(SimSession s) => Wp13TestWorld.Hash(SimOf(s).Rng);

        // A gear or drag that answers one fixed permille for every unit.
        internal sealed class Fixed : ISpreadFactor
        {
            private readonly int permille;
            internal Fixed(int permille) { this.permille = permille; }
            public int SpreadPermille(EntityId unit) => permille;
        }

        // An action limit that denies the given actions to every unit.
        internal sealed class Deny : IActionLimit
        {
            private readonly UnitAction denied;
            internal Deny(UnitAction denied) { this.denied = denied; }
            public UnitAction Denied(EntityId unit) => denied;
        }
    }
}
