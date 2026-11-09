using System;
using System.Collections.Generic;

namespace WarCore.Presentation
{
    public enum PlayerKeyAction { Block = 0, Throw = 1, Skill = 2 }

    // One key -> one held action. Key names are host-defined (e.g. the host's key enum names).
    public sealed class PlayerKeyBinding
    {
        public string Key { get; }
        public PlayerKeyAction Action { get; }
        /// <summary>Skill slot (>= 1) for Skill; 0 for every other action.</summary>
        public int SkillSlot { get; }

        public PlayerKeyBinding(string key, PlayerKeyAction action, int skillSlot = 0)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key name must not be empty.", nameof(key));
            if (action == PlayerKeyAction.Skill)
            {
                if (skillSlot < 1) throw new ArgumentException("A skill binding needs a slot >= 1.", nameof(skillSlot));
            }
            else if (action == PlayerKeyAction.Block || action == PlayerKeyAction.Throw)
            {
                if (skillSlot != 0) throw new ArgumentException("Only a skill binding carries a slot.", nameof(skillSlot));
            }
            else throw new ArgumentException("Unknown action.", nameof(action));
            Key = key; Action = action; SkillSlot = skillSlot;
        }
    }

    public sealed class PlayerInputMapper
    {
        private readonly PlayerKeyBinding[] bindings;
        private bool hasAim;
        private GroundPick lastAim;
        private long? lastAimYMm; // A4: height of the last valid pick from the aim overload; null after any other pick

        public PlayerInputMapper() { bindings = Array.Empty<PlayerKeyBinding>(); }

        // The table is copied; later changes to the caller's list do not affect this mapper.
        public PlayerInputMapper(IReadOnlyList<PlayerKeyBinding> bindings)
        {
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));
            var copy = new PlayerKeyBinding[bindings.Count];
            for (int i = 0; i < copy.Length; i++)
                copy[i] = bindings[i] ?? throw new ArgumentException("Binding must not be null.", nameof(bindings));
            this.bindings = copy;
        }

        // Screen origin is bottom-left. Outside the window or with a nonpositive viewport,
        // retain the last valid pick. Before the first valid pick, use the current focus's
        // ground point (without storing it as a valid mouse pick).
        public PlayerInput Map(IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool mouseInWindow, bool primary) =>
            Map(camera, ix, iy, x, y, w, h, mouseInWindow, primary, null);

        // Block / throw: any bound key held. Skill: the first skill binding in table order whose
        // key is held, else 0. A null isKeyDown means no key is held.
        public PlayerInput Map(IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool mouseInWindow, bool primary, Func<string, bool> isKeyDown)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            if (mouseInWindow && w > 0 && h > 0)
            {
                lastAim = camera.ScreenToGround(x, y, w, h);
                lastAimYMm = null;
                hasAim = true;
            }
            GroundPick aim = hasAim ? lastAim : new GroundPick(camera.FocusX, camera.FocusZ);
            var move = camera.MoveAxes(ix, iy);
            bool block = false, @throw = false;
            int skillSlot = 0;
            if (isKeyDown != null)
            {
                foreach (PlayerKeyBinding binding in bindings)
                {
                    if (!isKeyDown(binding.Key)) continue;
                    if (binding.Action == PlayerKeyAction.Block) block = true;
                    else if (binding.Action == PlayerKeyAction.Throw) @throw = true;
                    else if (skillSlot == 0) skillSlot = binding.SkillSlot;
                }
            }
            return new PlayerInput(Milli(move.X), Milli(move.Z), aim.XMm, aim.ZMm, primary, block, @throw, skillSlot);
        }

        // A4: as above, but a valid pick inside the window takes aim (AimPick's result) when it is given, with its height.
        // aim == null inside the window picks the flat plane, as the overloads above do; outside the window the last pick
        // and its height are kept; before the first valid pick, the focus with no height. The overloads above never output
        // a height and clear it on a pick inside the window.
        public PlayerInput Map(IsoCamera camera, float ix, float iy, float x, float y,
            float w, float h, bool mouseInWindow, bool primary, Func<string, bool> isKeyDown, AimTarget? aim)
        {
            bool picked = mouseInWindow && w > 0 && h > 0 && aim.HasValue;
            // With a pick in hand the flat plane point is never computed: it can lie outside the world when the pick does not.
            PlayerInput flat = Map(camera, ix, iy, x, y, w, h, mouseInWindow && !picked, primary, isKeyDown);
            if (picked)
            {
                lastAim = new GroundPick(aim.Value.X, aim.Value.Z);
                lastAimYMm = aim.Value.YMm;
                hasAim = true;
            }
            if (!hasAim) return flat;
            return new PlayerInput(flat.MoveXMilli, flat.MoveZMilli, lastAim.XMm, lastAim.ZMm, flat.Primary,
                flat.Block, flat.Throw, flat.SkillSlot, lastAimYMm);
        }

        // Scale then truncate toward zero. Clamp before the cast to keep the conversion bounded.
        private static int Milli(float axis) => (int)Math.Clamp(axis * 1000f, -1000f, 1000f);
    }
}
