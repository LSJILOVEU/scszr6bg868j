using System;
using System.Reflection;
using System.Security.Cryptography;
using NUnit.Framework;
using WarCore.Presentation;
using Simulation = WarCore.Sim.Sim;

namespace WarCore.Tests
{
    public sealed class M3LegacyRegressionTests
    {
        private const ulong M1Hash600 = 0x62306703E44CF8EBUL;
        private const ulong M2bHash600 = 0xCEDECB8218C40918UL;
        private const string M2bSnapshot150 = "5FEF92D3C09340C571B4A843BC9EE6897E9198A6F62C759FA67E02D9408C5C7E";

        [Test]
        public void LegacyRecipes_RetainPreM3HashesAndSnapshotBytes()
        {
            var recipe = new WorldRecipe(41, MoverKind.Interim, new[] { new UnitSpawn(1, 0, 1, 0, 0, 3000) },
                new[] { BridgeCommand.MoveTo(1, 1, 10000, 0), BridgeCommand.MoveTo(150, 1, 10000, 10000) });
            var m1 = SimSession.Create(recipe);
            var m2 = SimSession.Create(DemoRecipes.M2b(), DemoRecipes.M2bMapBytes());
            for (int tick = 1; tick <= 600; tick++)
            {
                m1.StepOnce(); m2.StepOnce();
                if (tick == 150)
                {
                    using var sha = SHA256.Create();
                    Assert.That(BitConverter.ToString(sha.ComputeHash(SimulationOf(m2).SaveRetainedSnapshot())).Replace("-", ""), Is.EqualTo(M2bSnapshot150));
                }
            }
            Assert.That(m1.StateHash, Is.EqualTo(M1Hash600));
            Assert.That(m2.StateHash, Is.EqualTo(M2bHash600));
        }

        internal static Simulation SimulationOf(SimSession session) =>
            (Simulation)typeof(SimSession).GetField("sim", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(session);
    }
}
