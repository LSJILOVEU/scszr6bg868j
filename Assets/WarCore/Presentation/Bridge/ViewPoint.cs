namespace WarCore.Presentation
{
    public readonly struct ViewPoint
    {
        public readonly int Id, Owner, Capturing;
        public readonly float X, Z, Radius, Progress;
        public ViewPoint(int id, float x, float z, float radius, int owner, int capturing, float progress)
        { Id = id; X = x; Z = z; Radius = radius; Owner = owner; Capturing = capturing; Progress = progress; }
    }
    public readonly struct ViewMatch
    {
        public readonly int Winner, EndTick, EndReason, AliveOne, AliveTwo, ReserveOne, ReserveTwo;
        // Read from the match mode by SimSession. Frames built by hand without it fall back to Winner != 0.
        public readonly bool IsFinished;
        public ViewMatch(int winner, int endTick, int endReason, int aliveOne, int aliveTwo, int reserveOne, int reserveTwo, bool? isFinished = null)
        { Winner = winner; EndTick = endTick; EndReason = endReason; AliveOne = aliveOne; AliveTwo = aliveTwo; ReserveOne = reserveOne; ReserveTwo = reserveTwo; IsFinished = isFinished ?? winner != 0; }
    }
}
