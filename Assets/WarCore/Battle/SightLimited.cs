using System;
using WarCore.Combat;
using WarCore.Sim;

namespace WarCore.Battle
{
    public sealed class SightLimited : ITargetSelector
    {
        private readonly ITargetSelector inner;
        public SimFix Sight { get; }
        public ulong PolicyKey { get; }
        public SightLimited(ITargetSelector inner, SimFix sight)
        {
            if (sight.Raw <= 0) throw new ArgumentOutOfRangeException(nameof(sight));
            this.inner = inner; Sight = sight;
            var hash = new StateHash(); hash.Add("battle.sight.v1"); hash.Add(inner.PolicyKey); hash.Add(sight.Raw); PolicyKey = hash.Value;
        }
        public bool TrySelect(CombatSystem world, EntityId shooter, SimFix range, out EntityId target) =>
            inner.TrySelect(world, shooter, range < Sight ? range : Sight, out target);
    }
}
