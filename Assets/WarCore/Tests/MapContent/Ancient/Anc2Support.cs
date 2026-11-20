using System;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // ANC-2: the ancient cartridge plus one layer (spear, spearman) that lives next to the tests, not in the cartridge folder.
    // A layer file in Content/Ancient/Data would be listed by the editor catalog and change a locked catalog test.
    internal static class Anc2
    {
        internal const string FieldDirectory = "Assets/WarCore/Tests/MapContent/Ancient/Data";
        internal const string ShippedDirectory = "Assets/WarCore/Content/Ancient/Data";

        // Reads a layer from the test folder first, then from the shipped cartridge. edit (layer id, text) -> text changes the
        // text in memory before it is parsed; the files on disk are never touched.
        private sealed class Source : IContentSource
        {
            private readonly IContentSource field = new FileContentSource(FieldDirectory), shipped = new FileContentSource(ShippedDirectory);
            private readonly Func<string, string, string> edit;
            internal Source(Func<string, string, string> edit) { this.edit = edit; }
            public bool TryRead(string layerId, out string text, out string location)
            {
                if (!field.TryRead(layerId, out text, out location) && !shipped.TryRead(layerId, out text, out location)) return false;
                if (edit != null) text = edit(layerId, text);
                return true;
            }
        }

        internal static ContentPack Load(Func<string, string, string> edit = null) => ContentLoader.Load(new Source(edit), "ancient-field");

        // Replaces one piece of text in one layer; fails loudly when the piece is not there, so a changed file never turns an
        // edit into a silent no-op.
        internal static Func<string, string, string> Replace(string layer, string from, string to) => (id, text) =>
        {
            if (id != layer) return text;
            if (!text.Contains(from)) throw new InvalidOperationException("layer " + layer + " does not contain the text to replace");
            return text.Replace(from, to);
        };

        // PLACEHOLDER fixture values: 96 x 64 m flat ground, three points on the middle line, 60 against 60 in six columns
        // (front to back: shield, shield, spear, spear, sword, sword), seed 41, no reserves.
        internal static M6World World(ContentPack pack, ulong seed = 41)
        {
            var units = new UnitSpawn[120];
            string[] rank = { "ancient.guard", "ancient.guard", "ancient.spearman", "ancient.spearman", "ancient.swordsman", "ancient.swordsman" };
            for (int i = 0; i < 60; i++)
            {
                int column = i % 6, row = i / 6;
                long z = 22000 + 2000 * row;
                units[i] = UnitSpawn.OfType((ulong)i + 1, pack, rank[column], 1, 1, 30000 - 1500 * column, z);
                units[i + 60] = UnitSpawn.OfType((ulong)i + 61, pack, rank[column], 2, 2, 66000 + 1500 * column, z);
            }
            var points = new[]
            {
                new ControlPointDef(1, SimVec3.Metres(48, 0, 12), SimFix.FromInt(4)),
                new ControlPointDef(2, SimVec3.Metres(48, 0, 32), SimFix.FromInt(4)),
                new ControlPointDef(3, SimVec3.Metres(48, 0, 52), SimFix.FromInt(4)),
            };
            var anchors = new[] { new SpawnAnchor(1, SimVec3.Metres(4, 0, 32)), new SpawnAnchor(2, SimVec3.Metres(92, 0, 32)) };
            return Build(pack, units, points, anchors, seed);
        }

        internal static M6World Build(ContentPack pack, UnitSpawn[] units, ControlPointDef[] points, SpawnAnchor[] anchors, ulong seed = 41)
        {
            var recipe = new WorldRecipe(seed, MoverKind.InterimCrowd, units, Array.Empty<BridgeCommand>())
                .WithCombat(pack.BuildCombat()).WithMatch(pack.BuildMatch(points, anchors))
                .WithCrowdPass(CrowdPassKind.TeamIdle).WithTerrainRules(pack).WithFacing().WithBlockAi();
            byte[] map = WcMapWriter.Write(Wp23.Make(96, 64, 0, 4 * SimFix.Scale, Array.Empty<(int, int, int, int)>(),
                2 * SimFix.Scale, (x, z) => 0, new[] { "ancient.ground" }, (x, z) => 0));
            return new M6World(recipe, map);
        }

        // What one run produced. A strike is told apart by its damage: sword 34 (8.5 behind a shield), spear 40 (10 behind a shield).
        internal sealed class Tally
        {
            internal int SwordHits, SpearHits, SwordBlocked, SpearBlocked;
            internal int Hits => SwordHits + SpearHits;
            internal int Blocked => SwordBlocked + SpearBlocked;

            internal void Add(CombatSystem combat)
            {
                foreach (StrikeRecord s in combat.Strikes)
                {
                    if (s.Kind != StrikeKind.Landed) continue;
                    if (s.DamageRaw == 34 * SimFix.Scale) SwordHits++;
                    else if (s.DamageRaw == 40 * SimFix.Scale) SpearHits++;
                    else if (s.DamageRaw == SimFix.Ratio(17, 2).Raw) SwordBlocked++;
                    else if (s.DamageRaw == 10 * SimFix.Scale) SpearBlocked++;
                }
            }
        }

        internal static int Deaths(M6World w, int team)
        {
            int count = 0;
            foreach (UnitSpawn unit in w.Recipe.Units)
                if (unit.Team == team && w.Dead(unit.Key)) count++;
            return count;
        }
    }
}
