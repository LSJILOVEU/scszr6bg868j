using System;
using WarCore.Sim;

namespace WarCore.Interim
{
    public readonly struct MoveIntent : IStateHash
    {
        public const string ComponentId = "interim.move-intent";
        public SimVec3 Target { get; }
        public SimFix SpeedPerTick { get; }
        public int Squad { get; }
        public bool Active { get; }

        public MoveIntent(SimVec3 target, SimFix speedPerTick, int squad, bool active)
        {
            target.ValidateWorld();
            if (speedPerTick.Raw <= 0)
                throw new ArgumentOutOfRangeException(nameof(speedPerTick));
            Target = target;
            SpeedPerTick = speedPerTick;
            Squad = squad;
            Active = active;
        }

        public void WriteState(StateHash hash)
        {
            Target.WriteState(hash);
            SpeedPerTick.WriteState(hash);
            hash.Add(Squad);
            hash.Add(Active);
        }
    }
}
