using System;
using WarCore.Sim;

namespace WarCore.Combat
{
    // ML-3: a standing unit's body, every height measured up from the foot (the unit's Position).
    // Height: the capsule from the foot to the top of the head. Eye: the viewpoint, where the unit looks from and where
    // others look and aim at it. Muzzle: where its shots start.
    // Radius (HC-1): the body's half width, the collider and walking clearance of every unit of the pack (BodyRadius).
    // A pack without a body keeps the legacy geometry: a 1 m capsule centred on the foot, eye 0.5 m, shots from the foot.
    public readonly struct CombatBody
    {
        public readonly SimFix Height, Eye, Muzzle, Radius;

        public CombatBody(SimFix height, SimFix eye, SimFix muzzle) : this(height, eye, muzzle, BodyRadius.Default) { }

        public CombatBody(SimFix height, SimFix eye, SimFix muzzle, SimFix radius)
        {
            if (!BodyRadius.IsValid(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
            if (height.Raw <= 0 || height.Raw > 32768L * SimFix.Scale) throw new ArgumentOutOfRangeException(nameof(height));
            if (eye.Raw < 0 || eye.Raw > height.Raw) throw new ArgumentOutOfRangeException(nameof(eye));
            if (muzzle.Raw < 0 || muzzle.Raw > height.Raw) throw new ArgumentOutOfRangeException(nameof(muzzle));
            Height = height; Eye = eye; Muzzle = muzzle; Radius = radius;
        }

        // The radius is appended only when it is not the default, so a body that never set one keeps the bytes it had before HC-1.
        internal void WriteState(StateHash hash)
        {
            hash.Add(Height.Raw); hash.Add(Eye.Raw); hash.Add(Muzzle.Raw);
            if (Radius.Raw != BodyRadius.DefaultRaw) { hash.Add("combat.body.radius.v1"); hash.Add(Radius.Raw); }
        }
    }
}
