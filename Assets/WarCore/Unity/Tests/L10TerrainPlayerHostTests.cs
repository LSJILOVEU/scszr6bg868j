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
            GameObject map = MapView.Build(session.Map, null, null), parent = new GameObject("L10 points");
            try
            {
                new PointViews(parent.transform, ViewGround.For(session.Map)).Apply(session.Current);
                Assert.That(TerrainHost.SelfCheck(session.Map, session, session.Current, map.transform, parent.transform), Is.Empty);
            }
            finally
            {
                Transform ground = map.transform.Find("Ground");
                MeshFilter filter = ground == null ? null : ground.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null && filter.sharedMesh.name != "Cube")
                    UnityEngine.Object.DestroyImmediate(filter.sharedMesh);
                UnityEngine.Object.DestroyImmediate(map);
                UnityEngine.Object.DestroyImmediate(parent);
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

        [TestCase("L10", true), TestCase("L10-x", true), TestCase("L10_1", true)]
        [TestCase(null, false), TestCase("", false), TestCase("l10", false), TestCase("L8b", false), TestCase("L10/x", false)]
        [TestCase("L10\\x", false), TestCase("L10.x", false), TestCase("L10 x", false)]
        public void L10_IsShotPrefix(string prefix, bool expected)
        {
            Assert.That(TerrainPlayerHost.IsShotPrefix(prefix), Is.EqualTo(expected));
        }
    }
}
