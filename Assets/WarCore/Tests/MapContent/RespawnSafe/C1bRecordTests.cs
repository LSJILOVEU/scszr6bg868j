using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using WarCore.Battle;
using WarCore.Combat;
using WarCore.Control;
using WarCore.Presentation;
using WarCore.Sim;
using static WarCore.Tests.Wp16TestWorld;

namespace WarCore.Tests
{
    // C1b record (no improvement gate): the MP demo on the M7 map with a 30-tick respawn, the controlled unit killed on a fixed
    // table. Before every tick's respawn step (order 4) a sampler notes, for the dead controlled unit, the living enemies inside
    // every owned point with a spawn and which of them the C1 rule would pick (the nearest). Each switch is then joined with that sample and
    // the new unit is watched for 30 ticks (switch tick included). Output goes to C1B_RECORD_OUT when set, else to the test log.
    public sealed class C1bRecordTests
    {
        internal const int RespawnTicks = 30, Window = 30, End = 3000;
        internal static readonly long[] Kills = KillTable(300, 150, 2850);

        internal static long[] KillTable(long first, long step, long last)
        {
            var ticks = new List<long>();
            for (long t = first; t <= last; t += step) ticks.Add(t);
            return ticks.ToArray();
        }

        internal static WorldRecipe Recipe()
        {
            WorldRecipe source = DemoRecipes.MpDemo();
            MatchPack m = source.Match;
            var pack = new MatchPack(m.Points.ToArray(), m.Bases.ToArray(), m.Num, m.Den, m.Strict, m.CaptureTicks, m.Interrupt,
                m.DecayPerTick, m.IntervalTicks, m.ThinkInterval, m.MaxTicks, m.Sight, m.GridColumns, m.GridRows, m.GridSpacing, RespawnTicks);
            return source.WithMatch(pack);
        }

        [Test, Explicit("C1b record: run alone and keep the output."), Category("C1bRecord")]
        public void RecordSwitchesIntoContestedPoints()
        {
            var w = new M6World(Recipe(), DemoRecipes.M7MapBytes());
            var lines = new StringBuilder();
            lines.AppendLine("requests systems with order < 4: " + string.Join(", ", EarlyRequests(w)));
            int order = FreeOrder(w);
            var sampler = new C1bAnchorSampler(w, order);
            w.Sim.RegisterGameplay("test.c1b-sampler", sampler, SimSystemPhase.Requests);
            w.Sim.RegisterGameplay("test.respawn-kills", new KillControlled(w, Kills), SimSystemPhase.Queries);
            lines.AppendLine("sampler order " + order + "; respawnTicks " + RespawnTicks + "; kills " + string.Join(",", Kills));
            var switches = new List<(long tick, ulong key, string sample, SimVec3 at)>();
            int contested = 0;
            long finished = -1;
            for (long tick = 1; tick <= End; tick++)
            {
                if (w.Match.Winner != 0) { finished = w.Match.EndTick; break; }
                w.Sim.Step(default);
                if (Switches(w) == 1)
                {
                    ulong key = w.Session.ControlledKey.Value;
                    switches.Add((tick, key, sampler.Sample(tick), Wp16TestWorld.Point(w, key)));
                    if (sampler.Contested(tick)) contested++;
                }
                foreach (var s in switches) Watch(w, s.key, tick);
            }
            int unplanned = 0, incomplete = 0, survived = 0;
            lines.AppendLine("tick | key | sample before order 4: C1 anchor and enemies inside, then every owned spawn point | deployed at | 30-tick window");
            foreach (var s in switches)
            {
                string outcome = Outcome(s.tick, s.key, finished);
                if (outcome.StartsWith("unplanned", StringComparison.Ordinal)) unplanned++;
                else if (outcome.StartsWith("incomplete", StringComparison.Ordinal)) incomplete++;
                else survived++;
                lines.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} | {1} | {2} | ({3:F2}, {4:F2}) | {5}",
                    s.tick, s.key, s.sample, s.at.X.Raw / 65536.0, s.at.Z.Raw / 65536.0, outcome));
            }
            lines.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "switches {0}; C1 anchor had living enemies at sampling {1}; unplanned death in window {2}; incomplete {3}; survived {4}; match end {5}",
                switches.Count, contested, unplanned, incomplete, survived, finished));
            string path = Environment.GetEnvironmentVariable("C1B_RECORD_OUT");
            if (!string.IsNullOrEmpty(path)) File.WriteAllText(path, lines.ToString());
            TestContext.Out.Write(lines.ToString());
            Assert.That(switches.Count, Is.GreaterThan(0), "the record must contain switches");
        }

        private readonly Dictionary<ulong, long> deaths = new Dictionary<ulong, long>();

        private void Watch(M6World w, ulong key, long tick)
        {
            if (!deaths.ContainsKey(key) && w.Dead(key)) deaths[key] = tick;
        }

        // Window [t, t + 29]: a death there that is not on the kill table is unplanned; a planned kill, the match end or the end of
        // the record inside the window leaves the observation incomplete.
        private string Outcome(long t, ulong key, long finished)
        {
            long last = t + Window - 1;
            if (deaths.TryGetValue(key, out long died) && died <= last && Array.IndexOf(Kills, died) < 0) return "unplanned death at " + died;
            foreach (long kill in Kills) if (kill >= t && kill <= last) return "incomplete (planned kill at " + kill + ")";
            if (finished >= 0 && finished <= last) return "incomplete (match ended at " + finished + ")";
            if (last > End) return "incomplete (record ends at " + End + ")";
            return "survived";
        }

        internal static List<string> EarlyRequests(M6World w)
        {
            var ids = new List<string>();
            foreach (object entry in (System.Collections.IEnumerable)typeof(WarCore.Sim.Sim).GetField("systems",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(w.Sim))
            {
                var type = entry.GetType();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                int order = (int)type.GetField("Order", flags).GetValue(entry);
                var phase = (SimSystemPhase)type.GetField("Phase", flags).GetValue(entry);
                if (phase == SimSystemPhase.Requests && order < 4) ids.Add((string)type.GetField("Id", flags).GetValue(entry) + "@" + order);
            }
            return ids;
        }

        internal static int FreeOrder(M6World w)
        {
            List<string> taken = EarlyRequests(w);
            for (int order = 3; order >= 0; order--)
                if (!taken.Exists(id => id.EndsWith("@" + order, StringComparison.Ordinal))) return order;
            throw new InvalidOperationException("No free order below 4.");
        }
    }

    // Requests phase, before respawn (order 4): what the C1 rule would choose for the dead controlled unit at this tick.
    internal sealed class C1bAnchorSampler : IRetainedGameplaySystem
    {
        private readonly M6World world;
        private readonly Dictionary<long, string> samples = new Dictionary<long, string>();
        private readonly HashSet<long> contested = new HashSet<long>();
        private readonly int order;
        internal C1bAnchorSampler(M6World world, int order) { this.world = world; this.order = order; }
        public int Order => order;
        public string StateTypeId => "test.c1b-sampler";
        public int StateVersion => 1;
        internal bool Contested(long tick) => contested.Contains(tick);
        internal string Sample(long tick) => samples.TryGetValue(tick, out string s) ? s : "no sample";

        public void Step(SimContext c)
        {
            CombatSystem combat = world.Combat;
            EntityId player = world.System<PlayerControl>().Current;
            if (!combat.Entities.IsAlive(player) || !combat.IsDead(player)) return;
            int team = combat.Unit(player).Team;
            SimVec3 body = combat.Point(player);
            MatchPack pack = world.Recipe.Match;
            int best = -1; SimWide bestDistance = default;
            var text = new System.Text.StringBuilder();
            int bestEnemies = 0;
            for (int i = 0; i < pack.Points.Length; i++)
            {
                if (world.Capture.Points[i].Owner != team || !pack.Points[i].HasSpawn) continue;
                int enemies = EnemiesInside(combat, team, pack.Points[i]);
                text.Append(text.Length == 0 ? "" : " ").Append("p").Append(pack.Points[i].Id).Append('=').Append(enemies);
                SimWide d = CombatSystem.Horizontal(pack.Points[i].Spawn - body).LengthSquared;
                if (best < 0 || d < bestDistance) { best = i; bestDistance = d; bestEnemies = enemies; }
            }
            if (best < 0) { samples[c.Clock.Tick] = "no owned spawn point (base)"; return; }
            if (bestEnemies > 0) contested.Add(c.Clock.Tick);
            samples[c.Clock.Tick] = "C1 anchor p" + pack.Points[best].Id + " enemies " + bestEnemies + "; owned " + text;
        }

        private int EnemiesInside(CombatSystem combat, int team, in ControlPointDef point)
        {
            int enemies = 0;
            foreach (UnitSpawn unit in world.Recipe.Units)
            {
                EntityId e = world.Unit(unit.Key);
                if (!combat.Entities.IsAlive(e) || !combat.Entities.TryGet(combat.Entities.Resolve<Combatant>(), e, out Combatant u)
                    || u.Team != 3 - team || !combat.Entities.TryGet(combat.Entities.Resolve<Position>(), e, out Position at) || combat.IsDead(e)) continue;
                if (CaptureSystem.Inside(at.Value, point)) enemies++;
            }
            return enemies;
        }

        public void SaveState(SimStateWriter w) { }
        public void LoadState(SimStateReader r) { }
        public void WriteState(StateHash h) { }
    }
}
