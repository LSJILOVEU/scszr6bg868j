using System;
using System.Collections.Generic;
using NUnit.Framework;
using WarCore.Content;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // L10 (recorded first, on the base): flat worlds with a controlled player keep every value when the terrain step for the
    // player is added. All fixtures are frozen test literals.
    public sealed class L10FlatLockTests
    {
        // The six-entry demo script of L10, copied here as literals (coordinates unchanged).
        internal static readonly (long AtTick, PlayerInput Input)[] DemoScript =
        {
            (1, new PlayerInput(1000, 0)),
            (100, new PlayerInput(0, 0, 30000, 16500, primary: true)),
            (160, new PlayerInput(0, 1000, 30000, 16500)),
            (220, new PlayerInput(-1000, 0, 0, 21500)),
            (320, new PlayerInput(0, -1000, 8500, 0)),
            (400, new PlayerInput(0, 0, 40000, 16500, primary: true)),
        };

        private static readonly (long AtTick, PlayerInput Input)[] F5Script =
        {
            (1, new PlayerInput(500, 0, 20000, 15000)),
            (10, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (25, new PlayerInput(500, 0, 20000, 15000, block: true, @throw: true)),
            (26, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (30, new PlayerInput(500, 0, 20000, 15000, block: true, skillSlot: 2)),
            (36, new PlayerInput(500, 0, 20000, 15000, block: true)),
            (41, new PlayerInput(500, 0, 20000, 15000))
        };

        private static Dictionary<long, string> Run((long AtTick, PlayerInput Input)[] script, long[] at)
        {
            var w = F5World.World();
            foreach (var item in script) w.Session.SetPlayerInput(item.AtTick, item.Input);
            var seen = new Dictionary<long, string>();
            long last = at[at.Length - 1];
            for (long tick = 1; tick <= last; tick++)
            {
                w.Session.StepOnce();
                if (Array.IndexOf(at, tick) < 0) continue;
                ulong key = w.Session.ControlledKey ?? 0;
                SimVec3 p = M3TestWorld.Position(w.Session, key);
                seen[tick] = w.Session.StateHash.ToString("X16") + "|" + key + "|" + p.X.Raw + "," + p.Y.Raw + "," + p.Z.Raw;
            }
            return seen;
        }

        [Test]
        public void L10Flat_F5ScriptAtTick60()
        {
            Assert.That(Run(F5Script, new long[] { 60 })[60], Is.EqualTo(F5At60));
        }

        [Test]
        public void L10Flat_DemoScriptOnTheF5World()
        {
            Dictionary<long, string> seen = Run(DemoScript, new long[] { 150, 300, 450 });
            Assert.That(seen[150], Is.EqualTo(DemoAt150));
            Assert.That(seen[300], Is.EqualTo(DemoAt300));
            Assert.That(seen[450], Is.EqualTo(DemoAt450));
        }

        // Diagonal into the corner of a 1 m block on flat ground: the whole step is refused, then X only or Z only slides.
        [Test]
        public void L10Flat_SlideAlongABlock()
        {
            var w = new M6World(Recipe(new[] { U(1, 8600, 8600), U(2, 70000, 35000, team: 2, squad: 2) }),
                WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 10, 1, 3))));
            var trail = new List<string>();
            for (int tick = 1; tick <= 30; tick++)
            {
                w.Sim.Step(Input(1, 1));
                SimVec3 p = Point(w);
                trail.Add(p.X.Raw + "," + p.Z.Raw);
            }
            Assert.That(string.Join(";", trail), Is.EqualTo(SlideTrail));
        }

        // The same block with a frozen recipe (L10Fixture: literal match, no weapons; nothing read from production content).
        // From the south-west the corner refuses the whole step and X only slides; from the west face X is refused and Z only
        // slides. Each trail is recorded on the base; both slide kinds are counted so the trail cannot lose one unnoticed.
        [TestCase(8600, 8600, "X")]
        [TestCase(8600, 10600, "Z")]
        public void L10Flat_FrozenSlide(long xMm, long zMm, string axis)
        {
            UnitSpawn[] units = { L10Fixture.U(1, xMm, zMm), L10Fixture.U(2, 70000, 35000, team: 2) };
            var w = new M6World(M6World.RecipeOf(units, L10Fixture.QuietMatch(), L10Fixture.Off()).WithPlayer(1),
                WcMapWriter.Write(M3TestWorld.Boxes(80, 40, (10, 10, 1, 3))));
            var trail = new List<string>();
            int xOnly = 0, zOnly = 0;
            SimVec3 before = Point(w);
            for (int tick = 1; tick <= 30; tick++)
            {
                w.Sim.Step(Input(1, 1));
                SimVec3 p = Point(w);
                bool dx = p.X.Raw != before.X.Raw, dz = p.Z.Raw != before.Z.Raw;
                if (dx && !dz) xOnly++;
                if (dz && !dx) zOnly++;
                trail.Add(p.X.Raw + "," + p.Z.Raw);
                before = p;
            }
            Assert.That((xOnly, zOnly), Is.EqualTo(axis == "X" ? (10, 0) : (0, 10)), "X-only and Z-only steps");
            Assert.That(string.Join(";", trail), Is.EqualTo(axis == "X" ? FrozenTrailX : FrozenTrailZ));
        }

        private const string FrozenTrailX = SlideTrail; // equal to the trail above, recorded on the base with this frozen recipe
        private const string FrozenTrailZ =
            "572876,703948;582143,713215;591410,722482;600677,731749;609944,741016;619211,750283;628478,759550;637745,768817;637745,778084;637745,787351;637745,796618;637745,805885;637745,815152;637745,824419;637745,833686;637745,842953;637745,852220;637745,861487;647012,870754;656279,880021;665546,889288;674813,898555;684080,907822;693347,917089;702614,926356;711881,935623;721148,944890;730415,954157;739682,963424;748949,972691";

        private const string F5At60 = "2986499F0453B1F8|1|753655,0,655360";
        private const string DemoAt150 = "82603C921ABF193D|1|747109,0,655360";
        private const string DemoAt300 = "98AFAE2282E05A3B|1|26224,0,1441780";
        private const string DemoAt450 = "0928ECF095E96318|1|26224,0,393220";
        private const string SlideTrail =
            "572876,572876;582143,582143;591410,591410;600677,600677;609944,609944;619211,619211;628478,628478;637745,637745;647012,637745;656279,637745;665546,637745;674813,637745;684080,637745;693347,637745;702614,637745;711881,637745;721148,637745;730415,637745;739682,647012;748949,656279;758216,665546;767483,674813;776750,684080;786017,693347;795284,702614;804551,711881;813818,721148;823085,730415;832352,739682;841619,748949";
    }
}
