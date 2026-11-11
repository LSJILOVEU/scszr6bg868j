using System;
using System.Linq;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class M7BehaviorLockTests
    {
        internal static byte[] NarrowMap() => WcMapWriter.Write(M3TestWorld.Boxes(80, 40,
            (28, 0, 1, 19), (28, 21, 1, 19), (51, 0, 1, 19), (51, 21, 1, 19)));
        internal static WorldRecipe Demo(ulong seed)
        {
            var d = DemoRecipes.M6();
            return new WorldRecipe(seed, d.Mover, d.Units.ToArray(), d.Script.ToArray()).WithCombat(d.Combat).WithMatch(d.Match).WithCrowdPass(d.CrowdPass);
        }
        internal static M6World Real(ulong seed = 41, bool endless = false)
        {
            var units = new UnitSpawn[200];
            for (int i = 0; i < units.Length; i++)
            {
                int slot = i % 100, team = i < 100 ? 1 : 2;
                long x = 1000 + slot % 10 * 750;
                if (team == 2) x = 80000 - x;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, 80250 + slot / 10 * 1500, 6000, team, team - 1);
            }
            var recipe = M6World.RecipeOf(units, RwrMatch.CreateM6(), RwrPack.CreateM6(), seed);
            return new M6World(endless ? M6World.Endless(recipe) : recipe, RealMap());
        }
        internal static byte[] RealMap() => WcMapWriter.Write(M3TestWorld.Boxes(80, 112));
        internal static string Record(bool narrow, ulong seed)
        {
            var w = new M6World(Demo(seed), narrow ? NarrowMap() : DemoRecipes.M6MapBytes());
            ulong h300 = 0;
            while (w.Match.Winner == 0 && w.Sim.Clock.Tick < 9000)
            { w.Step(); if (w.Sim.Clock.Tick == 300) h300 = w.Hash; }
            return $"{narrow},{seed},{w.Match.EndTick},{w.Match.Winner},{(int)w.Match.EndReason},{h300:X16},{w.Hash:X16}";
        }
        internal static string RecordReal(ulong seed)
        {
            // Columns: end tick, winner, hash at 300, hash at 600, hash at 900, hash at the end tick. The real match ends at 722..748, so
            // tick 900 only exists in the twin world whose rule never ends the match (D7 keeps the 900-tick window); 300/600 and the end tick
            // are read from the real world.
            var w = Real(seed); ulong h300 = 0, h600 = 0;
            for (int tick = 1; !w.Sim.IsFinished && tick <= 900; tick++)
            { w.Step(); if (tick == 300) h300 = w.Hash; if (tick == 600) h600 = w.Hash; }
            var endless = Real(seed, endless: true);
            for (int tick = 1; tick <= 900; tick++) endless.Step();
            return $"real,{seed},{w.Match.EndTick},{w.Match.Winner},{h300:X16},{h600:X16},{endless.Hash:X16},{w.Hash:X16}";
        }
        // Behaviour baseline (not a determinism lock): first recorded at fa5c23c, re-recorded by WP-17 (R), WP-19 (A0) and WP-21 (H1: guard slots, pass-through, engaged window).
        private static readonly string[] Golden = {
            "False,41,1174,1,1,14CBFE729A28AD4C,332F6CE935438FB2",
            "False,42,1214,1,1,82454C24B9750B5B,09C1554C9965686B",
            "False,43,1213,1,1,40A0105312ADEF82,0D51BADC703FADAE",
            "False,44,1741,2,1,E600BCE9B836C51E,E748B9C0B4F2A155",
            "False,45,1860,1,1,F90156D142BCE18B,703515CD276D3984",
            "False,46,1420,1,1,5F3B83681371249C,E1F5D61A29210E8F",
            "False,47,1229,1,1,4F410C6F60A82C60,187AFCE0131561C0",
            "False,48,2100,2,1,063D7788133D2723,F8123AB8D67C8432",
            "False,49,1194,1,1,909CB8BD179E43C6,C0C8DD3EFEF87DAC",
            "False,50,1189,2,1,7BC3BAC680F17625,95B2C6045C95C38C",
            "False,51,1211,2,1,1B30FACBCF856C52,4B8E3BD065F871EB",
            "False,52,1229,2,1,27D5B7D0261A17C1,2821B8C3B3F1F957",
            "False,53,1273,2,1,9F400445E6E19C38,ADC2FBB883B8B529",
            "False,54,1791,1,1,06E42369220DA0CE,6713DF70D30BE215",
            "False,55,1747,1,1,95BE3D6B3ECDC933,1ABB35CE7E92D1B3",
            "False,56,1176,1,1,641AF6719F760920,54F7231FC0EB0A8D",
            "False,57,2701,1,2,69C1984FB3C08F6B,19E238794713970B",
            "False,58,1442,1,1,0B3020AD882F07CE,3C35F3DC53042B8A",
            "False,59,1193,2,1,DDFCFDA3A6AB4CFF,DDFFD4ED379ABAC5",
            "False,60,1207,2,1,75566843F8E8D9E6,98D9626CBAF72DF5",
            "True,41,2100,2,1,A9B9D11E94A2227D,A4D86113DCE9DB0C",
            "True,42,1561,2,1,9947E1B97072681E,C9E71E654ABD6ECF",
            "True,43,2618,2,1,A752A84B5147C1B8,E1DC450D92A43C94",
            "True,44,1230,2,1,35C09342035DDA2C,F5297FCE5E42D136",
            "True,45,1993,1,1,F3DD73BE203422DE,17D36CF80D23968F",
            "True,46,1740,1,1,838ECBD9939FFB7A,0A75FEE895A51CAA",
            "True,47,1202,2,1,B33E6F4FF0D77D0D,B75D1CAAA50EA3EB",
            "True,48,1211,1,1,6B3989D648CBF473,11B3259D39399502",
            "True,49,1218,1,1,EE6EF90F4E069240,6C80719CEFC6F15B",
            "True,50,1200,2,1,4DF79054BCE1202E,939FD5123AD7CB29",
            "True,51,1203,2,1,B103432329358F5F,70630BFA2A12B9E0",
            "True,52,2134,2,1,E6AE51434FEF8F64,05657DCC56402679",
            "True,53,2720,2,2,F5C7EBE0B96A9F28,16B3A978321049A0",
            "True,54,1740,2,1,6FCBE7A9CDC46414,A463CA5F2C2A6C4E",
            "True,55,1715,2,1,7C9327F24AF781B9,0CC922AF4E06A3C4",
            "True,56,2701,2,2,C63748EC91E52547,D6AD886972940E6C",
            "True,57,1380,2,1,AC58CB67388F302F,CD9CD2462453A81E",
            "True,58,1258,2,1,B0B2241C3AE66FA9,9151B6050B254A25",
            "True,59,1271,1,1,4217B3543FAECB0A,2407048FFFF254B0",
            "True,60,1198,2,1,2BA90D4A6E9BC711,795BF2D29AC226AB",
            "real,41,756,2,11951392F9288951,105738F94D1F649A,2817C8BB57C1C47A,C943A44DAD2E934F",
            "real,42,756,2,2C1D7100BC81C11F,B2123F2959A096E4,5D607D0A6C2E2FC6,F7CF4BEFAC33CE8E",
            "real,43,741,1,5F239765FCDA3135,B8DEE2834EAF7CA6,F7224D912083B912,AA679DF6E50281D4",
        };
        [TestCaseSource(nameof(Golden))]
        public void Fa5c23cBehaviorIsBitExact(string expected)
        {
            string[] f = expected.Split(','); ulong seed = ulong.Parse(f[1]);
            Assert.That(f[0] == "real" ? RecordReal(seed) : Record(bool.Parse(f[0]), seed), Is.EqualTo(expected));
        }
        [Category("Manual"), Test, Explicit("Record only when the dispatch authorises a re-record; list old and new values in the PR, never regenerate to pass a gate.")]
        public void RecordFa5c23c()
        {
            foreach (bool narrow in new[] { false, true })
                for (ulong seed = 41; seed <= 60; seed++) TestContext.Out.WriteLine("LOCK " + Record(narrow, seed));
            for (ulong seed = 41; seed <= 43; seed++) TestContext.Out.WriteLine("LOCK " + RecordReal(seed));
        }
    }
}
