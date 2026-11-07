using NUnit.Framework;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5u (6): the full HUD text of the M7 world at tick 600, recorded on the base code before UnitStatus gained Blocking.
    // P0HudTests only checks substrings; these two strings pin every character and line break.
    public sealed class F5uHudBaselineTests
    {
        // The M7 world written out here (same units, seed, combat and match as the M7 demo at the base commit), so a later
        // change to the demo recipes cannot move this fixture.
        internal static WorldRecipe M7World()
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            int[] weapons = { content.WeaponOfUnitType(RwrPack.SoldierTeam1), content.WeaponOfUnitType(RwrPack.SoldierTeam2) };
            var units = new UnitSpawn[300];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 150 ? 1 : 2, rank = i % 150;
                int x = team == 1 ? 6000 + rank % 10 * 2000 : 154000 - rank % 10 * 2000;
                int z = 41000 + rank % 100 / 10 * 2000;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, z, 6000, team, weapons[team - 1], rank >= 100);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>())
                .WithCombat(content.BuildCombat()).WithMatch(RwrMatch.CreateM7(content)).WithCrowdPass(CrowdPassKind.TeamIdle);
        }

        private static SimSession AtTick600()
        {
            SimSession session = SimSession.Create(M7World(), M7DemoMap.Bytes());
            while (session.Tick < 600) session.StepOnce();
            return session;
        }

        private const string FollowedText = "Tick 600\nStateHash 8D7460197CEA0D74\nFollow #1  HP 100/100  Ammo 30\n"
            + "Points  P1 T1  P2 -  P3 -  P4 -  P5 T2\nGround (12.3, 56.8)";
        private const string NothingText = "Tick 600\nStateHash 8D7460197CEA0D74\nFollow -\n"
            + "Points  P1 T1  P2 -  P3 -  P4 -  P5 T2\nGround -";

        [Test]
        public void F5uHudFullText_FollowedUnitAtTick600_IsUnchanged()
        {
            SimSession session = AtTick600();
            Assert.That(session.TryUnitStatus(1, out UnitStatus status), Is.True);
            Assert.That(HudLines.Build(session.Current, session.StateHash, 1, status, new GroundPick(12.34f, 56.78f)), Is.EqualTo(FollowedText));
        }

        [Test]
        public void F5uHudFullText_NothingFollowedAtTick600_IsUnchanged()
        {
            SimSession session = AtTick600();
            Assert.That(HudLines.Build(session.Current, session.StateHash, null, null, null), Is.EqualTo(NothingText));
        }
    }
}
