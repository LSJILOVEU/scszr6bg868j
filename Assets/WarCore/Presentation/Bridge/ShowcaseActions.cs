using System;

namespace WarCore.Presentation
{
    // SHOW-2: the two orders a person gives in a stage that takes them, built on public session commands only (nothing in the
    // simulation changes): G throws a grenade, a blast of the stage's kind that bursts FuseTicks later where the player aimed,
    // cut to Reach from the player; Y steps the stance order of the stage's squad: stand, crouch, prone, stand... The grenade
    // does not fly in the simulation (the drawn one flies for the length of the fuse). PLACEHOLDER numbers.
    // RC2-1: and the three orders of the squad the player leads (T, C, X), in a stage whose pack has its rules.
    public sealed class ShowcaseActions
    {
        // The fuse (also the drawn grenade's flight), the wait before a new throw, and the reach. The picture reads these
        // (ShowcaseFx through BlastLook); nothing else repeats them.
        public const long FuseTicks = 33, CooldownTicks = 90, ReachMm = 28000;

        private readonly ShowcaseStage stage;
        private readonly int levels;
        private long readyAt;

        public ShowcaseActions(ShowcaseStage stage, int stanceLevels)
        {
            this.stage = stage ?? throw new ArgumentNullException(nameof(stage));
            if (stanceLevels < 1) throw new ArgumentOutOfRangeException(nameof(stanceLevels), "Level 0, standing, is always there.");
            levels = stanceLevels;
        }

        public bool RealGrenades => stage.GrenadeBlast != null;
        public bool StanceOrders => stage.OrderSquad > 0 && levels > 1;
        public int Thrown { get; private set; }
        // The stance level last ordered (0 until the first order).
        public int Stance { get; private set; }

        // Ticks until the next throw may start (0: ready).
        public long WaitTicks(long tick) => Math.Max(0, readyAt - tick);

        // Queue a grenade from the controlled unit toward (aimXMm, aimZMm). False, with nothing queued, where the stage has no
        // real grenade, the player has no living unit, the last throw is cooling down, or the session refuses the point (off
        // the map). land: where it will burst.
        public bool TryThrow(SimSession session, long aimXMm, long aimZMm, out long landXMm, out long landZMm)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            landXMm = aimXMm; landZMm = aimZMm;
            if (!RealGrenades || WaitTicks(session.Tick) > 0) return false;
            ulong? key = session.ControlledKey;
            if (!key.HasValue || !session.Current.TryGet(key.Value, out ViewEntity me) || !me.Alive) return false;
            long fromX = (long)Math.Round(me.X * 1000.0), fromZ = (long)Math.Round(me.Z * 1000.0);
            long dx = aimXMm - fromX, dz = aimZMm - fromZ;
            double length = Math.Sqrt((double)dx * dx + (double)dz * dz);
            if (length > ReachMm)
            {
                landXMm = fromX + (long)Math.Round(dx * ReachMm / length);
                landZMm = fromZ + (long)Math.Round(dz * ReachMm / length);
            }
            try { session.Enqueue(BridgeCommand.Blast(session.Tick + FuseTicks, landXMm, landZMm, stage.GrenadeBlast)); }
            catch (ArgumentException) { return false; }
            readyAt = session.Tick + CooldownTicks;
            Thrown++;
            return true;
        }

        // Order the stage's squad one stance lower (prone steps back to standing) from the next tick. False, with nothing
        // queued, where the stage takes no stance orders.
        public bool NextStance(SimSession session)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (!StanceOrders) return false;
            int next = (Stance + 1) % levels;
            session.Enqueue(BridgeCommand.Stance(session.Tick + 1, stage.OrderSquad, next));
            Stance = next;
            return true;
        }

        // RC2-1: the orders of the squad the player leads, in a world whose pack has the led squad's rules (session.LedOrders).
        // Each is queued for the next tick that is free. False, with nothing queued, where the world has no led squad, the
        // player has no living unit, or the order has nothing to do.
        // T: the soldiers near the player follow it; given again while they follow, they hold where they stand.
        public bool FollowOrHold(SimSession session) => Led(session, false, session != null && session.LedOrder == 1
            ? (Func<long, BridgeCommand>)(tick => BridgeCommand.Hold(tick, BridgeCommand.Led))
            : tick => BridgeCommand.Follow(tick, BridgeCommand.Led, 0));

        // C: the squad goes to the point and holds there. A point the session refuses (beyond the world's bounds, not on the
        // plate of its layer) queues nothing; a point nobody can stand on is queued and the simulation leaves the squad as it is.
        public bool GoTo(SimSession session, long xMm, long zMm, int layer = 0)
            => Led(session, false, tick => BridgeCommand.MoveTo(tick, BridgeCommand.Led, xMm, zMm, layer));

        // X: the squad breaks up and the AI takes its soldiers back.
        public bool Dismiss(SimSession session) => Led(session, true, BridgeCommand.Dismiss);

        private static bool Led(SimSession session, bool needsSquad, Func<long, BridgeCommand> order)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ulong? key = session.ControlledKey;
            if (!session.LedOrders || needsSquad && session.LedOrder == 0) return false;
            if (!key.HasValue || !session.Current.TryGet(key.Value, out ViewEntity me) || !me.Alive) return false;
            try { session.Enqueue(order(session.FreeTick(session.Tick + 1))); }
            catch (ArgumentException) { return false; }
            return true;
        }

        // The HUD line for the orders of this stage, or "" where it takes none.
        public string Line(long tick)
        {
            if (!RealGrenades && !StanceOrders) return "";
            string text = "";
            if (RealGrenades) text += "GRENADE " + (WaitTicks(tick) > 0 ? ShowcaseHud.Seconds(WaitTicks(tick)) : "ready") + " (G)";
            if (StanceOrders) text += (text == "" ? "" : "  ") + "TEAM " + StanceName(Stance) + " (Y)";
            return text + "\n";
        }

        public static string StanceName(int level) => level == 0 ? "standing" : level == 1 ? "crouching" : level == 2 ? "prone" : "stance " + level;
    }
}
