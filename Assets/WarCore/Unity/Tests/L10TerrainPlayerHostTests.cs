using System;
using NUnit.Framework;
using UnityEngine;
using WarCore.Presentation;

namespace WarCore.Unity.Tests
{
    // L10: the production terrain-player recipe passes TerrainHost.SelfCheck, the HUD names the player and its height, and
    // IsShotPrefix keeps every output a flat file starting with L10.
    public sealed class L10TerrainPlayerHostTests
    {
        private static SimSession Demo() => SimSession.Create(TerrainPlayerDemo.Recipe(), TerrainBake.DemoMapBytes());

        [Test]
        public void L10_ProductionRecipe_SelfCheckIsClean()
        {
            SimSession session = Demo();
            GameObject map = MapView.Build(session.Map, null, null), parent = new GameObject("L10 points"), units = new GameObject("L10 units");
            try
            {
                new PointViews(parent.transform, ViewGround.For(session.Map)).Apply(session.Current);
                new EntityViews(units.transform, new Vector3(0.5f, 1f, 0.5f), 0.5f).Apply(session.Current, session.Current, 0f);
                Assert.That(TerrainHost.SelfCheck(session.Map, session, session.Current, map.transform, parent.transform), Is.Empty);
                Assert.That(units.transform.childCount, Is.EqualTo(session.Current.Entities.Length), "one cube per unit");
                Assert.That(session.Current.TryGet(1, out ViewEntity player), Is.True);
                Transform cube = units.transform.Find("Unit 1");
                Assert.That(cube, Is.Not.Null);
                Assert.That(cube.position, Is.EqualTo(new Vector3(player.X, player.Y + 0.5f, player.Z)), "the player cube stands on its point");
            }
            finally
            {
                Transform ground = map.transform.Find("Ground");
                MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(map);
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(units);
            }
        }

        [Test]
        public void L10_HudAtTickZero_NamesThePlayerAndItsHeight()
        {
            SimSession session = Demo();
            string hud = TerrainPlayerHost.Hud(session.Current, session.StateHash, session.Map, session.ControlledKey);
            Assert.That(hud, Does.StartWith("Tick 0\nStateHash " + session.StateHash.ToString("X16") + "\n"));
            Assert.That(hud, Does.Contain("\nPlayer 1\n"));
            Assert.That(hud, Does.Contain("\nPlayerY "));
            Assert.That(hud, Does.Not.Contain("\nPlayerY -"));
        }

        [Test]
        public void L10_ShotLabel_TickHashPlayerAndHold_AllDrawable()
        {
            SimSession session = Demo();
            string[] label = TerrainPlayerHost.ShotLabel(session.Current, session.StateHash, session.ControlledKey, true);
            Assert.That(label[0], Is.EqualTo("TICK 0"));
            Assert.That(label[1], Is.EqualTo("STATEHASH " + session.StateHash.ToString("X16")));
            Assert.That(label[2], Is.EqualTo("PLAYER 1"));
            Assert.That(label[3], Does.StartWith("PLAYERY ").And.Not.EqualTo("PLAYERY -"));
            Assert.That(label[4], Is.EqualTo("HOLD"));
            Assert.That(TerrainPlayerHost.ShotLabel(session.Current, session.StateHash, null, false),
                Is.EqualTo(new[] { "TICK 0", "STATEHASH " + session.StateHash.ToString("X16"), "PLAYER -", "PLAYERY -" }));
            foreach (string line in label)
                foreach (char c in line)
                    Assert.That(TerrainPlayerHost.HasGlyph(c), Is.True, "glyph for '" + c + "'");
            foreach (char c in "0123456789ABCDEF.-")
                Assert.That(TerrainPlayerHost.HasGlyph(c), Is.True, "glyph for '" + c + "'");
        }

        [TestCase("L10", true), TestCase("L10-x", true), TestCase("L10_1", true)]
        [TestCase(null, false), TestCase("", false), TestCase("l10", false), TestCase("L8b", false), TestCase("L10/x", false)]
        [TestCase("L10\\x", false), TestCase("L10.x", false), TestCase("L10 x", false)]
        public void L10_IsShotPrefix(string prefix, bool expected)
        {
            Assert.That(TerrainPlayerHost.IsShotPrefix(prefix), Is.EqualTo(expected));
        }
    }
}
