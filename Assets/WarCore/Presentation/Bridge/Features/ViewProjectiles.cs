using System;
using System.Collections.Generic;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Presentation
{
    // The projectiles of a session for the picture: those in flight at its committed tick and those that ended on it, appended
    // to the lists (either may be null). Read on demand, never hashed; nothing is added without a projectile weapon.
    public static class ViewProjectiles
    {
        public static void Read(SimSession session, List<ViewProjectile> flying, List<ViewImpact> landed)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            ProjectileSystem system = session.ProjectilesForView;
            if (system == null) return;
            long tick = system.LastTick;
            for (int i = 0; flying != null && i < system.Count; i++)
            {
                ProjectileView view = system.InFlight(i);
                bool fresh = view.LaunchTick >= tick;
                SimVec3 now = fresh ? view.Origin : view.PositionAt(tick), before = fresh ? view.Origin : view.PositionAt(tick - 1);
                flying.Add(new ViewProjectile(Metres(now.X), Metres(now.Y), Metres(now.Z), Metres(before.X), Metres(before.Y), Metres(before.Z),
                    view.Team, fresh));
            }
            if (landed == null) return;
            foreach (ProjectileImpact impact in system.Impacts)
                landed.Add(new ViewImpact(Metres(impact.End.X), Metres(impact.End.Y), Metres(impact.End.Z), impact.Team,
                    impact.Outcome == FireOutcome.Hit || impact.Outcome == FireOutcome.FriendHit, impact.DamageRaw > 0));
        }

        private static float Metres(SimFix value) => (float)value.Raw / SimFix.Scale;
    }
}
