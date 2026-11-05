using NUnit.Framework;
using WarCore.Presentation;

namespace WarCore.Tests
{
    public sealed class P0HudTests
    {
        [Test]
        public void HudShowsTickHashHealthAmmoPointsAndGroundAtTick600()
        {
            WorldRecipe d = DemoRecipes.M7();
            SimSession session = SimSession.Create(d, DemoRecipes.M7MapBytes());
            while (session.Tick < 600) session.StepOnce();

            Assert.That(session.TryUnitStatus(1, out UnitStatus status), Is.True);
            Assert.That(status.MaxHealth, Is.EqualTo(100f));
            Assert.That(status.Health, Is.InRange(0f, 100f));
            Assert.That(status.Ammo, Is.GreaterThanOrEqualTo(0));
            Assert.That(session.TryUnitStatus(999999, out _), Is.False);

            string text = HudLines.Build(session.Current, session.StateHash, 1, status, new GroundPick(12.34f, 56.78f));
            Assert.That(text, Does.Contain("Tick 600"));
            Assert.That(text, Does.Contain(session.StateHash.ToString("X16")));
            Assert.That(text, Does.Contain($"HP {status.Health:F0}/100"));
            Assert.That(text, Does.Contain($"Ammo {status.Ammo}"));
            Assert.That(session.Current.Points.Length, Is.EqualTo(5));
            foreach (ViewPoint p in session.Current.Points)
                Assert.That(text, Does.Contain($"P{p.Id} {(p.Owner == 0 ? "-" : "T" + p.Owner)}"));
            Assert.That(text, Does.Contain("Ground (12.3, 56.8)"));
            Assert.That(HudLines.Build(session.Current, session.StateHash, null, null, null), Does.Contain("Follow -").And.Contain("Ground -"));
        }

        [Test]
        public void HealthReadMatchesGameplayResourceAndDropsToZeroOnDeath()
        {
            SimSession session = SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes());
            while (session.Winner == 0 && session.Tick < 9000) session.StepOnce();
            bool sawDead = false;
            foreach (ViewEntity e in session.Current.Entities)
            {
                Assert.That(session.TryUnitStatus(e.Key, out UnitStatus s), Is.True);
                if (!e.Alive) { Assert.That(s.Health, Is.LessThanOrEqualTo(0f)); sawDead = true; }
                else Assert.That(s.Health, Is.GreaterThan(0f));
            }
            Assert.That(sawDead, Is.True);
        }

        // Read-only: reading the HUD numbers changes nothing in the world, and the host never queues a command.
        [Test]
        public void ReadingTheHudEveryTickLeavesHashesAndCommandLogUntouched()
        {
            SimSession plain = SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes());
            SimSession read = SimSession.Create(DemoRecipes.M7(), DemoRecipes.M7MapBytes());
            for (int tick = 0; tick < 300; tick++)
            {
                plain.StepOnce(); read.StepOnce();
                read.TryUnitStatus(1, out UnitStatus status);
                HudLines.Build(read.Current, read.StateHash, 1, status, new GroundPick(1f, 2f));
                Assert.That(read.StateHash, Is.EqualTo(plain.StateHash), $"tick {tick + 1}");
            }
            Assert.That(read.CommandLog, Is.Empty);
            string host = System.IO.File.ReadAllText(System.IO.Path.Combine(ArchitectureTests.AssetRoot(), "WarCore/Unity/P0Host.cs"));
            Assert.That(host, Does.Not.Contain("Enqueue"));
        }
    }
}
