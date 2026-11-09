using System;
using NUnit.Framework;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.L10Fixture;

namespace WarCore.Tests
{
    // L10 (5): the 0.25 m base cap of terrain worlds holds for the player at its first non-zero step, with the AI's message.
    public sealed class L10CapTests
    {
        [Test]
        public void L10Cap_ExactlyTheCapWalks()
        {
            var w = Demo(12500, 6500, speed: 7500);
            SimVec3 start = At(w);
            Assert.DoesNotThrow(() => Walk(w, 1000, 0, 30));
            Assert.That(At(w).X.Raw, Is.GreaterThan(start.X.Raw));
        }

        [Test]
        public void L10Cap_OneStepAboveTheCapThrowsOnTheFirstMove()
        {
            var w = Demo(12500, 6500, speed: 7530);
            w.Session.SetPlayerInput(1, new PlayerInput(1000, 0));
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(() => w.Session.StepOnce());
            Assert.That(e.Message, Is.EqualTo("Terrain worlds cap a unit step at 0.25 m per tick."));
        }

        [Test]
        public void L10Cap_AboveTheCapWithoutInputDoesNotThrow()
        {
            var w = Demo(12500, 6500, speed: 7530);
            Assert.DoesNotThrow(() => { for (int t = 0; t < 60; t++) w.Session.StepOnce(); });
        }
    }
}
