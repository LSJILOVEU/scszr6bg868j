using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // ML-3: a standing unit's body, every height measured up from the foot (the unit's Position).
    // Height: the capsule from the foot to the top of the head. Eye: the viewpoint, where the unit looks from and where
    // others look and aim at it. Muzzle: where its shots start.
    // A pack without a body keeps the legacy geometry: a 1 m capsule centred on the foot, eye 0.5 m, shots from the foot.
    public readonly struct CombatBody
    {
        public readonly SimFix Height, Eye, Muzzle;

        public CombatBody(SimFix height, SimFix eye, SimFix muzzle)
        {
            if (height.Raw <= 0 || height.Raw > 32768L * SimFix.Scale) throw new ArgumentOutOfRangeException(nameof(height));
            if (eye.Raw < 0 || eye.Raw > height.Raw) throw new ArgumentOutOfRangeException(nameof(eye));
            if (muzzle.Raw < 0 || muzzle.Raw > height.Raw) throw new ArgumentOutOfRangeException(nameof(muzzle));
            Height = height; Eye = eye; Muzzle = muzzle;
        }

        internal void WriteState(StateHash hash) { hash.Add(Height.Raw); hash.Add(Eye.Raw); hash.Add(Muzzle.Raw); }
    }
}
