using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Presentation;
using WarCore.Sim;

namespace WarCore.Tests
{
    // NT-1 A2: recorded on the base code (main 60b3c8f) without any NT-1 production change. A two-team world keeps every tick hash, its snapshot bytes
    // and the reinforcement state version 1.
    // RC1-K5a: the playtest values were recorded again when the playtest layer's rifle spread changed (content only, no
    // code); the capture world, which reads no playtest layer, keeps the first values. RC2-3: recorded again when the
    // playtest layer took the commander (dispatch "commander"); the capture world keeps its values. RC2-3c: recorded
    // again when the commander's R5 ties went to the target picked first; the capture world keeps its values.
    public sealed class Nt1TwoTeamLockTests
    {
        private const ulong PlaytestT150 = 0x0C870BC5784DC33BUL, PlaytestT300 = 0xB799A57324935B49UL, PlaytestT600 = 0x0C44A847C612353CUL;
        private const string PlaytestSnapshotT300 = "13b211d71e751f5d4d5eb09e405221d4f489d93e24dd8ca5fbfa3975e83ffc63";
        private const ulong CaptureT150 = 0xBDAEFF0FA8F64974UL, CaptureT300 = 0x91AC4582DEF52DD1UL, CaptureT600 = 0xE770C2B73EB0A92DUL;
        private const string CaptureSnapshotT300 = "2bee54d4111a8c1ddb0554913243dbda0d95ab4acfe116f1063a896f67fc9dde";

        [Test]
        public void Nt1TwoTeams_PlaytestKeepsHashesAndSnapshotBytes()
        {
            // The playtest recipe without M7b's dash (the dash adds state, so the recorded values predate it).
            WarCore.Content.ContentPack layer = WarCore.Content.Rwr.RwrPack.Load(DemoRecipes.PlaytestLayer);
            var w = new M6World(DemoRecipes.MpRespawnDemo(layer).WithPack(layer), DemoRecipes.M7MapBytes());
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
