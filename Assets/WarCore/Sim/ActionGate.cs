using System;
using System.Collections.Generic;

namespace WarCore.Sim
{
    // What a unit may do. The values are fixed: content packs will name them.
    [Flags]
    public enum UnitAction
    {
        None = 0,
        Move = 1,
        Dash = 2,
        Traverse = 4,
        Fire = 8,
        Melee = 16,
        Block = 32,
        Throw = 64,
        Reload = 128,
        Stance = 256,
        Interact = 512,
        Capture = 1024
    }

    // One source of "may not": a state that forbids some actions while it lasts. It reads only components that are already saved,
    // never a cache of its own, so a restored world answers the same.
    public interface IActionLimit
    {
        UnitAction Denied(EntityId unit);
    }

    // The table "may this unit do this now": the union of what every registered limit forbids. It keeps nothing but the list of
    // limits, is not a system and is not saved or hashed. With no limit registered everything is allowed; a caller without a
    // gate (null) treats that as allowing everything too.
    public sealed class ActionGate
    {
        private readonly List<IActionLimit> limits = new List<IActionLimit>();

        public int Count => limits.Count;

        public void Add(IActionLimit limit)
        {
            if (limit == null)
                throw new ArgumentNullException(nameof(limit));
            for (int i = 0; i < limits.Count; i++)
                if (ReferenceEquals(limits[i], limit))
                    throw new ArgumentException("This limit is already registered.", nameof(limit));
            limits.Add(limit);
        }

        public UnitAction Denied(EntityId unit)
        {
            UnitAction denied = UnitAction.None;
            for (int i = 0; i < limits.Count; i++)
                denied |= limits[i].Denied(unit);
            return denied;
        }

        // True when no bit of action is denied.
        public bool Allows(EntityId unit, UnitAction action)
        {
            if (action == UnitAction.None)
                throw new ArgumentOutOfRangeException(nameof(action));
            return (Denied(unit) & action) == UnitAction.None;
        }
    }
}
