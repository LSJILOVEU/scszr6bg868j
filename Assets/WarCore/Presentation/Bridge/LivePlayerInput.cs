using System;

namespace WarCore.Presentation
{
    // One adapter per live session. Call Apply before Advance on every host frame.
    // Replay loads PlayerLog directly through SetPlayerInput and does not use this adapter.
    public sealed class LivePlayerInput
    {
        private SimSession owner;
        private long clickTick;
        private long releaseTick;

        public void Apply(SimSession session, PlayerInput input, bool pressedThisFrame)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (owner != null && !ReferenceEquals(owner, session))
                throw new InvalidOperationException("Use a separate live input adapter for each session.");
            owner = session;
            // A queued click is consumed only when the session reaches its tick. A zero-tick
            // frame keeps it latched. Acknowledge consumption on the next Apply, before new edges.
            if (clickTick <= session.Tick) clickTick = 0;
            long next = checked(session.Tick + 1);
            if (pressedThisFrame) clickTick = next;

            // A newer held state must also replace an unconsumed synthetic release, otherwise
            // a short click followed by a hold during zero-tick frames would cancel that hold.
            if (releaseTick > session.Tick) session.SetPlayerInput(releaseTick, input);
            releaseTick = 0;
            if (clickTick != 0 && !input.Primary)
            {
                // Copy every field; only Primary is forced on. Block, throw and skill pass as held.
                session.SetPlayerInput(next, new PlayerInput(input.MoveXMilli, input.MoveZMilli,
                    input.AimXMm, input.AimZMm, true, input.Block, input.Throw, input.SkillSlot));
                releaseTick = checked(next + 1);
                session.SetPlayerInput(releaseTick, input);
            }
            else session.SetPlayerInput(next, input);
        }
    }
}
