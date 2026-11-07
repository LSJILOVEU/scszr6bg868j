using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Content;
using WarCore.Content.Rwr;
using WarCore.Presentation;

namespace WarCore.Tests
{
    // F5u (6): the full HUD text of the M7 world at tick 600, recorded on the base code before UnitStatus gained Blocking.
    // P0HudTests only checks substrings; these two strings pin every character and line break.
    public sealed class F5uHudBaselineTests
    {
        // Digests of the inputs this fixture was recorded with: the rwr-m6 combat pack and M7 match rules, and the M7 map file.
        // A change to the content data or the map fails here, at the input, instead of being followed by the fixture.
        private const ulong CombatDigest = 0x934F8562D85447D2UL, MatchDigest = 0x582B535D9D24F6EBUL;
        private const string MapFileHash = "A56E4C46DE51D3F106D0B603A62EA293DEFF03AF28CAE8B10D18C46E95107440";

        // The M7 world written out here (units, seed, weapon slots and crowd pass are literals; the combat and match rules come from
        // the rwr-m6 content layer and are pinned by digest), so a change to the demo recipes cannot move this fixture.
        internal static WorldRecipe M7World()
        {
            ContentPack content = RwrPack.Load(RwrPack.M6Layer);
            CombatPack combat = content.BuildCombat();
            MatchPack match = RwrMatch.CreateM7(content);
            Assert.That(combat.PolicyDigest, Is.EqualTo(CombatDigest), "combat pack");
            Assert.That(match.PolicyDigest, Is.EqualTo(MatchDigest), "match rules");
            int[] weapons = { 0, 1 };
            var units = new UnitSpawn[300];
            for (int i = 0; i < units.Length; i++)
            {
                int team = i < 150 ? 1 : 2, rank = i % 150;
                int x = team == 1 ? 6000 + rank % 10 * 2000 : 154000 - rank % 10 * 2000;
                int z = 41000 + rank % 100 / 10 * 2000;
                units[i] = new UnitSpawn((ulong)i + 1, 0, team, x, z, 6000, team, weapons[team - 1], rank >= 100);
            }
            return new WorldRecipe(41, MoverKind.InterimCrowd, units, System.Array.Empty<BridgeCommand>())
                .WithCombat(combat).WithMatch(match).WithCrowdPass(CrowdPassKind.TeamIdle);
        }

        internal static byte[] M7Map()
        {
            byte[] bytes = M7DemoMap.Bytes();
            Assert.That(ViewMap.Parse(bytes).FileHash, Is.EqualTo(MapFileHash), "M7 map file");
            return bytes;
        }

        private static SimSession AtTick600()
        {
            SimSession session = SimSession.Create(M7World(), M7Map());
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

        // (6b) Same frame, the status marked as blocking: the recorded text with one "Blocking" line inserted after the Follow line.
        [Test]
        public void F5uHudFullText_BlockingAddsOneLineAfterFollow()
        {
            SimSession session = AtTick600();
            Assert.That(session.TryUnitStatus(1, out UnitStatus s), Is.True);
            var blocking = new UnitStatus(s.Health, s.MaxHealth, s.Ammo, s.WaitTicksLeft, s.Reloading, s.Alive, s.HasWeapon, true);
            const string follow = "Follow #1  HP 100/100  Ammo 30\n";
            int at = FollowedText.IndexOf(follow, System.StringComparison.Ordinal) + follow.Length;
            Assert.That(HudLines.Build(session.Current, session.StateHash, 1, blocking, new GroundPick(12.34f, 56.78f)),
                Is.EqualTo(FollowedText.Insert(at, "Blocking\n")));
        }

        [Test]
        public void F5uHudFullText_NothingFollowedAtTick600_IsUnchanged()
        {
            SimSession session = AtTick600();
            Assert.That(HudLines.Build(session.Current, session.StateHash, null, null, null), Is.EqualTo(NothingText));
        }
    }
}
