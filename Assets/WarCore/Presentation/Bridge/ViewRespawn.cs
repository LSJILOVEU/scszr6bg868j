namespace WarCore.Presentation
{
    // The controlled unit's respawn as of the committed tick. None: respawn is off, or the unit is not dead.
    // Waiting: TicksLeft more steps until the delay is met (not a promise of the switch: a reserve, an anchor
    // and a successful deployment are still needed). Overdue: the delay is met but no switch happened yet
    // (the anchor is blocked, or the team has no owned spawn point and no base). NoReserve: the team has no reserve left.
    public enum RespawnPhase
    {
        None = 0, Waiting = 1, Overdue = 2, NoReserve = 3
    }

    public readonly struct ViewRespawn
    {
        public RespawnPhase State { get; }
        public long TicksLeft { get; }
        public int Reserves { get; }
        // Parameters may only be appended at the end.
        public ViewRespawn(RespawnPhase state, long ticksLeft, int reserves)
        {
            State = state; TicksLeft = ticksLeft; Reserves = reserves;
        }
    }
}
