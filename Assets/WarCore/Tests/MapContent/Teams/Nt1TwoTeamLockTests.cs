using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A2: recorded on the base code before any NT-1 change. A two-team world keeps every tick hash, its snapshot bytes
    // and the reinforcement state version 1.
    public sealed class Nt1TwoTeamLockTests
    {
        private const ulong PlaytestT150 = 0x3F35FD88E4C439EEUL, PlaytestT300 = 0xA7FDA5A0F969E4F6UL, PlaytestT600 = 0xF9E97436D9EEEA3EUL;
        private const string PlaytestSnapshotT300 = "97b8c1485ec0c2adfe84b030ac2513327bbb99c852b037ff7be2fe08e95ecbe5";
        private const ulong CaptureT150 = 0xBDAEFF0FA8F64974UL, CaptureT300 = 0x91AC4582DEF52DD1UL, CaptureT600 = 0xE770C2B73EB0A92DUL;
        private const string CaptureSnapshotT300 = "2bee54d4111a8c1ddb0554913243dbda0d95ab4acfe116f1063a896f67fc9dde";

        [Test]
        public void Nt1TwoTeams_PlaytestKeepsHashesAndSnapshotBytes()
        {
            var w = new M6World(DemoRecipes.MpPlaytest(), DemoRecipes.M7MapBytes());
            Check(w, "playtest", PlaytestT150, PlaytestT300, PlaytestT600, PlaytestSnapshotT300);
        }

        // Two points, reserves on both sides: points change hands and reserves deploy inside the 600 ticks.
        [Test]
        public void Nt1TwoTeams_CaptureAndReinforceWorldKeepsHashesAndSnapshotBytes()
        {
            var w = new M6World(CaptureRecipe());
            Check(w, "capture", CaptureT150, CaptureT300, CaptureT600, CaptureSnapshotT300);
        }

        internal static WorldRecipe CaptureRecipe()
        {
            var points = new[] { new ControlPointDef(1, M6World.P(20), SimFix.FromInt(6), 0, true, M6World.P(22)),
                new ControlPointDef(2, M6World.P(60), SimFix.FromInt(6), 2) };
            var units = new[] { M6World.U(1, 1), M6World.U(2, 2, 60), M6World.U(3, 1, 5), M6World.U(8, 2, 70),
                M6World.U(4, 1, reserve: true, speed: 6000), M6World.U(5, 1, reserve: true, speed: 6000),
                M6World.U(6, 2, reserve: true, speed: 6000), M6World.U(7, 2, reserve: true, speed: 6000) };
            return M6World.Endless(M6World.RecipeOf(units, M6World.Pack(points, ticks: 10, interval: 5, think: 1)));
        }

        private static void Check(M6World w, string name, ulong t150, ulong t300, ulong t600, string snapshot300)
        {
            Assert.That(w.Reinforce.StateVersion, Is.EqualTo(1));
            ulong h150 = 0, h300 = 0, h600 = 0; string s300 = null; int captures = 0, deploys = 0;
            for (int tick = 1; tick <= 600; tick++)
            {
                w.Step();
                captures += w.Capture.Changes.Length; deploys += w.Reinforce.Deployed.Length;
                if (tick == 150) h150 = w.Hash;
                if (tick == 300) { h300 = w.Hash; s300 = Sha(w.Sim.SaveRetainedSnapshot()); }
                if (tick == 600) h600 = w.Hash;
            }
            TestContext.Progress.WriteLine($"NT1 {name} t150=0x{h150:X16}UL t300=0x{h300:X16}UL t600=0x{h600:X16}UL snap300={s300} captures={captures} deploys={deploys}");
            Assert.That((h150, h300, h600), Is.EqualTo((t150, t300, t600)));
            Assert.That(s300, Is.EqualTo(snapshot300));
            Assert.That(w.Reinforce.StateVersion, Is.EqualTo(1));
        }

        private static string Sha(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var text = new System.Text.StringBuilder(64);
                foreach (byte b in digest) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }
    }
}
