using System;
using System.Text;

namespace WarCore.Presentation
{
    // The showcase's HUD text, built without Unity so the headless tests read the same lines the player does.
    public static class ShowcaseHud
    {
        public const string Keys =
            "MOVE      W A S D\n" +
            "AIM       mouse\n" +
            "FIRE      left mouse button\n" +
            "DASH      Left Shift\n" +
            "BLOCK     Space or right mouse button\n" +
            "GRENADE   G (real in stage 6; preview only elsewhere: it bursts but hurts nobody)\n" +
            "ORDER     C your team: stand, crouch, prone (stage 6)\n" +
            "CAMERA    Q E turn, R F tilt, wheel zoom\n" +
            "STAGE     Tab next, Backspace restart, P pause\n" +
            "SHOW      V squad lines, M missing list, H this help\n" +
            "SOUND     N on or off";

        public const string GrenadeStub = "GRENADE PREVIEW: looks only. It bursts but hurts nobody here; stage 6 has the real one.";

        // Stage line, both teams, the points, the player's unit and its respawn or takeover line.
        // status is null when no unit is controlled; inFlight is the number of projectiles in the air.
        public static string Status(ShowcaseStage stage, int index, int count, FrameView frame, ulong? key, UnitStatus? status,
            bool lostPlayer, bool alive, int inFlight)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var text = new StringBuilder();
            text.Append(FormattableString.Invariant($"STAGE {index + 1}/{count}  {stage.Title}    {Clock(frame.Tick)}\n"));
            ViewMatch m = frame.Match;
            text.Append(FormattableString.Invariant($"TEAM 1 (you)  alive {m.AliveOne}  reserve {m.ReserveOne}      TEAM 2  alive {m.AliveTwo}  reserve {m.ReserveTwo}\n"));
            text.Append("POINTS");
            foreach (ViewPoint p in frame.Points)
                text.Append(FormattableString.Invariant($"  {p.Id}:{(p.Owner == 0 ? "-" : "T" + p.Owner)}"));
            text.Append('\n');
            if (inFlight > 0)
                text.Append(FormattableString.Invariant($"BULLETS IN THE AIR  {inFlight}\n"));
            if (key.HasValue && status.HasValue && alive)
            {
                UnitStatus s = status.Value;
                text.Append(FormattableString.Invariant($"YOU  #{key.Value}  HP {s.Health:F0}/{s.MaxHealth:F0}"));
                if (s.HasWeapon)
                    text.Append(FormattableString.Invariant($"  AMMO {s.Ammo}  ")).Append(s.Reloading ? "reloading " + Seconds(s.WaitTicksLeft) : "ready");
                if (s.HasDash)
                    text.Append("  DASH ").Append(s.DashTicksLeft > 0 ? Seconds(s.DashTicksLeft) : "ready");
                if (s.Blocking)
                    text.Append("  BLOCKING");
                text.Append('\n');
            }
            else
                text.Append("YOU  down\n");
            string line = HudLines.PlayerLine(frame, lostPlayer, alive, key);
            if (line != null)
                text.Append(line).Append('\n');
            if (m.IsFinished)
                text.Append(FormattableString.Invariant($"MATCH OVER: team {m.Winner} wins. Backspace = again, Tab = next stage\n"));
            return text.ToString();
        }

        // The controlled unit's squad: how many of it are alive (the unit included) and who leads. "" without a living unit.
        public static string Squad(FrameView frame, ulong? key)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (!key.HasValue || !frame.TryGet(key.Value, out ViewEntity me) || !me.Alive) return "";
            if (me.Band == 0) return "SQUAD  none\n";
            int alive = 0;
            foreach (ViewEntity e in frame.Entities)
                if (e.Alive && e.Band == me.Band) alive++;
            return me.Band == me.Key ? FormattableString.Invariant($"SQUAD  you lead {alive} soldiers\n")
                : FormattableString.Invariant($"SQUAD  {alive} soldiers, leader #{me.Band}\n");
        }

        // The controlled unit's own shots and kills; kills are counted for shots that land on the tick they are fired only.
        public static string Score(int shots, int kills) => FormattableString.Invariant($"SHOTS {shots}  KILLS {kills}\n");

        // "What is on" for the stage, one line each.
        public static string Shows(ShowcaseStage stage)
        {
            if (stage == null) throw new ArgumentNullException(nameof(stage));
            var text = new StringBuilder("ON IN THIS STAGE\n");
            foreach (string line in stage.Shows) text.Append("- ").Append(line).Append('\n');
            return text.ToString();
        }

        public static string Missing()
        {
            var text = new StringBuilder("NOT BUILT YET\n");
            foreach (string line in Showcase.Missing) text.Append("- ").Append(line).Append('\n');
            return text.ToString();
        }

        // Ticks as seconds with one decimal ("1.5 s"), rounded up to the next tenth so a wait never shows 0.0 s early.
        public static string Seconds(long ticks)
        {
            long tenths = (Math.Max(0, ticks) * 10 + SimSession.TickRate - 1) / SimSession.TickRate;
            return FormattableString.Invariant($"{tenths / 10}.{tenths % 10} s");
        }

        // Ticks as m:ss.
        public static string Clock(long tick)
        {
            long seconds = Math.Max(0, tick) / SimSession.TickRate;
            return FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00}");
        }
    }
}
