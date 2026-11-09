using System;
using System.Globalization;

namespace WarCore.Presentation
{
    // Every player-facing HUD line of respawn and takeover in one table, so another language or skin passes its own
    // table instead of editing HudLines. Formats take one argument ({0}: seconds, or the unit's view key).
    public sealed class HudText
    {
        public static readonly HudText English = new HudText(
            "Respawn in {0} s", "Respawn: waiting for an available spawn point", "No reserves left: no respawn",
            "Down", "Now controlling #{0}");

        public string RespawnIn { get; }
        public string RespawnWaiting { get; }
        public string NoReserve { get; }
        public string Down { get; }
        public string NowControlling { get; }

        // Parameters may only be appended at the end.
        public HudText(string respawnIn, string respawnWaiting, string noReserve, string down, string nowControlling)
        {
            RespawnIn = respawnIn ?? throw new ArgumentNullException(nameof(respawnIn));
            RespawnWaiting = respawnWaiting ?? throw new ArgumentNullException(nameof(respawnWaiting));
            NoReserve = noReserve ?? throw new ArgumentNullException(nameof(noReserve));
            Down = down ?? throw new ArgumentNullException(nameof(down));
            NowControlling = nowControlling ?? throw new ArgumentNullException(nameof(nowControlling));
        }

        internal static string Format(string format, object value) => string.Format(CultureInfo.InvariantCulture, format, value);
    }
}
